using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PricingApi.Data;
using PricingApi.Domain;
using PricingApi.Dtos;
using PricingApi.Storage;
using Microsoft.EntityFrameworkCore.Storage;

namespace PricingApi.Services;

public sealed record CurrentUser(string Id, string Name)
{
    public const string AdminRole = "Admin";

    public static CurrentUser From(ClaimsPrincipal p)
    {
        var id = p.FindFirstValue("oid")
                 ?? p.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
                 ?? throw new InvalidOperationException("Token has no object id claim.");
        return new CurrentUser(id, p.FindFirstValue("name") ?? p.Identity?.Name ?? "unknown");
    }
}

public enum ResultKind { Ok, NotFound, Conflict, Invalid }

public sealed record Result<T>(ResultKind Kind, T? Value = default, string? Message = null,
    Dictionary<string, string[]>? Errors = null)
{
    public static Result<T> Ok(T v) => new(ResultKind.Ok, v);
    public static Result<T> NotFound() => new(ResultKind.NotFound);
    public static Result<T> Conflict(string m) => new(ResultKind.Conflict, Message: m);
    public static Result<T> Invalid(Dictionary<string, string[]> e) => new(ResultKind.Invalid, Errors: e);
}

public interface ICaseService
{
    Task<Result<SaveCaseResponse>> SaveAsync(Guid? id, SaveCaseRequest req, CurrentUser user, CancellationToken ct);
    Task<Result<CaseDetail>> GetAsync(Guid id, CurrentUser user, CancellationToken ct);
    Task<IReadOnlyList<CaseSummary>> ListAsync(CurrentUser user, CaseStatus? status, CancellationToken ct);
    Task<IReadOnlyList<AdminCaseSummary>> ListAllAsync(CaseStatus? status, CancellationToken ct);
    Task<Result<bool>> DeleteDraftAsync(Guid id, CurrentUser user, CancellationToken ct);
    Task<Result<IReadOnlyList<CaseVersionSummary>>> ListVersionsAsync(Guid id, CurrentUser user, CancellationToken ct);
    Task<Result<CaseVersionDetail>> GetVersionAsync(Guid id, int revision, CurrentUser user, CancellationToken ct);
    Task<Result<PublishCaseFilesResponse>> PublishFilesAsync(Guid id, CurrentUser user, CancellationToken ct);
    Task<Result<IReadOnlyList<SharePointFileVersionDto>>> ListFileVersionsAsync(Guid id, CurrentUser user, CancellationToken ct);
}

