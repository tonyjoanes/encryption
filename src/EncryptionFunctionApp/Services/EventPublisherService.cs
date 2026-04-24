using System.Text.Json;
using Azure.Messaging.ServiceBus;
using EncryptionFunctionApp.Models;
using EncryptionFunctionApp.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EncryptionFunctionApp.Services;

public sealed class EventPublisherService : IEventPublisherService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ServiceBusSender _sender;
    private readonly ILogger<EventPublisherService> _logger;

    public EventPublisherService(
        ServiceBusClient serviceBusClient,
        IConfiguration configuration,
        ILogger<EventPublisherService> logger)
    {
        _logger = logger;

        var queueName = configuration["ServiceBusQueueName"]
            ?? throw new InvalidOperationException("ServiceBusQueueName app setting is missing.");

        // ServiceBusSender is safe to reuse across calls.
        _sender = serviceBusClient.CreateSender(queueName);
    }

    public async Task PublishFileUploadedAsync(FileUploadedEvent evt, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(evt, SerializerOptions);

        var message = new ServiceBusMessage(json)
        {
            ContentType = "application/json",
            CorrelationId = evt.CorrelationId.ToString(),
            // Subject enables SQL filter subscriptions without body deserialisation.
            Subject = $"file-uploaded.{evt.FileType}",
            ApplicationProperties =
            {
                ["fileType"] = evt.FileType,
                ["blobPath"] = evt.BlobPath,
            },
        };

        await _sender.SendMessageAsync(message, cancellationToken);

        _logger.LogInformation(
            "Published file-uploaded event. FileType={FileType} BlobPath={BlobPath} CorrelationId={CorrelationId}",
            evt.FileType, evt.BlobPath, evt.CorrelationId);
    }
}
