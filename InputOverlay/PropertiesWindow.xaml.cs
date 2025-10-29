using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Globalization;
using Keys = System.Windows.Forms.Keys;

namespace InputOverlay
{
    public partial class PropertiesWindow : Window
    {
        private MainWindow main;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

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

            // 初期値
            ChkMovable.IsChecked = main.AllowMove;

            if (main.OverlayBorder.Background is SolidColorBrush brush)
                OpacitySlider.Value = brush.Color.A / 255.0;
            else
                OpacitySlider.Value = 0.5;

            if (FindName("TextOpacitySlider") is Slider txtSlider)
            {
                txtSlider.Value = main.TextOpacity;
                txtSlider.ValueChanged += Apply_Changed;
            }

            if (FindName("ChkShadowEnabled") is CheckBox chkShadow)
            {
                chkShadow.IsChecked = main.ShadowEnabled;
                chkShadow.Checked += Apply_Changed;
                chkShadow.Unchecked += Apply_Changed;
            }
            if (FindName("ShadowOffsetSlider") is Slider off)
            {
                off.Value = Math.Max(0, main.ShadowOffset);
                off.ValueChanged += Apply_Changed;
            }
            if (FindName("ChkOutlineEnabled") is CheckBox chkOutline)
            {
                chkOutline.IsChecked = main.OutlineEnabled;
                chkOutline.Checked += Apply_Changed;
                chkOutline.Unchecked += Apply_Changed;
            }

            MagnifierWidthBox.Text = main.MagnifierWidth.ToString();
            MagnifierHeightBox.Text = main.MagnifierHeight.ToString();
            MagnifierZoomBox.Text = main.MagnifierZoom.ToString("0.0", CultureInfo.InvariantCulture);
            PenThicknessBox.Text = main.PenThickness.ToString(CultureInfo.InvariantCulture);

            FillKeyCombo(ShortcutOptionsBox, main.ShortcutOptions);
            ChkShiftOptions.IsChecked = main.UseShiftOptions;
            FillKeyCombo(ShortcutVisibilityBox, main.ShortcutVisibility);
            ChkShiftVisibility.IsChecked = main.UseShiftVisibility;
            FillKeyCombo(ShortcutPaintBox, main.ShortcutPaint);
            ChkShiftPaint.IsChecked = main.UseShiftPaint;
            FillKeyCombo(ShortcutMagnifierBox, main.ShortcutMagnifier);
            ChkShiftMagnifier.IsChecked = main.UseShiftMagnifier;

            // ポインタ
            if (FindName("ShortcutPointerBox") is ComboBox sp) FillKeyCombo(sp, main.ShortcutPointer);
            if (FindName("ChkShiftPointer") is CheckBox spShift) spShift.IsChecked = main.UseShiftPointer;
            if (FindName("PointerSizeSlider") is Slider ps) ps.Value = main.PointerSize;
            if (FindName("PointerOpacitySlider") is Slider po) po.Value = main.PointerOpacity;
            if (FindName("ChkPointerEnabled") is CheckBox cpe) cpe.IsChecked = main.PointerEnabled;

            // 全画面ズーム
            if (FindName("ShortcutFullZoomBox") is ComboBox fzBox) FillKeyCombo(fzBox, main.ShortcutFullZoom);
            if (FindName("ChkShiftFullZoom") is CheckBox fzShift) fzShift.IsChecked = main.UseShiftFullZoom;
            if (FindName("FullZoomScaleBox") is TextBox fzScale) fzScale.Text = main.FullZoomScale.ToString("0.0", CultureInfo.InvariantCulture);

            if (FindName("WindowScaleSlider") is Slider windowScale)
            {
                windowScale.Value = main.UiScale;
                windowScale.ValueChanged += (s2, e2) => main.SetUiScale(windowScale.Value);
            }

            // 変更イベント
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

            // ポインタ
            if (FindName("ShortcutPointerBox") is ComboBox sp2) sp2.SelectionChanged += Apply_Changed;
            if (FindName("ChkShiftPointer") is CheckBox spShift2)
            {
                spShift2.Checked += Apply_Changed;
                spShift2.Unchecked += Apply_Changed;
            }
            if (FindName("PointerSizeSlider") is Slider ps2) ps2.ValueChanged += Apply_Changed;
            if (FindName("PointerOpacitySlider") is Slider po2) po2.ValueChanged += Apply_Changed;
            if (FindName("ChkPointerEnabled") is CheckBox cpe2)
            {
                cpe2.Checked += Apply_Changed;
                cpe2.Unchecked += Apply_Changed;
            }

            // 全画面ズーム
            if (FindName("ShortcutFullZoomBox") is ComboBox fzBox2) fzBox2.SelectionChanged += Apply_Changed;
            if (FindName("ChkShiftFullZoom") is CheckBox fzShift2)
            {
                fzShift2.Checked += Apply_Changed;
                fzShift2.Unchecked += Apply_Changed;
            }
            if (FindName("FullZoomScaleBox") is TextBox fzScale2) fzScale2.TextChanged += Apply_Changed;

            // テキストボックスのクリックでフォーカス
            MagnifierWidthBox.PreviewMouseDown += TextBox_PreviewMouseDown;
            MagnifierHeightBox.PreviewMouseDown += TextBox_PreviewMouseDown;
            MagnifierZoomBox.PreviewMouseDown += TextBox_PreviewMouseDown;
            PenThicknessBox.PreviewMouseDown += TextBox_PreviewMouseDown;
            FullZoomScaleBox.PreviewMouseDown += TextBox_PreviewMouseDown;

            Apply_Changed(null, EventArgs.Empty);
        }