public sealed class CaseService(
    PricingDbContext db,
    IFileStore files,
    ISharePointLibraryService sharePoint,
    ILogger<CaseService> log) : ICaseService
{
    private const int MaxDraftsPerUser = 50;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<Result<SaveCaseResponse>> SaveAsync(Guid? id, SaveCaseRequest req, CurrentUser user, CancellationToken ct)
    {
        Normalize(req);
        var errors = CaseValidator.Validate(req, req.Submit);

        PricingCase c;
        if (id is null)
        {
            if (errors.Count > 0) return Result<SaveCaseResponse>.Invalid(errors);

            if (await db.Cases.CountAsync(x => x.OwnerId == user.Id && x.Status == CaseStatus.Draft, ct) >= MaxDraftsPerUser)
                return Result<SaveCaseResponse>.Conflict($"You can keep at most {MaxDraftsPerUser} drafts. Delete one first.");

            c = new PricingCase
            {
                Id = Guid.NewGuid(),
                CaseNumber = await NextCaseNumberAsync(ct),
                OwnerId = user.Id,
                OwnerName = user.Name,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Cases.Add(c);
        }
        else
        {
            // Tracked load: only the creator may touch a case, and a missing and a foreign case look identical.
            c = (await db.Cases.Include(x => x.Attachments).FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == user.Id, ct))!;
            if (c is null) return Result<SaveCaseResponse>.NotFound();
            if (c.Status != CaseStatus.Draft) return Result<SaveCaseResponse>.Conflict("Only drafts can be edited.");

            if (string.IsNullOrEmpty(req.RowVersion)) errors["rowVersion"] = ["Required when updating."];
            if (errors.Count > 0) return Result<SaveCaseResponse>.Invalid(errors);

            try { db.Entry(c).Property(x => x.RowVersion).OriginalValue = Convert.FromBase64String(req.RowVersion!); }
            catch (FormatException) { return Result<SaveCaseResponse>.Invalid(new() { ["rowVersion"] = ["Malformed."] }); }

            db.CaseVersions.Add(CreateSnapshot(c, user));
            c.Revision++;
        }

        Apply(c, req);

        var attachmentError = await SyncAttachmentsAsync(c, req.AttachmentIds ?? [], user, ct);
        if (attachmentError is not null) return Result<SaveCaseResponse>.Invalid(new() { ["attachmentIds"] = [attachmentError] });

        c.UpdatedAt = DateTimeOffset.UtcNow;
        if (req.Submit)
        {
            c.Status = CaseStatus.Open;
            c.SubmittedAt = c.UpdatedAt;
        }

        var orphaned = db.ChangeTracker.Entries<CaseAttachment>()
            .Where(e => e.State == EntityState.Deleted).Select(e => e.Entity.StorageKey).ToList();

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return Result<SaveCaseResponse>.Conflict("This draft was changed elsewhere. Reload it and try again.");
        }

        // Remove blobs only after the database commit so a failed save never loses files.
        foreach (var key in orphaned)
        {
            try { await files.DeleteAsync(key, ct); }
            catch (Exception ex) { log.LogWarning(ex, "Could not delete stored file {Key}", key); }
        }

        return Result<SaveCaseResponse>.Ok(new(c.Id, c.CaseNumber, c.Status.ToString(),
            c.Revision, Convert.ToBase64String(c.RowVersion), c.UpdatedAt));
    }

    public async Task<Result<CaseDetail>> GetAsync(Guid id, CurrentUser user, CancellationToken ct)
    {
        var c = await db.Cases.AsNoTracking().Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == user.Id, ct);
        return c is null ? Result<CaseDetail>.NotFound() : Result<CaseDetail>.Ok(ToDetail(c));
    }

    public async Task<IReadOnlyList<CaseSummary>> ListAsync(CurrentUser user, CaseStatus? status, CancellationToken ct)
    {
        var q = db.Cases.AsNoTracking().Where(x => x.OwnerId == user.Id);
        if (status is not null) q = q.Where(x => x.Status == status);
        return await q.OrderByDescending(x => x.UpdatedAt).Take(200)
            .Select(x => new CaseSummary(x.Id, x.CaseNumber, x.Status.ToString(), x.Title, x.Code, x.Priority, x.DueDate, x.UpdatedAt, x.OwnerName))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AdminCaseSummary>> ListAllAsync(CaseStatus? status, CancellationToken ct)
    {
        var q = db.Cases.AsNoTracking();
        if (status is not null) q = q.Where(x => x.Status == status);
        return await q.OrderByDescending(x => x.CreatedAt).Take(1000)
            .Select(x => new AdminCaseSummary(
                x.Id,
                x.CaseNumber,
                x.Status.ToString(),
                x.Title,
                x.OwnerId,
                x.OwnerName,
                x.Code,
                x.Priority,
                x.DueDate,
                x.Revision,
                x.CreatedAt,
                x.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<Result<bool>> DeleteDraftAsync(Guid id, CurrentUser user, CancellationToken ct)
    {
        var c = await db.Cases.Include(x => x.Attachments).FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == user.Id, ct);
        if (c is null) return Result<bool>.NotFound();
        if (c.Status != CaseStatus.Draft) return Result<bool>.Conflict("Only drafts can be deleted.");

        var keys = c.Attachments.Select(a => a.StorageKey).ToList();
        db.Cases.Remove(c);
        await db.SaveChangesAsync(ct);
        foreach (var key in keys)
        {
            try { await files.DeleteAsync(key, ct); }
            catch (Exception ex) { log.LogWarning(ex, "Could not delete stored file {Key}", key); }
        }
        return Result<bool>.Ok(true);
    }

    public async Task<Result<IReadOnlyList<CaseVersionSummary>>> ListVersionsAsync(Guid id, CurrentUser user, CancellationToken ct)
    {
        var owned = await db.Cases.AsNoTracking().AnyAsync(x => x.Id == id && x.OwnerId == user.Id, ct);
        if (!owned) return Result<IReadOnlyList<CaseVersionSummary>>.NotFound();

        var rows = await db.CaseVersions.AsNoTracking()
            .Where(x => x.CaseId == id)
            .OrderByDescending(x => x.Revision)
            .ToListAsync(ct);

        IReadOnlyList<CaseVersionSummary> list = rows
            .Select(x => new CaseVersionSummary(x.CaseId, x.Revision, x.Status.ToString(), x.SavedAt, x.SavedBy))
            .ToList();
        return Result<IReadOnlyList<CaseVersionSummary>>.Ok(list);
    }

    public async Task<Result<CaseVersionDetail>> GetVersionAsync(Guid id, int revision, CurrentUser user, CancellationToken ct)
    {
        var owned = await db.Cases.AsNoTracking().AnyAsync(x => x.Id == id && x.OwnerId == user.Id, ct);
        if (!owned) return Result<CaseVersionDetail>.NotFound();

        var v = await db.CaseVersions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CaseId == id && x.Revision == revision, ct);
        if (v is null) return Result<CaseVersionDetail>.NotFound();

        var data = JsonSerializer.Deserialize<SaveCaseRequest>(v.DataJson, JsonOptions) ?? new SaveCaseRequest();
        var attachments = JsonSerializer.Deserialize<List<AttachmentDto>>(v.AttachmentsJson, JsonOptions) ?? [];
        return Result<CaseVersionDetail>.Ok(new CaseVersionDetail(
            v.CaseId, v.Revision, v.Status.ToString(), v.SavedAt, v.SavedBy, data, attachments));
    }

    public async Task<Result<PublishCaseFilesResponse>> PublishFilesAsync(Guid id, CurrentUser user, CancellationToken ct)
    {
        var c = await db.Cases.Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == user.Id, ct);
        if (c is null) return Result<PublishCaseFilesResponse>.NotFound();

        var alreadyPublished = await db.CaseFileVersions
            .Where(x => x.CaseId == c.Id && x.CaseRevision == c.Revision)
            .Select(x => x.AttachmentId)
            .ToListAsync(ct);
        var already = alreadyPublished.ToHashSet();

        foreach (var a in c.Attachments.Where(x => !already.Contains(x.Id)))
        {
            var stream = await files.OpenReadAsync(a.StorageKey, ct);
            if (stream is null)
                return Result<PublishCaseFilesResponse>.Conflict($"Attachment '{a.FileName}' storage payload was not found.");

            await using (stream)
            {
                var uploaded = await sharePoint.UploadCaseFileAsync(c, a, stream, ct);
                db.CaseFileVersions.Add(new CaseFileVersion
                {
                    Id = Guid.NewGuid(),
                    CaseId = c.Id,
                    AttachmentId = a.Id,
                    CaseRevision = c.Revision,
                    FileName = a.FileName,
                    SharePointItemId = uploaded.ItemId,
                    SharePointWebUrl = uploaded.WebUrl,
                    SharePointETag = uploaded.ETag,
                    SharePointVersion = uploaded.VersionLabel,
                    UploadedAt = DateTimeOffset.UtcNow,
                    UploadedBy = user.Id,
                });
            }
        }

        await db.SaveChangesAsync(ct);

        var rows = await db.CaseFileVersions.AsNoTracking()
            .Where(x => x.CaseId == c.Id && x.CaseRevision == c.Revision)
            .OrderBy(x => x.FileName)
            .Select(x => new SharePointFileVersionDto(
                x.Id, x.CaseId, x.AttachmentId, x.CaseRevision, x.FileName,
                x.SharePointItemId, x.SharePointWebUrl, x.SharePointETag, x.SharePointVersion, x.UploadedAt, x.UploadedBy))
            .ToListAsync(ct);

        return Result<PublishCaseFilesResponse>.Ok(new PublishCaseFilesResponse(c.Id, c.Revision, rows));
    }

    public async Task<Result<IReadOnlyList<SharePointFileVersionDto>>> ListFileVersionsAsync(Guid id, CurrentUser user, CancellationToken ct)
    {
        var owned = await db.Cases.AsNoTracking().AnyAsync(x => x.Id == id && x.OwnerId == user.Id, ct);
        if (!owned) return Result<IReadOnlyList<SharePointFileVersionDto>>.NotFound();

        var rows = await db.CaseFileVersions.AsNoTracking()
            .Where(x => x.CaseId == id)
            .OrderByDescending(x => x.CaseRevision)
            .ThenBy(x => x.FileName)
            .Select(x => new SharePointFileVersionDto(
                x.Id, x.CaseId, x.AttachmentId, x.CaseRevision, x.FileName,
                x.SharePointItemId, x.SharePointWebUrl, x.SharePointETag, x.SharePointVersion, x.UploadedAt, x.UploadedBy))
            .ToListAsync(ct);

        return Result<IReadOnlyList<SharePointFileVersionDto>>.Ok(rows);
    }

    // ---- helpers ----

    private async Task<string> NextCaseNumberAsync(CancellationToken ct)
    {
        using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT NEXT VALUE FOR dbo.CaseNumbers";
        cmd.CommandType = System.Data.CommandType.Text;
        await db.Database.OpenConnectionAsync(ct);
        using var transaction = await db.Database.BeginTransactionAsync(ct);
        cmd.Transaction = transaction.GetDbTransaction();
        try
        {
            var result = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            return $"PC-{DateTime.UtcNow.Year}-{result:D4}";
        }
        finally { await transaction.RollbackAsync(ct); }
    }

    private static void Normalize(SaveCaseRequest r)
    {
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        r.Title = r.Title?.Trim() ?? "";
        r.RequestType = T(r.RequestType); r.WorkType = T(r.WorkType); r.RequestSource = T(r.RequestSource);
        r.Jurisdiction = T(r.Jurisdiction); r.Priority = T(r.Priority); r.SiteOfService = T(r.SiteOfService);
        r.Code = T(r.Code)?.ToUpperInvariant();
        r.CodeMode ??= "assigned";
        if (r.CodeMode == "pending") r.Code = null;
    }

    private static void Apply(PricingCase c, SaveCaseRequest r)
    {
        c.Title = r.Title;
        c.RequestType = r.RequestType; c.WorkType = r.WorkType; c.Priority = r.Priority ?? "Medium";
        c.RequestSource = r.RequestSource; c.DateReceived = r.DateReceived;
        c.CodeMode = r.CodeMode!; c.Code = r.Code; c.Descriptor = r.Descriptor; c.Parenthetical = r.Parenthetical;
        c.Manufacturer = r.Manufacturer; c.Product = r.Product; c.SiteOfService = r.SiteOfService;
        c.EffectiveDate = r.EffectiveDate; c.Jurisdiction = r.Jurisdiction; c.DueDate = r.DueDate;
        c.Assignee = r.Assignee; c.Urgent = r.Urgent;
        c.NotifyTeams = r.NotifyTeams; c.LockRouting = r.LockRouting;
        c.IssueSummary = r.IssueSummary; c.ComparatorCodes = r.ComparatorCodes; c.SpecialInstructions = r.SpecialInstructions;

        // Routing outcomes are derived here, never trusted from the browser.
        var rd = r.Routing ?? new RoutingDto();
        var taRequired = rd.NovelTechnology;
        var cmdRequired = rd.ClinicalJudgment || rd.MethodologyChanges;
        c.Routing = new RoutingPlan
        {
            ClinicalJudgment = rd.ClinicalJudgment, NovelTechnology = rd.NovelTechnology,
            MethodologyChanges = rd.MethodologyChanges, Rationale = rd.Rationale,
            TechnologyAssessmentRequired = taRequired, CmdReviewRequired = cmdRequired,
        };

        bool Applies(string name) => name switch
        {
            "Technology assessment" => taRequired,
            "Methodology approval" or "CMD review" => cmdRequired,
            _ => true,
        };

        c.WorkPortions.Clear();
        foreach (var p in (r.WorkPortions ?? []).OrderBy(p => p.Sequence))
        {
            var applicable = Applies(p.Name);
            c.WorkPortions.Add(new WorkPortion
            {
                Sequence = c.WorkPortions.Count + 1,
                Name = p.Name,
                Applicable = applicable,
                Team = applicable && !string.IsNullOrWhiteSpace(p.Team) ? p.Team : null,
                DueDate = applicable ? p.DueDate : null,
            });
        }
    }

    // Replaces the attached set with exactly the requested ids; only files the caller uploaded can be linked.
    private async Task<string?> SyncAttachmentsAsync(PricingCase c, List<Guid> ids, CurrentUser user, CancellationToken ct)
    {
        var wanted = ids.Distinct().ToHashSet();

        foreach (var existing in c.Attachments.Where(a => !wanted.Contains(a.Id)).ToList())
        {
            c.Attachments.Remove(existing);
            db.Attachments.Remove(existing);
        }

        var missing = wanted.Except(c.Attachments.Select(a => a.Id)).ToList();
        if (missing.Count == 0) return null;

        var candidates = await db.Attachments
            .Where(a => missing.Contains(a.Id) && a.UploadedBy == user.Id && a.CaseId == null)
            .ToListAsync(ct);
        if (candidates.Count != missing.Count) return "One or more attachments were not found or are already in use.";

        foreach (var a in candidates) c.Attachments.Add(a);
        return null;
    }

    private static PricingCaseVersion CreateSnapshot(PricingCase c, CurrentUser user) => new()
    {
        Id = Guid.NewGuid(),
        CaseId = c.Id,
        Revision = c.Revision,
        Status = c.Status,
        SavedAt = DateTimeOffset.UtcNow,
        SavedBy = user.Id,
        DataJson = JsonSerializer.Serialize(ToRequest(c), JsonOptions),
        AttachmentsJson = JsonSerializer.Serialize(
            c.Attachments.Select(a => new AttachmentDto(a.Id, a.FileName, a.Size, a.ContentType)).ToList(), JsonOptions),
    };

    private static SaveCaseRequest ToRequest(PricingCase c) => new()
    {
        Title = c.Title,
        RequestType = c.RequestType,
        WorkType = c.WorkType,
        Priority = c.Priority,
        RequestSource = c.RequestSource,
        DateReceived = c.DateReceived,
        CodeMode = c.CodeMode,
        Code = c.Code,
        Descriptor = c.Descriptor,
        Parenthetical = c.Parenthetical,
        Manufacturer = c.Manufacturer,
        Product = c.Product,
        SiteOfService = c.SiteOfService,
        EffectiveDate = c.EffectiveDate,
        Jurisdiction = c.Jurisdiction,
        DueDate = c.DueDate,
        Assignee = c.Assignee,
        Urgent = c.Urgent,
        Routing = new RoutingDto
        {
            ClinicalJudgment = c.Routing.ClinicalJudgment,
            NovelTechnology = c.Routing.NovelTechnology,
            MethodologyChanges = c.Routing.MethodologyChanges,
            Rationale = c.Routing.Rationale,
        },
        WorkPortions = c.WorkPortions.OrderBy(p => p.Sequence)
            .Select(p => new WorkPortionDto
            {
                Sequence = p.Sequence,
                Name = p.Name,
                Team = p.Team,
                DueDate = p.DueDate,
            }).ToList(),
        NotifyTeams = c.NotifyTeams,
        LockRouting = c.LockRouting,
        IssueSummary = c.IssueSummary,
        ComparatorCodes = c.ComparatorCodes,
        SpecialInstructions = c.SpecialInstructions,
        AttachmentIds = c.Attachments.Select(a => a.Id).ToList(),
    };

    private static CaseDetail ToDetail(PricingCase c) =>
        new(c.Id, c.CaseNumber, c.Status.ToString(), c.Revision, Convert.ToBase64String(c.RowVersion),
            c.CreatedAt, c.UpdatedAt, c.SubmittedAt, ToRequest(c),
            c.Attachments.Select(a => new AttachmentDto(a.Id, a.FileName, a.Size, a.ContentType)).ToList(), c.OwnerName);
}
