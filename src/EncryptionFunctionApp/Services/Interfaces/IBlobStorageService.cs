namespace EncryptionFunctionApp.Services.Interfaces;

public interface IBlobStorageService
{
    Task<string> SaveIncomingAsync(
        string fileType,
        string originalFileName,
        Guid correlationId,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<string> SaveEncryptedAsync(
        string fileType,
        string originalFileName,
        Stream encryptedContent,
        CancellationToken cancellationToken = default);

    Task DeleteIncomingAsync(string blobName, CancellationToken cancellationToken = default);
}
