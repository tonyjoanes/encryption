using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage.Blobs;
using Azure.Messaging.ServiceBus;
using EncryptionFunctionApp.Services;
using EncryptionFunctionApp.Services.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Single credential instance shared by all Azure SDK clients.
        // Works with developer tools locally (az login, VS) and Managed Identity in Azure.
        var credential = new DefaultAzureCredential();

        // Blob Storage — Managed Identity, no connection string.
        var storageUri = new Uri(
            context.Configuration["StorageConnection__serviceUri"]
            ?? throw new InvalidOperationException("StorageConnection__serviceUri is required."));
        services.AddSingleton(new BlobServiceClient(storageUri, credential));

        // Service Bus — Managed Identity, no connection string.
        var sbNamespace = context.Configuration["ServiceBusConnection__fullyQualifiedNamespace"]
            ?? throw new InvalidOperationException("ServiceBusConnection__fullyQualifiedNamespace is required.");
        services.AddSingleton(new ServiceBusClient(sbNamespace, credential));

        // Key Vault — PGP public key retrieved at startup by PgpEncryptionService.
        var keyVaultUri = context.Configuration["KeyVaultUri"]
            ?? throw new InvalidOperationException("KeyVaultUri is required.");
        services.AddSingleton(new SecretClient(new Uri(keyVaultUri), credential));

        // Application services — singletons: all are thread-safe and stateless after construction.
        services.AddSingleton<IPgpEncryptionService, PgpEncryptionService>();
        services.AddSingleton<IBlobStorageService, BlobStorageService>();
        services.AddSingleton<IEventPublisherService, EventPublisherService>();
    })
    .Build();

await host.RunAsync();
