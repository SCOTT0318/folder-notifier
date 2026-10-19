using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FolderNotifier;

internal sealed record UpdateRelease(Version Version, Uri Page, string Notes, Uri Installer, long Size,
    string? Digest, Uri? Checksum)
{
    public string DisplayVersion => AppUpdater.FormatVersion(Version);
}

internal sealed class AppUpdater
{
    internal const string Repository = "https://github.com/SCOTT0318/folder-notifier";
    private const string DownloadPrefix = Repository + "/releases/download/";
    private const long MaxInstallerSize = 200 * 1024 * 1024;
    private static readonly HttpClient SharedClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient client;
    internal static Version CurrentVersion => typeof(AppUpdater).Assembly.GetName().Version!;

    internal AppUpdater(HttpClient? client = null) => this.client = client ?? SharedClient;

    internal static string FormatVersion(Version version) => version.Revision > 0
        ? version.ToString(4) : version.ToString(3);

    internal static Version ParseVersion(string tag)
    {
        if (!Regex.IsMatch(tag, @"^[vV]?\d+\.\d+\.\d+(\.\d+)?$") ||
            !Version.TryParse(tag.TrimStart('v', 'V'), out var version))
            throw new InvalidDataException("정식 버전 정보를 읽을 수 없습니다.");
        return new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
    }

    internal async Task<UpdateRelease> CheckAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/SCOTT0318/folder-notifier/releases/latest");
        request.Headers.UserAgent.ParseAdd("FolderNotifier/" + FormatVersion(CurrentVersion));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidDataException("아직 공개된 정식 버전이 없습니다. 나중에 다시 확인해 주세요.");
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token),
            cancellationToken: timeout.Token);
        return ParseRelease(json.RootElement);
    }

    internal static UpdateRelease ParseRelease(JsonElement release)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("정식 배포 버전만 설치할 수 있습니다.");
        var version = ParseVersion(release.GetProperty("tag_name").GetString() ?? "");
        var page = new Uri(release.GetProperty("html_url").GetString() ?? "");
        if (!page.AbsoluteUri.StartsWith(Repository + "/releases/tag/", StringComparison.Ordinal))
            throw new InvalidDataException("공식 배포 페이지가 아닙니다.");
        var assets = release.GetProperty("assets").EnumerateArray().ToArray();
        var installer = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == "FolderNotifier-Setup.exe");
        if (installer.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("설치 파일이 아직 준비되지 않았습니다. 나중에 다시 확인해 주세요.");
        var url = OfficialDownload(installer.GetProperty("browser_download_url").GetString());
        var size = installer.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaxInstallerSize) throw new InvalidDataException("설치 파일 크기가 올바르지 않습니다.");
        var digest = installer.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
        if (digest is not null && !Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$")) digest = null;
        var checksum = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == "FolderNotifier-Setup.sha256");
        var checksumUrl = checksum.ValueKind == JsonValueKind.Undefined ? null
            : OfficialDownload(checksum.GetProperty("browser_download_url").GetString());
        if (digest is null && checksumUrl is null)
            throw new InvalidDataException("설치 파일의 검증 정보가 없습니다. 배포 페이지를 확인해 주세요.");
        return new(version, page, release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            url, size, digest?[7..], checksumUrl);
    }

    private static Uri OfficialDownload(string? value)
    {
        if (value is null || !value.StartsWith(DownloadPrefix, StringComparison.Ordinal) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("공식 설치 파일 주소가 아닙니다.");
        return uri;
    }

    internal async Task<string> DownloadAsync(UpdateRelease release, IProgress<int> progress,
        CancellationToken token, string? downloadRoot = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var directory = Path.Combine(downloadRoot ?? Path.Combine(Path.GetTempPath(), "FolderNotifier-updates"),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "FolderNotifier-Setup.exe");
        try
        {
            var expectedHash = release.Digest;
            if (expectedHash is null)
            {
                using var checksumResponse = await client.GetAsync(release.Checksum!, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                checksumResponse.EnsureSuccessStatusCode();
                await checksumResponse.Content.LoadIntoBufferAsync(4096, timeout.Token);
                var checksumText = await checksumResponse.Content.ReadAsStringAsync(timeout.Token);
                var match = Regex.Match(checksumText.Trim(), @"\A([a-fA-F0-9]{64})\s+\*?FolderNotifier-Setup\.exe\z");
                if (!match.Success) throw new InvalidDataException("설치 파일 검증 정보를 읽을 수 없습니다.");
                expectedHash = match.Groups[1].Value;
            }
            using var response = await client.GetAsync(release.Installer, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != release.Size)
                throw new InvalidDataException("설치 파일 크기가 배포 정보와 다릅니다. 다시 확인해 주세요.");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                long received = 0;
                int count;
                progress.Report(0);
                while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    received += count;
                    if (received > release.Size || received > MaxInstallerSize)
                        throw new InvalidDataException("설치 파일 크기가 배포 정보와 다릅니다.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    progress.Report((int)(received * 100 / release.Size));
                }
                if (received != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("설치 파일 검증에 실패했습니다. 다시 확인 후 다운로드해 주세요.");
            }
            token.ThrowIfCancellationRequested();
            return path;
        }
        catch
        {
            // Only this invocation's uniquely named directory is removed.
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(directory);
            throw;
        }
    }

    internal static ProcessStartInfo InstallerStartInfo(string path) => new()
    {
        FileName = path,
        UseShellExecute = true,
        Arguments = "/FROMAPP=1 /DIR=\"" + Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory) + "\""
    };

    internal static void LaunchInstaller(string path)
    {
        if (Process.Start(InstallerStartInfo(path)) is null)
            throw new IOException("설치 프로그램을 시작하지 못했습니다.");
    }
}
