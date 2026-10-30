using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

namespace EntraApi.Services
{
    public class SharePointOptions
    {
        public string TenantId { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public string DriveId { get; set; } = "";
        public string RootFolder { get; set; } = "PricingAttachments";
    }

    public record UploadedSharePointFile(
        string Id,
        string Name,
        string? WebUrl,
        long Size);

    public class SharePointGraphService
    {
        private readonly HttpClient _http;
        private readonly SharePointOptions _options;
        private readonly ClientSecretCredential _credential;

        private static readonly JsonSerializerOptions JsonOptions =
            new(JsonSerializerDefaults.Web);

        public SharePointGraphService(
            HttpClient http,
            IConfiguration configuration)
        {
            _http = http;

            _options = configuration
                .GetSection("SharePoint")
                .Get<SharePointOptions>()
                ?? throw new InvalidOperationException(
                    "SharePoint configuration is missing.");

            _credential = new ClientSecretCredential(
                _options.TenantId,
                _options.ClientId,
                _options.ClientSecret);
        }

        private async Task<HttpRequestMessage> CreateRequestAsync(
            HttpMethod method,
            string path)
        {
            var token = await _credential.GetTokenAsync(
                new TokenRequestContext(
                    ["https://graph.microsoft.com/.default"]));

            var request = new HttpRequestMessage(
                method,
                $"https://graph.microsoft.com/v1.0{path}");

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token.Token);

            return request;
        }

        private async Task<JsonDocument> SendJsonAsync(
            HttpMethod method,
            string path,
            object? body = null)
        {
            using var request = await CreateRequestAsync(method, path);

            if (body is not null)
            {
                request.Content = JsonContent.Create(
                    body, options: JsonOptions);
            }

            using var response = await _http.SendAsync(request);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            return JsonDocument.Parse(json);
        }

        // Gets an existing folder or creates it under a known parent.
        private async Task<string> EnsureFolderAsync(
            string parentId,
            string folderName)
        {
            var path =
                $"/drives/{Uri.EscapeDataString(_options.DriveId)}" +
                $"/items/{Uri.EscapeDataString(parentId)}/children" +
                "?$select=id,name,folder";

            using (var result = await SendJsonAsync(HttpMethod.Get, path))
            {
                var children = result.RootElement
                    .GetProperty("value");

                foreach (var child in children.EnumerateArray())
                {
                    if (child.GetProperty("name").GetString()
                        == folderName
                        && child.TryGetProperty("folder", out _))
                    {
                        return child.GetProperty("id").GetString()!;
                    }
                }
            }

            var createPath =
                $"/drives/{Uri.EscapeDataString(_options.DriveId)}" +
                $"/items/{Uri.EscapeDataString(parentId)}/children";

            try
            {
                using var result = await SendJsonAsync(
                    HttpMethod.Post,
                    createPath,
                    new
                    {
                        name = folderName,
                        folder = new { },
                        conflictBehavior = "fail"
                    });

                return result.RootElement
                    .GetProperty("id").GetString()!;
            }
            catch (HttpRequestException)
            {
                // Another request may have created the folder.
                // Recheck before treating this as a failure.
                using var result = await SendJsonAsync(
                    HttpMethod.Get, path);

                foreach (var child in result.RootElement
                             .GetProperty("value").EnumerateArray())
                {
                    if (child.GetProperty("name").GetString()
                        == folderName
                        && child.TryGetProperty("folder", out _))
                    {
                        return child.GetProperty("id").GetString()!;
                    }
                }

                throw;
            }
        }

        private async Task<string> GetRootIdAsync()
        {
            var path =
                $"/drives/{Uri.EscapeDataString(_options.DriveId)}/root";

            using var result = await SendJsonAsync(
                HttpMethod.Get, path);

            return result.RootElement.GetProperty("id").GetString()!;
        }

        public async Task<UploadedSharePointFile> UploadAsync(
            int requestId,
            int versionNumber,
            string fileName,
            Stream content,
            string? contentType)
        {
            // The root document library folder is identified by DriveId.
            var rootId = await GetRootIdAsync();

            var appFolder = await EnsureFolderAsync(
                rootId, _options.RootFolder);

            var requestFolder = await EnsureFolderAsync(
                appFolder, $"Request-{requestId}");

            var versionFolder = await EnsureFolderAsync(
                requestFolder, $"Version-{versionNumber}");

            // Give each uploaded file a unique name so a second upload
            // cannot overwrite a previous attachment.
            var safeName = Path.GetFileName(fileName);

            if (string.IsNullOrWhiteSpace(safeName))
                throw new ArgumentException("A file name is required.");

            var storedName =
                $"{Guid.NewGuid():N}_{safeName}";

            var uploadPath =
                $"/drives/{Uri.EscapeDataString(_options.DriveId)}" +
                $"/items/{Uri.EscapeDataString(versionFolder)}:" +
                $"/{Uri.EscapeDataString(storedName)}:/content";

            using var request = await CreateRequestAsync(
                HttpMethod.Put, uploadPath);

            request.Content = new StreamContent(content);

            request.Content.Headers.ContentType =
                new MediaTypeHeaderValue(
                    string.IsNullOrWhiteSpace(contentType)
                        ? "application/octet-stream"
                        : contentType);

            using var response = await _http.SendAsync(request);

            response.EnsureSuccessStatusCode();

            using var result = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync());

            var item = result.RootElement;

            return new UploadedSharePointFile(
                item.GetProperty("id").GetString()!,
                item.GetProperty("name").GetString()!,
                item.TryGetProperty("webUrl", out var url)
                    ? url.GetString()
                    : null,
                item.TryGetProperty("size", out var size)
                    ? size.GetInt64()
                    : 0);
        }
    }
}
