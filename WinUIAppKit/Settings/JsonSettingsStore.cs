using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WinUIAppKit.Settings;

/// <summary>
/// Loads and saves one settings object as JSON (CONF-2, CONF-3, CONF-4). Writes are atomic, so a crash
/// mid-save cannot leave a truncated file. The settings type stays in the app.
/// </summary>
/// <remarks>
/// <para>Created with a list of migrations, the store is versioned: it writes an integer
/// <c>SchemaVersion</c> into the file, upgrades older files one step at a time after backing them up,
/// and reports first run, migration, an unreadable file and a reset separately through
/// <see cref="Load()"/> and <see cref="Reset"/>. Created without one, it behaves as it always has and
/// writes no version.</para>
/// <para>The store owns the <c>SchemaVersion</c> property; the settings class should not declare one.</para>
/// </remarks>
/// <typeparam name="T">The app's settings class; needs a parameterless constructor for defaults.</typeparam>
public sealed class JsonSettingsStore<T> where T : class, new()
{
    /// <summary>The JSON property that holds the schema version in a versioned store's file.</summary>
    public const string SchemaVersionProperty = "SchemaVersion";

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
    private readonly IReadOnlyList<Action<JsonObject>>? _migrations;

    /// <summary>Create an unversioned store, as before schema versions existed.</summary>
    /// <param name="filePath">Full path of the settings file (its folder is created on first save).</param>
    /// <param name="options">Serializer options, or <see cref="DefaultOptions"/>.</param>
    public JsonSettingsStore(string filePath, JsonSerializerOptions? options = null)
    {
        FilePath = filePath;
        _options = options ?? DefaultOptions;
    }

    /// <summary>Create a versioned store (CONF-3).</summary>
    /// <param name="filePath">Full path of the settings file (its folder is created on first save).</param>
    /// <param name="migrations">
    /// One step per schema version, in order: the step at index <c>n</c> upgrades the file's JSON from
    /// version <c>n</c> to <c>n + 1</c>, editing the object in place. A file with no version is version 0,
    /// so the current version is the number of steps. Steps are never removed or reordered once shipped.
    /// </param>
    /// <param name="options">Serializer options, or <see cref="DefaultOptions"/>.</param>
    public JsonSettingsStore(string filePath, IReadOnlyList<Action<JsonObject>> migrations, JsonSerializerOptions? options = null)
        : this(filePath, options)
    {
        _migrations = migrations;
    }

    /// <summary>Full path of the settings file (it may not exist yet).</summary>
    public string FilePath { get; }

    /// <summary>The schema version this app writes: the number of migrations, or 0 for an unversioned store.</summary>
    public int CurrentSchemaVersion => _migrations?.Count ?? 0;

    // ── Versioned load ───────────────────────────────────────────────────

