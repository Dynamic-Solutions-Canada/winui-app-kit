using static WinUIAppKit.Interop.NativeMethods;

namespace WinUIAppKit;

/// <summary>
/// One running copy per user session. The first instance claims a named mutex; later launches post a
/// registered window message to the first instance's (hidden) host window, then exit.
/// </summary>
public static class SingleInstance
{
    // Held for the life of the process; releasing it would let a second copy start.
    private static Mutex? s_mutex;

    /// <summary>Claim the instance. Returns false when another copy already owns <paramref name="mutexName"/>.</summary>
    public static bool TryClaim(string mutexName)
    {
        s_mutex = new Mutex(true, mutexName, out bool created);
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
            PostMessage(existing, (uint)RegisterWindowMessage(messageName), 0, 0);
    }
}
