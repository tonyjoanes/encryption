using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using EncryptionFunctionApp.Services;
using EncryptionFunctionApp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text;
using System.Text.RegularExpressions;

namespace EncryptionFunctionApp.Tests.Services;

public class BlobStorageServiceTests
{
    private readonly Mock<BlobServiceClient> _mockServiceClient;
    private readonly Mock<BlobContainerClient> _mockContainerClient;
    private readonly Mock<BlobClient> _mockBlobClient;
    private readonly BlobStorageService _sut;

    // Captured from mock callbacks so each test can assert on them.
    private string? _capturedContainerName;
    private string? _capturedBlobName;
    private BlobUploadOptions? _capturedUploadOptions;

    public BlobStorageServiceTests()
    {
        (_mockServiceClient, _mockContainerClient, _mockBlobClient) = AzureSdkMockFactory.CreateBlobChain();

        _mockServiceClient
            .Setup(c => c.GetBlobContainerClient(It.IsAny<string>()))
            .Callback<string>(name => _capturedContainerName = name)
            .Returns(_mockContainerClient.Object);

        _mockContainerClient
            .Setup(c => c.GetBlobClient(It.IsAny<string>()))
            .Callback<string>(name => _capturedBlobName = name)
            .Returns(_mockBlobClient.Object);

        _mockBlobClient
            .Setup(b => b.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<BlobUploadOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<Stream, BlobUploadOptions, CancellationToken>((_, opts, _) => _capturedUploadOptions = opts)
            .ReturnsAsync(Mock.Of<Azure.Response<BlobContentInfo>>());

        _sut = new BlobStorageService(_mockServiceClient.Object, NullLogger<BlobStorageService>.Instance);
    }

    // ── SaveIncomingAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task SaveIncomingAsync_UsesIncomingContainer()
    {
        await _sut.SaveIncomingAsync("members", "data.csv", Guid.NewGuid(), EmptyStream());

        _capturedContainerName.Should().Be("incoming");
    }

    [Fact]
    public async Task SaveIncomingAsync_BlobNameIncludesCorrelationId()
    {
        var correlationId = Guid.NewGuid();

        await _sut.SaveIncomingAsync("members", "data.csv", correlationId, EmptyStream());

        _capturedBlobName.Should().StartWith(correlationId.ToString());
    }

    [Fact]
    public async Task SaveIncomingAsync_BlobNameIncludesSanitizedFileName()
    {
        await _sut.SaveIncomingAsync("members", "data.csv", Guid.NewGuid(), EmptyStream());

        _capturedBlobName.Should().EndWith("data.csv");
    }

    [Fact]
    public async Task SaveIncomingAsync_ReturnsBlobName()
    {
        var correlationId = Guid.NewGuid();

        var result = await _sut.SaveIncomingAsync("members", "data.csv", correlationId, EmptyStream());

        result.Should().StartWith(correlationId.ToString());
    }

    [Fact]
    public async Task SaveIncomingAsync_SetsFileTypeMetadata()
    {
        await _sut.SaveIncomingAsync("addresses", "data.csv", Guid.NewGuid(), EmptyStream());

        _capturedUploadOptions!.Metadata.Should().ContainKey("fileType")
            .WhoseValue.Should().Be("addresses");
    }

    [Fact]
    public async Task SaveIncomingAsync_SetsOriginalFileNameMetadata()
    {
        await _sut.SaveIncomingAsync("members", "march-export.csv", Guid.NewGuid(), EmptyStream());

        _capturedUploadOptions!.Metadata.Should().ContainKey("originalFileName")
            .WhoseValue.Should().Be("march-export.csv");
    }

    [Fact]
    public async Task SaveIncomingAsync_SetsCorrelationIdMetadata()
    {
        var correlationId = Guid.NewGuid();

        await _sut.SaveIncomingAsync("members", "data.csv", correlationId, EmptyStream());

        _capturedUploadOptions!.Metadata.Should().ContainKey("correlationId")
            .WhoseValue.Should().Be(correlationId.ToString());
    }

    [Fact]
    public async Task SaveIncomingAsync_SetsReceivedAtMetadata_ParseableAsDateTimeOffset()
    {
        await _sut.SaveIncomingAsync("members", "data.csv", Guid.NewGuid(), EmptyStream());

        _capturedUploadOptions!.Metadata.Should().ContainKey("receivedAt");
        DateTimeOffset.TryParse(
            _capturedUploadOptions.Metadata["receivedAt"],
            out _).Should().BeTrue();
    }

    [Fact]
    public async Task SaveIncomingAsync_SetsCsvContentType()
    {
        await _sut.SaveIncomingAsync("members", "data.csv", Guid.NewGuid(), EmptyStream());

        _capturedUploadOptions!.HttpHeaders.ContentType.Should().Be("text/csv");
    }

    [Fact]
    public async Task SaveIncomingAsync_SanitizesPathTraversal()
    {
        await _sut.SaveIncomingAsync("members", "../../../etc/passwd", Guid.NewGuid(), EmptyStream());

        // Path.GetFileName strips the directory; remaining chars should be clean.
        _capturedBlobName.Should().NotContain("..");
        _capturedBlobName.Should().NotContain("/");
    }

    [Fact]
    public async Task SaveIncomingAsync_ReplacesSpacesWithUnderscore()
    {
        await _sut.SaveIncomingAsync("members", "my file.csv", Guid.NewGuid(), EmptyStream());

        // Sanitized name is the blob name suffix after the correlationId.
        _capturedBlobName.Should().Contain("my_file.csv");
    }

    [Fact]
    public async Task SaveIncomingAsync_PreservesDotsAndHyphens()
    {
        await _sut.SaveIncomingAsync("members", "my-data.csv", Guid.NewGuid(), EmptyStream());

        _capturedBlobName.Should().EndWith("my-data.csv");
    }

    // ── SaveEncryptedAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task SaveEncryptedAsync_UsesEncryptedContainerForFileType()
    {
        await _sut.SaveEncryptedAsync("members", "data.csv", EmptyStream());

        _capturedContainerName.Should().Be("encrypted-members");
    }

    [Fact]
    public async Task SaveEncryptedAsync_ContainerNameIsAlwaysLowercase()
    {
        await _sut.SaveEncryptedAsync("Members", "data.csv", EmptyStream());

        _capturedContainerName.Should().Be("encrypted-members");
    }

    [Fact]
    public async Task SaveEncryptedAsync_BlobNameEndsWithPgpExtension()
    {
        await _sut.SaveEncryptedAsync("members", "data.csv", EmptyStream());

        _capturedBlobName.Should().EndWith(".pgp");
    }

    [Fact]
    public async Task SaveEncryptedAsync_BlobNameHasTimestampPrefix()
    {
        await _sut.SaveEncryptedAsync("members", "data.csv", EmptyStream());

        // Pattern: yyyyMMddTHHmmssZ_{name}.pgp
        _capturedBlobName.Should().MatchRegex(@"^\d{8}T\d{6}Z_");
    }

    [Fact]
    public async Task SaveEncryptedAsync_SetsPgpContentType()
    {
        await _sut.SaveEncryptedAsync("members", "data.csv", EmptyStream());

        _capturedUploadOptions!.HttpHeaders.ContentType.Should().Be("application/pgp-encrypted");
    }

    [Fact]
    public async Task SaveEncryptedAsync_ReturnsBlobPath_ContainerSlashBlobName()
    {
        var result = await _sut.SaveEncryptedAsync("members", "data.csv", EmptyStream());

        result.Should().StartWith("encrypted-members/");
        result.Should().EndWith(".pgp");
    }

    [Fact]
    public async Task SaveEncryptedAsync_SanitizesSpacesInOriginalFileName()
    {
        await _sut.SaveEncryptedAsync("members", "my file.csv", EmptyStream());

        _capturedBlobName.Should().Contain("my_file.csv");
    }

    // ── DeleteIncomingAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task DeleteIncomingAsync_UsesIncomingContainer()
    {
        await _sut.DeleteIncomingAsync("some-blob");

        _capturedContainerName.Should().Be("incoming");
    }

    [Fact]
    public async Task DeleteIncomingAsync_CallsDeleteIfExistsOnCorrectBlob()
    {
        await _sut.DeleteIncomingAsync("target-blob-name");

        _capturedBlobName.Should().Be("target-blob-name");
        _mockBlobClient.Verify(
            b => b.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(),
                It.IsAny<BlobRequestConditions?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Stream EmptyStream() =>
        new MemoryStream(Encoding.UTF8.GetBytes("id,name\n1,Alice"));
}
