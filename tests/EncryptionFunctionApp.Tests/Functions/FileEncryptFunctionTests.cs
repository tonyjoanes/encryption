using Azure.Storage.Blobs;
using EncryptionFunctionApp.Functions;
using EncryptionFunctionApp.Models;
using EncryptionFunctionApp.Services.Interfaces;
using EncryptionFunctionApp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EncryptionFunctionApp.Tests.Functions;

public class FileEncryptFunctionTests
{
    private readonly Mock<IPgpEncryptionService> _mockEncryption;
    private readonly Mock<IBlobStorageService> _mockBlobStorage;
    private readonly Mock<IEventPublisherService> _mockEventPublisher;
    private readonly FileEncryptFunction _sut;

    private static readonly Guid TestCorrelationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public FileEncryptFunctionTests()
    {
        _mockEncryption = new Mock<IPgpEncryptionService>();
        _mockEncryption
            .Setup(e => e.EncryptAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream("encrypted"u8.ToArray()));

        _mockBlobStorage = new Mock<IBlobStorageService>();
        _mockBlobStorage
            .Setup(b => b.SaveEncryptedAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("encrypted-members/20240101T000000Z_data.csv.pgp");
        _mockBlobStorage
            .Setup(b => b.DeleteIncomingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockEventPublisher = new Mock<IEventPublisherService>();
        _mockEventPublisher
            .Setup(p => p.PublishFileUploadedAsync(
                It.IsAny<FileUploadedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _sut = new FileEncryptFunction(
            _mockEncryption.Object,
            _mockBlobStorage.Object,
            _mockEventPublisher.Object,
            NullLogger<FileEncryptFunction>.Instance);
    }

    // ── Metadata validation (TryParseMetadata) ───────────────────────────────

    [Fact]
    public async Task RunAsync_MissingFileTypeMetadata_SkipsEntirePipeline()
    {
        var blobClient = AzureSdkMockFactory.CreateTriggeredBlobClient(new Dictionary<string, string>());

        await _sut.RunAsync(blobClient.Object, "test-blob", CancellationToken.None);

        _mockEncryption.Verify(
            e => e.EncryptAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockBlobStorage.Verify(
            b => b.DeleteIncomingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_UnknownFileType_SkipsEntirePipeline()
    {
        var blobClient = AzureSdkMockFactory.CreateTriggeredBlobClient(
            new Dictionary<string, string> { ["fileType"] = "invoices" });

        await _sut.RunAsync(blobClient.Object, "test-blob", CancellationToken.None);

        _mockEncryption.Verify(
            e => e.EncryptAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_FileType_CaseInsensitive_Proceeds()
    {
        var blobClient = AzureSdkMockFactory.CreateTriggeredBlobClient(ValidMetadata(fileType: "MEMBERS"));

        await _sut.RunAsync(blobClient.Object, "test-blob", CancellationToken.None);

        _mockEncryption.Verify(
            e => e.EncryptAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_MissingOriginalFileName_UsesBlobName()
    {
        var metadata = new Dictionary<string, string>
        {
            ["fileType"] = "members",
            ["correlationId"] = TestCorrelationId.ToString(),
        };
        var blobClient = AzureSdkMockFactory.CreateTriggeredBlobClient(metadata);

        await _sut.RunAsync(blobClient.Object, "my-blob-name", CancellationToken.None);

        _mockBlobStorage.Verify(
            b => b.SaveEncryptedAsync(
                It.IsAny<string>(), "my-blob-name",
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_MissingCorrelationId_StillProcesses()
    {
        var metadata = new Dictionary<string, string>
        {
            ["fileType"] = "members",
            ["originalFileName"] = "data.csv",
        };
        var blobClient = AzureSdkMockFactory.CreateTriggeredBlobClient(metadata);

        await _sut.RunAsync(blobClient.Object, "test-blob", CancellationToken.None);

        _mockEventPublisher.Verify(
            p => p.PublishFileUploadedAsync(
                It.Is<FileUploadedEvent>(e => e.CorrelationId != Guid.Empty),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_InvalidCorrelationId_GeneratesNewGuid()
    {
        var metadata = new Dictionary<string, string>
        {
            ["fileType"] = "members",
            ["correlationId"] = "not-a-guid",
        };
        var blobClient = AzureSdkMockFactory.CreateTriggeredBlobClient(metadata);

        await _sut.RunAsync(blobClient.Object, "test-blob", CancellationToken.None);

        _mockEventPublisher.Verify(
            p => p.PublishFileUploadedAsync(
                It.Is<FileUploadedEvent>(e => e.CorrelationId != Guid.Empty),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Happy path: pipeline contract ────────────────────────────────────────

    [Fact]
    public async Task RunAsync_HappyPath_CallsEncryptOnce()
    {
        var blobClient = AzureSdkMockFactory.CreateTriggeredBlobClient(ValidMetadata());

        await _sut.RunAsync(blobClient.Object, "test-blob", CancellationToken.None);

        _mockEncryption.Verify(
            e => e.EncryptAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_HappyPath_DeletesIncomingBlobWithCorrectName()
    {
        var blobClient = AzureSdkMockFactory.CreateTriggeredBlobClient(ValidMetadata());

        await _sut.RunAsync(blobClient.Object, "my-exact-blob", CancellationToken.None);

        _mockBlobStorage.Verify(
            b => b.DeleteIncomingAsync("my-exact-blob", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_HappyPath_PublishesEventWithCorrectFields()
    {
        const string expectedBlobPath = "encrypted-members/20240101T000000Z_data.csv.pgp";
        _mockBlobStorage
            .Setup(b => b.SaveEncryptedAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedBlobPath);

        var blobClient = AzureSdkMockFactory.CreateTriggeredBlobClient(ValidMetadata());

        FileUploadedEvent? capturedEvent = null;
        _mockEventPublisher
            .Setup(p => p.PublishFileUploadedAsync(It.IsAny<FileUploadedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<FileUploadedEvent, CancellationToken>((evt, _) => capturedEvent = evt)
            .Returns(Task.CompletedTask);

        await _sut.RunAsync(blobClient.Object, "test-blob", CancellationToken.None);

        capturedEvent.Should().NotBeNull();
        capturedEvent!.FileType.Should().Be("members");
        capturedEvent.BlobPath.Should().Be(expectedBlobPath);
        capturedEvent.OriginalFileName.Should().Be("data.csv");
        capturedEvent.CorrelationId.Should().Be(TestCorrelationId);
    }

    [Fact]
    public async Task RunAsync_HappyPath_PassesBlobPathFromSaveEncryptedToEvent()
    {
        const string returnedPath = "encrypted-addresses/ts_export.csv.pgp";
        _mockBlobStorage
            .Setup(b => b.SaveEncryptedAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(returnedPath);

        FileUploadedEvent? capturedEvent = null;
        _mockEventPublisher
            .Setup(p => p.PublishFileUploadedAsync(It.IsAny<FileUploadedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<FileUploadedEvent, CancellationToken>((evt, _) => capturedEvent = evt)
            .Returns(Task.CompletedTask);

        var blobClient = AzureSdkMockFactory.CreateTriggeredBlobClient(ValidMetadata(fileType: "addresses"));
        await _sut.RunAsync(blobClient.Object, "test-blob", CancellationToken.None);

        capturedEvent!.BlobPath.Should().Be(returnedPath);
    }

    // ── Pipeline ordering: delete only after full success ────────────────────

    [Fact]
    public async Task RunAsync_SaveEncryptedThrows_DeleteIncomingNotCalled()
    {
        _mockBlobStorage
            .Setup(b => b.SaveEncryptedAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage failed"));

        var blobClient = AzureSdkMockFactory.CreateTriggeredBlobClient(ValidMetadata());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RunAsync(blobClient.Object, "test-blob", CancellationToken.None));

        _mockBlobStorage.Verify(
            b => b.DeleteIncomingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_PublishEventThrows_DeleteIncomingNotCalled()
    {
        _mockEventPublisher
            .Setup(p => p.PublishFileUploadedAsync(
                It.IsAny<FileUploadedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Bus unavailable"));

        var blobClient = AzureSdkMockFactory.CreateTriggeredBlobClient(ValidMetadata());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RunAsync(blobClient.Object, "test-blob", CancellationToken.None));

        _mockBlobStorage.Verify(
            b => b.DeleteIncomingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_SaveEncryptedThrows_PublishEventNotCalled()
    {
        _mockBlobStorage
            .Setup(b => b.SaveEncryptedAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage failed"));

        var blobClient = AzureSdkMockFactory.CreateTriggeredBlobClient(ValidMetadata());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.RunAsync(blobClient.Object, "test-blob", CancellationToken.None));

        _mockEventPublisher.Verify(
            p => p.PublishFileUploadedAsync(
                It.IsAny<FileUploadedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Dictionary<string, string> ValidMetadata(string fileType = "members") =>
        new()
        {
            ["fileType"] = fileType,
            ["originalFileName"] = "data.csv",
            ["correlationId"] = TestCorrelationId.ToString(),
            ["receivedAt"] = DateTimeOffset.UtcNow.ToString("O"),
        };
}
