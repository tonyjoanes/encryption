using Azure.Storage.Blobs;
using EncryptionFunctionApp.Constants;
using EncryptionFunctionApp.Models;
using EncryptionFunctionApp.Services.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EncryptionFunctionApp.Functions;

public sealed class FileEncryptFunction
{
    private readonly IPgpEncryptionService _encryption;
    private readonly IBlobStorageService _blobStorage;
    private readonly IEventPublisherService _eventPublisher;
    private readonly ILogger<FileEncryptFunction> _logger;

    public FileEncryptFunction(
        IPgpEncryptionService encryption,
        IBlobStorageService blobStorage,
        IEventPublisherService eventPublisher,
        ILogger<FileEncryptFunction> logger)
    {
        _encryption = encryption;
        _blobStorage = blobStorage;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    [Function("FileEncrypt")]
    public async Task RunAsync(
        [BlobTrigger("incoming/{blobName}",
            Source = BlobTriggerSource.EventGrid,
            Connection = "StorageConnection")] BlobClient blobClient,
        string blobName,
        CancellationToken cancellationToken)
    {
        // Fetch metadata and content stream from the blob client.
        // BlobClient binding is used instead of Stream so we can access blob metadata.
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
        var metadata = properties.Value.Metadata;

        if (!TryParseMetadata(metadata, blobName, out var incomingMetadata))
            return;

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = incomingMetadata.CorrelationId,
            ["FileType"] = incomingMetadata.FileType,
        });

        _logger.LogInformation("Starting encryption for blob {BlobName}", blobName);

        await using var blobStream = await blobClient.OpenReadAsync(cancellationToken: cancellationToken);
        var encryptedStream = await _encryption.EncryptAsync(blobStream, cancellationToken);

        var blobPath = await _blobStorage.SaveEncryptedAsync(
            incomingMetadata.FileType,
            incomingMetadata.OriginalFileName,
            encryptedStream,
            cancellationToken);

        var evt = new FileUploadedEvent(
            FileType: incomingMetadata.FileType,
            BlobPath: blobPath,
            OriginalFileName: incomingMetadata.OriginalFileName,
            UploadedAt: DateTimeOffset.UtcNow,
            CorrelationId: incomingMetadata.CorrelationId);

        await _eventPublisher.PublishFileUploadedAsync(evt, cancellationToken);

        // Delete the staging blob only after all downstream steps succeed.
        // If any step above throws, the blob remains and Event Grid retries automatically.
        await _blobStorage.DeleteIncomingAsync(blobName, cancellationToken);

        _logger.LogInformation(
            "Encryption pipeline complete. BlobPath={BlobPath} CorrelationId={CorrelationId}",
            blobPath, incomingMetadata.CorrelationId);
    }

    private bool TryParseMetadata(
        IDictionary<string, string> metadata,
        string blobName,
        out IncomingBlobMetadata result)
    {
        result = default!;

        if (!metadata.TryGetValue("fileType", out var fileType) || !FileTypes.All.Contains(fileType))
        {
            _logger.LogError(
                "Blob {BlobName} has missing or unknown fileType metadata '{FileType}'. Skipping.",
                blobName, metadata.GetValueOrDefault("fileType"));
            return false;
        }

        if (!metadata.TryGetValue("originalFileName", out var originalFileName))
            originalFileName = blobName;

        if (!metadata.TryGetValue("correlationId", out var correlationIdStr) ||
            !Guid.TryParse(correlationIdStr, out var correlationId))
            correlationId = Guid.NewGuid();

        DateTimeOffset receivedAt = DateTimeOffset.UtcNow;
        if (metadata.TryGetValue("receivedAt", out var receivedAtStr))
            DateTimeOffset.TryParse(receivedAtStr, out receivedAt);

        result = new IncomingBlobMetadata(fileType, originalFileName, correlationId, receivedAt);
        return true;
    }
}
