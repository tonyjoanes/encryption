namespace EncryptionFunctionApp.Models;

public sealed record FileUploadedEvent(
    string FileType,
    string BlobPath,
    string OriginalFileName,
    DateTimeOffset UploadedAt,
    Guid CorrelationId
);
