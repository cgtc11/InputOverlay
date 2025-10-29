using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace InputOverlay
{
    public partial class PaintWindow : Window
    {
        // === Win32 API 用 ===
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private Polyline currentLine;

        public PaintWindow(System.Windows.Forms.Screen targetScreen)
        {
            InitializeComponent();

            // --- ウィンドウをオーバーレイ化 ---
            this.WindowStyle = WindowStyle.None;        // タイトルバー消す
            this.AllowsTransparency = true;             // 透明化
            this.ResizeMode = ResizeMode.NoResize;      // サイズ変更不可
            this.Topmost = true;                        // 常に最前面
            this.Background = Brushes.Transparent;      // 背景透明

            // Win32 スタイル変更（非アクティブ＆タスクバー非表示）
            this.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                exStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
            };

            // === モニタごとに位置とサイズを指定 ===
            this.WindowState = WindowState.Normal; // Maximizedではなく手動指定
            this.Left = targetScreen.Bounds.Left;
            this.Top = targetScreen.Bounds.Top;
            this.Width = targetScreen.Bounds.Width;
            this.Height = targetScreen.Bounds.Height;

            this.Cursor = Cursors.Cross;
        }

        private void DrawCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var main = (MainWindow)System.Windows.Application.Current.MainWindow;

                currentLine = new Polyline
                {
                    Stroke = main.PenColor,
                    StrokeThickness = main.PenThickness,
                    StrokeLineJoin = PenLineJoin.Round
                };
                currentLine.Points.Add(e.GetPosition(DrawCanvas));
                DrawCanvas.Children.Add(currentLine);
            }
        }

        private void DrawCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (currentLine != null && e.LeftButton == MouseButtonState.Pressed)
            {
                currentLine.Points.Add(e.GetPosition(DrawCanvas));
            }
        }

        private void DrawCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            currentLine = null;
        }
    }
}
