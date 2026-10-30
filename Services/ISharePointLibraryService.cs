using PricingApi.Domain;

namespace PricingApi.Services;

public sealed record SharePointUploadResult(string ItemId, string WebUrl, string ETag, string VersionLabel);

public interface ISharePointLibraryService
{
    Task<SharePointUploadResult> UploadCaseFileAsync(
        PricingCase @case,
        CaseAttachment attachment,
        Stream content,
        CancellationToken ct);
}