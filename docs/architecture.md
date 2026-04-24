# Architecture

## Overview

A two-stage Azure Function pipeline that receives CSV files, encrypts them with PGP, stores the encrypted output in Blob Storage, and publishes a metadata event to Service Bus for downstream consumers.

The receive and encrypt stages are deliberately decoupled: the HTTP caller gets a fast 202 response and the raw file is safe in blob storage even if the encryption step later fails.

---

## Architecture Diagram

```mermaid
flowchart TB
    Caller(["fa:fa-user Caller"])

    subgraph FuncApp["Azure Function App · Consumption Plan"]
        FR["FileReceiveFunction\nPOST /api/receive\n─────────────────\nValidates fileType + file\nSaves raw blob with metadata\nReturns 202 + correlationId"]
        FE["FileEncryptFunction\nBlob Trigger · Event Grid\n─────────────────\nReads blob metadata\nStreams through PGP encrypt\nSaves encrypted file\nPublishes event\nDeletes staging blob"]
        HF["HealthFunction\nGET /api/health"]
    end

    subgraph Storage["Azure Blob Storage"]
        INC[("incoming/\n─────────\nStaging area\nRaw CSV files\n+ metadata")]
        ENC[("encrypted-members/\nencrypted-addresses/\n...\n─────────\nPGP-encrypted\n.pgp files")]
    end

    KV[["Key Vault\n─────────────\npgp-public-key"]]
    EG{{"Event Grid\nSystem Topic\n─────────────\nBlobCreated filter\non incoming/"}}
    SB[("Service Bus Queue\nfile-uploaded\n─────────────\nfileType\nblobPath\ncorrelationId")]
    AI["Application\nInsights"]
    DS(["fa:fa-server Downstream\nConsumers"])

    Caller -->|"POST multipart\nx-functions-key"| FR
    FR -->|"202 { correlationId }"| Caller
    FR -->|"Save + set metadata\n(fileType, correlationId)"| INC

    INC -->|"BlobCreated event"| EG
    EG -->|"Trigger"| FE

    FE -->|"Get pgp-public-key\nManaged Identity"| KV
    FE -->|"OpenRead stream"| INC
    FE -->|"Upload .pgp"| ENC
    FE -->|"Delete staging blob"| INC
    FE -->|"Publish event\nManaged Identity"| SB

    SB -->|"SQL filter by fileType"| DS

    FR -. "traces + logs" .-> AI
    FE -. "traces + logs" .-> AI

    style FuncApp fill:#e8f4fd,stroke:#0078d4,stroke-width:2px
    style Storage fill:#fff4e6,stroke:#e36f1e,stroke-width:2px
    style KV fill:#f0f9f0,stroke:#107c10,stroke-width:2px
    style EG fill:#fdf4ff,stroke:#8764b8,stroke-width:2px
    style SB fill:#fff4e6,stroke:#e36f1e,stroke-width:2px
    style AI fill:#fef0f0,stroke:#c00000,stroke-width:2px
```

---

## Sequence Diagram

```mermaid
sequenceDiagram
    autonumber

    actor Caller
    participant FR as FileReceiveFunction<br/>(HTTP Trigger)
    participant INC as Blob Storage<br/>incoming/
    participant EG as Event Grid
    participant FE as FileEncryptFunction<br/>(Blob Trigger)
    participant KV as Key Vault
    participant ENC as Blob Storage<br/>encrypted-{type}/
    participant SB as Service Bus
    participant DS as Downstream<br/>Consumers

    Caller->>FR: POST /api/receive<br/>{ file: <csv>, fileType: "members" }<br/>x-functions-key: <api-key>

    FR->>FR: Validate fileType ∈ FileTypes.All<br/>Validate file present + non-empty<br/>Generate correlationId

    FR->>INC: Upload raw CSV blob<br/>Name: {correlationId}_{filename}<br/>Metadata: fileType, originalFileName,<br/>correlationId, receivedAt

    FR-->>Caller: 202 Accepted<br/>{ correlationId }

    Note over INC,EG: BlobCreated event fires automatically

    INC->>EG: BlobCreated<br/>Subject: .../incoming/{blob}
    EG->>FE: Trigger with BlobClient

    FE->>INC: GetProperties → read metadata<br/>(fileType, correlationId, originalFileName)

    Note over FE,KV: PGP key cached in singleton at startup

    FE->>KV: GetSecret("pgp-public-key")<br/>via Managed Identity

    FE->>INC: OpenReadStream

    FE->>FE: PgpEncryptionService.EncryptAsync<br/>(streaming, no full file in memory)

    FE->>ENC: Upload {timestamp}_{name}.pgp<br/>ContentType: application/pgp-encrypted

    FE->>SB: SendMessage<br/>Subject: "file-uploaded.members"<br/>Body: { fileType, blobPath,<br/>originalFileName, uploadedAt, correlationId }<br/>ApplicationProperties: fileType, blobPath

    FE->>INC: DeleteBlob (staging cleanup)

    SB-->>DS: Deliver message<br/>(SQL filter: fileType = 'members')

    Note over DS: Downstream reads encrypted file<br/>from blobPath using its own credentials
```

---

## Azure Resources

| Resource | SKU | Purpose |
|---|---|---|
| Function App | Consumption (Y1) | Hosts FileReceiveFunction, FileEncryptFunction, HealthFunction |
| App Service Plan | Dynamic | Scales to zero when idle |
| Storage Account | Standard LRS | `incoming/` staging container + `encrypted-{type}/` output containers |
| Key Vault | Standard, RBAC | Holds `pgp-public-key` secret; accessed via Managed Identity |
| Service Bus Namespace | Standard | `file-uploaded` queue with DLQ, 14-day TTL, 10 max deliveries |
| Event Grid System Topic | — | Scoped to Storage Account; routes `BlobCreated` events from `incoming/` |
| Application Insights | — | Distributed tracing; all logs include `CorrelationId` and `FileType` |
| Log Analytics Workspace | PerGB2018, 30d | Backend for Application Insights |

## Security (WAF)

All Azure resource connections use **Managed Identity** — no connection strings or secrets in app settings.

| Role | Assigned to | Scope |
|---|---|---|
| Storage Blob Data Contributor | Function App MI | Storage Account |
| Azure Service Bus Data Sender | Function App MI | Service Bus Namespace |
| Key Vault Secrets User | Function App MI | Key Vault |

## Extensibility

Adding a new file type (e.g. `invoices`) requires changes in exactly two places:

1. **`src/EncryptionFunctionApp/Constants/FileTypes.cs`** — add constant + include in `All` set
2. **`iac/main.bicepparam`** — add `"invoices"` to the `fileTypes` array

Re-deploying Bicep creates the `encrypted-invoices` container. No trigger, function, or Service Bus changes are needed.
