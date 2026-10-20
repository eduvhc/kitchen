# StoringKit

Object storage for .NET 10 behind one contract. Code written against `IObjectStore` runs unchanged on Azure Blob Storage, AWS S3, Cloudflare R2 and MinIO, and both providers are held to the same behaviour by one shared test suite run against real emulators.

```csharp
var put = await store.PutAsync($"invoices/{id}.pdf", pdf, new PutObjectOptions
{
    ContentType = "application/pdf",
    Condition = WriteCondition.IfNotExists,     // create-only: a retried write cannot overwrite
});

if (put.Error is { Code: StoringErrors.AlreadyExists }) { /* someone got there first */ }

var link = await store.GetReadUrlAsync($"invoices/{id}.pdf", TimeSpan.FromMinutes(15));
```

## Packages

| Package | Contents |
| --- | --- |
| `StoringKit` | `IObjectStore`, the `ObjectStore` base for providers, errors, registration, traces and metrics. No cloud SDK. |
| `StoringKit.Azure` | Azure Blob Storage, on `Azure.Storage.Blobs` |
| `StoringKit.S3` | AWS S3 and S3-compatible stores (Cloudflare R2, MinIO), on `AWSSDK.S3` |

```bash
dotnet add package StoringKit.Azure     # or StoringKit.S3
```

A module that only stores and reads files references `StoringKit` alone and never restores a cloud SDK; the host picks the provider. Azure Blob Storage has no S3-compatible API, which is why it has its own provider rather than going through the AWS SDK or a proxy.

## The contract

| Operation | Returns | Notes |
| --- | --- | --- |
| `PutAsync(key, stream, options)` | `Result<PutReceipt>` | Streams from the current position. Content type, user metadata, MD5, write condition. |
| `GetAsync(key)` | `Result<ObjectContent>` | Properties plus a response stream; dispose it. |
| `GetPropertiesAsync(key)` | `Result<StoredObject>` | Size, content type, ETag, last modified, metadata, MD5. |
| `ExistsAsync(key)` | `Result<bool>` | A missing object is `false`, not an error. |
| `DeleteAsync(key)` | `Result` | Deleting a missing object succeeds, so retries are safe. |
| `ListAsync(prefix, options)` | `Result<ObjectPage>` | Flat listing in key order, up to 1000 per page, with a continuation token. |
| `GetReadUrlAsync(key, validFor)` | `Result<Uri>` | A time-limited link: an Azure SAS or an S3 presigned URL. Up to 7 days. |
| `GetPublicUrl(key)` | `Result<Uri>` | The permanent URL under `PublicBaseUrl`, for public stores. No request. |

### Results and errors

Expected failures come back as a failed `Result` carrying a ResultingKit `Error`. Branch on `Error.Code` — the codes are the same for every provider — or on `Error.Kind`.

