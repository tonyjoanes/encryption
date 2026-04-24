using EncryptionFunctionApp.Constants;
using EncryptionFunctionApp.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace EncryptionFunctionApp.Functions;

public sealed class FileReceiveFunction
{
    private readonly IBlobStorageService _blobStorage;
    private readonly ILogger<FileReceiveFunction> _logger;

    public FileReceiveFunction(IBlobStorageService blobStorage, ILogger<FileReceiveFunction> logger)
    {
        _blobStorage = blobStorage;
        _logger = logger;
    }

    [Function("FileReceive")]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "receive")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
        });

        _logger.LogInformation("File receive request started");

        if (!req.HasFormContentType)
        {
            _logger.LogWarning("Request is not multipart/form-data");
            return new BadRequestObjectResult("Request must be multipart/form-data.");
        }

        IFormCollection form;
        try
        {
            form = await req.ReadFormAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse form data");
            return new BadRequestObjectResult("Could not parse form data.");
        }

        var fileType = form["fileType"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(fileType) || !FileTypes.All.Contains(fileType))
        {
            _logger.LogWarning("Invalid fileType: {FileType}", fileType);
            return new BadRequestObjectResult(
                $"Missing or invalid 'fileType'. Valid values: {string.Join(", ", FileTypes.All)}");
        }

        var file = form.Files["file"];
        if (file is null || file.Length == 0)
        {
            _logger.LogWarning("No file or empty file provided");
            return new BadRequestObjectResult("A non-empty 'file' field is required.");
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var blobName = await _blobStorage.SaveIncomingAsync(
                fileType, file.FileName, correlationId, stream, cancellationToken);

            _logger.LogInformation(
                "File received and staged. FileType={FileType} BlobName={BlobName}",
                fileType, blobName);

            return new AcceptedResult(
                location: (string?)null,
                value: new { correlationId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save incoming file");
            return new ObjectResult("An error occurred processing your request.")
            {
                StatusCode = StatusCodes.Status500InternalServerError,
            };
        }
    }
}
