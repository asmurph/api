using EntraApi.Services;
using PricingApi.Domain;

namespace PricingApi.Services;

public sealed class SharePointLibraryService(SharePointGraphService graph) : ISharePointLibraryService
{
    public async Task<SharePointUploadResult> UploadCaseFileAsync(
        PricingCase @case,
        CaseAttachment attachment,
        Stream content,
        CancellationToken ct)
    {
        var requestId = ToRequestId(@case.Id);

        var uploaded = await graph.UploadAsync(
            requestId,
            @case.Revision,
            attachment.FileName,
            content,
            attachment.ContentType);

        return new SharePointUploadResult(
            uploaded.Id,
            uploaded.WebUrl ?? "",
            "",
            @case.Revision.ToString());
    }

    private static int ToRequestId(Guid caseId)
    {
        var value = BitConverter.ToInt32(caseId.ToByteArray(), 0);
        if (value == int.MinValue) return 0;
        return Math.Abs(value);
    }
}