        private void FillKeyCombo(ComboBox combo, Keys selected)
        {
            combo.Items.Clear();
            foreach (Keys k in Enum.GetValues(typeof(Keys)))
            {
                if ((k >= Keys.F1 && k <= Keys.F12) ||
                    (k >= Keys.A && k <= Keys.Z) ||
                    (k >= Keys.D0 && k <= Keys.D9))
                {
                    var item = new ComboBoxItem { Content = k.ToString(), Tag = k };
                    combo.Items.Add(item);
                    if (k == selected) combo.SelectedItem = item;
                }
            }
        }

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

        // 全角→半角、小数点/カンマ正規化
        private static string NormalizeNumber(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            s = s.Trim();
            s = s.Replace('，', ',').Replace('．', '.').Replace(",", ""); // 桁区切りは除去
            // 全角数字
            var map = "０１２３４５６７８９";
            for (int i = 0; i < map.Length; i++) s = s.Replace(map[i], (char)('0' + i));
            // 全角マイナス
            s = s.Replace('－', '-').Replace('＋', '+');
            return s;
        }

        private static bool TryParseDouble(string input, out double value)
        {
            input = NormalizeNumber(input);
            // CurrentCulture → Invariant の順で試す
            if (double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) return true;
            if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return true;
            return false;
        }

        private void Apply_Changed(object sender, EventArgs e)
        {
            if (main == null) return;

            main.AllowMove = ChkMovable.IsChecked == true;

            if (int.TryParse(NormalizeNumber(MagnifierWidthBox.Text), out int w) && w > 0) main.MagnifierWidth = w;
            if (int.TryParse(NormalizeNumber(MagnifierHeightBox.Text), out int h) && h > 0) main.MagnifierHeight = h;
            if (TryParseDouble(MagnifierZoomBox.Text, out double z) && z > 0) main.MagnifierZoom = z;

            if (PenColorBox.SelectedItem is ComboBoxItem pItem)
                main.PenColor = (Brush)new BrushConverter().ConvertFromString(pItem.Tag.ToString());
            if (TryParseDouble(PenThicknessBox.Text, out double thick) && thick > 0)
                main.PenThickness = thick;

            double alpha = OpacitySlider.Value;
            Color bg = Colors.Black; bg.A = (byte)(Math.Max(0, Math.Min(1, alpha)) * 255);
            main.OverlayBorder.Background = new SolidColorBrush(bg);

            if (FindName("ChkShadowEnabled") is CheckBox chk) main.ShadowEnabled = chk.IsChecked == true;
            if (FindName("ShadowOffsetSlider") is Slider off) main.ShadowOffset = Math.Max(0, off.Value);
            if (FindName("ChkOutlineEnabled") is CheckBox chkOutline) main.OutlineEnabled = chkOutline.IsChecked == true;

            Color baseTextColor = Colors.White;
            if (ColorPicker.SelectedItem is ComboBoxItem item)
            {
                try { baseTextColor = (Color)ColorConverter.ConvertFromString(item.Tag.ToString()); }
                catch { baseTextColor = Colors.White; }
            }
            double txtAlpha = main.TextOpacity;
            if (FindName("TextOpacitySlider") is Slider txt) txtAlpha = txt.Value;
            main.ApplyTextBrush(baseTextColor, txtAlpha);

            // 既存ショートカット
            if (ShortcutOptionsBox.SelectedItem is ComboBoxItem optItem) main.ShortcutOptions = (Keys)optItem.Tag;
            main.UseShiftOptions = ChkShiftOptions.IsChecked == true;

            if (ShortcutVisibilityBox.SelectedItem is ComboBoxItem visItem) main.ShortcutVisibility = (Keys)visItem.Tag;
            main.UseShiftVisibility = ChkShiftVisibility.IsChecked == true;

            if (ShortcutPaintBox.SelectedItem is ComboBoxItem paintItem) main.ShortcutPaint = (Keys)paintItem.Tag;
            main.UseShiftPaint = ChkShiftPaint.IsChecked == true;

            if (ShortcutMagnifierBox.SelectedItem is ComboBoxItem magItem) main.ShortcutMagnifier = (Keys)magItem.Tag;
            main.UseShiftMagnifier = ChkShiftMagnifier.IsChecked == true;

            // ポインタ
            if (FindName("ShortcutPointerBox") is ComboBox sp && sp.SelectedItem is ComboBoxItem spItem)
                main.ShortcutPointer = (Keys)spItem.Tag;
            if (FindName("ChkShiftPointer") is CheckBox spShift)
                main.UseShiftPointer = spShift.IsChecked == true;
            if (FindName("PointerSizeSlider") is Slider ps2) main.PointerSize = ps2.Value;
            if (FindName("PointerOpacitySlider") is Slider po2) main.PointerOpacity = po2.Value;
            if (FindName("ChkPointerEnabled") is CheckBox cpe) main.PointerEnabled = cpe.IsChecked == true;
            main.ApplyPointerEnabled();
            main.UpdatePointerAppearance();

            // 全画面ズーム倍率（ここを堅牢化）
            if (FindName("FullZoomScaleBox") is TextBox fzScale &&
                TryParseDouble(fzScale.Text, out double s) && s > 0.1)
            {
                main.FullZoomScale = s;
            }

            if (FindName("WindowScaleSlider") is Slider windowScale) main.SetUiScale(windowScale.Value);

            main.UpdateShadowVisuals();
        }

        // Main → UI 反映（必要時）
        public void OnPointerStateChangedFromMain(bool enabled, Keys newKey)
        {
            if (FindName("ChkPointerEnabled") is CheckBox cpe) cpe.IsChecked = enabled;
            if (FindName("ShortcutPointerBox") is ComboBox sp) FillKeyCombo(sp, newKey);
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
    }
}
