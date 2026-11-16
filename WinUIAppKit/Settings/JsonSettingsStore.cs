using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinUIAppKit.Settings;

/// <summary>
/// Loads and saves one settings object as JSON. Writes are atomic (temp file, then rename) so a crash
/// mid-save can't leave a truncated file. The settings type and any migrations stay in the app.
/// </summary>
/// <typeparam name="T">The app's settings class; needs a parameterless constructor for first-run defaults.</typeparam>
public sealed class JsonSettingsStore<T> where T : class, new()
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Default serializer options. Nulls are omitted but defaults are written: an omitted <c>false</c>
    /// for a bool that defaults to <c>true</c> would otherwise load back as <c>true</c>.
    /// </summary>
    public static JsonSerializerOptions DefaultOptions { get; } = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null,   // PascalCase, matching the property names
    };

    private readonly JsonSerializerOptions _options;

    /// <param name="filePath">Full path of the settings file (its folder is created on first save).</param>
    /// <param name="options">Serializer options, or <see cref="DefaultOptions"/>.</param>
    public JsonSettingsStore(string filePath, JsonSerializerOptions? options = null)
    {
        FilePath = filePath;
        _options = options ?? DefaultOptions;
    }

    /// <summary>Full path of the settings file (it may not exist yet).</summary>
    public string FilePath { get; }

    /// <summary>
    /// Load the settings. A missing file gives fresh defaults with <paramref name="isFirstRun"/> true.
    /// An unreadable file is kept as <c>.bad</c> for inspection and also gives defaults, but is not
    /// treated as a first run (so first-run actions such as startup registration don't repeat).
    /// </summary>
    public T Load(out bool isFirstRun)
    {
        isFirstRun = !File.Exists(FilePath);
        if (isFirstRun) return new T();

        try
        {
            var loaded = JsonSerializer.Deserialize<T>(File.ReadAllText(FilePath), _options);
            if (loaded != null) return loaded;
            Logger.Warn("Settings file {Path} was empty", FilePath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Settings file {Path} is unreadable; keeping a copy and using defaults", FilePath);
            try { File.Copy(FilePath, FilePath + ".bad", overwrite: true); } catch { }
        }
        return new T();
    }

    /// <summary>Save the settings atomically. Failures are logged, never thrown, so UI edits can't crash the app.</summary>
    public void Save(T settings)
    {
        try
        {
            string? directory = Path.GetDirectoryName(FilePath);
            if (directory != null) Directory.CreateDirectory(directory);

            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, _options));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error saving settings to {Path}", FilePath);
        }
    }
}
