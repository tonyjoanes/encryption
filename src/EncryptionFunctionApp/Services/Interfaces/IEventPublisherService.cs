using EncryptionFunctionApp.Models;

namespace EncryptionFunctionApp.Services.Interfaces;

public interface IEventPublisherService
{
    Task PublishFileUploadedAsync(FileUploadedEvent evt, CancellationToken cancellationToken = default);
}
