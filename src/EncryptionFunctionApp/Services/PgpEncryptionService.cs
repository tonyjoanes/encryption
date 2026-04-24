using Azure.Security.KeyVault.Secrets;
using EncryptionFunctionApp.Services.Interfaces;
using Microsoft.Extensions.Logging;
using PgpCore;

namespace EncryptionFunctionApp.Services;

public sealed class PgpEncryptionService : IPgpEncryptionService
{
    private readonly EncryptionKeys _encryptionKeys;
    private readonly ILogger<PgpEncryptionService> _logger;

    public PgpEncryptionService(SecretClient secretClient, ILogger<PgpEncryptionService> logger)
    {
        _logger = logger;

        _logger.LogInformation("Loading PGP public key from Key Vault");

        var secret = secretClient.GetSecret("pgp-public-key");
        var publicKeyArmored = secret.Value.Value
            ?? throw new InvalidOperationException("Key Vault secret 'pgp-public-key' is empty.");

        using var keyStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(publicKeyArmored));
        _encryptionKeys = new EncryptionKeys(keyStream);

        _logger.LogInformation("PGP public key loaded successfully");
    }

    public async Task<Stream> EncryptAsync(Stream plaintext, CancellationToken cancellationToken = default)
    {
        // PGP instance is not thread-safe for some operations; create per call and reuse EncryptionKeys.
        var pgp = new PGP(_encryptionKeys);
        var outputStream = new MemoryStream();

        await pgp.EncryptStreamAsync(plaintext, outputStream);

        outputStream.Position = 0;
        return outputStream;
    }
}
