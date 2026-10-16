using System.Diagnostics;
using FolderNotifier.Core;

internal static class RealtimeMonitorTests
{
    internal static async Task<int> RunAsync(string parent)
    {
        var passed = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            passed++;
            Console.WriteLine("PASS " + description);
        }
        var folder = Directory.CreateDirectory(Path.Combine(parent, "realtime")).FullName;
        File.WriteAllText(Path.Combine(folder, "baseline.txt"), "baseline");
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
        {
            var monitor = new RealtimeFolderMonitor(folder, true, reconciliationInterval: TimeSpan.FromMinutes(5));
            await using var updates = monitor.WatchAsync(timeout.Token).GetAsyncEnumerator();
            await updates.MoveNextAsync();
            Check(updates.Current is { IsRealtime: true, Trigger: MonitorTrigger.Initial, Scan.IsBaseline: true }
                && updates.Current.Scan.AddedFiles.Count == 0, "Native watcher starts with an existing-file baseline");
            var added = Path.Combine(folder, "event.txt");
            var timer = Stopwatch.StartNew();
            File.WriteAllText(added, "created");
            await updates.MoveNextAsync();
            Check(updates.Current.Trigger == MonitorTrigger.FileSystemEvent && updates.Current.Scan.AddedFiles.SequenceEqual([added])
                && timer.Elapsed < TimeSpan.FromSeconds(2), "Native creation event detects a file before the old polling interval");
            Console.WriteLine($"Native event latency: {timer.Elapsed.TotalMilliseconds:F0} ms");
            var renamed = Path.Combine(folder, "renamed-event.txt");
            File.Move(added, renamed);
            await UntilFile(updates, renamed);
            Check(updates.Current.Trigger == MonitorTrigger.FileSystemEvent, "Rename wakes an event scan and discovers the new path");
            var nested = Directory.CreateDirectory(Path.Combine(folder, "new-directory")).FullName;
            var nestedFile = Path.Combine(nested, "nested-event.txt");
            File.WriteAllText(nestedFile, "nested");
            await UntilFile(updates, nestedFile);
            Check(updates.Current.IsRealtime, "New nested directories and files are detected by recursive event watching");
            // Directory moves may raise only a directory event; the scan discovers its contents.
            var outside = Directory.CreateDirectory(Path.Combine(parent, "incoming")).FullName;
            File.WriteAllText(Path.Combine(outside, "moved.txt"), "moved directory");
            var movedDirectory = Path.Combine(folder, "moved-directory");
            Directory.Move(outside, movedDirectory);
            await UntilFile(updates, Path.Combine(movedDirectory, "moved.txt"));
            Check(updates.Current.Trigger == MonitorTrigger.FileSystemEvent, "Moving a populated directory into the watched tree detects its files");
        }

