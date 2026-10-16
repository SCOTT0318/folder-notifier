using FolderNotifier.Core;

var testParent = Path.GetFullPath(args.Length > 0 ? args[0] : Path.GetTempPath());
var testRoot = Path.Combine(testParent, "FolderNotifier-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
var passed = 0;
try
{
    File.WriteAllText(Path.Combine(testRoot, "기존.txt"), "existing");
    var monitor = new FolderMonitor(testRoot, false);
    Check(monitor.Scan() is { IsBaseline: true, Error: null, AddedFiles.Count: 0 }, "Existing files establish baseline");
    var added = Path.Combine(testRoot, "새 파일.txt");
    File.WriteAllText(added, "new");
    Check(monitor.Scan().AddedFiles.SequenceEqual([added]), "Korean file names detected");
    Check(monitor.Scan().AddedFiles.Count == 0, "No duplicate on unchanged scan");
    File.AppendAllText(added, "changed");
    Check(monitor.Scan().AddedFiles.Count == 0, "Content edits do not notify");
    File.Delete(added);
    Check(monitor.Scan().AddedFiles.Count == 0, "Deletes do not notify");
    File.WriteAllText(added, "recreated");
    Check(monitor.Scan().AddedFiles.SequenceEqual([added]), "Recreated files notify again");

    var child = Directory.CreateDirectory(Path.Combine(testRoot, "하위 폴더")).FullName;
    var nestedFile = Path.Combine(child, "nested.txt");
    File.WriteAllText(nestedFile, "nested");
    Check(monitor.Scan().AddedFiles.Count == 0, "Subfolders excluded by default");
    var recursive = new FolderMonitor(testRoot, true);
    recursive.Scan();
    var nestedNew = Path.Combine(child, "nested-new.txt");
    File.WriteAllText(nestedNew, "nested new");
    Check(recursive.Scan().AddedFiles.SequenceEqual([nestedNew]), "Recursive addition detected");

    var renamed = Path.Combine(testRoot, "renamed.txt");
    File.Move(added, renamed);
    Check(monitor.Scan().AddedFiles.SequenceEqual([renamed]), "Rename treated as new path");
    var batch = Enumerable.Range(0, 120).Select(i => Path.Combine(testRoot, $"batch-{i:000}.txt")).ToArray();
    foreach (var path in batch) File.WriteAllText(path, "batch");
    Check(monitor.Scan().AddedFiles.Count == 120, "Bulk copy detected in single batch");

    var fail = false;
    var injectedFiles = new[] { "C:\\demo\\original.txt" };
    IEnumerable<string> Enumerate(CancellationToken token)
    {
        foreach (var path in injectedFiles) yield return path;
        if (fail) throw new IOException("Simulated disconnected share after partial scan");
    }
    var recovery = new FolderMonitor(Enumerate);
    recovery.Scan();
    fail = true;
    injectedFiles = ["C:\\demo\\new.txt"];
    Check(recovery.Scan() is { Error: not null, AddedFiles.Count: 0 }, "Partial failed scan never notifies");
    fail = false;
    injectedFiles = ["C:\\demo\\original.txt", "C:\\demo\\new.txt"];
    Check(recovery.Scan().AddedFiles.SequenceEqual(["C:\\demo\\new.txt"]), "Recovery preserves old snapshot");

    var inaccessible = new FolderMonitor(_ => throw new UnauthorizedAccessException("Denied"));
    Check(inaccessible.Scan().Error is not null, "Access errors reported");
    var missing = new FolderMonitor(Path.Combine(testRoot, "missing"), false);
    Check(missing.Scan().Error is not null, "Missing folder reported");
    var caseFiles = new[] { "C:\\demo\\FILE.txt" };
    var caseMonitor = new FolderMonitor(_ => caseFiles);
    caseMonitor.Scan();
    caseFiles = ["c:\\DEMO\\file.TXT"];
    Check(caseMonitor.Scan().AddedFiles.Count == 0, "Windows path comparison ignores case");
    using var cancel = new CancellationTokenSource();
    cancel.Cancel();
    try { monitor.Scan(cancel.Token); throw new Exception("Cancellation ignored"); }
    catch (OperationCanceledException) { passed++; Console.WriteLine("PASS Cancellation honored"); }
    var restart = new FolderMonitor(testRoot, false);
    Check(restart.Scan().AddedFiles.Count == 0, "Restart establishes fresh baseline");

    passed += await RealtimeMonitorTests.RunAsync(testRoot);

    Console.WriteLine($"\nAll {passed} checks passed.");
}
finally
{
    // This exact, uniquely generated temporary directory belongs to this test only.
    var resolvedRoot = Path.GetFullPath(testRoot);
    if (!resolvedRoot.StartsWith(Path.TrimEndingDirectorySeparator(testParent) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Test cleanup path escaped the test parent.");
    Directory.Delete(resolvedRoot, recursive: true);
}

void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL " + name);
    passed++;
    Console.WriteLine("PASS " + name);
}
