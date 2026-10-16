namespace FolderNotifier.Core;

public sealed record ScanResult(IReadOnlyList<string> AddedFiles, string? Error, bool IsBaseline = false);

/// <summary>Only successful, complete scans replace the previous snapshot.</summary>
public sealed class FolderMonitor
{
    private HashSet<string>? knownFiles;
    private readonly Func<CancellationToken, IEnumerable<string>> enumerate;

    public FolderMonitor(string folder, bool includeSubfolders)
        : this(token => EnumerateFiles(folder, includeSubfolders, token)) { }

    public FolderMonitor(Func<CancellationToken, IEnumerable<string>> enumerateFiles)
    {
        enumerate = enumerateFiles;
    }

    public ScanResult Scan(CancellationToken token = default)
    {
        try
        {
            var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in enumerate(token))
            {
                token.ThrowIfCancellationRequested();
                current.Add(path);
            }
            token.ThrowIfCancellationRequested();
            var isBaseline = knownFiles is null;
            var added = isBaseline
                ? Array.Empty<string>()
                : current.Where(path => !knownFiles!.Contains(path))
                    .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            knownFiles = current;
            return new ScanResult(added, null, isBaseline);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new ScanResult(Array.Empty<string>(), ex.Message);
        }
    }

    private static IEnumerable<string> EnumerateFiles(string folder, bool recursive, CancellationToken token)
    {
        // Do not traverse junctions/symlinks or silently omit inaccessible directories.
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false
        };
        foreach (var path in Directory.EnumerateFiles(folder, "*", options))
        {
            token.ThrowIfCancellationRequested();
            yield return path;
        }
    }
}
