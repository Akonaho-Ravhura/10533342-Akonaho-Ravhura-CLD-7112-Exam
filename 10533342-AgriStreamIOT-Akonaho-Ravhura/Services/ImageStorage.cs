using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace _10533342_AgriStreamIOT_Akonaho_Ravhura.Services
{
    /// <summary>Stores drone images. Azure Blob Storage in production, a local folder as a fallback.</summary>
    public interface IImageStorage
    {
        /// <summary>Uploads an image and returns the blob name to store in the database.</summary>
        Task<string> UploadAsync(Stream content, string contentType, string fileName, CancellationToken ct = default);

        /// <summary>Opens an image for streaming to the browser, or null if it does not exist.</summary>
        Task<StoredImage?> OpenAsync(string blobName, CancellationToken ct = default);

        Task DeleteAsync(string blobName, CancellationToken ct = default);

        /// <summary>Shown on the admin dashboard so you can tell which storage is in use.</summary>
        string Description { get; }
    }

    public sealed record StoredImage(Stream Content, string ContentType);

    public static class ImageNames
    {
        public static readonly Dictionary<string, string> AllowedUploadTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
            [".gif"] = "image/gif"
        };

        /// <summary>Random, collision-free name grouped by date, e.g. 2026/11/06/3f2a...c1.png.</summary>
        public static string NewBlobName(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return $"{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid():N}{ext}";
        }
    }

    /// <summary>Private Azure Blob Storage container. Images are streamed through the app, never exposed publicly.</summary>
    public class BlobImageStorage : IImageStorage
    {
        private readonly BlobContainerClient _container;
        private bool _ensured;

        public BlobImageStorage(string connectionString, string containerName)
        {
            _container = new BlobContainerClient(connectionString, containerName);
        }

        public string Description => $"Azure Blob Storage ({_container.AccountName}/{_container.Name})";

        private async Task EnsureContainerAsync(CancellationToken ct)
        {
            if (_ensured) return;
            await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
            _ensured = true;
        }

        public async Task<string> UploadAsync(Stream content, string contentType, string fileName, CancellationToken ct = default)
        {
            await EnsureContainerAsync(ct);
            var name = ImageNames.NewBlobName(fileName);
            await _container.GetBlobClient(name).UploadAsync(content,
                new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } }, ct);
            return name;
        }

        public async Task<StoredImage?> OpenAsync(string blobName, CancellationToken ct = default)
        {
            var blob = _container.GetBlobClient(blobName);
            try
            {
                var result = await blob.DownloadStreamingAsync(cancellationToken: ct);
                return new StoredImage(result.Value.Content, result.Value.Details.ContentType ?? "application/octet-stream");
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task DeleteAsync(string blobName, CancellationToken ct = default) =>
            await _container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: ct);
    }

    /// <summary>Fallback when no blob connection string is configured: files under App_Data (not web-accessible).</summary>
    public class LocalImageStorage : IImageStorage
    {
        private readonly string _root;

        public LocalImageStorage(string root)
        {
            _root = root;
            Directory.CreateDirectory(_root);
        }

        public string Description => "Local folder (App_Data/drone-images). Configure ConnectionStrings:BlobStorage to use Azure.";

        private string PathFor(string blobName)
        {
            var full = Path.GetFullPath(Path.Combine(_root, blobName));
            if (!full.StartsWith(Path.GetFullPath(_root), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid image name.");
            return full;
        }

        public async Task<string> UploadAsync(Stream content, string contentType, string fileName, CancellationToken ct = default)
        {
            var name = ImageNames.NewBlobName(fileName);
            var path = PathFor(name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var file = File.Create(path);
            await content.CopyToAsync(file, ct);
            return name;
        }

        public Task<StoredImage?> OpenAsync(string blobName, CancellationToken ct = default)
        {
            var path = PathFor(blobName);
            if (!File.Exists(path)) return Task.FromResult<StoredImage?>(null);
            var ext = Path.GetExtension(path);
            var type = ext.Equals(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml"
                : ImageNames.AllowedUploadTypes.GetValueOrDefault(ext, "application/octet-stream");
            return Task.FromResult<StoredImage?>(new StoredImage(File.OpenRead(path), type));
        }

        public Task DeleteAsync(string blobName, CancellationToken ct = default)
        {
            var path = PathFor(blobName);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }
    }
}
