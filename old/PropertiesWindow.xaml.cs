using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace InputOverlay
{
    public partial class PropertiesWindow : Window
    {
        private MainWindow main;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private bool _isRightDragging = false;
        private Point _dragStartScreen;
        private double _windowStartLeft;
        private double _windowStartTop;

        public PropertiesWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            main = mainWindow;

            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = false;
            this.Background = Brushes.LightGray;
            this.ResizeMode = ResizeMode.NoResize;
            this.Topmost = true;

            this.SourceInitialized += (s, e) =>
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                exStyle |= WS_EX_TOOLWINDOW;
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle);
            };

            // 初期値反映
            ChkMovable.IsChecked = main.AllowMove;

            if (main.OverlayBorder.Background is SolidColorBrush brush)
                OpacitySlider.Value = brush.Color.A / 255.0;
            else
                OpacitySlider.Value = 0.5;

            MagnifierWidthBox.Text = main.MagnifierWidth.ToString();
            MagnifierHeightBox.Text = main.MagnifierHeight.ToString();
            MagnifierZoomBox.Text = main.MagnifierZoom.ToString("0.0");
            PenThicknessBox.Text = main.PenThickness.ToString();

            // ショートカット初期値
            FillKeyCombo(ShortcutOptionsBox, main.ShortcutOptions);
            ChkShiftOptions.IsChecked = main.UseShiftOptions;

            FillKeyCombo(ShortcutVisibilityBox, main.ShortcutVisibility);
            ChkShiftVisibility.IsChecked = main.UseShiftVisibility;

            FillKeyCombo(ShortcutPaintBox, main.ShortcutPaint);
            ChkShiftPaint.IsChecked = main.UseShiftPaint;

            FillKeyCombo(ShortcutMagnifierBox, main.ShortcutMagnifier);
            ChkShiftMagnifier.IsChecked = main.UseShiftMagnifier;

            // ★ ウィンドウ拡大スライダー
            if (FindName("WindowScaleSlider") is Slider windowScale)
            {
                windowScale.Value = main.UiScale;
                windowScale.ValueChanged += (s, e) =>
                {
                    main.SetUiScale(windowScale.Value);
                };
            }

            // イベント登録（SizeSlider は削除済み）
            ChkMovable.Checked += Apply_Changed;
            ChkMovable.Unchecked += Apply_Changed;
            OpacitySlider.ValueChanged += Apply_Changed;
            ColorPicker.SelectionChanged += Apply_Changed;
            MagnifierWidthBox.TextChanged += Apply_Changed;
            MagnifierHeightBox.TextChanged += Apply_Changed;
            MagnifierZoomBox.TextChanged += Apply_Changed;
            PenColorBox.SelectionChanged += Apply_Changed;
            PenThicknessBox.TextChanged += Apply_Changed;

            ShortcutOptionsBox.SelectionChanged += Apply_Changed;
            ShortcutVisibilityBox.SelectionChanged += Apply_Changed;
            ShortcutPaintBox.SelectionChanged += Apply_Changed;
            ShortcutMagnifierBox.SelectionChanged += Apply_Changed;

            ChkShiftOptions.Checked += Apply_Changed;
            ChkShiftOptions.Unchecked += Apply_Changed;
            ChkShiftVisibility.Checked += Apply_Changed;
            ChkShiftVisibility.Unchecked += Apply_Changed;
            ChkShiftPaint.Checked += Apply_Changed;
            ChkShiftPaint.Unchecked += Apply_Changed;
            ChkShiftMagnifier.Checked += Apply_Changed;
            ChkShiftMagnifier.Unchecked += Apply_Changed;

            MagnifierWidthBox.PreviewMouseDown += TextBox_PreviewMouseDown;
            MagnifierHeightBox.PreviewMouseDown += TextBox_PreviewMouseDown;
            MagnifierZoomBox.PreviewMouseDown += TextBox_PreviewMouseDown;
            PenThicknessBox.PreviewMouseDown += TextBox_PreviewMouseDown;
        }

        private void FillKeyCombo(System.Windows.Controls.ComboBox combo, System.Windows.Forms.Keys selected)
        {
            combo.Items.Clear();
            foreach (System.Windows.Forms.Keys k in Enum.GetValues(typeof(System.Windows.Forms.Keys)))
            {
                if ((k >= System.Windows.Forms.Keys.F1 && k <= System.Windows.Forms.Keys.F12) ||
                    (k >= System.Windows.Forms.Keys.A && k <= System.Windows.Forms.Keys.Z) ||
                    (k >= System.Windows.Forms.Keys.D0 && k <= System.Windows.Forms.Keys.D9))
                {
                    var item = new ComboBoxItem { Content = k.ToString(), Tag = k };
                    combo.Items.Add(item);
                    if (k == selected) combo.SelectedItem = item;
                }
            }
        }

        // 右ドラッグ移動
        private void PropertiesWindow_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && main.AllowMove)
            {
                _isRightDragging = true;
                _dragStartScreen = PointToScreen(e.GetPosition(this));
                _windowStartLeft = this.Left;
                _windowStartTop = this.Top;
                this.CaptureMouse();
                e.Handled = true;
            }
        }

        private void PropertiesWindow_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isRightDragging)
            {
                var cur = PointToScreen(e.GetPosition(this));
                var offsetX = cur.X - _dragStartScreen.X;
                var offsetY = cur.Y - _dragStartScreen.Y;
                this.Left = _windowStartLeft + offsetX;
                this.Top = _windowStartTop + offsetY;
            }
        }

        private void PropertiesWindow_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isRightDragging)
            {
                _isRightDragging = false;
                this.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void TextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox tb && !tb.IsFocused)
            {
                tb.Focus();
                e.Handled = true;
            }
        }

        private void Apply_Changed(object sender, EventArgs e)
        {
            if (main == null) return;

            main.AllowMove = ChkMovable.IsChecked == true;

            if (int.TryParse(MagnifierWidthBox.Text, out int w) && w > 0)
                main.MagnifierWidth = w;
            if (int.TryParse(MagnifierHeightBox.Text, out int h) && h > 0)
                main.MagnifierHeight = h;
            if (double.TryParse(MagnifierZoomBox.Text, out double z) && z > 0)
                main.MagnifierZoom = z;

            if (PenColorBox.SelectedItem is ComboBoxItem pItem)
                main.PenColor = (Brush)new BrushConverter().ConvertFromString(pItem.Tag.ToString());
            if (double.TryParse(PenThicknessBox.Text, out double thick) && thick > 0)
                main.PenThickness = thick;

            // 文字サイズ関連は削除済み

            double alpha = OpacitySlider.Value;
            Color bgColor = Colors.Black;
            bgColor.A = (byte)(alpha * 255);
            main.OverlayBorder.Background = new SolidColorBrush(bgColor);

            if (ColorPicker.SelectedItem is ComboBoxItem item)
            {
                var brush = (Brush)new BrushConverter().ConvertFromString(item.Tag.ToString());
                main.MouseText.Foreground = brush;
                main.KeyText.Foreground = brush;
                main.PrevKeyText.Foreground = brush;
            }

            // ショートカット反映
            if (ShortcutOptionsBox.SelectedItem is ComboBoxItem optItem)
                main.ShortcutOptions = (System.Windows.Forms.Keys)optItem.Tag;
            main.UseShiftOptions = ChkShiftOptions.IsChecked == true;

            if (ShortcutVisibilityBox.SelectedItem is ComboBoxItem visItem)
                main.ShortcutVisibility = (System.Windows.Forms.Keys)visItem.Tag;
            main.UseShiftVisibility = ChkShiftVisibility.IsChecked == true;

            if (ShortcutPaintBox.SelectedItem is ComboBoxItem paintItem)
                main.ShortcutPaint = (System.Windows.Forms.Keys)paintItem.Tag;
            main.UseShiftPaint = ChkShiftPaint.IsChecked == true;

            if (ShortcutMagnifierBox.SelectedItem is ComboBoxItem magItem)
                main.ShortcutMagnifier = (System.Windows.Forms.Keys)magItem.Tag;
            main.UseShiftMagnifier = ChkShiftMagnifier.IsChecked == true;

            // ウィンドウ拡大の再適用
            if (FindName("WindowScaleSlider") is Slider windowScale)
            {
                main.SetUiScale(windowScale.Value);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}
