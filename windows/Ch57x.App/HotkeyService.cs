using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Ch57x.App;

/// <summary>System-wide hotkeys via RegisterHotKey. Defaults: Ctrl+Alt+1..9 → profile 1..9.</summary>
public sealed class HotkeyService : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2;

    private readonly HwndSource _src;
    private readonly Action<int> _onProfile; // 0-based index
    private readonly List<int> _ids = new();

    public HotkeyService(Action<int> onProfile)
    {
        _onProfile = onProfile;
        // hidden message-only window for receiving WM_HOTKEY
        var param = new HwndSourceParameters("Ch57x.HotkeyMsgWindow") { WindowStyle = 0, ParentWindow = new IntPtr(-3) };
        _src = new HwndSource(param);
        _src.AddHook(WndProc);

        // Ctrl+Alt+1..9
        for (int i = 1; i <= 9; i++)
        {
            int id = i;
            if (RegisterHotKey(_src.Handle, id, MOD_CONTROL | MOD_ALT, (uint)(0x30 + i))) _ids.Add(id);
            else Log.Write($"⚠ 핫키 Ctrl+Alt+{i} 등록 실패 (다른 앱이 점유)");
        }
        Log.Write($"전역 핫키 등록: Ctrl+Alt+1..9 → 프로필 1..9 ({_ids.Count}개)");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = w.ToInt32();
            _onProfile(id - 1);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _ids) UnregisterHotKey(_src.Handle, id);
        _src.Dispose();
    }
}
