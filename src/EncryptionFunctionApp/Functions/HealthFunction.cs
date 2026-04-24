using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace EncryptionFunctionApp.Functions;

public static class HealthFunction
{
    [Function("Health")]
    public static IActionResult Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest req)
    {
        return new OkObjectResult(new { status = "healthy" });
    }
}
