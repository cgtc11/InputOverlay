using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace InputOverlay
{
    public partial class MagnifierWindow : Window
    {
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out System.Drawing.Point lpPoint);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DeleteObject(IntPtr hObject);

        // Win32 API 定義（オーバーレイ用）
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        // === 右ドラッグ移動用フィールド ===
        private bool _isRightDragging = false;
        private System.Windows.Point _dragStartPoint;
        private System.Windows.Point _windowStartPoint;

        public MagnifierWindow()
        {
            InitializeComponent();

            // オーバーレイ設定
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.ResizeMode = ResizeMode.NoResize;
            this.Topmost = true;
            this.Background = System.Windows.Media.Brushes.Transparent;

            // タスクバー非表示
            this.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                exStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
            };
        }

        // === 右ドラッグでウィンドウ移動 ===
        private void Window_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isRightDragging = true;
            _dragStartPoint = PointToScreen(e.GetPosition(this));
            _windowStartPoint = new System.Windows.Point(this.Left, this.Top);

            this.CaptureMouse();
            e.Handled = true;
        }

        private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isRightDragging)
            {
                var current = PointToScreen(e.GetPosition(this));
                this.Left = _windowStartPoint.X + (current.X - _dragStartPoint.X);
                this.Top = _windowStartPoint.Y + (current.Y - _dragStartPoint.Y);
            }
        }

        private void Window_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isRightDragging)
            {
                _isRightDragging = false;
                this.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        // === 一度だけキャプチャして拡大表示 ===
        public void CaptureAtCursor(int width, int height, double zoom)
        {
            GetCursorPos(out System.Drawing.Point cursor);

            int captureWidth = (int)(width / zoom);
            int captureHeight = (int)(height / zoom);

            int x = cursor.X - captureWidth / 2;
            int y = cursor.Y - captureHeight / 2;

            using (Bitmap bmp = new Bitmap(captureWidth, captureHeight))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(x, y, 0, 0, bmp.Size);

                var hBitmap = bmp.GetHbitmap();
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromWidthAndHeight(width, height));

                    MagnifierImage.Source = source;
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
        }
    }
}
