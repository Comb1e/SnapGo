using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SnapGo.App.Helpers;

public class HotkeyHelper : IDisposable
{
    private readonly int _hotkeyId;
    private HwndSource? _hwndSource;
    private readonly uint _modifiers;
    private readonly uint _key;

    public event EventHandler? HotkeyPressed;

    public HotkeyHelper(int hotkeyId, uint modifiers, uint key)
    {
        _hotkeyId = hotkeyId;
        _modifiers = modifiers;
        _key = key;
    }

    public void Register(IntPtr windowHandle)
    {
        _hwndSource = HwndSource.FromHwnd(windowHandle);
        _hwndSource?.AddHook(WndProc);

        if (!NativeMethods.RegisterHotKey(windowHandle, _hotkeyId, _modifiers, _key))
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"Failed to register hotkey (modifiers=0x{_modifiers:X}, key=0x{_key:X}). Error: {error}. " +
                "The hotkey may be in use by another application.");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == _hotkeyId)
        {
            // Check if Ctrl is still held (to distinguish from other apps stealing the hotkey)
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, _hotkeyId);
            // Do NOT dispose _hwndSource — it belongs to WPF (shared MainWindow HwndSource)
            _hwndSource = null;
        }
    }
}
