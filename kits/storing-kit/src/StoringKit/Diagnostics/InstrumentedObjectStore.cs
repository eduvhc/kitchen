using System.Diagnostics;
using ResultingKit;

namespace StoringKit.Diagnostics;

/// <summary>
/// Wraps a registered store so every operation emits a span and metrics, whichever provider it is. Keys, content and
/// metadata values are never recorded: keys routinely carry user ids and file names.
/// </summary>
internal sealed class InstrumentedObjectStore : IObjectStore, IAsyncDisposable, IDisposable
{
    private readonly IObjectStore _inner;
    private readonly StoringKitMetrics _metrics;

    public InstrumentedObjectStore(IObjectStore inner, string store, string provider, StoringKitMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(metrics);

        _inner = inner;
        _metrics = metrics;
        Store = store;
        Provider = provider;
    }

    public string Store { get; }

    public string Provider { get; }

    internal IObjectStore Inner => _inner;

    public Task<Result<PutReceipt>> PutAsync(string key, Stream content, PutObjectOptions? options = null, CancellationToken ct = default) =>
        RunAsync(
            ObjectStoreOperation.Put,
            () => _inner.PutAsync(key, content, options, ct),
            (activity, result) =>
            {
                activity.SetTag(StoringKitTags.WriteCondition, StoringKitTags.ToTagValue(options?.Condition));

                if (result.IsSuccess)
                {
                    activity.SetTag(StoringKitTags.ObjectSize, result.Value.Size);
                }
            });

    public Task<Result<ObjectContent>> GetAsync(string key, CancellationToken ct = default) =>
        RunAsync(
            ObjectStoreOperation.Get,
            () => _inner.GetAsync(key, ct),
            (activity, result) =>
            {
                if (result.IsSuccess)
                {
                    activity.SetTag(StoringKitTags.ObjectSize, result.Value.Properties.Size);
                }
            });

    public Task<Result<StoredObject>> GetPropertiesAsync(string key, CancellationToken ct = default) =>
        RunAsync(
            ObjectStoreOperation.GetProperties,
            () => _inner.GetPropertiesAsync(key, ct),
            (activity, result) =>
            {
                if (result.IsSuccess)
                {
                    activity.SetTag(StoringKitTags.ObjectSize, result.Value.Size);
                }
            });

    public Task<Result<bool>> ExistsAsync(string key, CancellationToken ct = default) =>
        RunAsync(
            ObjectStoreOperation.Exists,
            () => _inner.ExistsAsync(key, ct),
            (activity, result) =>
            {
                if (result.IsSuccess)
                {
                    activity.SetTag(StoringKitTags.ObjectExists, result.Value);
                }
            });

    public Task<Result> DeleteAsync(string key, CancellationToken ct = default) =>
        RunAsync(ObjectStoreOperation.Delete, () => _inner.DeleteAsync(key, ct), enrich: null);

    public Task<Result<ObjectPage>> ListAsync(string? prefix = null, ListObjectsOptions? options = null, CancellationToken ct = default) =>
        RunAsync(
            ObjectStoreOperation.List,
            () => _inner.ListAsync(prefix, options, ct),
            (activity, result) =>
            {
                if (result.IsSuccess)
                {
                    activity.SetTag(StoringKitTags.ListedCount, result.Value.Objects.Count);
                }
            });

    public Task<Result<Uri>> GetReadUrlAsync(string key, TimeSpan validFor, CancellationToken ct = default) =>
        RunAsync(ObjectStoreOperation.GetReadUrl, () => _inner.GetReadUrlAsync(key, validFor, ct), enrich: null);

    // Pure string work, no request: not worth a span.
    public Result<Uri> GetPublicUrl(string key) => _inner.GetPublicUrl(key);

    public void Dispose()
    {
        if (_inner is IDisposable disposable)
        {
            disposable.Dispose();
        }
        else if (_inner is IAsyncDisposable asyncDisposable)
        {
            asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_inner is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }

        (_inner as IDisposable)?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<TResult> RunAsync<TResult>(
        ObjectStoreOperation operation,
        Func<Task<TResult>> call,
        Action<Activity, TResult>? enrich)
        where TResult : Result
    {
        var operationName = StoringKitTags.ToTagValue(operation);
        var start = Stopwatch.GetTimestamp();

        using var activity = StoringKitInstrumentation.ActivitySource.StartActivity($"{operationName} {Store}", ActivityKind.Client);

        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(StoringKitTags.Store, Store);
            activity.SetTag(StoringKitTags.Provider, Provider);
            activity.SetTag(StoringKitTags.Operation, operationName);
        }

        TResult result;

        try
        {
            result = await call().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var errorType = ex.GetType().FullName ?? ex.GetType().Name;

            if (activity is not null)
            {
                activity.SetTag(StoringKitTags.Outcome, StoringKitTags.Failure);
                activity.SetTag(StoringKitTags.ErrorType, errorType);

                // No exception event: SDK messages can carry the request URL, and with it the key.
                activity.SetStatus(ActivityStatusCode.Error);
            }

            _metrics.Record(Store, Provider, operationName, StoringKitTags.Failure, errorType, errorKind: null, Stopwatch.GetElapsedTime(start));
            throw;
        }

        if (activity is { IsAllDataRequested: true })
        {
            enrich?.Invoke(activity, result);
        }

        if (result.IsSuccess)
        {
            activity?.SetTag(StoringKitTags.Outcome, StoringKitTags.Success);
            _metrics.Record(Store, Provider, operationName, StoringKitTags.Success, errorType: null, errorKind: null, Stopwatch.GetElapsedTime(start));
            return result;
        }

        var error = result.Error;
        var kind = StoringKitTags.ToTagValue(error.Kind);

        if (activity is not null)
        {
            activity.SetTag(StoringKitTags.Outcome, StoringKitTags.Failure);
            activity.SetTag(StoringKitTags.ErrorType, error.Code);
            activity.SetTag(StoringKitTags.ErrorKind, kind);

            // The code, not the message: messages name the key.
            activity.SetStatus(ActivityStatusCode.Error, error.Code);
        }

        _metrics.Record(Store, Provider, operationName, StoringKitTags.Failure, error.Code, kind, Stopwatch.GetElapsedTime(start));
        return result;
    }
}
