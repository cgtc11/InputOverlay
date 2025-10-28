using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace InputOverlay
{
    public partial class PointerWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public PointerWindow()
        {
            InitializeComponent();
            this.Topmost = true;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.IsHitTestVisible = false; // WPF側でも透過

            this.SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT; // ★完全パススルー
                SetWindowLong(hwnd, GWL_EXSTYLE, ex);
            };
        }

        public void SetAppearance(double diameter, double opacity, Brush fill)
        {
            if (PointerEllipse == null) return;
            PointerEllipse.Width = diameter;
            PointerEllipse.Height = diameter;
            PointerEllipse.Opacity = Math.Max(0, Math.Min(1, opacity));
            PointerEllipse.Fill = fill ?? Brushes.Yellow;
        }

        // 画面座標で中心合わせ
        public void MoveCenterToScreenPoint(double screenX, double screenY)
        {
            double d = PointerEllipse?.Width ?? 50;
            this.Left = screenX - d / 2.0;
            this.Top = screenY - d / 2.0;
            this.Width = d;
            this.Height = d;
        }
    }
}
