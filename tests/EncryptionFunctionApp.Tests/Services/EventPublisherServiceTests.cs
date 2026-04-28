using Azure.Messaging.ServiceBus;
using EncryptionFunctionApp.Models;
using EncryptionFunctionApp.Services;
using EncryptionFunctionApp.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text.Json;

namespace EncryptionFunctionApp.Tests.Services;

public class EventPublisherServiceTests
{
    private const string QueueName = "file-uploaded";

    private readonly Mock<ServiceBusClient> _mockClient;
    private readonly Mock<ServiceBusSender> _mockSender;
    private readonly IConfiguration _configuration;
    private readonly EventPublisherService _sut;

    // Captured from mock callback — populated on each PublishFileUploadedAsync call.
    private ServiceBusMessage? _capturedMessage;

    public EventPublisherServiceTests()
    {
        (_mockClient, _mockSender) = AzureSdkMockFactory.CreateServiceBusChain(QueueName);

        _mockSender
            .Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Callback<ServiceBusMessage, CancellationToken>((msg, _) => _capturedMessage = msg)
            .Returns(Task.CompletedTask);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ServiceBusQueueName"] = QueueName })
            .Build();

        _sut = new EventPublisherService(_mockClient.Object, _configuration, NullLogger<EventPublisherService>.Instance);
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_CallsCreateSenderWithConfiguredQueueName()
    {
        _mockClient.Verify(c => c.CreateSender(QueueName), Times.Once);
    }

    [Fact]
    public void Constructor_MissingQueueNameConfig_ThrowsInvalidOperationException()
    {
        var emptyConfig = new ConfigurationBuilder().Build();

        var act = () => new EventPublisherService(
            _mockClient.Object, emptyConfig, NullLogger<EventPublisherService>.Instance);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ServiceBusQueueName*");
    }

    // ── Message content ──────────────────────────────────────────────────────

    [Fact]
    public async Task PublishFileUploadedAsync_Subject_IsFileDotUploadedDotFileType()
    {
        await _sut.PublishFileUploadedAsync(BuildEvent(fileType: "members"));

        _capturedMessage!.Subject.Should().Be("file-uploaded.members");
    }

    [Fact]
    public async Task PublishFileUploadedAsync_CorrelationId_MatchesEvent()
    {
        var correlationId = Guid.NewGuid();
        await _sut.PublishFileUploadedAsync(BuildEvent(correlationId: correlationId));

        _capturedMessage!.CorrelationId.Should().Be(correlationId.ToString());
    }

    [Fact]
    public async Task PublishFileUploadedAsync_ApplicationProperties_ContainsFileType()
    {
        await _sut.PublishFileUploadedAsync(BuildEvent(fileType: "addresses"));

        _capturedMessage!.ApplicationProperties["fileType"].Should().Be("addresses");
    }

    [Fact]
    public async Task PublishFileUploadedAsync_ApplicationProperties_ContainsBlobPath()
    {
        const string blobPath = "encrypted-members/blob.pgp";
        await _sut.PublishFileUploadedAsync(BuildEvent(blobPath: blobPath));

        _capturedMessage!.ApplicationProperties["blobPath"].Should().Be(blobPath);
    }

    [Fact]
    public async Task PublishFileUploadedAsync_ContentType_IsApplicationJson()
    {
        await _sut.PublishFileUploadedAsync(BuildEvent());

        _capturedMessage!.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task PublishFileUploadedAsync_Body_IsValidCamelCaseJson()
    {
        var correlationId = Guid.NewGuid();
        var evt = BuildEvent(
            fileType: "members",
            blobPath: "encrypted-members/blob.pgp",
            originalFileName: "data.csv",
            correlationId: correlationId);

        await _sut.PublishFileUploadedAsync(evt);

        using var doc = JsonDocument.Parse(_capturedMessage!.Body.ToString());
        var root = doc.RootElement;

        // camelCase property names confirm JsonNamingPolicy.CamelCase is applied.
        root.GetProperty("fileType").GetString().Should().Be("members");
        root.GetProperty("blobPath").GetString().Should().Be("encrypted-members/blob.pgp");
        root.GetProperty("originalFileName").GetString().Should().Be("data.csv");
        root.GetProperty("correlationId").GetString().Should().Be(correlationId.ToString());
        root.TryGetProperty("uploadedAt", out _).Should().BeTrue();
    }

    [Fact]
    public async Task PublishFileUploadedAsync_CallsSendMessageOnce()
    {
        await _sut.PublishFileUploadedAsync(BuildEvent());

        _mockSender.Verify(
            s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PublishFileUploadedAsync_SenderThrows_ExceptionPropagates()
    {
        _mockSender
            .Setup(s => s.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceBusException("Bus unavailable", ServiceBusFailureReason.ServiceCommunicationProblem));

        var act = async () => await _sut.PublishFileUploadedAsync(BuildEvent());

        await act.Should().ThrowAsync<ServiceBusException>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static FileUploadedEvent BuildEvent(
        string fileType = "members",
        string blobPath = "encrypted-members/blob.pgp",
        string originalFileName = "data.csv",
        Guid? correlationId = null) =>
        new(
            FileType: fileType,
            BlobPath: blobPath,
            OriginalFileName: originalFileName,
            UploadedAt: DateTimeOffset.UtcNow,
            CorrelationId: correlationId ?? Guid.NewGuid());
}
