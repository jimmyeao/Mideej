using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace Mideej.Controls
{
    public partial class ColorPickerDialog : Window, INotifyPropertyChanged
    {
        private Color _selectedColor;
        private byte _redValue;
        private byte _greenValue;
        private byte _blueValue;
        private string _hexValue = "#FFFFFF";
        private bool _isUpdating;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Color SelectedColor
        {
            get => _selectedColor;
            set
            {
                if (_selectedColor != value)
                {
                    _selectedColor = value;
                    OnPropertyChanged();
                    UpdateComponentsFromColor();
                }
            }
        }

        public byte RedValue
        {
            get => _redValue;
            set
            {
                if (_redValue != value)
                {
                    _redValue = value;
                    OnPropertyChanged();
                    UpdateColorFromComponents();
                }
            }
        }

        public byte GreenValue
        {
            get => _greenValue;
            set
            {
                if (_greenValue != value)
                {
                    _greenValue = value;
                    OnPropertyChanged();
                    UpdateColorFromComponents();
                }
            }
        }

        public byte BlueValue
        {
            get => _blueValue;
            set
            {
                if (_blueValue != value)
                {
                    _blueValue = value;
                    OnPropertyChanged();
                    UpdateColorFromComponents();
                }
            }
        }

        public string HexValue
        {
            get => _hexValue;
            set
            {
                if (_hexValue != value)
                {
                    _hexValue = value;
                    OnPropertyChanged();
                    UpdateColorFromHex();
                }
            }
        }

        public ColorPickerDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void UpdateComponentsFromColor()
        {
            if (_isUpdating) return;

            _isUpdating = true;
            try
            {
                _redValue = _selectedColor.R;
                _greenValue = _selectedColor.G;
                _blueValue = _selectedColor.B;
                _hexValue = $"#{_selectedColor.R:X2}{_selectedColor.G:X2}{_selectedColor.B:X2}";

                OnPropertyChanged(nameof(RedValue));
                OnPropertyChanged(nameof(GreenValue));
                OnPropertyChanged(nameof(BlueValue));
                OnPropertyChanged(nameof(HexValue));
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void UpdateColorFromComponents()
        {
            if (_isUpdating) return;

            _isUpdating = true;
            try
            {
                _selectedColor = Color.FromRgb(_redValue, _greenValue, _blueValue);
                _hexValue = $"#{_selectedColor.R:X2}{_selectedColor.G:X2}{_selectedColor.B:X2}";

                OnPropertyChanged(nameof(SelectedColor));
                OnPropertyChanged(nameof(HexValue));
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void UpdateColorFromHex()
        {
            if (_isUpdating) return;

            _isUpdating = true;
            try
            {
                var hex = _hexValue.TrimStart('#');
                if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out _))
                {
                    _selectedColor = (Color)ColorConverter.ConvertFromString("#" + hex);
                    _redValue = _selectedColor.R;
                    _greenValue = _selectedColor.G;
                    _blueValue = _selectedColor.B;

                    OnPropertyChanged(nameof(SelectedColor));
                    OnPropertyChanged(nameof(RedValue));
                    OnPropertyChanged(nameof(GreenValue));
                    OnPropertyChanged(nameof(BlueValue));
                }
            }
            catch
            {
                // Invalid hex value, ignore
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