| Code | Kind | When |
| --- | --- | --- |
| `storing.not_found` | `NotFound` | Get or properties of a missing object |
| `storing.already_exists` | `Conflict` | An `IfNotExists` write found an object there |
| `storing.precondition_failed` | `Conflict` | An `IfMatch` write found another version, or no object |
| `storing.invalid_key` | `Validation` | The key breaks the [key rules](#keys-and-metadata) |
| `storing.invalid_metadata` | `Validation` | Metadata breaks the rules |
| `storing.invalid_argument` | `Validation` | Content type, MD5 format, page size or URL lifetime out of range |
| `storing.checksum_mismatch` | `Validation` | The service received bytes that do not match the MD5 |
| `storing.too_large` | `Validation` | Bigger than the provider takes in one write |
| `storing.throttled` | `Transient` | 429, S3 `SlowDown`, Azure `ServerBusy` |
| `storing.unavailable` | `Transient` | 5xx, connection failures |
| `storing.timeout` | `Transient` | Request timeouts |

`Transient` is the only kind worth retrying. Both SDKs already retry internally, so a transient error here means their retries ran out.

A misconfigured store **throws** the SDK's exception instead — wrong credentials, a bucket or container that does not exist, a denied permission, a feature the endpoint does not implement (R2 answers `501 NotImplemented`). No retry or input change fixes those, so they surface like any other bug. Caller cancellation throws `OperationCanceledException`.

### Conditional writes

`WriteCondition.IfNotExists` (`If-None-Match: *`) and `WriteCondition.IfMatch(etag)` (`If-Match`) are checked atomically by the service, on every provider: of any number of concurrent create-only writes to one key, exactly one succeeds. They are what idempotent writes are built on — derive the key from the operation's identity, write create-only, and treat `storing.already_exists` as "already done".

ETags are opaque; pass back exactly what a `PutReceipt` or `StoredObject` gave you. Deletes are unconditional: R2 documents no conditional headers on `DeleteObject`.

### Checksums

Every put is verified end to end with `Content-MD5`, which Azure, S3, R2 and MinIO all check before committing. StoringKit computes the MD5 of seekable streams itself; pass `PutObjectOptions.ContentMd5` when you already have it (for example from a client upload). A mismatch is `storing.checksum_mismatch` and nothing is stored.

`StoredObject.ContentMd5` reports it back: Azure stores it as the blob's `Content-MD5`; S3 does not return `Content-MD5` after upload, so the S3 provider also keeps it in a `storingkit-md5` metadata entry, which is hidden from `Metadata`. Objects written by other tools — and Azure uploads from non-seekable streams without a supplied MD5 — may have none.

### Public and private access

Access belongs to the **store**, not to individual objects. Azure can make a container publicly readable, but S3 and R2 have no per-object public flag that works everywhere — R2 implements no ACLs at all, and new AWS buckets block public ACLs by default. So StoringKit never changes access settings: make the bucket or container public outside StoringKit, set `PublicBaseUrl`, and keep public and private objects in separate named stores.

| | Public store | Private store |
| --- | --- | --- |
| Azure | Container with blob-level public access; `PublicBaseUrl` = the container URL or a CDN in front of it | Default |
| AWS S3 | Public-read bucket policy or CloudFront; `PublicBaseUrl` = that origin | Default |
| R2 | `r2.dev` subdomain or a custom domain on the bucket; `PublicBaseUrl` = that domain | Default |
| MinIO | Anonymous-read bucket policy; `PublicBaseUrl` = `{endpoint}/{bucket}/` | Default |

For private objects, hand out `GetReadUrlAsync` links instead.

### Keys and metadata

The rules are the intersection of what every provider stores without rewriting:

- **Keys**: 1–1024 UTF-8 bytes; `/` separates segments; no leading, trailing or doubled `/`; no `.` or `..` segment; no trailing `.`; no `\` (Azure rewrites it to `/`) and no control characters. Keys are case-sensitive.
- **Metadata names**: letters, digits and `_`, not starting with a digit (Azure's rule); stored and returned lower-cased (S3's behaviour).
- **Metadata values**: non-empty printable ASCII without leading or trailing spaces.
- **Size**: names and values together at most 2000 bytes (S3 and R2 cap metadata at 2 KB).

## Providers

| | Azure Blob Storage | AWS S3 | Cloudflare R2 | MinIO |
| --- | --- | --- | --- | --- |
| Package | `StoringKit.Azure` | `StoringKit.S3` | `StoringKit.S3` | `StoringKit.S3` |
| Conditional writes | ✅ | ✅ | ✅ | ✅ |
| `Content-MD5` verification | ✅ | ✅ | ✅ | ✅ |
| Read URLs | SAS: account key or user delegation key | Presigned | Presigned | Presigned |
| Largest single put | Unlimited in practice — the SDK stages blocks | 5 GiB | 5 GiB | 5 GiB |
| Non-seekable streams | Streamed | Buffered to a temporary file first | Buffered | Buffered |
| Tested against | Azurite | — | — | MinIO |

The S3 provider sends each object in a single `PutObject`, which keeps conditional writes on the one call every S3-compatible store supports them on. Content above 5 GiB fails with `storing.too_large`.

### Cloudflare R2

R2 speaks the S3 API, with differences that matter here:

- **Checksums.** AWS SDKs released since December 2024 add CRC32 checksums to every upload and validate them on every download (`WHEN_SUPPORTED`). R2 and other S3-compatible stores reject them. Whenever `ServiceUrl` is not an `amazonaws.com` host, StoringKit sets `RequestChecksumCalculation` and `ResponseChecksumValidation` to `WHEN_REQUIRED`, and integrity rests on `Content-MD5`, which R2 checks.
- **Payload signing.** R2 does not implement streaming SigV4 (`aws-chunked`). For non-AWS endpoints StoringKit disables chunked encoding and, over HTTPS, payload signing.
- **No ACLs.** `x-amz-acl` and the ACL APIs are not implemented; see [public access](#public-and-private-access).
- **No conditional deletes documented, no object tagging, no lifecycle on put.** None are part of the contract.
- **Region** is `auto`.

## Registration

Each store has a name. The one called `Default` is also registered as a plain `IObjectStore`; the rest are keyed services. Options bind from `Storing:{name}` and are validated at startup, so a missing bucket name fails the host before it serves a request.

```json
{
  "Storing": {
    "Default": {
      "ConnectionString": "<from a secret store>",
      "ContainerName": "documents"
    },
    "Public": {
      "ConnectionString": "<from a secret store>",
      "ContainerName": "avatars",
      "PublicBaseUrl": "https://cdn.example.com/avatars/"
    }
  }
}
```

```csharp
builder.Services.AddAzureBlobStore(builder.Configuration);            // Storing:Default
builder.Services.AddAzureBlobStore(builder.Configuration, "Public");  // Storing:Public

public sealed class AvatarService(
    IObjectStore documents,
    [FromKeyedServices("Public")] IObjectStore avatars) { … }
```

### Azure

| Setting | Notes |
| --- | --- |
| `ConnectionString` | Account key, SAS, or `UseDevelopmentStorage=true` |
| `ServiceUri` + `Credential` | Instead of a connection string: `https://{account}.blob.core.windows.net` and a `TokenCredential` such as `DefaultAzureCredential`, set in code |
| `ContainerName` | Required; must exist |
| `PublicBaseUrl` | For public containers |

Read URLs are signed with the account key when the connection string has one, and with a cached user delegation key when authenticating with Microsoft Entra ID (the identity needs `Storage Blob Delegator` or equivalent). A SAS connection string cannot sign new URLs; `GetReadUrlAsync` then throws.

```csharp
builder.Services.AddAzureBlobStore(o =>
{
    o.ServiceUri = new Uri("https://myaccount.blob.core.windows.net");
    o.Credential = new DefaultAzureCredential();
    o.ContainerName = "documents";
});
```

### S3, R2 and MinIO

| Setting | AWS S3 | Cloudflare R2 | MinIO |
| --- | --- | --- | --- |
| `BucketName` | required | required | required |
| `Region` | `eu-west-1`, … | `auto` | optional (`us-east-1`) |
| `ServiceUrl` | unset | `https://{account}.r2.cloudflarestorage.com` | `http://minio:9000` |
| `ForcePathStyle` | `false` | `false` | `true` |
| `AccessKeyId` / `SecretAccessKey` | or unset for the SDK's default credential chain | R2 API token | access key |
| `PublicBaseUrl` | public origin | `r2.dev` or custom domain | `{endpoint}/{bucket}/` |

`Credentials` takes any `AWSCredentials` in code, such as an assumed role.

```csharp
builder.Services.AddS3Store(o =>
{
    o.BucketName = "documents";
    o.ServiceUrl = new Uri($"https://{accountId}.r2.cloudflarestorage.com");
    o.Region = "auto";
    o.AccessKeyId = builder.Configuration["R2:AccessKeyId"];
    o.SecretAccessKey = builder.Configuration["R2:SecretAccessKey"];
});
```

### Your own provider

Derive from `ObjectStore` — it validates keys, metadata and arguments, computes MD5s and turns the exceptions you classify into results — and register it with `AddObjectStore(name, "myprovider", sp => new MyStore(...))`. It is instrumented like the built-in ones.

## Observability

StoringKit traces and measures every store operation on its own, the way `HttpClient` and EF Core do: it uses only `ActivitySource` and `Meter` from the runtime, with no OpenTelemetry package. Nothing is recorded until the host listens:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(StoringKitInstrumentation.ActivitySourceName))
    .WithMetrics(m => m.AddMeter(StoringKitInstrumentation.MeterName));
