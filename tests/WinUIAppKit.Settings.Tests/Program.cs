using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WinUIAppKit.Settings;

// Portable checks for the settings store (schema version, migrations, backups, unreadable files,
// reset), the protected store and atomic writes. Exits non-zero when any check fails.

var failures = new List<string>();
int passed = 0;

void Check(bool condition, string description)
{
    if (condition)
    {
        passed++;
    }
    else
    {
        failures.Add(description);
    }
}

// Runs one scenario in its own empty folder, which is deleted afterwards.
void Scenario(string name, Action<string> body)
{
    string folder = Path.Combine(Path.GetTempPath(), "kit-settings-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try
    {
        body(folder);
    }
    catch (Exception ex)
    {
        failures.Add($"{name}: threw {ex.GetType().Name}: {ex.Message}");
    }
    finally
    {
        Directory.Delete(folder, recursive: true);
    }
}

// Two migration steps: v0 -> v1 renames OldName to Name, v1 -> v2 doubles Count.
var migrationLog = new List<int>();
IReadOnlyList<Action<JsonObject>> migrations =
[
    root =>
    {
        migrationLog.Add(0);
        if (root.Remove("OldName", out var oldName))
        {
            root["Name"] = oldName;
        }
    },
    root =>
    {
        migrationLog.Add(1);
        root["Count"] = (root["Count"]?.GetValue<int>() ?? 0) * 2;
    },
];

JsonSettingsStore<TestSettings> Versioned(string folder) => new(Path.Combine(folder, "settings.json"), migrations);

JsonObject ReadJson(string path) => (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;

// ── Settings store ───────────────────────────────────────────────────────

Scenario("first run", folder =>
{
    var store = Versioned(folder);
    var result = store.Load();
    Check(result.Outcome == SettingsLoadOutcome.FirstRun, "first run: outcome is FirstRun");
    Check(result.FileSchemaVersion is null && result.BackupPath is null, "first run: no file version and no backup");
    Check(File.Exists(store.FilePath), "first run: defaults are written");

    var json = ReadJson(store.FilePath);
    Check(json.First().Key == "SchemaVersion" && json["SchemaVersion"]!.GetValue<int>() == 2, "first run: SchemaVersion 2 is the first property");
    Check(json["Enabled"]!.GetValue<bool>() && json["Name"]!.GetValue<string>() == "default", "first run: default values are written (CONF-3)");
    Check(!json.ContainsKey("Optional"), "first run: nulls are omitted");

    Check(store.Load().Outcome == SettingsLoadOutcome.Loaded, "second launch: outcome is Loaded, not FirstRun");
});

Scenario("written defaults keep a false", folder =>
{
    var store = Versioned(folder);
    store.Save(new TestSettings { Enabled = false });
    var result = store.Load();
    Check(result.Outcome == SettingsLoadOutcome.Loaded && !result.Settings.Enabled, "a saved false for a default-true bool loads back false");
});

Scenario("migrate from an unversioned file", folder =>
{
    var store = Versioned(folder);
    string original = """{ "OldName": "kept", "Count": 3, "Enabled": false }""";
    File.WriteAllText(store.FilePath, original);
    migrationLog.Clear();

    var result = store.Load();
    Check(result.Outcome == SettingsLoadOutcome.Migrated && result.FileSchemaVersion == 0, "v0: outcome is Migrated from version 0");
    Check(migrationLog.SequenceEqual([0, 1]), "v0: both steps ran, in order");
    Check(result.Settings.Name == "kept" && result.Settings.Count == 6 && !result.Settings.Enabled, "v0: migrated values are loaded");
    Check(result.BackupPath == store.FilePath + ".v0.bak" && File.ReadAllText(result.BackupPath!) == original, "v0: the original file is backed up as .v0.bak before migrating");
    Check(ReadJson(store.FilePath)["SchemaVersion"]!.GetValue<int>() == 2, "v0: the migrated file is saved at version 2");
    Check(store.Load().Outcome == SettingsLoadOutcome.Loaded, "v0: the next load needs no migration");
});

Scenario("migrate from version 1", folder =>
{
    var store = Versioned(folder);
    File.WriteAllText(store.FilePath, """{ "SchemaVersion": 1, "Name": "n", "Count": 5 }""");
    migrationLog.Clear();

    var result = store.Load();
    Check(migrationLog.SequenceEqual([1]), "v1: only the v1 -> v2 step ran");
    Check(result.Outcome == SettingsLoadOutcome.Migrated && result.FileSchemaVersion == 1 && result.Settings.Count == 10, "v1: migrated to version 2");
    Check(File.Exists(store.FilePath + ".v1.bak"), "v1: backed up as .v1.bak");
});

Scenario("newer version", folder =>
{
    var store = Versioned(folder);
    string newer = """{ "SchemaVersion": 7, "Name": "future", "FutureSetting": true }""";
    File.WriteAllText(store.FilePath, newer);

    var result = store.Load();
    Check(result.Outcome == SettingsLoadOutcome.NewerVersion && result.FileSchemaVersion == 7, "newer: outcome is NewerVersion");
    Check(result.Settings.Name == "future", "newer: known values are loaded");
    Check(File.ReadAllText(store.FilePath + ".v7.bak") == newer, "newer: the newer file is backed up as .v7.bak");
    Check(File.ReadAllText(store.FilePath) == newer, "newer: loading does not rewrite the file");
});

Scenario("unreadable file", folder =>
{
    var store = Versioned(folder);
    File.WriteAllText(store.FilePath, "{ not json");

    var result = store.Load();
    Check(result.Outcome == SettingsLoadOutcome.Unreadable, "unreadable: outcome is Unreadable, not FirstRun");
    Check(result.BackupPath == store.FilePath + ".bad" && File.ReadAllText(result.BackupPath!) == "{ not json", "unreadable: the file is kept as .bad");
    Check(result.Settings.Name == "default", "unreadable: defaults are returned");
    Check(store.Load().Outcome == SettingsLoadOutcome.Loaded, "unreadable: defaults were written, so the user is told only once");
});

Scenario("wrong JSON shapes are unreadable", folder =>
{
    var store = Versioned(folder);
    foreach (string text in new[] { "[]", "null", """{ "SchemaVersion": "two" }""", """{ "SchemaVersion": -1 }""" })
    {
        File.WriteAllText(store.FilePath, text);
        Check(store.Load().Outcome == SettingsLoadOutcome.Unreadable, $"unreadable: {text} is reported as Unreadable");
    }
});

Scenario("failing migration", folder =>
{
    var path = Path.Combine(folder, "settings.json");
    var store = new JsonSettingsStore<TestSettings>(path, [_ => throw new InvalidOperationException("bug")]);
    string original = """{ "Name": "precious" }""";
    File.WriteAllText(path, original);

    var result = store.Load();
    Check(result.Outcome == SettingsLoadOutcome.Unreadable, "failed migration: reported as Unreadable");
    Check(File.ReadAllText(path + ".v0.bak") == original && File.ReadAllText(path + ".bad") == original, "failed migration: the original survives as .v0.bak and .bad");
});

Scenario("reset", folder =>
{
    var store = Versioned(folder);
    store.Save(new TestSettings { Name = "mine", Count = 4 });
    string before = File.ReadAllText(store.FilePath);

    var result = store.Reset();
    Check(result.Outcome == SettingsLoadOutcome.Reset, "reset: outcome is Reset, not FirstRun");
    Check(result.BackupPath is not null && result.BackupPath.EndsWith(".reset", StringComparison.Ordinal) && File.ReadAllText(result.BackupPath) == before, "reset: the old file is renamed to .<time>.reset with its contents");
    Check(result.Settings.Name == "default" && store.Load().Settings.Name == "default", "reset: defaults are returned and written");

    var second = store.Reset();
    Check(second.BackupPath is not null && File.Exists(second.BackupPath), "reset: a second reset in the same second still keeps a copy");
});

Scenario("reset with no file", folder =>
{
    var result = Versioned(folder).Reset();
    Check(result.Outcome == SettingsLoadOutcome.Reset && result.BackupPath is null, "reset without a file: nothing to keep");
});

Scenario("unversioned store", folder =>
{
    var store = new JsonSettingsStore<TestSettings>(Path.Combine(folder, "settings.json"));
    var defaults = store.Load(out bool isFirstRun);
    Check(isFirstRun && !File.Exists(store.FilePath), "unversioned: Load(out) reports first run and writes nothing, as before");

    store.Save(new TestSettings { Count = 2 });
    Check(!ReadJson(store.FilePath).ContainsKey("SchemaVersion"), "unversioned: Save writes no SchemaVersion");
    Check(store.Load(out isFirstRun).Count == 2 && !isFirstRun, "unversioned: Load(out) reads the saved file");
});

// ── Atomic writes ───────────────────────────────────────────────────────

Scenario("atomic write", folder =>
{
    string path = Path.Combine(folder, "nested", "file.txt");
    AtomicFile.WriteAllText(path, "one");
    AtomicFile.WriteAllText(path, "two");
    Check(File.ReadAllText(path) == "two", "atomic: the second write replaces the first, creating the folder");
    Check(Directory.GetFiles(Path.GetDirectoryName(path)!).Length == 1, "atomic: no temporary file is left behind");
});

// ── Protected store ─────────────────────────────────────────────────────

Scenario("protected store round trip", folder =>
{
    var store = new ProtectedStore(folder, "protected-{0}.dat", [], new FakeProtector());
    Check(!store.Exists("api-key") && store.Load<Secret>("api-key") is null, "protected: absent name loads as null");

    store.Save("api-key", new Secret("s3cret"));
    string path = Path.Combine(folder, "protected-api-key.dat");
    Check(store.Exists("api-key") && File.Exists(path), "protected: the app's file-name format is used");
    Check(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("s3cret", StringComparison.Ordinal), "protected: the value is not stored in plain text");
    Check(store.Load<Secret>("api-key")?.Value == "s3cret", "protected: the value loads back");

    store.Clear("api-key");
    Check(!store.Exists("api-key"), "protected: Clear removes the file");
});

Scenario("protected store keeps an unreadable file", folder =>
{
    var errors = new List<string>();
    var protector = new FakeProtector();
    var store = new ProtectedStore(folder, "cloud-{0}.dat", [], protector, (message, _) => errors.Add(message));
    store.Save("webdav", new Secret("hunter2"));

    protector.FailUnprotect = true;
    Check(store.Load<Secret>("webdav") is null, "protected unreadable: loads as null");
    Check(store.Exists("webdav"), "protected unreadable: the file is not deleted (another process may read it)");
    Check(errors.Count == 1 && !errors[0].Contains("hunter2", StringComparison.Ordinal), "protected unreadable: one error reported, without the value");
});

Scenario("protected store names", folder =>
{
    var store = new ProtectedStore(folder, "protected-{0}.dat", [], new FakeProtector());
    foreach (string bad in new[] { "", "..", "../x", "a/b", "a\\b", "c:x" })
    {
        bool threw = false;
        try
        {
            store.Save(bad, new Secret("x"));
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        Check(threw, $"protected: name '{bad}' is rejected");
    }

    Check(Directory.GetFiles(folder).Length == 0, "protected: rejected names write nothing");

    bool formatRejected = false;
    try
    {
        _ = new ProtectedStore(folder, "no-placeholder.dat", [], new FakeProtector());
    }
    catch (ArgumentException)
    {
        formatRejected = true;
    }

    Check(formatRejected, "protected: a file-name format without {0} is rejected");
});

Scenario("protected store folder is resolved per call", folder =>
{
    string current = Path.Combine(folder, "a");
    var store = new ProtectedStore(() => current, "p-{0}.dat", [], new FakeProtector());
    store.Save("k", new Secret("1"));
    current = Path.Combine(folder, "b");
    Check(!store.Exists("k") && File.Exists(Path.Combine(folder, "a", "p-k.dat")), "protected: a folder resolver is read on every call");
});

// ── Report ──────────────────────────────────────────────────────────────

foreach (string failure in failures)
{
    Console.Error.WriteLine($"FAIL {failure}");
}

Console.WriteLine($"WinUIAppKit.Settings.Tests: {passed} passed, {failures.Count} failed");
return failures.Count == 0 ? 0 : 1;

/// <summary>Settings used by the checks: one default-true bool, a default string, a nullable and a number.</summary>
internal sealed class TestSettings
{
    public bool Enabled { get; set; } = true;

    public string Name { get; set; } = "default";

    public string? Optional { get; set; }

    public int Count { get; set; }
}

/// <summary>A stored credential shape.</summary>
internal sealed record Secret(string Value);

/// <summary>Reversible stand-in for DPAPI so the checks run off Windows. Not encryption.</summary>
internal sealed class FakeProtector : IDataProtector
{
    public bool FailUnprotect { get; set; }

    public byte[] Protect(byte[] plain) => plain.Select(b => (byte)(b ^ 0x5A)).ToArray();

    public byte[] Unprotect(byte[] cipher) =>
        FailUnprotect ? throw new CryptographicException("cannot decrypt") : Protect(cipher);
}
