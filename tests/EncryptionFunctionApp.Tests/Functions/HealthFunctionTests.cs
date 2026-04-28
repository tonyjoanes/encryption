using EncryptionFunctionApp.Functions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace EncryptionFunctionApp.Tests.Functions;

public class HealthFunctionTests
{
    private static readonly HttpRequest AnyRequest = new Mock<HttpRequest>().Object;

    [Fact]
    public void Run_ReturnsOkObjectResult()
    {
        var result = HealthFunction.Run(AnyRequest);

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public void Run_ResponseBody_HasHealthyStatus()
    {
        var result = (OkObjectResult)HealthFunction.Run(AnyRequest);

        result.Value.Should().BeEquivalentTo(new { status = "healthy" });
    }
}
