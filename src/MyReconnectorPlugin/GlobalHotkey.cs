using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MyReconnector
{
    /// <summary>
    /// System-wide Ctrl+F12, so the plugin can be triggered while Hearthstone has focus —
    /// the same hotkey the standalone app registers.
    ///
    /// A hidden HwndSource is used rather than HDT's main window: that window can be closed to
    /// the tray or recreated, which would silently drop the hotkey. This window lives exactly as
    /// long as the plugin does.
    ///
    /// Only user32 is imported, which is not on HDT's plugin-loader blocklist (see the notes in
    /// ReconnectCore.cs about iphlpapi).
    /// </summary>
    internal sealed class GlobalHotkey : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HotkeyId = 0xB1A5;

        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_NOREPEAT = 0x4000;   // holding the keys fires once, not repeatedly
        private const uint VK_F12 = 0x7B;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private HwndSource _source;
        private bool _registered;

        /// <summary>Raised on the UI thread when the hotkey is pressed.</summary>
        public event Action Pressed;

        public string Description => "Ctrl+F12";

        /// <summary>
        /// Returns false if the combination is already taken by another process — most likely the
        /// standalone HsReconnector.exe running at the same time.
        /// </summary>
        public bool Register()
        {
            bool ok = false;
            RunOnUiThread(() =>
            {
                if (_registered)
                {
                    ok = true;
                    return;
                }

                if (_source == null)
                {
                    // classStyle, style, exStyle, x, y, name, parent. style 0 omits WS_VISIBLE,
                    // so the window never appears.
                    _source = new HwndSource(0, 0, 0, 0, 0, "MyReconnectorHotkey", IntPtr.Zero);
                    _source.AddHook(HwndHook);
                }

                ok = RegisterHotKey(_source.Handle, HotkeyId, MOD_CONTROL | MOD_NOREPEAT, VK_F12);
                _registered = ok;
            });
            return ok;
        }

        public void Unregister()
        {
            RunOnUiThread(() =>
            {
                if (_source != null && _registered)
                    UnregisterHotKey(_source.Handle, HotkeyId);
                _registered = false;
            });
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                handled = true;
                var pressed = Pressed;
                if (pressed != null)
                    pressed();
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            RunOnUiThread(() =>
            {
                if (_source == null)
                    return;
                if (_registered)
                    UnregisterHotKey(_source.Handle, HotkeyId);
                _registered = false;
                _source.RemoveHook(HwndHook);
                _source.Dispose();
                _source = null;
            });
        }

        /// <summary>Window creation and hotkey registration must happen on the thread with the pump.</summary>
        private static void RunOnUiThread(Action action)
        {
            var app = Application.Current;
            if (app == null || app.Dispatcher.CheckAccess())
                action();
            else
                app.Dispatcher.Invoke(action);
        }
    }
}
