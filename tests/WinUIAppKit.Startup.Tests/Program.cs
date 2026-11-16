using WinUIAppKit.Startup;

// Portable checks for when a moved, unpackaged copy re-points its start-with-Windows entry.
// Exits non-zero when any check fails.

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

const string Here = @"C:\Users\me\Downloads\MyApp-1.2.0\MyApp.exe";
const string Old = @"C:\Users\me\Downloads\MyApp-1.1.0\MyApp.exe";

// ── The value written ───────────────────────────────────────────────────

Check(RunEntry.Format(Here, "--silent") == "\"" + Here + "\" --silent", "format: quoted path then the silent argument");
Check(RunEntry.Format(Here, "") == "\"" + Here + "\"", "format: no argument when there is none");
Check(RunEntry.Format(@"C:\Program Files\My App\MyApp.exe", "--silent").StartsWith("\"C:\\Program Files\\My App\\MyApp.exe\"", StringComparison.Ordinal), "format: a path with spaces stays one quoted argument");

// ── When to re-point ────────────────────────────────────────────────────

Check(RunEntry.Repoint(null, Here, "--silent") is null, "repoint: no entry is never created (moving is not consent to start with Windows)");
Check(RunEntry.Repoint("\"" + Here + "\" --silent", Here, "--silent") is null, "repoint: an entry already pointing here is left alone");
Check(RunEntry.Repoint("\"" + Here.ToUpperInvariant() + "\" --SILENT", Here, "--silent") is null, "repoint: case differences are not a move (Windows paths ignore case)");
Check(RunEntry.Repoint("  \"" + Here + "\" --silent  ", Here, "--silent") is null, "repoint: surrounding spaces are not a move");
Check(RunEntry.Repoint("\"" + Old + "\" --silent", Here, "--silent") == "\"" + Here + "\" --silent", "repoint: an entry for an older copy now points here");
Check(RunEntry.Repoint("\"" + Here + "\"", Here, "--silent") == "\"" + Here + "\" --silent", "repoint: an entry in an older format gains the silent argument");
Check(RunEntry.Repoint("", Here, "--silent") == "\"" + Here + "\" --silent", "repoint: an empty entry that exists is repaired");

// ── Report ──────────────────────────────────────────────────────────────

foreach (string failure in failures)
{
    Console.Error.WriteLine($"FAIL {failure}");
}

Console.WriteLine($"WinUIAppKit.Startup.Tests: {passed} passed, {failures.Count} failed");
return failures.Count == 0 ? 0 : 1;
