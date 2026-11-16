using static WinUIAppKit.Interop.NativeMethods;

namespace WinUIAppKit.Tray;

/// <summary>
/// The tray icon's right-click menu: a native Win32 popup menu (what Windows' own tray icons use),
/// following the dark app mode. Build it fresh each time so item text reflects current state.
/// </summary>
public sealed class TrayMenu
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static bool s_darkModeEnabled;

    private readonly List<(string? Text, Action? OnClick, bool Checked, bool Enabled)> _items = new();

    /// <summary>Add a command. <paramref name="onClick"/> runs on the UI thread after the menu closes.</summary>
    public TrayMenu Add(string text, Action onClick, bool isChecked = false, bool enabled = true)
    {
        _items.Add((text, onClick, isChecked, enabled));
        return this;
    }

    /// <summary>Add a separator line.</summary>
    public TrayMenu AddSeparator()
    {
        _items.Add((null, null, false, true));
        return this;
    }

    /// <summary>
    /// Show the menu at a screen point (normally the cursor) and run the chosen command. Blocks until
    /// the menu closes, so call it from a dispatched callback, not from inside a window procedure.
    /// </summary>
    /// <param name="ownerHwnd">The tray icon's host window; it is brought to the foreground so the menu closes on outside clicks.</param>
    /// <param name="x">Screen x of the menu's corner, in physical pixels.</param>
    /// <param name="y">Screen y of the menu's corner, in physical pixels.</param>
    public void Show(IntPtr ownerHwnd, int x, int y)
    {
        EnableDarkMenus();

        IntPtr menu = CreatePopupMenu();
        try
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item.Text == null)
                {
                    AppendMenu(menu, MF_SEPARATOR, 0, null);
                    continue;
                }
                uint flags = MF_STRING | (item.Checked ? MF_CHECKED : 0) | (item.Enabled ? 0 : MF_GRAYED);
                AppendMenu(menu, flags, (UIntPtr)(uint)(i + 1), item.Text);
            }

            // Documented requirement for notification-area menus: the owner must be foreground, and a
            // WM_NULL afterwards makes the next click outside the menu dismiss it correctly.
            SetForegroundWindow(ownerHwnd);
            int chosen = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, x, y, ownerHwnd, IntPtr.Zero);
            PostMessage(ownerHwnd, WM_NULL, 0, 0);

            if (chosen > 0 && chosen <= _items.Count)
            {
                _items[chosen - 1].OnClick?.Invoke();
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    /// <summary>
    /// Win32 menus stay light unless the process opts in through uxtheme's undocumented
    /// SetPreferredAppMode(AllowDark). Harmless on builds where the export is missing.
    /// </summary>
    private static void EnableDarkMenus()
    {
        if (s_darkModeEnabled)
        {
            return;
        }

        s_darkModeEnabled = true;
        try
        {
            const int AllowDark = 1;
            SetPreferredAppMode(AllowDark);
            FlushMenuThemes();
        }
        catch (Exception ex) { Logger.Debug(ex, "Dark menus unavailable"); }
    }
}