        var controlledFolder = Directory.CreateDirectory(Path.Combine(parent, "controlled-events")).FullName;
        var source = new ControlledSource();
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            var monitor = new RealtimeFolderMonitor(controlledFolder, false, reconciliationInterval: TimeSpan.FromMilliseconds(250),
                retryInterval: TimeSpan.FromMilliseconds(30), debounce: TimeSpan.FromMilliseconds(10), subscribe: source.Subscribe);
            await using var updates = monitor.WatchAsync(timeout.Token).GetAsyncEnumerator();
            await updates.MoveNextAsync();
            var batch = Enumerable.Range(0, 40).Select(i => Path.Combine(controlledFolder, $"batch-{i}.txt")).ToArray();
            foreach (var path in batch) { File.WriteAllText(path, "batch"); source.Changed!(); }
            await updates.MoveNextAsync();
            Check(updates.Current.Scan.AddedFiles.Count == 40, "Burst signals coalesce without losing any added files");
            source.Changed!(); source.Changed!();
            await updates.MoveNextAsync();
            Check(updates.Current.Scan.AddedFiles.Count == 0, "Duplicate events never duplicate file notifications");
            File.AppendAllText(batch[0], "modified");
            File.Delete(batch[1]);
            source.Changed!();
            await updates.MoveNextAsync();
            Check(updates.Current.Scan.AddedFiles.Count == 0, "Content edits and deletions do not create file-addition alerts");
            File.WriteAllText(batch[1], "recreated"); source.Changed!();
            await updates.MoveNextAsync();
            Check(updates.Current.Scan.AddedFiles.SequenceEqual([batch[1]]), "A recreated file alerts after its deletion was observed");
            var nested = Directory.CreateDirectory(Path.Combine(controlledFolder, "ignored-child")).FullName;
            File.WriteAllText(Path.Combine(nested, "ignored.txt"), "not recursive"); source.Changed!();
            await updates.MoveNextAsync();
            Check(updates.Current.Scan.AddedFiles.Count == 0, "Nonrecursive mode excludes child-directory files");
            var missed = Path.Combine(controlledFolder, "missed.txt");
            File.WriteAllText(missed, "no event delivered");
            await updates.MoveNextAsync();
            Check(updates.Current.Trigger == MonitorTrigger.Reconciliation && updates.Current.Scan.AddedFiles.SequenceEqual([missed]),
                "Periodic reconciliation recovers a deliberately omitted event");
            var overflow = Path.Combine(controlledFolder, "overflow.txt");
            File.WriteAllText(overflow, "overflow recovery");
            source.Failed!(new InternalBufferOverflowException("simulated overflow"));
            await updates.MoveNextAsync();
            Check(source.Created == 2 && source.Disposed == 1 && updates.Current.IsRealtime
                && updates.Current.Scan.AddedFiles.SequenceEqual([overflow]), "Watcher overflow resubscribes and reconciles without resetting the baseline");
            source.FailSubscribe = true;
            source.Failed!(new IOException("simulated disconnect"));
            await updates.MoveNextAsync();
            Check(!updates.Current.IsRealtime && updates.Current.WatcherError is not null,
                "An unavailable event source is reported as fallback rather than realtime");
            var disconnected = Path.Combine(controlledFolder, "during-disconnect.txt");
            File.WriteAllText(disconnected, "fallback");
            source.FailSubscribe = false;
            await updates.MoveNextAsync();
            Check(updates.Current.Trigger == MonitorTrigger.Retry && updates.Current.IsRealtime
                && updates.Current.Scan.AddedFiles.SequenceEqual([disconnected]), "Retry reconnects and preserves additions during a watcher outage");
            var waiting = updates.MoveNextAsync().AsTask();
            timeout.Cancel();
            try { await waiting; throw new InvalidOperationException("Cancellation was ignored"); }
            catch (OperationCanceledException) { Check(source.Disposed == 3, "Stopping cancels the wait and disposes the active subscription"); }
        }

        var missingFolder = Path.Combine(parent, "late-folder");
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            var monitor = new RealtimeFolderMonitor(missingFolder, false, retryInterval: TimeSpan.FromMilliseconds(30));
            await using var updates = monitor.WatchAsync(timeout.Token).GetAsyncEnumerator();
            await updates.MoveNextAsync();
            Check(updates.Current.Scan.Error is not null && !updates.Current.IsRealtime, "Missing startup folders report a retryable error");
            Directory.CreateDirectory(missingFolder);
            File.WriteAllText(Path.Combine(missingFolder, "existing-on-recovery.txt"), "baseline");
            await updates.MoveNextAsync();
            Check(updates.Current.IsRealtime && updates.Current.Scan.IsBaseline && updates.Current.Scan.AddedFiles.Count == 0,
                "The first successful scan after startup failure establishes a baseline without false alerts");
        }
        return passed;
    }

    private static async Task UntilFile(IAsyncEnumerator<MonitorUpdate> updates, string path)
    {
        do { await updates.MoveNextAsync(); } while (!updates.Current.Scan.AddedFiles.Contains(path));
    }

    private sealed class ControlledSource
    {
        internal Action? Changed;
        internal Action<Exception>? Failed;
        internal bool FailSubscribe;
        internal int Created;
        internal int Disposed;
        internal IDisposable Subscribe(Action changed, Action<Exception> failed)
        {
            if (FailSubscribe) throw new IOException("Event source unavailable");
            Created++; Changed = changed; Failed = failed;
            return new Subscription(() => Disposed++);
        }
        private sealed class Subscription(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }
}
