using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web.Resource;
using PricingApi.Data;
using PricingApi.Domain;
using PricingApi.Dtos;
using PricingApi.Services;
using PricingApi.Storage;

namespace EntraApi.Controllers
{
    public class UploadAttachmentRequest
    {
        // Client-generated key that groups uploads made before the case has been saved.
        [System.ComponentModel.DataAnnotations.StringLength(64)]
        public string? OwnerId { get; set; }

        public IFormFile File { get; set; } = null!;
    }

    [ApiController]
    [Authorize]
    [RequiredScope("access_as_user")]
    [EnableRateLimiting("writes")]
    [Route("api/attachments")]
    public class AttachmentsController(PricingDbContext db, IFileStore store) : ControllerBase
    {
        private const long MaxBytes = 25 * 1024 * 1024;
        private const int MaxPendingPerUser = 100;

        // Files a browser would execute or that Windows would run; everything else (docs, images, archives...) is allowed.
        private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".msi", ".bat", ".cmd", ".com", ".scr", ".ps1", ".vbs", ".js", ".jse", ".wsf", ".hta", ".html", ".htm", ".svg",
    };

        [HttpPost]
        [RequestSizeLimit(MaxBytes + 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxBytes + 1024 * 1024)]
        [ProducesResponseType<AttachmentDto>(StatusCodes.Status201Created)]
        public async Task<IActionResult> Upload([FromForm] UploadAttachmentRequest request, CancellationToken ct)
        {
            var user = CurrentUser.From(User);
            var file = request.File;

            if (file is null || file.Length == 0) return Problem("Empty file.", statusCode: 400);
            if (file.Length > MaxBytes) return Problem("File exceeds 25 MB.", statusCode: StatusCodes.Status413PayloadTooLarge);

            // Never use the client-supplied name for storage; keep only a sanitized display name.
            var name = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(name)) return Problem("Invalid file name.", statusCode: 400);
            if (Blocked.Contains(Path.GetExtension(name))) return Problem("This file type is not allowed.", statusCode: 400);

            var pending = await db.Attachments.CountAsync(a => a.UploadedBy == user.Id && a.CaseId == null, ct);
            if (pending >= MaxPendingPerUser) return Problem("Too many unattached uploads.", statusCode: StatusCodes.Status409Conflict);

            string key;
            await using (var stream = file.OpenReadStream())
                key = await store.SaveAsync(stream, file.ContentType, ct);

            var a = new CaseAttachment
            {
                Id = Guid.NewGuid(),
                DraftKey = request.OwnerId ?? "",
                FileName = name.Length > 255 ? name[^255..] : name,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                Size = file.Length,
                StorageKey = key,
                UploadedBy = user.Id,
                UploadedAt = DateTimeOffset.UtcNow,
            };
            db.Attachments.Add(a);

            try { await db.SaveChangesAsync(ct); }
            catch { await store.DeleteAsync(key, CancellationToken.None); throw; }

            return CreatedAtAction(nameof(Download), new { id = a.Id }, new AttachmentDto(a.Id, a.FileName, a.Size, a.ContentType));
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Download(Guid id, CancellationToken ct)
        {
            var a = await FindOwnedAsync(id, ct);
            if (a is null) return NotFound();

            var stream = await store.OpenReadAsync(a.StorageKey, ct);
            if (stream is null) return NotFound();

            // Always a download, never inline, so uploaded content cannot run in the API's origin.
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(stream, "application/octet-stream", a.FileName);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            var a = await FindOwnedAsync(id, ct);
            if (a is null) return NotFound();

            if (a.CaseId is { } caseId)
            {
                var status = await db.Cases.Where(c => c.Id == caseId).Select(c => c.Status).FirstAsync(ct);
                if (status != CaseStatus.Draft) return Problem("Attachments on an open case cannot be removed.", statusCode: StatusCodes.Status409Conflict);
            }

            db.Attachments.Remove(a);
            await db.SaveChangesAsync(ct);
            await store.DeleteAsync(a.StorageKey, ct);
            return NoContent();
        }

        [HttpPut("{id:guid}")]
        [RequestSizeLimit(MaxBytes + 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxBytes + 1024 * 1024)]
        [ProducesResponseType<AttachmentDto>(StatusCodes.Status200OK)]
        public async Task<IActionResult> Update(Guid id, [FromForm] UploadAttachmentRequest request, CancellationToken ct)
        {
            var a = await FindOwnedAsync(id, ct);
            if (a is null) return NotFound();

            if (a.CaseId is { } caseId)
            {
                var status = await db.Cases.Where(c => c.Id == caseId).Select(c => c.Status).FirstAsync(ct);
                if (status != CaseStatus.Draft) return Problem("Attachments on an open case cannot be updated.", statusCode: StatusCodes.Status409Conflict);
            }

            var file = request.File;
            if (file is null || file.Length == 0) return Problem("Empty file.", statusCode: 400);
            if (file.Length > MaxBytes) return Problem("File exceeds 25 MB.", statusCode: StatusCodes.Status413PayloadTooLarge);

            var name = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(name)) return Problem("Invalid file name.", statusCode: 400);
            if (Blocked.Contains(Path.GetExtension(name))) return Problem("This file type is not allowed.", statusCode: 400);

            string newKey;
            await using (var stream = file.OpenReadStream())
                newKey = await store.SaveAsync(stream, file.ContentType, ct);

            var oldKey = a.StorageKey;

            a.DraftKey = request.OwnerId ?? a.DraftKey;
            a.FileName = name.Length > 255 ? name[^255..] : name;
            a.ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
            a.Size = file.Length;
            a.StorageKey = newKey;
            a.UploadedAt = DateTimeOffset.UtcNow;

            try { await db.SaveChangesAsync(ct); }
            catch { await store.DeleteAsync(newKey, CancellationToken.None); throw; }

            try { await store.DeleteAsync(oldKey, ct); } catch { }

            return Ok(new AttachmentDto(a.Id, a.FileName, a.Size, a.ContentType));
        }

        // Uploader only; for case attachments the case owner is the uploader in this app's model.
        private Task<CaseAttachment?> FindOwnedAsync(Guid id, CancellationToken ct)
        {
            var uid = CurrentUser.From(User).Id;
            return db.Attachments.FirstOrDefaultAsync(a => a.Id == id && a.UploadedBy == uid, ct);
        }
    }
}