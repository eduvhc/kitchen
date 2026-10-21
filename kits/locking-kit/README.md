# LockingKit

Distributed locks for .NET 10 behind one contract. Code written against `IDistributedLock` runs unchanged on Azure blob leases, PostgreSQL advisory locks and an in-memory provider. One engine does the hard parts for every backend: waiting, renewal, loss detection, release and telemetry. A single contract suite and a chaos suite hold all three to the same behaviour against real containers.

```csharp
builder.Services.AddLocking().UseAzureBlobLeases(builder.Configuration);   // or .UsePostgres(...), .UseInMemory()

var acquired = await locks.TryAcquireAsync($"orders/{id}", ct);
if (acquired.IsFailure) return acquired.Error;          // locking.busy: someone else has it. Not an exception.

await using var handle = acquired.Value;
using var work = CancellationTokenSource.CreateLinkedTokenSource(ct, handle.LostToken);

await ShipAsync(order, handle.FencingToken, work.Token);  // stops if the lock is lost
```

## Packages

| Package | Contents |
| --- | --- |
| `LockingKit` | `IDistributedLock`, the engine, results, registration, traces and metrics, and the in-memory provider. No cloud SDK. |
| `LockingKit.Azure` | Renewable leases on guard blobs, on `Azure.Storage.Blobs` |
| `LockingKit.Postgres` | Session-level advisory locks on a dedicated connection, on `Npgsql` |

```bash
dotnet add package LockingKit.Azure     # or LockingKit.Postgres
```

A module that only takes locks references `LockingKit` alone, and the host picks the backend. Tests use `UseInMemory()`, which has the same expiry, renewal and fencing behaviour as a real lease backend.

## Quick start

```json
{
  "Locking": {
    "Default": {
      "ConnectionString": "<from a secret store>",
      "ContainerName": "locks"
    }
  }
}
```

```csharp
builder.Services.AddLocking().UseAzureBlobLeases(builder.Configuration);   // Locking:Default

public sealed class NightlyExport(IDistributedLock locks)
{
    public async Task RunAsync(CancellationToken ct)
    {
        // Wait up to 30 s for the lock, then give up with locking.timeout.
        var acquired = await locks.AcquireAsync("jobs/nightly-export", TimeSpan.FromSeconds(30), ct);
        if (acquired.IsFailure) return;

        await using var handle = acquired.Value;

        foreach (var batch in Batches())
        {
            if (handle.EnsureHeld().IsFailure) return;   // locking.lost: stop instead of working unprotected
            await ExportAsync(batch, handle.LostToken);
        }
    }
}
```

## The contract

| Member | Returns | Notes |
| --- | --- | --- |
| `TryAcquireAsync(key)` | `Result<ILockHandle>` | One attempt. `locking.busy` when it is held. |
| `AcquireAsync(key, timeout)` | `Result<ILockHandle>` | Waits with backoff. `TimeSpan.Zero` tries once; `Timeout.InfiniteTimeSpan` waits until `ct`. |
| `AcquireAsync(key)` | `Result<ILockHandle>` | Waits until `ct` is cancelled. |

The handle:

