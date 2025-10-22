using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Forms;   // NotifyIcon / Keys / Screen
using System.Drawing;        // Icon, Point
using System.Collections.Generic;
using System.Windows.Media;  // ScaleTransform, SolidColorBrush, TranslateTransform
using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;

namespace InputOverlay
{
    public partial class MainWindow : Window
    {
        private const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        private const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_MOUSEWHEEL = 0x020A;

        private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        private const int WS_CAPTION = 0x00C00000, WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x00000020;

        private static IntPtr kbHook = IntPtr.Zero, msHook = IntPtr.Zero;
        private static LowLevelProc kbProc = HookCallback, msProc = MouseHookCallback;
        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out System.Drawing.Point lpPoint);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_FRAMECHANGED = 0x0020;

        private static MainWindow instance;

        public bool AllowMove { get; set; } = true;

        private MagnifierWindow magnifier;
        private List<PaintWindow> paintWindows = new List<PaintWindow>();
        public PropertiesWindow propertiesWindow;
        private NotifyIcon notifyIcon;

        public int MagnifierWidth { get; set; } = 1200;
        public int MagnifierHeight { get; set; } = 800;
        public double MagnifierZoom { get; set; } = 3.0;

        public System.Windows.Media.Brush PenColor { get; set; } = System.Windows.Media.Brushes.Red;
        public double PenThickness { get; set; } = 3.0;

        public Keys ShortcutOptions { get; set; } = Keys.F1; public bool UseShiftOptions { get; set; } = true;
        public Keys ShortcutVisibility { get; set; } = Keys.F2; public bool UseShiftVisibility { get; set; } = true;
        public Keys ShortcutPaint { get; set; } = Keys.F3; public bool UseShiftPaint { get; set; } = false;
        public Keys ShortcutMagnifier { get; set; } = Keys.F4; public bool UseShiftMagnifier { get; set; } = false;

        private System.Windows.Threading.DispatcherTimer keyTimer1, keyTimer2, keyTimer3;

        private bool _rIsDown = false, _rDownInside = false, _isRightDragging = false;
        private System.Drawing.Point _rStartPt;
        private System.Windows.Point _windowStartPt;
        private const int DRAG_THRESHOLD = 6;

        private IntPtr _hwnd = IntPtr.Zero;

        public double KeyTextClearSeconds { get; set; } = 5;
        public double PrevKeyTextClearSeconds { get; set; } = 4.8;
        public double PrevPrevKeyTextClearSeconds { get; set; } = 4.7;

        public double UiScale { get; private set; } = 1.0;
        private double _baseWidth = 0, _baseHeight = 0; private bool _baseSizeCaptured = false;

        public double TextOpacity { get; set; } = 1.0;

        // 影設定
        public bool ShadowEnabled { get; set; } = false;   // 「文字に影を出す」
        public bool OutlineEnabled { get; set; } = false;  // 「縁にする」
        public double ShadowOffset { get; set; } = 1.0;

        public MainWindow()
        {
            InitializeComponent();
            instance = this;

            _hwnd = new WindowInteropHelper(this).Handle;
            var module = Process.GetCurrentProcess().MainModule;
            kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, kbProc, GetModuleHandle(module.ModuleName), 0);
            msHook = SetWindowsHookEx(WH_MOUSE_LL, msProc, GetModuleHandle(module.ModuleName), 0);

            MouseText.Foreground = System.Windows.Media.Brushes.White;
            MouseText.Text = "🖱:□□■";
            KeyText.Text = "右ドラッグ:移動";
            PrevKeyText.Text = "Shift+F1:Help";

            ApplyTextBrush(MediaColors.White, TextOpacity);

