using PgpCore;
using System.Text;

namespace EncryptionFunctionApp.Tests.Fixtures;

/// <summary>
/// Generates a real RSA-1024 PGP key pair once per test class.
/// Used by PgpEncryptionServiceTests and FileEncryptFunctionTests to verify
/// actual encryption output without hitting Key Vault.
/// </summary>
public sealed class PgpKeyFixture
{
    public string PublicKeyArmored { get; }
    public string PrivateKeyArmored { get; }

    public PgpKeyFixture()
    {
        using var pubStream = new MemoryStream();
        using var privStream = new MemoryStream();

        // 1024-bit key is fine for tests — generates fast, not used in production.
        var pgp = new PGP();
        pgp.GenerateKey(pubStream, privStream, username: "test@example.com", strength: 1024);

        PublicKeyArmored = Encoding.UTF8.GetString(pubStream.ToArray());
        PrivateKeyArmored = Encoding.UTF8.GetString(privStream.ToArray());
    }
}
