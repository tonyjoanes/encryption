namespace EncryptionFunctionApp.Services.Interfaces;

public interface IPgpEncryptionService
{
    Task<Stream> EncryptAsync(Stream plaintext, CancellationToken cancellationToken = default);
}