    /// <summary>
    /// Load the settings and say what happened. Never throws.
    /// <list type="bullet">
    /// <item><b>FirstRun:</b> no file. Defaults are returned and written.</item>
    /// <item><b>Loaded:</b> the file was current.</item>
    /// <item><b>Migrated:</b> the file was older. It was backed up to <c>.v&lt;old version&gt;.bak</c>, upgraded and saved.</item>
    /// <item><b>NewerVersion:</b> a newer app wrote it. It was backed up to <c>.v&lt;its version&gt;.bak</c> and loaded as far as this app understands it; the next save drops what this version does not know.</item>
    /// <item><b>Unreadable:</b> it could not be parsed or migrated. It was kept as <c>.bad</c>, and defaults were returned and written. Tell the user once that settings were reset.</item>
    /// </list>
    /// </summary>
    public SettingsLoadResult<T> Load()
    {
        if (!File.Exists(FilePath))
        {
            var defaults = new T();
            Save(defaults);
            return new SettingsLoadResult<T>(defaults, SettingsLoadOutcome.FirstRun, null, null);
        }

        int? fileVersion = null;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject
                ?? throw new JsonException("The settings file does not hold a JSON object.");
            fileVersion = ReadSchemaVersion(root);

            // Newer than this app: keep the file the newer app wrote before this one rewrites it.
            if (fileVersion > CurrentSchemaVersion)
            {
                string backup = BackUp(fileVersion.Value);
                Logger.Warn("Settings file {Path} is schema version {FileVersion}, newer than {CurrentVersion}; loaded what this version understands", FilePath, fileVersion, CurrentSchemaVersion);
                return new SettingsLoadResult<T>(Deserialize(root), SettingsLoadOutcome.NewerVersion, fileVersion, backup);
            }

            if (fileVersion < CurrentSchemaVersion)
            {
                return Migrate(root, fileVersion.Value);
            }

            return new SettingsLoadResult<T>(Deserialize(root), SettingsLoadOutcome.Loaded, fileVersion, null);
        }
        catch (Exception ex)
        {
            return ReplaceUnreadable(ex, fileVersion);
        }
    }

    /// <summary>
    /// Reset to defaults at the user's request (CONF-4). The current file is renamed, not deleted, to
    /// <c>.&lt;UTC time&gt;.reset</c> so it can be recovered; defaults are written in its place. Nothing
    /// else is touched: state Windows owns (the startup task) and stored credentials stay as they are.
    /// </summary>
    public SettingsLoadResult<T> Reset()
    {
        string? renamed = null;
        if (File.Exists(FilePath))
        {
            renamed = $"{FilePath}.{DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}.reset";
            try
            {
                File.Move(FilePath, renamed);
            }
            catch (Exception ex)
            {
                // A rename can fail on a file another process holds; a copy still keeps the old settings.
                Logger.Warn(ex, "Could not rename {Path} for a reset; copying it instead", FilePath);
                try
                {
                    File.Copy(FilePath, renamed, overwrite: true);
                }
                catch (Exception copyEx)
                {
                    Logger.Error(copyEx, "Could not keep a copy of {Path} before resetting it", FilePath);
                    renamed = null;
                }
            }
        }

        var defaults = new T();
        Save(defaults);
        Logger.Info("Settings reset to defaults at the user's request; previous file kept as {Backup}", renamed ?? "(none)");
        return new SettingsLoadResult<T>(defaults, SettingsLoadOutcome.Reset, null, renamed);
    }

    /// <summary>The file's schema version: 0 when it has none (written before versioning).</summary>
    private static int ReadSchemaVersion(JsonObject root)
    {
        if (!root.TryGetPropertyValue(SchemaVersionProperty, out var node) || node is null)
        {
            return 0;
        }

        int version = node.GetValue<int>();
        if (version < 0)
        {
            throw new JsonException($"{SchemaVersionProperty} is negative.");
        }

        return version;
    }

    /// <summary>Back up the file, run each migration step from its version to the current one, and save.</summary>
    private SettingsLoadResult<T> Migrate(JsonObject root, int fromVersion)
    {
        string backup = BackUp(fromVersion);
        for (int version = fromVersion; version < CurrentSchemaVersion; version++)
        {
            _migrations![version](root);
        }

        root[SchemaVersionProperty] = CurrentSchemaVersion;
        var settings = Deserialize(root);
        Save(settings);
        Logger.Info("Settings migrated from schema version {From} to {To}; previous file kept as {Backup}", fromVersion, CurrentSchemaVersion, backup);
        return new SettingsLoadResult<T>(settings, SettingsLoadOutcome.Migrated, fromVersion, backup);
    }

    /// <summary>Keep the unreadable file as <c>.bad</c>, then load and write defaults.</summary>
    private SettingsLoadResult<T> ReplaceUnreadable(Exception ex, int? fileVersion)
    {
        string bad = FilePath + ".bad";
        Logger.Error(ex, "Settings file {Path} is unreadable; keeping it as {Bad} and using defaults", FilePath, bad);
        try
        {
            File.Copy(FilePath, bad, overwrite: true);
        }
        catch (Exception copyEx)
        {
            Logger.Error(copyEx, "Could not keep a copy of the unreadable settings file");
        }

        var defaults = new T();
        Save(defaults);
        return new SettingsLoadResult<T>(defaults, SettingsLoadOutcome.Unreadable, fileVersion, bad);
    }

    /// <summary>Copy the file to <c>.v&lt;version&gt;.bak</c>, replacing an older backup of the same version.</summary>
    private string BackUp(int version)
    {
        string backup = $"{FilePath}.v{version}.bak";
        File.Copy(FilePath, backup, overwrite: true);
        return backup;
    }

    private T Deserialize(JsonObject root) =>
        root.Deserialize<T>(_options) ?? throw new JsonException("The settings file deserialized to null.");

    // ── Unversioned load ─────────────────────────────────────────────────

    /// <summary>
    /// Load the settings without schema handling, as before versioning; prefer <see cref="Load()"/>.
    /// A missing file gives fresh defaults with <paramref name="isFirstRun"/> true and writes nothing.
    /// An unreadable file is kept as <c>.bad</c> for inspection and also gives defaults, but is not
    /// treated as a first run (so first-run actions such as startup registration don't repeat).
    /// </summary>
    /// <param name="isFirstRun">True when no settings file existed.</param>
    public T Load(out bool isFirstRun)
    {
        isFirstRun = !File.Exists(FilePath);
        if (isFirstRun)
        {
            return new T();
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<T>(File.ReadAllText(FilePath), _options);
            if (loaded != null)
            {
                return loaded;
            }

            Logger.Warn("Settings file {Path} was empty", FilePath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Settings file {Path} is unreadable; keeping a copy and using defaults", FilePath);
            try
            {
                File.Copy(FilePath, FilePath + ".bad", overwrite: true);
            }
            catch
            {
                // Best effort: defaults load either way.
            }
        }
        return new T();
    }

    // ── Save ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Save the settings atomically; a versioned store stamps the current <c>SchemaVersion</c> first in
    /// the file. Failures are logged, never thrown, so UI edits can't crash the app.
    /// </summary>
    /// <param name="settings">The settings to write.</param>
    public void Save(T settings)
    {
        try
        {
            AtomicFile.WriteAllText(FilePath, Serialize(settings));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error saving settings to {Path}", FilePath);
        }
    }

    private string Serialize(T settings)
    {
        if (_migrations is null)
        {
            return JsonSerializer.Serialize(settings, _options);
        }

        // The version goes first, so a person reading the file sees it before the settings.
        var body = JsonSerializer.SerializeToNode(settings, _options) as JsonObject
            ?? throw new JsonException("The settings type does not serialize to a JSON object.");
        var root = new JsonObject { [SchemaVersionProperty] = CurrentSchemaVersion };
        foreach (var (name, value) in body.ToList())
        {
            if (name == SchemaVersionProperty)
            {
                continue;
            }

            body.Remove(name);
            root[name] = value;
        }

        return root.ToJsonString(_options);
    }
}

