using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MyReconnector
{
    /// <summary>
    /// The RECONNECT button, in its own always-on-top window positioned over Hearthstone.
    ///
    /// Why not HDT's overlay canvas: that window is click-through by design (HDT manages its own
    /// mouse hook, see HookMouse/UnHookMouse in PluginManager's assembly), so a control parented
    /// to Core.OverlayCanvas renders correctly but never receives hover or click. Owning the
    /// window is the only way to reliably get mouse input, and it also means HDT overlay changes
    /// between versions cannot silently break the button again.
    ///
    /// Two extended styles make it behave like an overlay rather than an app window:
    ///   WS_EX_NOACTIVATE - clicking the button does not steal focus from Hearthstone, which
    ///                      would otherwise minimise or interrupt the game.
    ///   WS_EX_TOOLWINDOW - keeps it out of Alt+Tab and the taskbar.
    ///
    /// Only user32 is imported, which is not on HDT's plugin-loader blocklist.
    /// </summary>
    internal class ReconnectWindow : Window
    {
        private const double ButtonWidth = 140;
        private const double ButtonHeight = 42;
        private const double CooldownSeconds = 4;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out Rect32 lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect32
        {
            public int Left, Top, Right, Bottom;
        }

        private readonly Border _border;
        private readonly TextBlock _label;
        private readonly PluginConfig _config;

        private readonly Brush _idleBackground = new SolidColorBrush(Color.FromArgb(0xCC, 0x1A, 0x1A, 0x22));
        private readonly Brush _hoverBackground = new SolidColorBrush(Color.FromArgb(0xE6, 0x2E, 0x34, 0x38));
        private readonly Brush _unlockedBorder = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7));
        private readonly Brush _idleBorder = new SolidColorBrush(Color.FromRgb(0xFF, 0xB8, 0x00));

        private bool _coolingDown;
        private bool _dragging;
        private Point _dragStart;
        private bool _unlocked;

        /// <summary>Raised on click (when locked). Should start the reconnect.</summary>
        public event Action Clicked;
        /// <summary>Raised after a drag finishes, so the new position can be saved.</summary>
        public event Action PositionChanged;

        public bool Unlocked
        {
            get { return _unlocked; }
            set
            {
                _unlocked = value;
                _border.BorderBrush = value ? _unlockedBorder : _idleBorder;
                _label.Text = value ? "DRAG ME" : "RECONNECT";
            }
        }

        public ReconnectWindow(PluginConfig config)
        {
            _config = config;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Width = ButtonWidth;
            Height = ButtonHeight;
            Title = "MyReconnector";

            _label = new TextBlock
            {
                Text = "RECONNECT",
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            _border = new Border
            {
                Width = ButtonWidth,
                Height = ButtonHeight,
                CornerRadius = new CornerRadius(8),
                Background = _idleBackground,
                BorderBrush = _idleBorder,
                BorderThickness = new Thickness(1.5),
                Cursor = Cursors.Hand,
                Child = _label
            };
            Content = _border;

            _border.MouseEnter += (s, e) => { if (!_coolingDown) _border.Background = _hoverBackground; };
            _border.MouseLeave += (s, e) => _border.Background = _idleBackground;
            _border.MouseLeftButtonDown += OnMouseDown;
            _border.MouseMove += OnMouseMove;
            _border.MouseLeftButtonUp += OnMouseUp;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new WindowInteropHelper(this).Handle;
            int style = GetWindowLong(handle, GWL_EXSTYLE);
            SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }

        /// <summary>
        /// Keeps the window over the Hearthstone window, at the stored relative position.
        /// Falls back to the primary work area if Hearthstone has no visible window yet.
        /// </summary>
        public void UpdatePosition(IntPtr hearthstoneWindow)
        {
            if (_dragging)
                return;

            double left, top, width, height;
            if (!TryGetTargetArea(hearthstoneWindow, out left, out top, out width, out height))
                return;

            Left = left + _config.ButtonX * Math.Max(0, width - ButtonWidth);
            Top = top + _config.ButtonY * Math.Max(0, height - ButtonHeight);
        }

        /// <summary>Hearthstone's window in device-independent units, which is what Left/Top use.</summary>
        private bool TryGetTargetArea(IntPtr hearthstoneWindow, out double left, out double top,
            out double width, out double height)
        {
            var work = SystemParameters.WorkArea;
            left = work.Left;
            top = work.Top;
            width = work.Width;
            height = work.Height;

            if (hearthstoneWindow == IntPtr.Zero)
                return true;

            Rect32 r;
            if (!GetWindowRect(hearthstoneWindow, out r))
                return true;
            if (r.Right <= r.Left || r.Bottom <= r.Top)
                return true;

            // GetWindowRect is in physical pixels; WPF positions in DIPs. With the game at
            // 3840x2160 and 150% display scaling, skipping this conversion would place the
            // button 1.5x too far right and below the screen.
            var source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                var transform = source.CompositionTarget.TransformFromDevice;
                var topLeft = transform.Transform(new Point(r.Left, r.Top));
                var bottomRight = transform.Transform(new Point(r.Right, r.Bottom));
                left = topLeft.X;
                top = topLeft.Y;
                width = bottomRight.X - topLeft.X;
                height = bottomRight.Y - topLeft.Y;
            }
            else
            {
                left = r.Left;
                top = r.Top;
                width = r.Right - r.Left;
                height = r.Bottom - r.Top;
            }
            return true;
        }

        /// <summary>Shows reconnect feedback and blocks re-clicks for a few seconds.</summary>
        public void ShowStatus(string text)
        {
            _coolingDown = true;
            _label.Text = text;
            _border.Background = _idleBackground;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(CooldownSeconds) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                _coolingDown = false;
                _label.Text = Unlocked ? "DRAG ME" : "RECONNECT";
            };
            timer.Start();
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!Unlocked)
                return;
            _dragging = true;
            _dragStart = e.GetPosition(this);
            _border.CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging)
                return;

            // PointToScreen gives physical pixels; convert back so Left/Top stay in DIPs.
            var screen = PointToScreen(e.GetPosition(this));
            var source = PresentationSource.FromVisual(this);
            var point = (source != null && source.CompositionTarget != null)
                ? source.CompositionTarget.TransformFromDevice.Transform(screen)
                : screen;

            Left = point.X - _dragStart.X;
            Top = point.Y - _dragStart.Y;
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragging)
            {
                _dragging = false;
                _border.ReleaseMouseCapture();
                SavePosition();
                PositionChanged?.Invoke();
                e.Handled = true;
                return;
            }

            if (!_coolingDown && _border.IsMouseOver)
                Clicked?.Invoke();
        }

        /// <summary>Stores the position as a fraction of the game window, so it survives resolution changes.</summary>
        private void SavePosition()
        {
            double left, top, width, height;
            if (!TryGetTargetArea(FindHearthstoneWindow(), out left, out top, out width, out height))
                return;

            if (width > ButtonWidth)
                _config.ButtonX = Clamp01((Left - left) / (width - ButtonWidth));
            if (height > ButtonHeight)
                _config.ButtonY = Clamp01((Top - top) / (height - ButtonHeight));
        }

        private static double Clamp01(double v)
        {
            return v < 0 ? 0 : (v > 1 ? 1 : v);
        }

        public static IntPtr FindHearthstoneWindow()
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(
                         ReconnectorCore.Reconnect.HsProcessName))
            {
                using (p)
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                        return p.MainWindowHandle;
                }
            }
            return IntPtr.Zero;
        }
    }
}
