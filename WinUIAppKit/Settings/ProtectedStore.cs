using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;

namespace WinUIAppKit.Settings;

/// <summary>
/// Small objects kept outside <c>settings.json</c>, encrypted to the current Windows user (SEC-1, CONF-5).
/// Every credential, token or API key an app keeps belongs here, one file per name.
/// </summary>
/// <remarks>
/// <para>The settings file is plain text that users copy, export and attach to support requests, so a
/// credential inside it leaks with it. Windows data protection (DPAPI, current-user scope) ties the
/// ciphertext to the Windows user account: a copied file is useless on another machine or under another
/// user. That defends against the file being read or moved, not against malware already running as the
/// user, which nothing at this layer can.</para>
/// <para>The app supplies the folder, the file-name pattern and the entropy. Once an app has shipped,
/// those three are permanent: changing any of them makes every stored value unreadable.</para>
/// <para>Two processes may read the same file (an app and its shell extension), so:</para>
/// <list type="bullet">
/// <item><see cref="Save{T}"/> writes atomically, so a reader never sees half a file.</item>
/// <item><see cref="Load{T}"/> never deletes an unreadable file: a transient failure must not erase the
/// value for both processes. The app tells the user instead (see <see cref="Exists"/>).</item>
/// <item>This file and <see cref="AtomicFile"/> depend on nothing else in the kit, NLog or WinUI, so a
/// second process or a portable test harness can compile them in by linking the two source files.</item>
/// </list>
/// </remarks>
public sealed class ProtectedStore
{
    private readonly Func<string> _folder;
    private readonly string _fileNameFormat;
    private readonly IDataProtector _protector;
    private readonly Action<string, Exception>? _logError;

    /// <summary>Create a store for one app.</summary>
    /// <param name="folder">
    /// Returns the folder that holds the encrypted files, resolved on every call. Use the physical data
    /// folder when another process reads the files too (packaged apps' %AppData% is redirected).
    /// </param>
    /// <param name="fileNameFormat">The file name for a stored name, with <c>{0}</c> for the name, such as <c>protected-{0}.dat</c>.</param>
    /// <param name="entropy">Extra entropy so another app's DPAPI call alone cannot decrypt the files, such as the UTF-8 bytes of <c>"MyApp.ProtectedStore.v1"</c>. Ignored when <paramref name="protector"/> is given.</param>
    /// <param name="protector">The encryption to use; null for DPAPI with <paramref name="entropy"/>. Tests pass a fake so they run off Windows.</param>
    /// <param name="logError">Receives a short description and the exception when a read or write fails. Messages never contain the stored value. Null where there is nowhere useful to log.</param>
    /// <exception cref="ArgumentException"><paramref name="fileNameFormat"/> has no <c>{0}</c> or contains a path separator.</exception>
    /// <exception cref="PlatformNotSupportedException">No <paramref name="protector"/> was passed and this is not Windows.</exception>
    public ProtectedStore(Func<string> folder, string fileNameFormat, byte[] entropy, IDataProtector? protector = null, Action<string, Exception>? logError = null)
    {
        if (!fileNameFormat.Contains("{0}", StringComparison.Ordinal) || fileNameFormat.IndexOfAny(['\\', '/']) >= 0)
        {
            throw new ArgumentException("Use a file name with {0} for the stored name, and no folder.", nameof(fileNameFormat));
        }

        _folder = folder;
        _fileNameFormat = fileNameFormat;
        // DPAPI exists only on Windows; elsewhere (a portable test harness) a protector must be passed in.
        _protector = protector ?? (OperatingSystem.IsWindows()
            ? new DpapiDataProtector(entropy)
            : throw new PlatformNotSupportedException("Windows data protection is only available on Windows; pass a protector."));
        _logError = logError;
    }

    /// <summary>Create a store whose folder does not change while the app runs. See the main constructor.</summary>
    /// <param name="folder">The folder that holds the encrypted files.</param>
    /// <param name="fileNameFormat">The file name for a stored name, with <c>{0}</c> for the name.</param>
    /// <param name="entropy">Extra entropy for DPAPI.</param>
    /// <param name="protector">The encryption to use; null for DPAPI with <paramref name="entropy"/>.</param>
    /// <param name="logError">Receives failures; messages never contain the stored value.</param>
    public ProtectedStore(string folder, string fileNameFormat, byte[] entropy, IDataProtector? protector = null, Action<string, Exception>? logError = null)
        : this(() => folder, fileNameFormat, entropy, protector, logError)
    {
    }

