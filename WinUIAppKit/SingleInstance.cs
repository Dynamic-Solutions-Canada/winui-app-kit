using System.IO.MemoryMappedFiles;
using static WinUIAppKit.Interop.NativeMethods;

namespace WinUIAppKit;

/// <summary>
/// One running copy per user session. The first instance claims a named mutex; a later launch hands
/// over to it, then exits. Two ways to hand over:
/// <list type="bullet">
/// <item>A resident app posts a registered message to the first instance's hidden host window
/// (<see cref="SignalRunningInstance"/>), which decides what to show.</item>
/// <item>An app whose only window is visible (no host window) registers that window after claiming
/// (<see cref="RegisterWindow"/>); a later launch brings it forward (<see cref="BringRunningInstanceForward"/>).</item>
/// </list>
/// </summary>
public static class SingleInstance
{
    // Held for the life of the process; releasing either would let a second copy start, or lose the window.
    private static Mutex? _mutex;
    private static string? _mutexName;
    private static MemoryMappedFile? _windowRecord;

    /// <summary>Claim the instance. Returns false when another copy already owns <paramref name="mutexName"/>.</summary>
    public static bool TryClaim(string mutexName)
    {
        _mutex = new Mutex(true, mutexName, out bool created);
        _mutexName = mutexName;
        return created;
    }

    /// <summary>
    /// Ask the running copy to do something (usually "show yourself") by posting
    /// <paramref name="messageName"/> to its host window, found by <paramref name="hostWindowTitle"/>.
    /// </summary>
    public static void SignalRunningInstance(string hostWindowTitle, string messageName)
    {
        IntPtr existing = FindWindow(null, hostWindowTitle);
        if (existing != IntPtr.Zero)
        {
            PostMessage(existing, (uint)RegisterWindowMessage(messageName), 0, 0);
        }
    }

    /// <summary>
    /// Record <paramref name="hwnd"/> as the window a later launch brings forward, for an app with no
    /// hidden host window. Call once, after <see cref="TryClaim"/> succeeded and the window exists. The
    /// record lives in this session's named shared memory for as long as the process runs, so it needs
    /// no stable window title.
    /// </summary>
    /// <param name="hwnd">The app's main window (<c>WindowChrome.Hwnd(window)</c>).</param>
    public static void RegisterWindow(IntPtr hwnd)
    {
        if (_mutexName is null)
        {
            throw new InvalidOperationException("Call TryClaim before RegisterWindow.");
        }

        _windowRecord ??= MemoryMappedFile.CreateOrOpen(RecordName(_mutexName), sizeof(long));
        using var view = _windowRecord.CreateViewAccessor(0, sizeof(long));
        view.Write(0, hwnd.ToInt64());
    }

    /// <summary>
    /// From a second launch: bring the running copy's registered window to the front, restoring it if
    /// it is minimized. Call after <see cref="TryClaim"/> returned false, then exit. The launch the user
    /// just started holds the foreground right, so it sets the foreground itself, and also passes that
    /// right to the running copy in case it activates the window on its own.
    /// </summary>
    /// <param name="mutexName">The name passed to <see cref="TryClaim"/>.</param>
    /// <returns>False when no window is registered or it no longer exists.</returns>
    public static bool BringRunningInstanceForward(string mutexName)
    {
        IntPtr hwnd;
        try
        {
            using var record = MemoryMappedFile.OpenExisting(RecordName(mutexName), MemoryMappedFileRights.Read);
            using var view = record.CreateViewAccessor(0, sizeof(long), MemoryMappedFileAccess.Read);
            hwnd = new IntPtr(view.ReadInt64(0));
        }
        catch (FileNotFoundException)
        {
            // The running copy has not registered a window (yet, or it is a resident app).
            return false;
        }

        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
        {
            return false;
        }

        GetWindowThreadProcessId(hwnd, out uint processId);
        AllowSetForegroundWindow(processId);
        if (IsIconic(hwnd))
        {
            ShowWindowAsync(hwnd, SW_RESTORE);
        }

        return SetForegroundWindow(hwnd);
    }

    /// <summary>The session-local shared memory name for a mutex's window record (no backslashes allowed after the prefix).</summary>
    private static string RecordName(string mutexName) => @"Local\" + mutexName.Replace('\\', '_') + ".window";
}
