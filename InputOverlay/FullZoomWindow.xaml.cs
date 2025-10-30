using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace InputOverlay
{
    public partial class FullZoomWindow : Window
    {
        // ===== Win32 cursor control =====
        [DllImport("user32.dll")]
        private static extern int ShowCursor(bool bShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        private int _hideCount = 0;

        private void HideGlobalCursor()
        {
            Mouse.OverrideCursor = Cursors.None;
            this.Cursor = Cursors.None;
            while (ShowCursor(false) >= 0)
            {
                _hideCount++;
            }
        }

        private void RestoreGlobalCursor()
        {
            while (_hideCount > 0)
            {
                ShowCursor(true);
                _hideCount--;
            }
            Mouse.OverrideCursor = null;
            this.Cursor = Cursors.Arrow;
        }

        // ===== Magnification API =====
        private const string MAG_DLL = "Magnification.dll";

        [DllImport(MAG_DLL, ExactSpelling = true, SetLastError = true)]
        private static extern bool MagInitialize();

        [DllImport(MAG_DLL, ExactSpelling = true, SetLastError = true)]
        private static extern bool MagUninitialize();

        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const string WC_MAGNIFIER = "Magnifier";

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateWindowEx(
            int exStyle, string className, string windowName,
            int style, int x, int y, int width, int height,
            IntPtr parent, IntPtr hMenu, IntPtr hInst, IntPtr pvParam);

        [DllImport(MAG_DLL, ExactSpelling = true, SetLastError = true)]
        private static extern bool MagSetWindowSource(IntPtr hwndMag, RECT rect);

        [DllImport(MAG_DLL, ExactSpelling = true, SetLastError = true)]
        private static extern bool MagSetWindowTransform(IntPtr hwndMag, ref MAGTRANSFORM pTransform);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MAGTRANSFORM
        {
            public float m00; public float m01; public float m02;
            public float m10; public float m11; public float m12;
            public float m20; public float m21; public float m22;
        }

        private HwndSource _src;
        private IntPtr _hwndMag = IntPtr.Zero;

        // zoom state
        private double _scale = 2.0;
        private RECT _srcRect;
        private bool _dragging = false;
        private POINT _dragStart;
        private int _viewW;
        private int _viewH;
        private double _cx = double.NaN;
        private double _cy = double.NaN;
        private double _vx = 0;
        private double _vy = 0;
        private const double W = 10.0;
        private const double ZETA = 1.0;
        private const double MAX_SPEED = 6000.0;
        private const double APPLY_EPS = 0.05;
        private TimeSpan _lastTs = TimeSpan.Zero;

        // 2回目の初期反映をするかどうか
        private bool _needSecondInit = true;

        public FullZoomWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 1) マウス位置を取得
            GetCursorPos(out POINT cur);

            // 2) マウスがいるモニタの矩形
            var scr = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point(cur.X, cur.Y)
            ).Bounds;

            // 3) ウインドウをそのモニタに合わせる
            Left = scr.Left;
            Top = scr.Top;
            Width = scr.Width;
            Height = scr.Height;

            var hwnd = new WindowInteropHelper(this).Handle;
            _src = HwndSource.FromHwnd(hwnd);
            _src.CompositionTarget.BackgroundColor = Colors.Transparent;

            // クリック透過を外す
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            if ((ex & WS_EX_TRANSPARENT) != 0)
            {
                ex &= ~WS_EX_TRANSPARENT;
                SetWindowLong(hwnd, GWL_EXSTYLE, ex);
            }

            HideGlobalCursor();

            MagInitialize();
            _hwndMag = CreateWindowEx(
                0,
                WC_MAGNIFIER,
                "mag",
                WS_CHILD | WS_VISIBLE,
                0,
                0,
                scr.Width,
                scr.Height,
                hwnd,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero
            );

            // 初期中心をマウス位置に
            _cx = cur.X;
            _cy = cur.Y;

            // ここで一度反映（1回目）
            ApplyInitialView(cur);

            // Renderingで2回目をかぶせる
            CompositionTarget.Rendering += OnRendering;
        }

        private void ApplyInitialView(POINT cur)
        {
            // 仮想スクリーン（=全モニタの最小/最大）を取る
            var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            // マウスがいるモニタ
            var scr = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point(cur.X, cur.Y)
            ).Bounds;

            // 表示領域は「マウスがいるモニタのサイズ / scale」
            _viewW = (int)(scr.Width / _scale);
            _viewH = (int)(scr.Height / _scale);

            var mat = ScaleMatrix(_scale);
            MagSetWindowTransform(_hwndMag, ref mat);

            // カーソル中心でrectを作る
            var rect = RectFromCenter(cur.X, cur.Y, _viewW, _viewH);

            // クランプは仮想スクリーンでやる（ここがあなたの2番）
            ClampToVirtualScreen(ref rect, vs);

            _srcRect = rect;
            MagSetWindowSource(_hwndMag, _srcRect);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            CompositionTarget.Rendering -= OnRendering;
            _src?.Dispose();
            MagUninitialize();
            RestoreGlobalCursor();
        }

        private static MAGTRANSFORM ScaleMatrix(double s)
        {
            return new MAGTRANSFORM
            {
                m00 = (float)s,
                m11 = (float)s,
                m22 = 1f,
                m01 = 0,
                m02 = 0,
                m10 = 0,
                m12 = 0,
                m20 = 0,
                m21 = 0
            };
        }

        private static RECT RectFromCenter(int cx, int cy, int w, int h)
        {
            int l = cx - w / 2;
            int t = cy - h / 2;
            return new RECT
            {
                Left = l,
                Top = t,
                Right = l + w,
                Bottom = t + h
            };
        }

        private static void ClampToVirtualScreen(ref RECT r, System.Drawing.Rectangle vs)
        {
            if (r.Left < vs.Left) { r.Right += vs.Left - r.Left; r.Left = vs.Left; }
            if (r.Top < vs.Top) { r.Bottom += vs.Top - r.Top; r.Top = vs.Top; }
            if (r.Right > vs.Right) { r.Left -= (r.Right - vs.Right); r.Right = vs.Right; }
            if (r.Bottom > vs.Bottom) { r.Top -= (r.Bottom - vs.Bottom); r.Bottom = vs.Bottom; }
        }

        private void OnRendering(object sender, EventArgs e)
        {
            // ここが3番目のポイント：Loadedのあと、Renderingの最初でもう一回かぶせる
            if (_needSecondInit)
            {
                GetCursorPos(out POINT cur);
                ApplyInitialView(cur);
                _needSecondInit = false;
                // ここでreturnしておくとこのフレームはスプリングしない
                return;
            }

            if (_dragging) return;

            var args = (RenderingEventArgs)e;
            if (_lastTs == TimeSpan.Zero)
            {
                _lastTs = args.RenderingTime;
                return;
            }

            double dt = (args.RenderingTime - _lastTs).TotalSeconds;
            _lastTs = args.RenderingTime;
            if (dt <= 0 || dt > 0.1) return;

            GetCursorPos(out POINT p);

            double ex = _cx - p.X;
            double ey = _cy - p.Y;

            double ax = -2.0 * ZETA * W * _vx - (W * W) * ex;
            double ay = -2.0 * ZETA * W * _vy - (W * W) * ey;

            _vx += ax * dt;
            _vy += ay * dt;

            double v = Math.Sqrt(_vx * _vx + _vy * _vy);
            if (v > MAX_SPEED)
            {
                double s = MAX_SPEED / Math.Max(v, 1e-6);
                _vx *= s;
                _vy *= s;
            }

            _cx += _vx * dt;
            _cy += _vy * dt;

            ApplyCenter();
        }

        private void ApplyCenter(bool force = false)
        {
            GetCursorPos(out POINT cur);
            var vs = System.Windows.Forms.SystemInformation.VirtualScreen;

            var rect = RectFromCenter(
                (int)Math.Round(_cx),
                (int)Math.Round(_cy),
                _viewW,
                _viewH);

            ClampToVirtualScreen(ref rect, vs);

            if (!force)
            {
                double dx = rect.Left - _srcRect.Left;
                double dy = rect.Top - _srcRect.Top;
                if (dx * dx + dy * dy < APPLY_EPS * APPLY_EPS) return;
            }

            _srcRect = rect;
            MagSetWindowSource(_hwndMag, _srcRect);
        }

        private void FullZoomWindow_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _scale = e.Delta > 0
                ? Math.Min(8.0, _scale * 1.1)
                : Math.Max(1.0, _scale / 1.1);

            GetCursorPos(out POINT p);
            _cx = p.X;
            _cy = p.Y;
            _vx = 0;
            _vy = 0;
            // Wheelのときもカーソルがいるモニタをベースにしつつ
            // クランプは仮想スクリーンでする
            var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            _viewW = (int)(System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y)).Bounds.Width / _scale);
            _viewH = (int)(System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y)).Bounds.Height / _scale);

            var mat = ScaleMatrix(_scale);
            MagSetWindowTransform(_hwndMag, ref mat);

            var rect = RectFromCenter(p.X, p.Y, _viewW, _viewH);
            ClampToVirtualScreen(ref rect, vs);
            _srcRect = rect;
            MagSetWindowSource(_hwndMag, _srcRect);
        }

        private void FullZoomWindow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _dragging = true;
                GetCursorPos(out _dragStart);
                _vx = 0;
                _vy = 0;
            }
        }

        private void FullZoomWindow_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;

            GetCursorPos(out POINT cur);
            int dx = cur.X - _dragStart.X;
            int dy = cur.Y - _dragStart.Y;

            _srcRect.Left -= dx;
            _srcRect.Right -= dx;
            _srcRect.Top -= dy;
            _srcRect.Bottom -= dy;

            var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            ClampToVirtualScreen(ref _srcRect, vs);
            MagSetWindowSource(_hwndMag, _srcRect);

            _cx = (_srcRect.Left + _srcRect.Right) / 2.0;
            _cy = (_srcRect.Top + _srcRect.Bottom) / 2.0;

            _dragStart = cur;
        }

        private void FullZoomWindow_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _dragging = false;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        }

        private void Window_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // デスクトップのコンテキストメニューを出さない
            e.Handled = true;
        }
    }
}
