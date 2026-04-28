using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Moq;

namespace EncryptionFunctionApp.Tests.Helpers;

internal static class AzureSdkMockFactory
{
    /// <summary>
    /// Builds a wired BlobServiceClient → BlobContainerClient → BlobClient mock chain.
    /// The container client is returned for any container name.
    /// </summary>
    internal static (Mock<BlobServiceClient> ServiceClient,
                     Mock<BlobContainerClient> ContainerClient,
                     Mock<BlobClient> BlobClient)
        CreateBlobChain()
    {
        var mockBlobClient = new Mock<BlobClient>();
        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockServiceClient = new Mock<BlobServiceClient>();

        mockServiceClient
            .Setup(c => c.GetBlobContainerClient(It.IsAny<string>()))
            .Returns(mockContainerClient.Object);

        mockContainerClient
            .Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>?>(),
                It.IsAny<BlobContainerEncryptionScopeOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Response<BlobContainerInfo>?)null);

        mockContainerClient
            .Setup(c => c.GetBlobClient(It.IsAny<string>()))
            .Returns(mockBlobClient.Object);

        mockBlobClient
            .Setup(b => b.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<BlobUploadOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        var mockDeleteResponse = new Mock<Response<bool>>();
        mockDeleteResponse.SetupGet(r => r.Value).Returns(true);
        mockBlobClient
            .Setup(b => b.DeleteIfExistsAsync(
                It.IsAny<DeleteSnapshotsOption>(),
                It.IsAny<BlobRequestConditions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockDeleteResponse.Object);

        return (mockServiceClient, mockContainerClient, mockBlobClient);
    }

    /// <summary>
    /// Builds a mock BlobClient pre-configured with properties metadata and readable content stream.
    /// Used in FileEncryptFunctionTests where BlobClient is injected directly by the trigger binding.
    /// </summary>
    internal static Mock<BlobClient> CreateTriggeredBlobClient(
        IDictionary<string, string> metadata,
        byte[]? content = null)
    {
        var mock = new Mock<BlobClient>();

        var blobProperties = BlobsModelFactory.BlobProperties(metadata: metadata);
        mock.Setup(b => b.GetPropertiesAsync(
                It.IsAny<BlobRequestConditions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(blobProperties, Mock.Of<Response>()));

        mock.Setup(b => b.OpenReadAsync(
                It.IsAny<BlobOpenReadOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MemoryStream(content ?? "id,name\n1,Alice"u8.ToArray()));

        return mock;
    }

    /// <summary>
    /// Builds a SecretClient mock that returns the given armored PGP key for "pgp-public-key".
    /// </summary>
    internal static Mock<SecretClient> CreateSecretClientMock(string armoredPublicKey)
    {
        var mock = new Mock<SecretClient>();
        var secret = new KeyVaultSecret("pgp-public-key", armoredPublicKey);
        mock.Setup(c => c.GetSecret(
                "pgp-public-key",
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Response.FromValue(secret, Mock.Of<Response>()));
        return mock;
    }

    /// <summary>
    /// Builds a SecretClient mock whose GetSecret returns a secret with a null value,
    /// used to test the empty-key guard in PgpEncryptionService.
    /// </summary>
    internal static Mock<SecretClient> CreateSecretClientWithNullValue()
    {
        var mock = new Mock<SecretClient>();
        var mockResponse = new Mock<Response<KeyVaultSecret>>();
        // KeyVaultSecret.Value is the secret string; simulate an empty/null-value response
        // by returning a secret constructed with an empty string (closest public API).
        var secret = new KeyVaultSecret("pgp-public-key", "");
        mockResponse.SetupGet(r => r.Value).Returns(secret);
        mock.Setup(c => c.GetSecret(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(mockResponse.Object);
        return mock;
    }

    /// <summary>
    /// Builds a ServiceBusClient → ServiceBusSender mock chain.
    /// </summary>
    internal static (Mock<ServiceBusClient> Client, Mock<ServiceBusSender> Sender)
        CreateServiceBusChain(string queueName = "file-uploaded")
    {
        var mockSender = new Mock<ServiceBusSender>();
        mockSender
            .Setup(s => s.SendMessageAsync(
                It.IsAny<ServiceBusMessage>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mockClient = new Mock<ServiceBusClient>();
        mockClient
            .Setup(c => c.CreateSender(queueName))
            .Returns(mockSender.Object);

        return (mockClient, mockSender);
    }
}
