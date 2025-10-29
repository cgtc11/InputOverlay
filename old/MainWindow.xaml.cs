using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Forms;   // NotifyIcon / Keys
using System.Drawing;        // Icon
using System.Collections.Generic;
using System.Windows.Media;  // ScaleTransform

namespace InputOverlay
{
    public partial class MainWindow : Window
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEWHEEL = 0x020A;

        // ウィンドウスタイル
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TRANSPARENT = 0x00000020; // 既定は透過（背面へヒット）

        private static IntPtr kbHook = IntPtr.Zero;
        private static IntPtr msHook = IntPtr.Zero;

        private static LowLevelProc kbProc = HookCallback;
        private static LowLevelProc msProc = MouseHookCallback;

        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out System.Drawing.Point lpPoint);

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private static MainWindow instance;

        // ===== 公開プロパティ =====
        public bool AllowMove { get; set; } = true; // プロパティ画面から切替

        private MagnifierWindow magnifier;
        private List<PaintWindow> paintWindows = new List<PaintWindow>();
        public PropertiesWindow propertiesWindow;
        private NotifyIcon notifyIcon;

        public int MagnifierWidth { get; set; } = 1200;
        public int MagnifierHeight { get; set; } = 800;
        public double MagnifierZoom { get; set; } = 3.0;

        public System.Windows.Media.Brush PenColor { get; set; } = System.Windows.Media.Brushes.Red;
        public double PenThickness { get; set; } = 3.0;

        // === ショートカット設定 ===
        public Keys ShortcutOptions { get; set; } = Keys.F1;
        public bool UseShiftOptions { get; set; } = true;

        public Keys ShortcutVisibility { get; set; } = Keys.F2;
        public bool UseShiftVisibility { get; set; } = true;

        public Keys ShortcutPaint { get; set; } = Keys.F3;
        public bool UseShiftPaint { get; set; } = false;

        public Keys ShortcutMagnifier { get; set; } = Keys.F4;
        public bool UseShiftMagnifier { get; set; } = false;

        // === キー表示用タイマー ===
        private System.Windows.Threading.DispatcherTimer keyTimer1;
        private System.Windows.Threading.DispatcherTimer keyTimer2;
        private System.Windows.Threading.DispatcherTimer keyTimer3;

        // 右ドラッグ移動（フックで制御）
        private bool _rIsDown = false;                 // 右ボタン押下中か
        private bool _rDownInside = false;             // 押下開始がオーバーレイ上か
        private System.Drawing.Point _rStartPt;        // 押下開始スクリーン座標
        private System.Windows.Point _windowStartPt;   // 押下開始時のウィンドウ位置
        private bool _isRightDragging = false;         // いま移動中か
        private const int DRAG_THRESHOLD = 6;          // ドラッグ開始判定（px）

        // Win32 ハンドル
        private IntPtr _hwnd = IntPtr.Zero;

        // 表示タイマー用秒数
        public double KeyTextClearSeconds { get; set; } = 5;
        public double PrevKeyTextClearSeconds { get; set; } = 4.8;
        public double PrevPrevKeyTextClearSeconds { get; set; } = 4.7;

        // === UI スケール制御 ===
        /// <summary>現在の UI スケール（0.5〜3.0）</summary>
        public double UiScale { get; private set; } = 1.0;

        // スケール=1.0時の基準幅・高さ（Loaded 時に確定）
        private double _baseWidth = 0;
        private double _baseHeight = 0;
        private bool _baseSizeCaptured = false;

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

            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = Properties.Resources.InputOverlay_icon_256x256;
            notifyIcon.Text = "InputOverlay";
            notifyIcon.Visible = true;

            var menu = new ContextMenuStrip();
            menu.Items.Add("プロパティ", null, (s, e) => ShowProperties());
            menu.Items.Add("終了", null, (s, e) => this.Close());
            notifyIcon.ContextMenuStrip = menu;
            notifyIcon.DoubleClick += (s, e) => ShowProperties();

            // タイマー初期化
            keyTimer1 = new System.Windows.Threading.DispatcherTimer();
            keyTimer2 = new System.Windows.Threading.DispatcherTimer();
            keyTimer3 = new System.Windows.Threading.DispatcherTimer();

            keyTimer1.Tick += (s, e) => { KeyText.Text = ""; keyTimer1.Stop(); };
            keyTimer2.Tick += (s, e) => { PrevKeyText.Text = ""; keyTimer2.Stop(); };
            keyTimer3.Tick += (s, e) => { PrevPrevKeyText.Text = ""; keyTimer3.Stop(); };

            // スケール初期適用（Loaded 後に基準サイズ確定 → SetUiScale）
            this.Loaded += (s, e) =>
            {
                CaptureBaseSizeIfNeeded();
                SetUiScale(UiScale);
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;

            // 枠なし
            int style = GetWindowLong(_hwnd, GWL_STYLE);
            style &= ~WS_CAPTION;
            SetWindowLong(_hwnd, GWL_STYLE, style);

            // 初期状態は透過（クリックは背面へ）
            int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);

            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
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
            else
            {
                propertiesWindow.Activate();
            }
        }

        // ========= 透過の一時切替（右操作中だけ前面でヒットさせる） =========
        private void SetHitTestVisible(bool enable)
        {
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            if (enable)
                ex &= ~WS_EX_TRANSPARENT;   // 透過OFF → このウインドウでヒット
            else
                ex |= WS_EX_TRANSPARENT;    // 透過ON → 背面へ抜ける
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }

        // ========= キーボードフック =========
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                var key = (System.Windows.Forms.Keys)Marshal.ReadInt32(lParam);

                // ショートカット処理…
                if (key == instance.ShortcutOptions &&
                    (!instance.UseShiftOptions || (Control.ModifierKeys & Keys.Shift) == Keys.Shift))
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
                        else
                        {
                            main.propertiesWindow.Close();
                            main.propertiesWindow = null;
                        }
                    });
                }
                else if (key == instance.ShortcutVisibility &&
                         (!instance.UseShiftVisibility || (Control.ModifierKeys & Keys.Shift) == Keys.Shift))
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var main = instance;
                        if (main.Visibility == Visibility.Visible) main.Hide();
                        else main.Show();
                    });
                }
                else if (key == instance.ShortcutPaint &&
                         (!instance.UseShiftPaint || (Control.ModifierKeys & Keys.Shift) == Keys.Shift))
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
                                pw.Owner = null;
                                pw.Show();
                                main.paintWindows.Add(pw);
                            }
                        }
                        else
                        {
                            foreach (var pw in main.paintWindows) pw.Close();
                            main.paintWindows.Clear();
                        }
                    });
                }
                else if (key == instance.ShortcutMagnifier &&
                         (!instance.UseShiftMagnifier || (Control.ModifierKeys & Keys.Shift) == Keys.Shift))
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var main = instance;
                        if (main.magnifier == null || !main.magnifier.IsVisible)
                        {
                            main.magnifier = new MagnifierWindow();
                            main.magnifier.Owner = main;
                            main.magnifier.Show();

                            main.magnifier.CaptureAtCursor(main.MagnifierWidth, main.MagnifierHeight, main.MagnifierZoom);
                            main.magnifier.Width = main.MagnifierWidth;
                            main.magnifier.Height = main.MagnifierHeight;

                            GetCursorPos(out System.Drawing.Point cursor);
                            double winLeft = cursor.X - main.MagnifierWidth / 2;
                            double winTop = cursor.Y - main.MagnifierHeight / 2;

                            var screen = Screen.FromPoint(cursor);
                            if (winLeft < screen.Bounds.Left) winLeft = screen.Bounds.Left;
                            if (winTop < screen.Bounds.Top) winTop = screen.Bounds.Top;
                            if (winLeft + main.MagnifierWidth > screen.Bounds.Right) winLeft = screen.Bounds.Right - main.MagnifierWidth;
                            if (winTop + main.MagnifierHeight > screen.Bounds.Bottom) winTop = screen.Bounds.Bottom - main.MagnifierHeight;

                            main.magnifier.Left = winLeft;
                            main.magnifier.Top = winTop;
                        }
                        else
                        {
                            main.magnifier.Close();
                            main.magnifier = null;
                        }
                    });
                }
                else
                {
                    // === 通常キーの表示 ===
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        string prefix = "";
                        if ((Control.ModifierKeys & Keys.Control) == Keys.Control) prefix += "Ctrl+";
                        if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift) prefix += "Shift+";
                        if ((Control.ModifierKeys & Keys.Alt) == Keys.Alt || (Control.ModifierKeys & Keys.Menu) == Keys.Menu)
                            prefix += "Alt+";

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

                        instance.keyTimer1.Stop();
                        instance.keyTimer2.Stop();
                        instance.keyTimer3.Stop();

                        if (!string.IsNullOrEmpty(instance.KeyText.Text))
                        {
                            instance.keyTimer1.Interval = TimeSpan.FromSeconds(instance.KeyTextClearSeconds);
                            instance.keyTimer1.Start();
                        }
                        if (!string.IsNullOrEmpty(instance.PrevKeyText.Text))
                        {
                            instance.keyTimer2.Interval = TimeSpan.FromSeconds(instance.PrevKeyTextClearSeconds);
                            instance.keyTimer2.Start();
                        }
                        if (!string.IsNullOrEmpty(instance.PrevPrevKeyText.Text))
                        {
                            instance.keyTimer3.Interval = TimeSpan.FromSeconds(instance.PrevPrevKeyTextClearSeconds);
                            instance.keyTimer3.Start();
                        }
                    });
                }
            }

            return CallNextHookEx(kbHook, nCode, wParam, lParam);
        }

        // ========= マウスフック =========
        private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                // 表示テキスト更新
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
                    if (delta > 0) state = "🖱:□▲□";
                    else if (delta < 0) state = "🖱:□▼□";

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        instance.MouseText.Text = state;
                        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                        timer.Tick += (s, e2) => { instance.MouseText.Text = "🖱:□□□"; timer.Stop(); };
                        timer.Start();
                    });

                    return CallNextHookEx(msHook, nCode, wParam, lParam);
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    instance.MouseText.Text = state;
                });

                // === 右ドラッグ移動ロジック（AllowMove で固定可） ===
                var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                // 押下イベント
                if (wParam == (IntPtr)WM_RBUTTONDOWN)
                {
                    bool inside = false;
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        double left = instance.Left;
                        double top = instance.Top;
                        double right = instance.Left + instance.ActualWidth;
                        double bottom = instance.Top + instance.ActualHeight;
                        inside = (ms.pt.x >= left && ms.pt.x < right && ms.pt.y >= top && ms.pt.y < bottom);
                    });

                    instance._rIsDown = true;
                    instance._rDownInside = inside;
                    instance._rStartPt = new System.Drawing.Point(ms.pt.x, ms.pt.y);
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        instance._windowStartPt = new System.Windows.Point(instance.Left, instance.Top);
                        if (inside)
                        {
                            // 右操作中のみ透過OFF＝背面に抜けない
                            instance.SetHitTestVisible(true);
                        }
                    });

                    if (inside) return (IntPtr)1; // OSの右クリックメニュー等を出さない
                }
                // 移動イベント（MOVEは食わない）
                else if (wParam == (IntPtr)WM_MOUSEMOVE)
                {
                    if (instance._rIsDown && instance._rDownInside)
                    {
                        int dx = ms.pt.x - instance._rStartPt.X;
                        int dy = ms.pt.y - instance._rStartPt.Y;

                        if (!instance._isRightDragging)
                        {
                            if (Math.Abs(dx) >= DRAG_THRESHOLD || Math.Abs(dy) >= DRAG_THRESHOLD)
                            {
                                instance._isRightDragging = true; // ドラッグ開始
                            }
                        }

                        // AllowMove=true のときのみ実際に動かす
                        if (instance._isRightDragging && instance.AllowMove)
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                instance.Left = instance._windowStartPt.X + dx;
                                instance.Top = instance._windowStartPt.Y + dy;
                            });
                            // return しない：カーソルはOS側で通常通り更新
                        }
                        // AllowMove=false のときは位置更新しない（固定）
                    }
                }
                // 離しイベント
                else if (wParam == (IntPtr)WM_RBUTTONUP)
                {
                    bool wasInside = instance._rDownInside;

                    instance._rIsDown = false;
                    instance._rDownInside = false;
                    instance._isRightDragging = false;

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        // 右操作終了：透過ONへ戻す
                        instance.SetHitTestVisible(false);
                    });

                    if (wasInside) return (IntPtr)1; // 単発右クリックも背面へ渡さない
                }
            }

            return CallNextHookEx(msHook, nCode, wParam, lParam);
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (kbHook != IntPtr.Zero) UnhookWindowsHookEx(kbHook);
            if (msHook != IntPtr.Zero) UnhookWindowsHookEx(msHook);

            if (notifyIcon != null)
            {
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            }

            if (paintWindows != null)
            {
                foreach (var pw in paintWindows) pw.Close();
                paintWindows.Clear();
            }
        }

        // XAML にバインド済みの右ドラッグ用ハンドラ（互換のため残置）
        private void Window_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
        private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e) { }
        private void Window_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) { }

        // ===== UI スケーリング処理 =====

        /// <summary>スケール=1.0時の基準ウインドウサイズを取得</summary>
        private void CaptureBaseSizeIfNeeded()
        {
            if (_baseSizeCaptured) return;

            double w = this.ActualWidth > 0 ? this.ActualWidth : (double.IsNaN(this.Width) ? 0 : this.Width);
            double h = this.ActualHeight > 0 ? this.ActualHeight : (double.IsNaN(this.Height) ? 0 : this.Height);

            if (w <= 0) w = 800;
            if (h <= 0) h = 600;

            _baseWidth = w / Math.Max(UiScale, 0.0001);
            _baseHeight = h / Math.Max(UiScale, 0.0001);
            _baseSizeCaptured = true;
        }

        /// <summary>ウインドウ全体のスケール設定（0.5〜3.0）</summary>
        public void SetUiScale(double scale)
        {
            // 範囲クランプ
            if (scale < 0.1) scale = 0.1;
            if (scale > 3.0) scale = 3.0;
            UiScale = scale;

            CaptureBaseSizeIfNeeded();

            // ルート要素に LayoutTransform（中身を拡縮）
            if (this.Content is FrameworkElement root)
            {
                root.LayoutTransform = new ScaleTransform(UiScale, UiScale);
            }
            // ウインドウ外形も基準からスケール
            if (_baseSizeCaptured && this.SizeToContent == SizeToContent.Manual)
            {
                this.Width = Math.Max(100.0, _baseWidth * UiScale);
                this.Height = Math.Max(100.0, _baseHeight * UiScale);
            }
        }
    }
}