            notifyIcon = new NotifyIcon { Icon = Properties.Resources.InputOverlay_icon_256x256, Text = "InputOverlay", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("プロパティ", null, (s, e) => ShowProperties());
            menu.Items.Add("終了", null, (s, e) => this.Close());
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.DoubleClick += (s, e) => ShowProperties();

            keyTimer1 = new System.Windows.Threading.DispatcherTimer();
            keyTimer2 = new System.Windows.Threading.DispatcherTimer();
            keyTimer3 = new System.Windows.Threading.DispatcherTimer();
            keyTimer1.Tick += (s, e) => { KeyText.Text = ""; keyTimer1.Stop(); };
            keyTimer2.Tick += (s, e) => { PrevKeyText.Text = ""; keyTimer2.Stop(); };
            keyTimer3.Tick += (s, e) => { PrevPrevKeyText.Text = ""; keyTimer3.Stop(); };

            this.Loaded += (s, e) => { CaptureBaseSizeIfNeeded(); SetUiScale(UiScale); UpdateShadowVisuals(); };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;

            int style = GetWindowLong(_hwnd, GWL_STYLE); style &= ~WS_CAPTION; SetWindowLong(_hwnd, GWL_STYLE, style);
            int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE); exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT; SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }

        private void ShowProperties()
        {
            if (propertiesWindow == null || !propertiesWindow.IsVisible)
            {
                propertiesWindow = new PropertiesWindow(this);
                propertiesWindow.Owner = this;
                propertiesWindow.Closed += (s, e) => propertiesWindow = null;
                propertiesWindow.Show();
            }
            else propertiesWindow.Activate();
        }

        private void SetHitTestVisible(bool enable)
        {
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            if (enable) ex &= ~WS_EX_TRANSPARENT; else ex |= WS_EX_TRANSPARENT;
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }

        // ==== キーボード・マウスフック（省略なし、前回回答と同じ） ====
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var key = (System.Windows.Forms.Keys)Marshal.ReadInt32(lParam);

                if (key == instance.ShortcutOptions &&
                    (!instance.UseShiftOptions || (System.Windows.Forms.Control.ModifierKeys & Keys.Shift) == Keys.Shift))
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var main = instance;
                        if (main.propertiesWindow == null || !main.propertiesWindow.IsVisible)
                        {
                            main.propertiesWindow = new PropertiesWindow(main);
                            main.propertiesWindow.Owner = main;
                            main.propertiesWindow.Closed += (s, e) => main.propertiesWindow = null;
                            main.propertiesWindow.Show();
                        }
                        else { main.propertiesWindow.Close(); main.propertiesWindow = null; }
                    });
                }
                else if (key == instance.ShortcutVisibility &&
                         (!instance.UseShiftVisibility || (System.Windows.Forms.Control.ModifierKeys & Keys.Shift) == Keys.Shift))
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var main = instance;
                        if (main.Visibility == Visibility.Visible) main.Hide(); else main.Show();
                    });
                }
                else if (key == instance.ShortcutPaint &&
                         (!instance.UseShiftPaint || (System.Windows.Forms.Control.ModifierKeys & Keys.Shift) == Keys.Shift))
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var main = instance;
                        if (main.paintWindows == null || main.paintWindows.Count == 0)
                        {
                            main.paintWindows = new List<PaintWindow>();
                            foreach (var screen in Screen.AllScreens)
                            {
                                var pw = new PaintWindow(screen);
                                pw.Owner = null; pw.Show(); main.paintWindows.Add(pw);
                            }
                        }
                        else { foreach (var pw in main.paintWindows) pw.Close(); main.paintWindows.Clear(); }
                    });
                }
                else if (key == instance.ShortcutMagnifier &&
                         (!instance.UseShiftMagnifier || (System.Windows.Forms.Control.ModifierKeys & Keys.Shift) == Keys.Shift))
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var main = instance;
                        if (main.magnifier == null || !main.magnifier.IsVisible)
                        {
                            main.magnifier = new MagnifierWindow();
                            main.magnifier.Owner = main; main.magnifier.Show();

                            main.magnifier.CaptureAtCursor(main.MagnifierWidth, main.MagnifierHeight, main.MagnifierZoom);
                            main.magnifier.Width = main.MagnifierWidth; main.magnifier.Height = main.MagnifierHeight;

                            GetCursorPos(out System.Drawing.Point cursor);
                            double winLeft = cursor.X - main.MagnifierWidth / 2;
                            double winTop = cursor.Y - main.MagnifierHeight / 2;

                            var screen = Screen.FromPoint(cursor);
                            if (winLeft < screen.Bounds.Left) winLeft = screen.Bounds.Left;
                            if (winTop < screen.Bounds.Top) winTop = screen.Bounds.Top;
                            if (winLeft + main.MagnifierWidth > screen.Bounds.Right) winLeft = screen.Bounds.Right - main.MagnifierWidth;
                            if (winTop + main.MagnifierHeight > screen.Bounds.Bottom) winTop = screen.Bounds.Bottom - main.MagnifierHeight;

                            main.magnifier.Left = winLeft; main.magnifier.Top = winTop;
                        }
                        else { main.magnifier.Close(); main.magnifier = null; }
                    });
                }
                else
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        string prefix = "";
                        if ((System.Windows.Forms.Control.ModifierKeys & Keys.Control) == Keys.Control) prefix += "Ctrl+";
                        if ((System.Windows.Forms.Control.ModifierKeys & Keys.Shift) == Keys.Shift) prefix += "Shift+";
                        if ((System.Windows.Forms.Control.ModifierKeys & Keys.Alt) == Keys.Alt ||
                            (System.Windows.Forms.Control.ModifierKeys & Keys.Menu) == Keys.Menu) prefix += "Alt+";

                        string keyName = key.ToString();
                        if (key == Keys.Return) keyName = "Enter";
                        if (key == Keys.Escape) keyName = "Esc";
                        if (key == Keys.Capital) keyName = "CapsLock";
                        if (key == Keys.Menu || key == Keys.Alt || key == Keys.LMenu || key == Keys.RMenu) keyName = "Alt";
                        if (key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey) keyName = "Ctrl";
                        if (key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey) keyName = "Shift";

                        string display = prefix + keyName;

                        instance.PrevPrevKeyText.Text = instance.PrevKeyText.Text;
                        instance.PrevKeyText.Text = instance.KeyText.Text;
                        instance.KeyText.Text = display;

                        instance.keyTimer1.Stop(); instance.keyTimer2.Stop(); instance.keyTimer3.Stop();
                        instance.keyTimer1.Interval = TimeSpan.FromSeconds(instance.KeyTextClearSeconds);
                        instance.keyTimer2.Interval = TimeSpan.FromSeconds(instance.PrevKeyTextClearSeconds);
                        instance.keyTimer3.Interval = TimeSpan.FromSeconds(instance.PrevPrevKeyTextClearSeconds);
                        if (!string.IsNullOrEmpty(instance.KeyText.Text)) instance.keyTimer1.Start();
                        if (!string.IsNullOrEmpty(instance.PrevKeyText.Text)) instance.keyTimer2.Start();
                        if (!string.IsNullOrEmpty(instance.PrevPrevKeyText.Text)) instance.keyTimer3.Start();
                    });
                }
            }
            return CallNextHookEx(kbHook, nCode, wParam, lParam);
        }

        private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                string state = instance.MouseText.Text;
                if (wParam == (IntPtr)WM_LBUTTONDOWN) state = "🖱:■□□";
                else if (wParam == (IntPtr)WM_LBUTTONUP) state = "🖱:□□□";
                else if (wParam == (IntPtr)WM_RBUTTONDOWN) state = "🖱:□□■";
                else if (wParam == (IntPtr)WM_RBUTTONUP) state = "🖱:□□□";
                else if (wParam == (IntPtr)WM_MBUTTONDOWN) state = "🖱:□■□";
                else if (wParam == (IntPtr)WM_MBUTTONUP) state = "🖱:□□□";
                else if (wParam == (IntPtr)WM_MOUSEWHEEL)
                {
                    int delta = Marshal.ReadInt32(lParam, 8);
                    state = (delta > 0) ? "🖱:□▲□" : "🖱:□▼□";
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        instance.MouseText.Text = state;
                        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                        timer.Tick += (s, e2) => { instance.MouseText.Text = "🖱:□□□"; timer.Stop(); };
                        timer.Start();
                    });
                    return CallNextHookEx(msHook, nCode, wParam, lParam);
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() => { instance.MouseText.Text = state; });

                var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (wParam == (IntPtr)WM_RBUTTONDOWN)
                {
                    bool inside = false;
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        double left = instance.Left, top = instance.Top;
                        double right = instance.Left + instance.ActualWidth, bottom = instance.Top + instance.ActualHeight;
                        inside = (ms.pt.x >= left && ms.pt.x < right && ms.pt.y >= top && ms.pt.y < bottom);
                    });

                    instance._rIsDown = true; instance._rDownInside = inside; instance._rStartPt = new System.Drawing.Point(ms.pt.x, ms.pt.y);
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        instance._windowStartPt = new System.Windows.Point(instance.Left, instance.Top);
                        if (inside && instance.AllowMove) instance.SetHitTestVisible(true);
                    });
                    if (inside && instance.AllowMove) return (IntPtr)1;
                }
                else if (wParam == (IntPtr)WM_MOUSEMOVE)
                {
                    if (instance._rIsDown && instance._rDownInside)
                    {
                        int dx = ms.pt.x - instance._rStartPt.X, dy = ms.pt.y - instance._rStartPt.Y;
                        if (!instance._isRightDragging && (Math.Abs(dx) >= DRAG_THRESHOLD || Math.Abs(dy) >= DRAG_THRESHOLD)) instance._isRightDragging = true;
                        if (instance._isRightDragging && instance.AllowMove)
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                instance.Left = instance._windowStartPt.X + dx;
                                instance.Top = instance._windowStartPt.Y + dy;
                            });
                        }
                    }
                }
                else if (wParam == (IntPtr)WM_RBUTTONUP)
                {
                    bool wasInside = instance._rDownInside;
                    instance._rIsDown = false; instance._rDownInside = false; instance._isRightDragging = false;
                    System.Windows.Application.Current.Dispatcher.Invoke(() => { if (instance.AllowMove) instance.SetHitTestVisible(false); });
                    if (wasInside && instance.AllowMove) return (IntPtr)1;
                }
            }
            return CallNextHookEx(msHook, nCode, wParam, lParam);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (kbHook != IntPtr.Zero) UnhookWindowsHookEx(kbHook);
            if (msHook != IntPtr.Zero) UnhookWindowsHookEx(msHook);
            if (notifyIcon != null) { notifyIcon.Visible = false; notifyIcon.Dispose(); }
            if (paintWindows != null) { foreach (var pw in paintWindows) pw.Close(); paintWindows.Clear(); }
        }

        private void Window_MouseRightButtonDown(object s, System.Windows.Input.MouseButtonEventArgs e) { }
        private void Window_MouseMove(object s, System.Windows.Input.MouseEventArgs e) { }
        private void Window_MouseRightButtonUp(object s, System.Windows.Input.MouseButtonEventArgs e) { }

        private void CaptureBaseSizeIfNeeded()
        {
            if (_baseSizeCaptured) return;
            double w = this.ActualWidth > 0 ? this.ActualWidth : (double.IsNaN(this.Width) ? 0 : this.Width);
            double h = this.ActualHeight > 0 ? this.ActualHeight : (double.IsNaN(this.Height) ? 0 : this.Height);
            if (w <= 0) w = 800; if (h <= 0) h = 600;
            _baseWidth = w / Math.Max(UiScale, 0.0001); _baseHeight = h / Math.Max(UiScale, 0.0001); _baseSizeCaptured = true;
        }

        public void SetUiScale(double scale)
        {
            if (scale < 0.1) scale = 0.1; if (scale > 3.0) scale = 3.0; UiScale = scale;
            CaptureBaseSizeIfNeeded();
            if (this.Content is FrameworkElement root) root.LayoutTransform = new ScaleTransform(UiScale, UiScale);
            if (_baseSizeCaptured && this.SizeToContent == SizeToContent.Manual)
            {
                this.Width = Math.Max(100.0, _baseWidth * UiScale);
                this.Height = Math.Max(100.0, _baseHeight * UiScale);
            }
        }

        // ===== 文字色＋文字透過の適用 =====
        public void ApplyTextBrush(MediaColor baseColor, double opacity01)
        {
            TextOpacity = Math.Max(0, Math.Min(1, opacity01));
            var fc = baseColor; fc.A = (byte)(TextOpacity * 255);
            var mainBrush = new SolidColorBrush(fc);

            if (MouseText != null) MouseText.Foreground = mainBrush;
            if (KeyText != null) KeyText.Foreground = mainBrush;
            if (PrevKeyText != null) PrevKeyText.Foreground = mainBrush;
            if (PrevPrevKeyText != null) PrevPrevKeyText.Foreground = mainBrush;

            // ★ 影色の決定：白・黄 → 黒影／それ以外 → 白影
            bool isWhite = baseColor.R == MediaColors.White.R && baseColor.G == MediaColors.White.G && baseColor.B == MediaColors.White.B;
            bool isYellow = baseColor.R == MediaColors.Yellow.R && baseColor.G == MediaColors.Yellow.G && baseColor.B == MediaColors.Yellow.B;
            var shadowColor = (isWhite || isYellow) ? MediaColors.Black : MediaColors.White;
            shadowColor.A = (byte)(TextOpacity * 255);
            var shadowBrush = new SolidColorBrush(shadowColor);

            // 右下影
            if (MouseTextShadowPos != null) MouseTextShadowPos.Foreground = shadowBrush;
            if (KeyTextShadowPos != null) KeyTextShadowPos.Foreground = shadowBrush;
            if (PrevKeyTextShadowPos != null) PrevKeyTextShadowPos.Foreground = shadowBrush;
            if (PrevPrevKeyTextShadowPos != null) PrevPrevKeyTextShadowPos.Foreground = shadowBrush;

            // 縁用7方向
            foreach (var tb in new System.Windows.Controls.TextBlock[]
            {
                MouseTextShadowU, MouseTextShadowD, MouseTextShadowL, MouseTextShadowR, MouseTextShadowUR, MouseTextShadowUL, MouseTextShadowLD,
                KeyTextShadowU,   KeyTextShadowD,   KeyTextShadowL,   KeyTextShadowR,   KeyTextShadowUR,   KeyTextShadowUL,   KeyTextShadowLD,
                PrevKeyTextShadowU, PrevKeyTextShadowD, PrevKeyTextShadowL, PrevKeyTextShadowR, PrevKeyTextShadowUR, PrevKeyTextShadowUL, PrevKeyTextShadowLD,
                PrevPrevKeyTextShadowU, PrevPrevKeyTextShadowD, PrevPrevKeyTextShadowL, PrevPrevKeyTextShadowR, PrevPrevKeyTextShadowUR, PrevPrevKeyTextShadowUL, PrevPrevKeyTextShadowLD
            })
            {
                if (tb != null) tb.Foreground = shadowBrush;
            }

            UpdateShadowVisuals();
        }

        public void UpdateShadowVisuals()
        {
            var off = Math.Max(0, ShadowOffset);

            // 可視切替
            var visPos = ShadowEnabled ? Visibility.Visible : Visibility.Collapsed;                 // 右下影
            var vis7 = (ShadowEnabled && OutlineEnabled) ? Visibility.Visible : Visibility.Collapsed; // 追加7方向

            if (MouseTextShadowPos != null) MouseTextShadowPos.Visibility = visPos;
            if (KeyTextShadowPos != null) KeyTextShadowPos.Visibility = visPos;
            if (PrevKeyTextShadowPos != null) PrevKeyTextShadowPos.Visibility = visPos;
            if (PrevPrevKeyTextShadowPos != null) PrevPrevKeyTextShadowPos.Visibility = visPos;

            foreach (var tb in new System.Windows.Controls.TextBlock[]
            {
                MouseTextShadowU, MouseTextShadowD, MouseTextShadowL, MouseTextShadowR, MouseTextShadowUR, MouseTextShadowUL, MouseTextShadowLD,
                KeyTextShadowU,   KeyTextShadowD,   KeyTextShadowL,   KeyTextShadowR,   KeyTextShadowUR,   KeyTextShadowUL,   KeyTextShadowLD,
                PrevKeyTextShadowU, PrevKeyTextShadowD, PrevKeyTextShadowL, PrevKeyTextShadowR, PrevKeyTextShadowUR, PrevKeyTextShadowUL, PrevKeyTextShadowLD,
                PrevPrevKeyTextShadowU, PrevPrevKeyTextShadowD, PrevPrevKeyTextShadowL, PrevPrevKeyTextShadowR, PrevPrevKeyTextShadowUR, PrevPrevKeyTextShadowUL, PrevPrevKeyTextShadowLD
            })
            {
                if (tb != null) tb.Visibility = vis7;
            }

            // 変位
            void TT(System.Windows.Controls.TextBlock tb, double x, double y)
            {
                if (tb == null) return;
                if (tb.RenderTransform is TranslateTransform t) { t.X = x; t.Y = y; }
                else tb.RenderTransform = new TranslateTransform(x, y);
            }

            // 右下
            TT(MouseTextShadowPos, off, off);
            TT(KeyTextShadowPos, off, off);
            TT(PrevKeyTextShadowPos, off, off);
            TT(PrevPrevKeyTextShadowPos, off, off);

            // 7方向
            TT(MouseTextShadowU, 0, -off); TT(MouseTextShadowD, 0, off);
            TT(MouseTextShadowL, -off, 0); TT(MouseTextShadowR, off, 0);
            TT(MouseTextShadowUR, off, -off);
            TT(MouseTextShadowUL, -off, -off);
            TT(MouseTextShadowLD, -off, off);

            TT(KeyTextShadowU, 0, -off); TT(KeyTextShadowD, 0, off);
            TT(KeyTextShadowL, -off, 0); TT(KeyTextShadowR, off, 0);
            TT(KeyTextShadowUR, off, -off);
            TT(KeyTextShadowUL, -off, -off);
            TT(KeyTextShadowLD, -off, off);

            TT(PrevKeyTextShadowU, 0, -off); TT(PrevKeyTextShadowD, 0, off);
            TT(PrevKeyTextShadowL, -off, 0); TT(PrevKeyTextShadowR, off, 0);
            TT(PrevKeyTextShadowUR, off, -off);
            TT(PrevKeyTextShadowUL, -off, -off);
            TT(PrevKeyTextShadowLD, -off, off);

            TT(PrevPrevKeyTextShadowU, 0, -off); TT(PrevPrevKeyTextShadowD, 0, off);
            TT(PrevPrevKeyTextShadowL, -off, 0); TT(PrevPrevKeyTextShadowR, off, 0);
            TT(PrevPrevKeyTextShadowUR, off, -off);
            TT(PrevPrevKeyTextShadowUL, -off, -off);
            TT(PrevPrevKeyTextShadowLD, -off, off);
        }
    }
}
