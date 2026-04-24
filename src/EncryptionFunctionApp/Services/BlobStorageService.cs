using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EncryptionFunctionApp.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace EncryptionFunctionApp.Services;

public sealed class BlobStorageService : IBlobStorageService
{
    private const string IncomingContainer = "incoming";

    private readonly BlobServiceClient _blobServiceClient;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(BlobServiceClient blobServiceClient, ILogger<BlobStorageService> logger)
    {
        _blobServiceClient = blobServiceClient;
        _logger = logger;
    }

    public async Task<string> SaveIncomingAsync(
        string fileType,
        string originalFileName,
        Guid correlationId,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(IncomingContainer);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var safeName = SanitizeFileName(originalFileName);
        var blobName = $"{correlationId}_{safeName}";

        var blobClient = containerClient.GetBlobClient(blobName);

        var metadata = new Dictionary<string, string>
        {
            ["fileType"] = fileType,
            ["originalFileName"] = originalFileName,
            ["correlationId"] = correlationId.ToString(),
            ["receivedAt"] = DateTimeOffset.UtcNow.ToString("O"),
        };

        await blobClient.UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "text/csv" },
            Metadata = metadata,
        }, cancellationToken);

        _logger.LogInformation("Saved incoming blob {BlobName}", blobName);
        return blobName;
    }

    public async Task<string> SaveEncryptedAsync(
        string fileType,
        string originalFileName,
        Stream encryptedContent,
        CancellationToken cancellationToken = default)
    {
        // Container names must be lowercase alphanumeric + hyphens.
        var containerName = $"encrypted-{fileType.ToLowerInvariant()}";
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var safeName = SanitizeFileName(originalFileName);
        var blobName = $"{timestamp}_{safeName}.pgp";

        var blobClient = containerClient.GetBlobClient(blobName);

        await blobClient.UploadAsync(encryptedContent, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/pgp-encrypted" },
        }, cancellationToken);

        var blobPath = $"{containerName}/{blobName}";
        _logger.LogInformation("Saved encrypted blob {BlobPath}", blobPath);
        return blobPath;
    }

    public async Task DeleteIncomingAsync(string blobName, CancellationToken cancellationToken = default)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(IncomingContainer);
        await containerClient.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
        _logger.LogInformation("Deleted incoming blob {BlobName}", blobName);
    }

    private static string SanitizeFileName(string fileName)
    {
        // Strip directory traversal and keep just the filename portion, no extension changes.
        var name = Path.GetFileName(fileName);
        // Replace any characters unsafe for blob names with underscores.
        return string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_'));
    }
}
