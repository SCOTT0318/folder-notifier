using System.Text.Json;

namespace FolderNotifier;

internal sealed class AppSettings
{
    private string storagePath = DefaultSettingsPath;
    public string Folder { get; set; } = "";
    public bool IncludeSubfolders { get; set; }
    public bool StartOnLaunch { get; set; }

    internal static string DefaultSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FolderNotifier", "settings.json");

    public static AppSettings Load(out string? error, string? path = null)
    {
        error = null;
        path ??= DefaultSettingsPath;
        try
        {
            var settings = File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new()
                : new AppSettings();
            settings.storagePath = path;
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = "설정을 읽지 못했습니다: " + ex.Message;
            return new AppSettings { storagePath = path };
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(storagePath)!);
        var temporary = storagePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, storagePath, overwrite: true);
    }
}
