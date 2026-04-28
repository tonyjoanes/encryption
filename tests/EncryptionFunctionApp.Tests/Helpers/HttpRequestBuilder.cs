using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Moq;
using System.Text;

namespace EncryptionFunctionApp.Tests.Helpers;

internal static class HttpRequestBuilder
{
    internal static HttpRequest Multipart(
        string? fileType = "members",
        string fileName = "data.csv",
        byte[]? content = null,
        bool includeFile = true,
        bool emptyFile = false)
    {
        var request = new Mock<HttpRequest>();
        request.SetupGet(r => r.HasFormContentType).Returns(true);

        var formFile = new Mock<IFormFile>();
        formFile.SetupGet(f => f.FileName).Returns(fileName);
        formFile.SetupGet(f => f.Length).Returns(
            emptyFile ? 0 : (content?.Length ?? 12));
        formFile.Setup(f => f.OpenReadStream())
            .Returns(new MemoryStream(content ?? Encoding.UTF8.GetBytes("id,name\n1,A")));

        var fileCollection = new Mock<IFormFileCollection>();
        fileCollection.Setup(c => c["file"])
            .Returns(includeFile ? formFile.Object : (IFormFile?)null);

        var form = new Mock<IFormCollection>();
        form.Setup(f => f["fileType"])
            .Returns(fileType is null ? StringValues.Empty : new StringValues(fileType));
        form.SetupGet(f => f.Files).Returns(fileCollection.Object);

        request.Setup(r => r.ReadFormAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(form.Object);

        return request.Object;
    }

    internal static HttpRequest NonForm()
    {
        var request = new Mock<HttpRequest>();
        request.SetupGet(r => r.HasFormContentType).Returns(false);
        return request.Object;
    }

    internal static HttpRequest WithThrowingForm()
    {
        var request = new Mock<HttpRequest>();
        request.SetupGet(r => r.HasFormContentType).Returns(true);
        request.Setup(r => r.ReadFormAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Form read failed"));
        return request.Object;
    }
}
