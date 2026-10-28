using Azure.Identity;
//using Azure.Storage.Blobs;
//using Azure.Storage.Blobs.Models;

namespace PricingApi.Storage;

public interface IFileStore
{
    Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct);
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

/// <summary>Development store. Files live outside wwwroot so they can never be served statically.</summary>
public sealed class LocalFileStore(IConfiguration config, IWebHostEnvironment env) : IFileStore
{
    private readonly string _root = Path.GetFullPath(
        config["Storage:LocalPath"] ?? Path.Combine(env.ContentRootPath, "App_Data", "uploads"));

    public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        var key = Guid.NewGuid().ToString("N");
        await using var fs = File.Create(Path.Combine(_root, key));
        await content.CopyToAsync(fs, ct);
        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? File.OpenRead(path) : null);
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    // Keys are server-generated GUIDs; GetFileName makes path traversal impossible regardless.
    private string PathFor(string key) => Path.Combine(_root, Path.GetFileName(key));
}

/// <summary>Production store. Uses managed identity (grant the app "Storage Blob Data Contributor").</summary>
//public sealed class BlobFileStore(IConfiguration config) : IFileStore
//{
//    private readonly BlobContainerClient _container = new(
//        new Uri(config["Storage:ContainerUri"] ?? throw new InvalidOperationException("Storage:ContainerUri is not configured.")),
//        new DefaultAzureCredential());

//    public async Task<string> SaveAsync(Stream content, string contentType, CancellationToken ct)
//    {
//        var key = Guid.NewGuid().ToString("N");
//        await _container.GetBlobClient(key).UploadAsync(content,
//            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);
//        return key;
//    }

//    public async Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
//    {
//        var blob = _container.GetBlobClient(key);
//        if (!await blob.ExistsAsync(ct)) return null;
//        return await blob.OpenReadAsync(cancellationToken: ct);
//    }

//    public Task DeleteAsync(string key, CancellationToken ct) =>
//        _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: ct);
//}
