using System.IO;
using System.Text;

namespace WinUIAppKit.Settings;

/// <summary>
/// Replaces a file in one step, so a reader (or a crash mid-write) sees either the old file or the new
/// one, never a truncated mix (CONF-2).
/// </summary>
/// <remarks>
/// Depends on nothing but the base library, so a second process (a shell extension) or a portable
/// test harness can compile it in with <c>&lt;Compile Include="...\AtomicFile.cs" Link="..." /&gt;</c>
/// alongside <see cref="ProtectedStore"/>.
/// </remarks>
public static class AtomicFile
{
    /// <summary>How many times to try the final rename when another process holds the target.</summary>
    private const int ReplaceAttempts = 3;

    /// <summary>Writes UTF-8 text (no byte-order mark) atomically. See <see cref="WriteAllBytes"/>.</summary>
    /// <param name="path">The file to create or replace.</param>
    /// <param name="contents">The complete new contents.</param>
    public static void WriteAllText(string path, string contents) =>
        WriteAllBytes(path, Encoding.UTF8.GetBytes(contents));

    /// <summary>
    /// Writes <paramref name="bytes"/> to a temporary file beside <paramref name="path"/>, then renames
    /// it over the target. The temporary file sits in the same folder so the rename stays on one
    /// volume, and the folder is created when missing. On failure the temporary file is deleted and
    /// the exception is rethrown; the original file is left as it was.
    /// </summary>
    /// <param name="path">The file to create or replace.</param>
    /// <param name="bytes">The complete new contents.</param>
    public static void WriteAllBytes(string path, byte[] bytes)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);

        string temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            ReplaceWithRetry(temp, path);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>
    /// Renames <paramref name="source"/> over <paramref name="target"/>. Another process may be reading
    /// the target for a few milliseconds, which makes Windows refuse the replace, so a short retry
    /// covers that without masking a real failure.
    /// </summary>
    private static void ReplaceWithRetry(string source, string target)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, target, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < ReplaceAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < ReplaceAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort: a leftover .tmp holds only what was being written, never the old file.
        }
    }
}
