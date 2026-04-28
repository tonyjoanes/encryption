using EncryptionFunctionApp.Services;
using EncryptionFunctionApp.Tests.Fixtures;
using EncryptionFunctionApp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PgpCore;
using System.Text;

namespace EncryptionFunctionApp.Tests.Services;

/// <summary>
/// Tests for PgpEncryptionService using a real PGP key pair generated once per class.
/// Validates actual encryption behaviour rather than mocking away the cryptography.
/// </summary>
public class PgpEncryptionServiceTests : IClassFixture<PgpKeyFixture>
{
    private readonly PgpKeyFixture _pgpKeys;
    private readonly PgpEncryptionService _sut;

    public PgpEncryptionServiceTests(PgpKeyFixture pgpKeys)
    {
        _pgpKeys = pgpKeys;

        var secretClient = AzureSdkMockFactory.CreateSecretClientMock(pgpKeys.PublicKeyArmored);
        _sut = new PgpEncryptionService(secretClient.Object, NullLogger<PgpEncryptionService>.Instance);
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_ValidKey_DoesNotThrow()
    {
        var secretClient = AzureSdkMockFactory.CreateSecretClientMock(_pgpKeys.PublicKeyArmored);

        var act = () => new PgpEncryptionService(
            secretClient.Object, NullLogger<PgpEncryptionService>.Instance);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_EmptyKeyValue_ThrowsInvalidOperationException()
    {
        var secretClient = AzureSdkMockFactory.CreateSecretClientWithNullValue();

        var act = () => new PgpEncryptionService(
            secretClient.Object, NullLogger<PgpEncryptionService>.Instance);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── EncryptAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EncryptAsync_ReturnsNonEmptyStream()
    {
        var plaintext = new MemoryStream("id,name\n1,Alice"u8.ToArray());

        var result = await _sut.EncryptAsync(plaintext);

        result.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task EncryptAsync_OutputStreamPositionedAtZero()
    {
        var plaintext = new MemoryStream("id,name\n1,Alice"u8.ToArray());

        var result = await _sut.EncryptAsync(plaintext);

        result.Position.Should().Be(0);
    }

    [Fact]
    public async Task EncryptAsync_OutputIsArmoredPgpMessage()
    {
        var plaintext = new MemoryStream("id,name\n1,Alice"u8.ToArray());

        var result = await _sut.EncryptAsync(plaintext);

        var text = Encoding.UTF8.GetString(((MemoryStream)result).ToArray());
        text.Should().StartWith("-----BEGIN PGP MESSAGE-----");
    }

    [Fact]
    public async Task EncryptAsync_OutputCanBeDecryptedWithPrivateKey()
    {
        const string original = "id,name\n1,Alice\n2,Bob";
        var plaintext = new MemoryStream(Encoding.UTF8.GetBytes(original));

        var encrypted = await _sut.EncryptAsync(plaintext);

        // Decrypt using the paired private key.
        using var privStream = new MemoryStream(Encoding.UTF8.GetBytes(_pgpKeys.PrivateKeyArmored));
        var decryptionKeys = new EncryptionKeys(privStream, passPhrase: null);
        var pgp = new PGP(decryptionKeys);

        using var decrypted = new MemoryStream();
        await pgp.DecryptStreamAsync(encrypted, decrypted);
        var decryptedText = Encoding.UTF8.GetString(decrypted.ToArray());

        decryptedText.Should().Be(original);
    }

    [Fact]
    public async Task EncryptAsync_EmptyPlaintext_StillProducesArmoredOutput()
    {
        var plaintext = new MemoryStream();

        var result = await _sut.EncryptAsync(plaintext);

        var text = Encoding.UTF8.GetString(((MemoryStream)result).ToArray());
        text.Should().StartWith("-----BEGIN PGP MESSAGE-----");
    }

    [Fact]
    public async Task EncryptAsync_ConcurrentCalls_AllSucceed()
    {
        // Validates that creating a new PGP instance per call with a shared EncryptionKeys is thread-safe.
        var tasks = Enumerable.Range(0, 5).Select(_ =>
            _sut.EncryptAsync(new MemoryStream("concurrent test"u8.ToArray())));

        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(s => s.Length.Should().BeGreaterThan(0));
    }
}