/// <summary>What <see cref="JsonSettingsStore{T}.Load()"/> or <see cref="JsonSettingsStore{T}.Reset"/> found.</summary>
public enum SettingsLoadOutcome
{
    /// <summary>The file was current.</summary>
    Loaded,

    /// <summary>There was no file: a first run. Defaults were written.</summary>
    FirstRun,

    /// <summary>An older file was backed up and upgraded.</summary>
    Migrated,

    /// <summary>A newer app wrote the file; it was backed up and loaded as far as this version understands it.</summary>
    NewerVersion,

    /// <summary>The file could not be read or migrated; it was kept as <c>.bad</c> and defaults were written.</summary>
    Unreadable,

    /// <summary>The user reset the settings; the old file was renamed and defaults were written.</summary>
    Reset,
}

/// <summary>The settings a store produced and how.</summary>
/// <typeparam name="T">The app's settings class.</typeparam>
/// <param name="Settings">The settings to use.</param>
/// <param name="Outcome">What happened; first run, unreadable and reset are told apart (UI-15).</param>
/// <param name="FileSchemaVersion">The schema version the file had before loading, or null when there was no readable version.</param>
/// <param name="BackupPath">The copy kept of the previous file (migration, newer version, unreadable or reset), or null.</param>
public sealed record SettingsLoadResult<T>(T Settings, SettingsLoadOutcome Outcome, int? FileSchemaVersion, string? BackupPath)
    where T : class;
