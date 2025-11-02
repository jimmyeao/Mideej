using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mideej.Controls
{
    public partial class ColorPickerControl : UserControl
    {
        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register(
                nameof(SelectedColor),
                typeof(Color),
                typeof(ColorPickerControl),
                new FrameworkPropertyMetadata(
                    Colors.White,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedColorChanged));

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(
                nameof(Label),
                typeof(string),
                typeof(ColorPickerControl),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty ColorTextProperty =
            DependencyProperty.Register(
                nameof(ColorText),
                typeof(string),
                typeof(ColorPickerControl),
                new PropertyMetadata("#FFFFFF"));

        public Color SelectedColor
        {
            get => (Color)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public string ColorText
        {
            get => (string)GetValue(ColorTextProperty);
            set => SetValue(ColorTextProperty, value);
        }

        public ColorPickerControl()
        {
            InitializeComponent();
            UpdateColorText();
        }

        private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ColorPickerControl control)
            {
                control.UpdateColorText();
            }
        }

        private void UpdateColorText()
        {
            ColorText = $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ColorPickerDialog
            {
                Owner = Window.GetWindow(this),
                SelectedColor = SelectedColor
            };

            if (dialog.ShowDialog() == true)
            {
                SelectedColor = dialog.SelectedColor;
            }
        }
    }
}