    /// <summary>The file a name is stored in.</summary>
    /// <param name="name">The stored name: letters, digits, '-', '_' and '.', as used by the app's callers.</param>
    /// <exception cref="ArgumentException">The name is empty or could leave the folder.</exception>
    public string PathFor(string name)
    {
        // Names come from app code, but a name like "..\x" must never reach a path outside the folder.
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..", StringComparison.Ordinal) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(['\\', '/', ':']) >= 0)
        {
            throw new ArgumentException($"'{name}' is not a valid protected store name.", nameof(name));
        }

        return Path.Combine(_folder(), string.Format(System.Globalization.CultureInfo.InvariantCulture, _fileNameFormat, name));
    }

    /// <summary>Whether a stored file exists, readable or not. Lets callers tell "never saved" from "unreadable".</summary>
    /// <param name="name">The stored name.</param>
    public bool Exists(string name)
    {
        try
        {
            return File.Exists(PathFor(name));
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Load a stored object, or null when absent or unreadable. An unreadable file (a restored profile,
    /// a different Windows user, corruption) is left in place; see the class remarks.
    /// </summary>
    /// <typeparam name="T">The stored object's type.</typeparam>
    /// <param name="name">The stored name.</param>
    public T? Load<T>(string name) where T : class
    {
        string path = PathFor(name);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            byte[] plain = _protector.Unprotect(File.ReadAllBytes(path));
            return JsonSerializer.Deserialize<T>(plain);
        }
        catch (Exception ex)
        {
            _logError?.Invoke($"Could not read protected store '{name}'; treating it as absent", ex);
            return null;
        }
    }

    /// <summary>
    /// Store an object, encrypted to the current user, replacing any earlier value atomically. Failures
    /// are reported to the error callback and not thrown; a caller that must know the value landed
    /// reads it back with <see cref="Load{T}"/>.
    /// </summary>
    /// <typeparam name="T">The stored object's type.</typeparam>
    /// <param name="name">The stored name.</param>
    /// <param name="value">The object to store.</param>
    public void Save<T>(string name, T value)
    {
        string path = PathFor(name);
        try
        {
            byte[] plain = JsonSerializer.SerializeToUtf8Bytes(value);
            AtomicFile.WriteAllBytes(path, _protector.Protect(plain));
        }
        catch (Exception ex)
        {
            _logError?.Invoke($"Could not save protected store '{name}'", ex);
        }
    }

    /// <summary>Forget a stored object. Failures are reported to the error callback, not thrown.</summary>
    /// <param name="name">The stored name.</param>
    public void Clear(string name)
    {
        string path = PathFor(name);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logError?.Invoke($"Could not delete protected store '{name}'", ex);
        }
    }
}

/// <summary>Symmetric encryption for <see cref="ProtectedStore"/>, abstracted so tests can run off Windows.</summary>
public interface IDataProtector
{
    /// <summary>Encrypt <paramref name="plain"/>.</summary>
    /// <param name="plain">The bytes to encrypt.</param>
    byte[] Protect(byte[] plain);

    /// <summary>Decrypt what <see cref="Protect"/> produced. Throws when the data cannot be decrypted.</summary>
    /// <param name="cipher">The encrypted bytes.</param>
    byte[] Unprotect(byte[] cipher);
}

/// <summary>Windows data protection (DPAPI) scoped to the current user.</summary>
/// <param name="entropy">Extra entropy mixed into every call; must match between saving and loading.</param>
[SupportedOSPlatform("windows")]
public sealed class DpapiDataProtector(byte[] entropy) : IDataProtector
{
    private readonly byte[] _entropy = entropy;

    /// <inheritdoc />
    public byte[] Protect(byte[] plain) =>
        ProtectedData.Protect(plain, _entropy, DataProtectionScope.CurrentUser);

    /// <inheritdoc />
    public byte[] Unprotect(byte[] cipher) =>
        ProtectedData.Unprotect(cipher, _entropy, DataProtectionScope.CurrentUser);
}