```

Every store registered through `AddAzureBlobStore`, `AddS3Store` or `AddObjectStore` is wrapped automatically.

**Keys, content and metadata values are never recorded** — keys routinely carry user ids and file names. Exceptions are recorded by type only, since SDK messages can contain the request URL. OpenTelemetry's object-storage attributes are AWS-specific and carry the key, so the attributes use the `storingkit.` prefix, apart from the general `error.type`.

**Span** — `{operation} {store}` (`put Default`, `get_read_url Public`), kind `Client`, one per call. `GetPublicUrl` makes no request and has no span.

| Attribute | Value |
| --- | --- |
| `storingkit.store` | The store name |
| `storingkit.provider` | `azure_blob`, `s3`, or a custom provider's name |
| `storingkit.operation` | `put`, `get`, `get_properties`, `exists`, `delete`, `list`, `get_read_url` |
| `storingkit.outcome` | `success` or `failure` |
| `storingkit.write.condition` | `none`, `if_not_exists` or `if_match`, on puts |
| `storingkit.object.size` | Bytes, on successful puts, gets and properties |
| `storingkit.object.exists` | On `exists` |
| `storingkit.list.object_count` | Objects on the page, on `list` |
| `error.type` | The `Error.Code` (`storing.not_found`), or the exception's full type name |
| `storingkit.error.kind` | The `ErrorKind` in snake case: `not_found`, `conflict`, `transient`, … |

**Metrics** — on the `StoringKit` meter, created through the host's `IMeterFactory`:

| Instrument | Type | Unit | Attributes |
| --- | --- | --- | --- |
| `storingkit.client.operations` | Counter | `{operation}` | `storingkit.store`, `storingkit.provider`, `storingkit.operation`, `storingkit.outcome`, plus `error.type` and `storingkit.error.kind` on failure |
| `storingkit.client.operation.duration` | Histogram | `s` | Same as above |

A get is timed until the response headers arrive, not until the caller finishes reading the stream. The histogram advises OpenTelemetry's bucket boundaries for seconds (5 ms to 10 s).

## Testing

`StoringKit.IntegrationTests` runs one contract suite against Azurite (`TestingKit.Azurite`) and MinIO (`TestingKit.Minio`). Use the same fixtures for your own tests: point a store at the fixture's connection string or endpoint, and every behaviour above holds.