| Member | Meaning |
| --- | --- |
| `LostToken` | Cancelled once the lock is no longer held, because it was lost or because the handle was disposed. Link it into the protected work. |
| `IsLost`, `LossReason` | `Expired` (renewals failed until the deadline), `Revoked` (the backend says it is gone), `ConnectionLost` (PostgreSQL). Not set by a normal release. |
| `EnsureHeld()` | A `Result`: success while held, `locking.lost` once lost or released. Call it before each step that must not run unprotected. |
| `ThrowIfLost()` | The same check in cancellation style, for work that already stops on `OperationCanceledException`: throws `LockLostException`, or `ObjectDisposedException` after dispose. |
| `FencingToken` | Grows with every holder of the key, when the provider issues them. See [Guarantees](#guarantees). |
| `DisposeAsync()` | Stops renewal, then releases with a 5 s timeout. Idempotent and safe to call concurrently. |

A handle renews itself in the background. The `ct` you pass to acquire only cancels the acquisition, never the handle.

### Results and errors

| Code | Kind | When |
| --- | --- | --- |
| `locking.busy` | `Conflict` | Someone else holds the lock |
| `locking.timeout` | `Conflict` | It stayed held for the whole wait |
| `locking.invalid_key` | `Validation` | Empty, longer than 512 characters, or not valid Unicode |
| `locking.invalid_argument` | `Validation` | A negative timeout |
| `locking.unavailable` | `Transient` | The backend could not be reached, timed out, throttled or failed. The only kind worth retrying. |
| `locking.misconfigured` | `Failure` | Wrong credentials, a denied permission, a container or database that does not exist |
| `locking.backend_failed` | `Failure` | Any other backend failure; the message names the exception |
| `locking.disposed` | `Failure` | The provider was disposed (the host is shutting down) |
| `locking.lost` | `Conflict` | From `EnsureHeld()`: the lock was lost or released |

A wait that never reached the backend ends in `locking.unavailable`, not `locking.timeout`, so an outage is not mistaken for contention. Every outcome is a result, never an exception: misconfiguration and unexpected backend failures included, and a waiter stops at the first failure no retry can fix. The one exception is cancellation: cancelling your `ct` throws `OperationCanceledException`, as it does throughout .NET. Locks are not reentrant: acquiring a key your own process holds is `locking.busy`.

### Keys

A key is any string of 1–512 characters, compared ordinally (case-sensitive). Each provider maps keys safely, and two different keys never share a lock:

- **Azure**: letters, digits, `-`, `_`, `.` and `/` pass through, so `orders/42` is the blob `locks/orders/42`. Every other character, and `~`, becomes `~XX` per UTF-8 byte. So do `/` and `.` in places Azure would rewrite: a leading, trailing or doubled `/`, and a `.` that starts a segment or ends the key. A name over Azure's 1024 characters or 254 segments becomes `locks/~sha256~{hex}`.
- **PostgreSQL**: the advisory key is the first 8 bytes of the SHA-256 of the UTF-8 key, read as a big-endian `bigint`. The mapping is stable and documented, so SQL can take the same lock: `pg_try_advisory_lock(('x' || left(encode(sha256(convert_to($1, 'UTF8')), 'hex'), 16))::bit(64)::bigint)`, available as `PostgresLockKeys.SqlExpression`. Two keys collide with negligible probability, and a collision only makes them exclude each other.

### Waiting

`AcquireAsync` retries with capped exponential backoff and equal jitter: 50 ms doubling to 1 s by default (`MinRetryDelay`, `MaxRetryDelay`). The cap makes waiting fair across processes, because a caller that has waited a minute polls as often as one that just arrived. Within one process, waiters for a key queue first come, first served. Only the front one polls, so twenty local callers make one stream of requests, and a local release wakes the next one at once.

## Guarantees

A lease lock gives **mutual exclusion among holders that are running**. It cannot stop a process that has paused (GC, VM suspension, a stalled thread) from waking up and carrying on as if it still held the lock, because no client-side check can run while the process is paused. This is the [well-known limitation](https://martin.kleppmann.com/2016/02/08/how-to-do-distributed-locking.html) of every lease-based lock, not a gap in this one. LockingKit narrows the window as far as a client can and gives you the tool to close it:

- **The handle gives up early.** The deadline is measured on a monotonic clock from when the last successful renewal was *sent*. The backend started its lease no earlier than that, so the handle declares loss a safety margin (10% of the lease) before the backend could expire it. That holds as long as the client's clock rate stays within 10% of the backend's. Wall clocks do not need to agree.
- **A hung renewal cannot postpone the loss.** A separate timer enforces the deadline and fires `LostToken` on time even if the renewal request never returns.
- **Work stops instead of continuing unprotected**, provided you pass `LostToken` into it or call `ThrowIfLost()` between steps.
- **Fencing tokens close the remaining window.** Send `FencingToken` with every write to the protected resource, and have the resource refuse a token lower than the highest it has seen. A former holder that wakes from a pause is then turned away whatever the timing. Without fencing, a lock is good for efficiency (not doing work twice) and not for correctness.
- **A lease never protects writes to other resources by itself.** Holding the lock does not make writes to unrelated objects transactional. Use fencing, or the resource's own conditional writes, as StoringKit's `IfMatch` does for blobs.

Per provider:

| | In-memory | Azure blob leases | PostgreSQL advisory locks |
| --- | --- | --- | --- |
| Excludes | One registration in one process | Every process using the same container | Every session on the same database |
| How a lock is held | A lease, renewed every third of `LeaseDuration` | A blob lease (15–60 s), renewed every third of it | A session lock, checked every `HealthCheckInterval` |
| Crashed holder | Freed when the lease expires | Freed when the lease expires (≤ `LeaseDuration`) | Freed the moment the server ends its session |
| Frozen holder | Loses it at expiry; `LostToken` fires first | Loses it at expiry; `LostToken` fires a 10% margin first | Keeps it: no expiry. Safe, but blocks others until its connection closes |
| Holder's session killed or failed over | — | — | Freed at once. The holder learns at its next check, so **two holders can overlap for up to one `HealthCheckInterval`** |
| Fencing tokens | Always: a counter | Opt-in (`FencingTokens`): a counter in the guard's `lockingkit_fence` metadata, incremented under the lease. 2 extra requests per acquisition | Opt-in (`FencingTokens`): the 64-bit transaction id from `pg_current_xact_id()`, which only grows. PostgreSQL 13+, a writable primary, one transaction id per acquisition |
| Writes to the guard itself | — | Fenced by Azure: `GetAzureBlobLease().Conditions` carries the lease id | — |
| Clock assumption | — | Client clock rate within 10% of Azure's | None |
| Tested against | In-process | Azurite | PostgreSQL 17 |

PostgreSQL advisory locks are not replicated. After a failover every lock is gone, and every holder sees `ConnectionLost`. They need session pooling: PgBouncer in transaction or statement mode breaks them.

## Providers

### Azure

| Setting | Notes |
| --- | --- |
| `ConnectionString` | Account key, SAS, or `UseDevelopmentStorage=true` |
| `ServiceUri` + `Credential` | Instead of a connection string: a `TokenCredential` such as `DefaultAzureCredential`, set in code. Needs `Storage Blob Data Contributor`. |
| `ContainerName` | Required; must exist |
| `BlobPrefix` | Where guard blobs go. Default `locks/` |
| `LeaseDuration` | 15–60 s, default 30 s. A crashed holder's lock is free again within this. |
| `FencingTokens` | Default `false` |
| `RequestTimeout` | Per request, default 5 s, at most 2 retries, so an unreachable service fails fast instead of after the SDK's 100 s default |

Each key is a guard blob. A missing guard is created empty and create-only, and an existing guard's content and metadata are never touched (fencing adds one metadata entry). Writes to the guard itself can be fenced by Azure:

```csharp
var lease = handle.GetAzureBlobLease()!;                 // the guard BlobClient and lease id
await lease.Blob.UploadAsync(state, new BlobUploadOptions { Conditions = lease.Conditions }, ct);
```

### PostgreSQL

| Setting | Notes |
| --- | --- |
| `ConnectionString` | Npgsql connection string; LockingKit builds its own data source |
| `DataSource` | An existing `NpgsqlDataSource` instead, set in code; not disposed by LockingKit |
| `HealthCheckInterval` | Default 5 s. How late a holder can learn the server freed its lock. |
| `FencingTokens` | Default `false` |

Every held lock keeps one connection open, so size `Maximum Pool Size` for the locks a process holds at once. A connection whose unlock could not be confirmed is never returned to the pool. It is closed instead, which ends the session and frees the lock.

### Several backends

Each provider has a name. The one called `Default` is also a plain `IDistributedLock`, and the rest are keyed services, like StoringKit's named stores. Options bind from `Locking:{name}` and are validated at startup.

```csharp
builder.Services.AddLocking().UseAzureBlobLeases(builder.Configuration);          // Locking:Default
builder.Services.AddLocking("Jobs").UsePostgres(builder.Configuration);           // Locking:Jobs

public sealed class Scheduler(IDistributedLock locks, [FromKeyedServices("Jobs")] IDistributedLock jobLocks) { … }
```

### Your own backend

Implement `ILockBackend`: one attempt that returns an `ILockLease` or `null` when busy, plus `RenewAsync` and `ReleaseAsync` on the lease. Pick its `LeaseTiming` (`Expiring(duration)` or `ConnectionHeld(interval)`) and register it with `AddLocking(name).UseBackend<TOptions>("myprovider", (sp, options) => new MyBackend(...))`. The engine adds waiting, renewal, loss detection, release, results and telemetry exactly as for the built-in backends.

## Testing

`LockingKit.IntegrationTests` runs every provider against a real backend: Azurite via `TestingKit.Azurite`, PostgreSQL via `TestingKit.Postgres`, and the in-memory provider. Each "process" in a test is a separate engine with its own client and pool.

**Contract suite**, the same for all three providers: busy and non-reentrant; release lets the next holder in at once; idempotent dispose; waiting and timeouts; 80 acquisitions by 20 contenders across 4 processes, checked for overlap by an interval checker; distinct look-alike keys (`a/b`, `a~2Fb`, `a%2Fb`, `..`, Unicode, 512 characters); cancellation before, during and after an attempt; renewal over three or more lease periods; growing fencing tokens; FIFO order of local waiters.

**Chaos suite**:

| Fault | Expected, and asserted |
| --- | --- |
| Holder freezes (renewals stop and never return) | Lease providers: `LostToken` fires (`Expired`) **before** another process can take the lock, and the other process gets it only after the backend's expiry. PostgreSQL: the holder gives up (`ConnectionLost`), and the server keeps excluding others until its connection closes. |
| Backend unreachable (Azurite container paused) | `LostToken` fires between 85% and 100% of the lease, before Azure's expiry. After the outage another process takes the key. Attempts during the outage are `locking.unavailable` within the request timeout. |
| Database unreachable (PostgreSQL container paused) | `LostToken` (`ConnectionLost`) within two checks. After dispose the key is free again. |
| Connection killed (`pg_terminate_backend`) | The lock is free at once, and the old holder's `LostToken` fires within one check |
| Lease broken or guard deleted from outside | `Revoked` at the next renewal |
| Lock lost mid-work | The work loop stops via `LostToken`/`ThrowIfLost`, the successor's fencing token is larger, and a late write with the old token is refused |
| Dispose racing an in-flight renewal | The renewal is cancelled before the release goes out, it never reaches the backend after the release, and the key is free at once |

`LockingKit.UnitTests` drives the renewal scheduler, backoff, waiting room and disposal on a `FakeTimeProvider`. Every timing decision goes through `TimeProvider`, so tests can check, for example, that a 30 s lease is lost at exactly 27 s after the last renewal was sent, whether the renewal failed or never answered.

## Observability

LockingKit traces and measures itself the way `HttpClient` and EF Core do. It uses only `ActivitySource` and `Meter` from the runtime, with no OpenTelemetry package, and nothing is recorded until the host listens:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(LockingKitInstrumentation.ActivitySourceName))
    .WithMetrics(m => m.AddMeter(LockingKitInstrumentation.MeterName));
```

**Keys are never recorded**, because they routinely carry ids. Exceptions are recorded by type only. OpenTelemetry has no lock conventions, so attributes use the `lockingkit.` prefix, apart from the general `error.type`.

**Spans**: `try_acquire {name}`, `acquire {name}` and `release {name}`, of kind `Client`. Attributes: `lockingkit.lock` (the provider name), `lockingkit.provider` (`azure_blob`, `postgres`, `in_memory`), `lockingkit.operation`, `lockingkit.outcome` (`acquired`, `busy`, `timeout`, `cancelled`, `failure`), `lockingkit.acquire.attempts`, `lockingkit.fencing_token`, and `error.type` and `lockingkit.error.kind` on failures. Contention is an outcome, not an error: busy and timeout spans have no error status.

**Metrics**, on the `LockingKit` meter, created through the host's `IMeterFactory`:

| Instrument | Type | Unit | Attributes |
| --- | --- | --- | --- |
| `lockingkit.acquires` | Counter | `{acquire}` | `lockingkit.lock`, `lockingkit.provider`, `lockingkit.operation`, `lockingkit.outcome`, plus `error.type` and `lockingkit.error.kind` on failure |
| `lockingkit.acquire.duration` | Histogram | `s` | Same; includes the wait |
| `lockingkit.held` | UpDownCounter | `{lock}` | `lockingkit.lock`, `lockingkit.provider` |
| `lockingkit.hold.duration` | Histogram | `s` | Same; recorded when a lock is released or lost |
| `lockingkit.renewals` | Counter | `{renewal}` | Plus `lockingkit.outcome` (`success`, `failure`) |
| `lockingkit.losses` | Counter | `{loss}` | Plus `lockingkit.loss.reason` (`expired`, `revoked`, `connection_lost`) |
| `lockingkit.releases` | Counter | `{release}` | Plus `lockingkit.outcome`, and `error.type` on failure |
