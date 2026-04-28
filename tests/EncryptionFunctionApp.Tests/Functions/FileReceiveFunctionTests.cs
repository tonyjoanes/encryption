using EncryptionFunctionApp.Functions;
using EncryptionFunctionApp.Services.Interfaces;
using EncryptionFunctionApp.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EncryptionFunctionApp.Tests.Functions;

public class FileReceiveFunctionTests
{
    private readonly Mock<IBlobStorageService> _mockBlobStorage;
    private readonly FileReceiveFunction _sut;

    public FileReceiveFunctionTests()
    {
        _mockBlobStorage = new Mock<IBlobStorageService>();
        _mockBlobStorage
            .Setup(b => b.SaveIncomingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("incoming/test-blob");

        _sut = new FileReceiveFunction(_mockBlobStorage.Object, NullLogger<FileReceiveFunction>.Instance);
    }

    // ── Validation: content type ─────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_NonFormContentType_Returns400()
    {
        var result = await _sut.RunAsync(HttpRequestBuilder.NonForm(), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RunAsync_NonFormContentType_NeverCallsBlobStorage()
    {
        await _sut.RunAsync(HttpRequestBuilder.NonForm(), CancellationToken.None);

        _mockBlobStorage.Verify(
            b => b.SaveIncomingAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(),
                                     It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Validation: fileType field ───────────────────────────────────────────

    [Fact]
    public async Task RunAsync_MissingFileType_Returns400()
    {
        var result = await _sut.RunAsync(HttpRequestBuilder.Multipart(fileType: null), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RunAsync_EmptyFileType_Returns400()
    {
        var result = await _sut.RunAsync(HttpRequestBuilder.Multipart(fileType: ""), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RunAsync_UnknownFileType_Returns400()
    {
        var result = await _sut.RunAsync(HttpRequestBuilder.Multipart(fileType: "invoices"), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
        ((BadRequestObjectResult)result).Value.As<string>().Should().Contain("fileType");
    }

    [Fact]
    public async Task RunAsync_UnknownFileType_ErrorMessageListsValidValues()
    {
        var result = (BadRequestObjectResult)await _sut.RunAsync(
            HttpRequestBuilder.Multipart(fileType: "unknown"), CancellationToken.None);

        result.Value.As<string>().Should().ContainAll("members", "addresses");
    }

    // ── Validation: file field ───────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_MissingFile_Returns400()
    {
        var result = await _sut.RunAsync(
            HttpRequestBuilder.Multipart(includeFile: false), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task RunAsync_EmptyFile_Returns400()
    {
        var result = await _sut.RunAsync(
            HttpRequestBuilder.Multipart(emptyFile: true), CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ValidRequest_Returns202()
    {
        var result = await _sut.RunAsync(
            HttpRequestBuilder.Multipart(fileType: "members"), CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task RunAsync_ValidRequest_ResponseContainsCorrelationId()
    {
        var result = (AcceptedResult)await _sut.RunAsync(
            HttpRequestBuilder.Multipart(fileType: "members"), CancellationToken.None);

        result.Value.Should().NotBeNull();
        var correlationId = result.Value!
            .GetType().GetProperty("correlationId")!
            .GetValue(result.Value);
        correlationId.Should().BeOfType<Guid>()
            .Which.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task RunAsync_ValidRequest_CallsSaveIncomingWithCorrectFileType()
    {
        await _sut.RunAsync(HttpRequestBuilder.Multipart(fileType: "addresses"), CancellationToken.None);

        _mockBlobStorage.Verify(
            b => b.SaveIncomingAsync(
                "addresses",
                It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunAsync_ValidRequest_PassesFileNameToBlobStorage()
    {
        await _sut.RunAsync(
            HttpRequestBuilder.Multipart(fileType: "members", fileName: "march-export.csv"),
            CancellationToken.None);

        _mockBlobStorage.Verify(
            b => b.SaveIncomingAsync(
                It.IsAny<string>(),
                "march-export.csv",
                It.IsAny<Guid>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("members")]
    [InlineData("MEMBERS")]
    public async Task RunAsync_KnownFileType_CaseInsensitive_Succeeds(string fileType)
    {
        var result = await _sut.RunAsync(
            HttpRequestBuilder.Multipart(fileType: fileType), CancellationToken.None);

        result.Should().BeOfType<AcceptedResult>();
    }

    // ── Error path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_ReadFormThrows_Returns400WithMessage()
    {
        var result = await _sut.RunAsync(HttpRequestBuilder.WithThrowingForm(), CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.As<string>().Should().Contain("parse");
    }

    [Fact]
    public async Task RunAsync_BlobStorageThrows_Returns500()
    {
        _mockBlobStorage
            .Setup(b => b.SaveIncomingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage unavailable"));

        var result = await _sut.RunAsync(
            HttpRequestBuilder.Multipart(fileType: "members"), CancellationToken.None);

        var obj = result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task RunAsync_BlobStorageThrows_DoesNotLeakExceptionDetails()
    {
        _mockBlobStorage
            .Setup(b => b.SaveIncomingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage unavailable"));

        var result = (ObjectResult)await _sut.RunAsync(
            HttpRequestBuilder.Multipart(fileType: "members"), CancellationToken.None);

        result.Value.As<string>().Should().NotContain("Storage unavailable");
    }
}
