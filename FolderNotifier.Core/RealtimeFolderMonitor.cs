using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace FolderNotifier.Core;

public enum MonitorTrigger { Initial, FileSystemEvent, Reconciliation, Retry }
public sealed record MonitorUpdate(ScanResult Scan, bool IsRealtime, string? WatcherError, MonitorTrigger Trigger);

/// <summary>Filesystem events wake a serialized snapshot scan; periodic scans recover missed events.</summary>
public sealed class RealtimeFolderMonitor
{
    private readonly FolderMonitor snapshot;
    private readonly Func<Action, Action<Exception>, IDisposable> subscribe;
    private readonly TimeSpan reconciliationInterval;
    private readonly TimeSpan retryInterval;
    private readonly TimeSpan debounce;
    private int started;

    public RealtimeFolderMonitor(string folder, bool includeSubfolders,
        TimeSpan? reconciliationInterval = null, TimeSpan? retryInterval = null,
        TimeSpan? debounce = null, Func<Action, Action<Exception>, IDisposable>? subscribe = null)
    {
        snapshot = new FolderMonitor(folder, includeSubfolders);
        this.reconciliationInterval = Positive(reconciliationInterval ?? TimeSpan.FromSeconds(30));
        this.retryInterval = Positive(retryInterval ?? TimeSpan.FromSeconds(2));
        this.debounce = Positive(debounce ?? TimeSpan.FromMilliseconds(100));
        this.subscribe = subscribe ?? ((changed, failed) => new ChangeSubscription(folder, includeSubfolders, changed, failed));
    }

    public async IAsyncEnumerable<MonitorUpdate> WatchAsync([EnumeratorCancellation] CancellationToken token = default)
    {
        if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("감시 인스턴스는 한 번만 실행할 수 있습니다.");
        var signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = false
        });
        IDisposable? subscription = null;
        Exception? failure = null;
        string? watcherError = null;
        var generation = 0;
        var trigger = MonitorTrigger.Initial;
        void Unsubscribe()
        {
            Interlocked.Increment(ref generation);
            subscription?.Dispose();
            subscription = null;
        }
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var reportedFailure = Interlocked.Exchange(ref failure, null);
                if (reportedFailure is not null)
                {
                    watcherError = reportedFailure.Message;
                    Unsubscribe();
                }
                if (subscription is null)
                {
                    try
                    {
                        var currentGeneration = Interlocked.Increment(ref generation);
                        subscription = subscribe(
                            () => { if (Volatile.Read(ref generation) == currentGeneration) signals.Writer.TryWrite(true); },
                            error =>
                            {
                                if (Volatile.Read(ref generation) != currentGeneration) return;
                                Interlocked.Exchange(ref failure, error);
                                signals.Writer.TryWrite(true);
                            });
                        watcherError = null;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                        or NotSupportedException or System.Security.SecurityException)
                    {
                        watcherError = ex.Message;
                    }
                }
                // Arm the watcher before establishing a baseline, avoiding a scan-to-watch gap.
                var scan = await Task.Run(() => snapshot.Scan(token), token).WaitAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (scan.Error is not null) Unsubscribe();
                yield return new MonitorUpdate(scan, subscription is not null, watcherError, trigger);

                var interval = subscription is null ? retryInterval : reconciliationInterval;
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
                wait.CancelAfter(interval);
                var signaled = false;
                try { signaled = await signals.Reader.WaitToReadAsync(wait.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
                token.ThrowIfCancellationRequested();
                if (signaled)
                {
                    while (signals.Reader.TryRead(out _)) { }
                    await Task.Delay(debounce, token).ConfigureAwait(false);
                    while (signals.Reader.TryRead(out _)) { }
                    trigger = MonitorTrigger.FileSystemEvent;
                }
                else trigger = subscription is null ? MonitorTrigger.Retry : MonitorTrigger.Reconciliation;
            }
        }
        finally { Unsubscribe(); signals.Writer.TryComplete(); }
    }

    private static TimeSpan Positive(TimeSpan interval) => interval > TimeSpan.Zero
        ? interval : throw new ArgumentOutOfRangeException(nameof(interval));

    private sealed class ChangeSubscription : IDisposable
    {
        private readonly FileSystemWatcher watcher;
        public ChangeSubscription(string folder, bool recursive, Action changed, Action<Exception> failed)
        {
            watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                InternalBufferSize = 16 * 1024
            };
            watcher.Created += (_, _) => changed();
            watcher.Deleted += (_, _) => changed();
            watcher.Renamed += (_, _) => changed();
            watcher.Error += (_, e) => failed(e.GetException());
            try { watcher.EnableRaisingEvents = true; }
            catch { watcher.Dispose(); throw; }
        }
        public void Dispose() => watcher.Dispose();
    }
}
