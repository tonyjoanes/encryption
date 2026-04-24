namespace EncryptionFunctionApp.Models;

public sealed record IncomingBlobMetadata(
    string FileType,
    string OriginalFileName,
    Guid CorrelationId,
    DateTimeOffset ReceivedAt
);
