using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace InputOverlay
{
    public partial class FullZoomWindow : Window
    {
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
        private struct POINT { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct MAGTRANSFORM
        {
            public float m00; public float m01; public float m02;
            public float m10; public float m11; public float m12;
            public float m20; public float m21; public float m22;
        }

        private HwndSource _src;
        private IntPtr _hwndMag = IntPtr.Zero;

        // 拡大・パン
        private double _scale = 2.0;
        private RECT _srcRect;
        private bool _dragging = false;
        private POINT _dragStart;

        // 表示領域サイズ(ソース矩形の幅高)
        private int _viewW, _viewH;

        // 追従(スプリング)
        private double _cx = double.NaN, _cy = double.NaN;   // 中心
        private double _vx = 0, _vy = 0;                     // 速度(px/s)
        private const double W = 10.0;                       // 角周波数
        private const double ZETA = 1.0;                     // 減衰比
        private const double MAX_SPEED = 6000.0;             // 速度上限(px/s)
        private const double APPLY_EPS = 0.05;               // 更新しきい値

        private TimeSpan _lastTs = TimeSpan.Zero;

        // 初回適用済みか
        private bool _initialApplied = false;

        public FullZoomWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // ① 今のマウス位置を先にとる（ここを最初に見たい）
            GetCursorPos(out POINT curPos);
            double startX = curPos.X;
            double startY = curPos.Y;

            // ② マウスがあるモニタを優先してズーム
            System.Windows.Forms.Screen targetScreen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point(curPos.X, curPos.Y)
            );
            if (targetScreen == null)
                targetScreen = System.Windows.Forms.Screen.PrimaryScreen;

            var b = targetScreen.Bounds;

            // ③ このズームウインドウをそのモニタいっぱいに
            Left = b.Left;
            Top = b.Top;
            Width = b.Width;
            Height = b.Height;

            // ④ WPFハンドルなど
            var hwnd = new WindowInteropHelper(this).Handle;
            _src = HwndSource.FromHwnd(hwnd);
            _src.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;

            // ⑤ メインウインドウ側の倍率を拾う
            if (Application.Current?.MainWindow is MainWindow mw && mw.FullZoomScale > 0.1)
                _scale = mw.FullZoomScale;

            // ⑥ 初期中心はマウス位置
            _cx = startX;
            _cy = startY;
            _vx = 0;
            _vy = 0;

            // ⑦ Magnifier生成
            MagInitialize();
            _hwndMag = CreateWindowEx(
                0,
                WC_MAGNIFIER,
                "mag",
                WS_CHILD | WS_VISIBLE,
                0,
                0,
                (int)Width,
                (int)Height,
                hwnd,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            // ⑧ まず倍率だけ反映
            _viewW = (int)(Width / _scale);
            _viewH = (int)(Height / _scale);
            var mat = ScaleMatrix(_scale);
            MagSetWindowTransform(_hwndMag, ref mat);

            // ⑨ ここで一発、マウス位置をソースにする（すぐあとでDispatcherでもう一回やる）
            var firstRect = RectFromCenter((int)Math.Round(_cx), (int)Math.Round(_cy), _viewW, _viewH);
            ClampSourceToVirtual(ref firstRect);
            _srcRect = firstRect;
            MagSetWindowSource(_hwndMag, _srcRect);

            // ⑩ 1フレーム遅らせてもう一度（「必ず左上になる」対策）
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                ForceApplyCurrentCenter();
            }));

            // ⑪ VSync駆動
            CompositionTarget.Rendering += OnRendering;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            CompositionTarget.Rendering -= OnRendering;
            _src?.Dispose();
            MagUninitialize();
        }

        private static MAGTRANSFORM ScaleMatrix(double s) => new MAGTRANSFORM
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

        private static RECT RectFromCenter(int cx, int cy, int w, int h)
        {
            int l = cx - w / 2;
            int t = cy - h / 2;
            return new RECT { Left = l, Top = t, Right = l + w, Bottom = t + h };
        }

        // マルチモニタで原点がマイナスでも動くように「仮想スクリーン」全体にクランプ
        private void ClampSourceToVirtual(ref RECT r)
        {
            int vLeft = (int)SystemParameters.VirtualScreenLeft;
            int vTop = (int)SystemParameters.VirtualScreenTop;
            int vWidth = (int)SystemParameters.VirtualScreenWidth;
            int vHeight = (int)SystemParameters.VirtualScreenHeight;

            int vRight = vLeft + vWidth;
            int vBottom = vTop + vHeight;

            if (r.Left < vLeft) { r.Right += (vLeft - r.Left); r.Left = vLeft; }
            if (r.Top < vTop) { r.Bottom += (vTop - r.Top); r.Top = vTop; }
            if (r.Right > vRight) { r.Left -= (r.Right - vRight); r.Right = vRight; }
            if (r.Bottom > vBottom) { r.Top -= (r.Bottom - vBottom); r.Bottom = vBottom; }
        }

        private void ForceApplyCurrentCenter()
        {
            _viewW = (int)(Width / _scale);
            _viewH = (int)(Height / _scale);

            var mat = ScaleMatrix(_scale);
            MagSetWindowTransform(_hwndMag, ref mat);

            var rect = RectFromCenter((int)Math.Round(_cx), (int)Math.Round(_cy), _viewW, _viewH);
            ClampSourceToVirtual(ref rect);
            _srcRect = rect;
            MagSetWindowSource(_hwndMag, _srcRect);

            _initialApplied = true;
        }

        // VSyncごとに追従更新
        private void OnRendering(object sender, EventArgs e)
        {
            // 初回だけ強制でかぶせる（Dispatcherが間に合わないPC向け）
            if (!_initialApplied)
            {
                ForceApplyCurrentCenter();
                return;
            }

            if (_dragging) return;

            var args = (RenderingEventArgs)e;
            if (_lastTs == TimeSpan.Zero) { _lastTs = args.RenderingTime; return; }
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
                _vx *= s; _vy *= s;
            }

            _cx += _vx * dt;
            _cy += _vy * dt;

            var rect = RectFromCenter((int)Math.Round(_cx), (int)Math.Round(_cy), _viewW, _viewH);
            ClampSourceToVirtual(ref rect);
            _srcRect = rect;
            MagSetWindowSource(_hwndMag, _srcRect);
        }

        // ===== マウス入力 =====
        private void FullZoomWindow_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            _scale = e.Delta > 0 ? Math.Min(8.0, _scale * 1.1) : Math.Max(1.0, _scale / 1.1);
            GetCursorPos(out POINT p);
            _cx = p.X; _cy = p.Y; _vx = 0; _vy = 0;

            _viewW = (int)(Width / _scale);
            _viewH = (int)(Height / _scale);

            var mat = ScaleMatrix(_scale);
            MagSetWindowTransform(_hwndMag, ref mat);

            var rect = RectFromCenter((int)Math.Round(_cx), (int)Math.Round(_cy), _viewW, _viewH);
            ClampSourceToVirtual(ref rect);
            _srcRect = rect;
            MagSetWindowSource(_hwndMag, _srcRect);
        }

        private void FullZoomWindow_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                _dragging = true;
                GetCursorPos(out _dragStart);
                _vx = 0; _vy = 0;
            }
        }

        private void FullZoomWindow_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_dragging) return;

            GetCursorPos(out POINT cur);
            int dx = cur.X - _dragStart.X;
            int dy = cur.Y - _dragStart.Y;

            _srcRect.Left -= dx; _srcRect.Right -= dx;
            _srcRect.Top -= dy; _srcRect.Bottom -= dy;
            ClampSourceToVirtual(ref _srcRect);
            MagSetWindowSource(_hwndMag, _srcRect);

            _cx = (_srcRect.Left + _srcRect.Right) / 2.0;
            _cy = (_srcRect.Top + _srcRect.Bottom) / 2.0;

            _dragStart = cur;
        }

        private void FullZoomWindow_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left) _dragging = false;
        }
    }
}
