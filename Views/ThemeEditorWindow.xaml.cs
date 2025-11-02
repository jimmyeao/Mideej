using Mideej.Models;
using Mideej.Services;
using Mideej.ViewModels;
using System;
using System.Windows;

namespace Mideej.Views
{
    public partial class ThemeEditorWindow : Window
    {
        private readonly Theme _originalTheme;

        public ThemeEditorViewModel ViewModel { get; }

        public ThemeEditorWindow(Theme theme, ThemeService themeService, Action? onThemeChanged = null, bool isNewTheme = false)
        {
            InitializeComponent();

            _originalTheme = theme.Clone();
            ViewModel = new ThemeEditorViewModel(theme, themeService, onThemeChanged, isNewTheme);
            DataContext = ViewModel;

            // Subscribe to property changes to detect successful save
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Monitor status messages to auto-close on success
            if (e.PropertyName == nameof(ViewModel.StatusMessage) &&
                ViewModel.StatusMessage.Contains("saved successfully", StringComparison.OrdinalIgnoreCase))
            {
                // Delay close slightly to allow user to see the message
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(1000)
                };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    DialogResult = true;
                    Close();
                };
                timer.Start();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            RestoreOriginalTheme();
            DialogResult = false;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            RestoreOriginalTheme();
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                {
                    DragMove();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error moving window: {ex.Message}");
            }
        }

        private void RestoreOriginalTheme()
        {
            // Restore original colors when canceling
            try
            {
                var resources = Application.Current.Resources;

                resources["BackgroundColor"] = _originalTheme.BackgroundColor;
                resources["SurfaceColor"] = _originalTheme.SurfaceColor;
                resources["SurfaceHighlightColor"] = _originalTheme.SurfaceHighlightColor;
                resources["PrimaryColor"] = _originalTheme.PrimaryColor;
                resources["PrimaryDarkColor"] = _originalTheme.PrimaryDarkColor;
                resources["AccentColor"] = _originalTheme.AccentColor;
                resources["TextPrimaryColor"] = _originalTheme.TextPrimaryColor;
                resources["TextSecondaryColor"] = _originalTheme.TextSecondaryColor;
                resources["BorderColor"] = _originalTheme.BorderColor;
                resources["ErrorColor"] = _originalTheme.ErrorColor;
                resources["WarningColor"] = _originalTheme.WarningColor;
                resources["SuccessColor"] = _originalTheme.SuccessColor;

                resources["BackgroundBrush"] = new System.Windows.Media.SolidColorBrush(_originalTheme.BackgroundColor);
                resources["SurfaceBrush"] = new System.Windows.Media.SolidColorBrush(_originalTheme.SurfaceColor);
                resources["SurfaceHighlightBrush"] = new System.Windows.Media.SolidColorBrush(_originalTheme.SurfaceHighlightColor);
                resources["PrimaryBrush"] = new System.Windows.Media.SolidColorBrush(_originalTheme.PrimaryColor);
                resources["PrimaryDarkBrush"] = new System.Windows.Media.SolidColorBrush(_originalTheme.PrimaryDarkColor);
                resources["AccentBrush"] = new System.Windows.Media.SolidColorBrush(_originalTheme.AccentColor);
                resources["TextPrimaryBrush"] = new System.Windows.Media.SolidColorBrush(_originalTheme.TextPrimaryColor);
                resources["TextSecondaryBrush"] = new System.Windows.Media.SolidColorBrush(_originalTheme.TextSecondaryColor);
                resources["TextBrush"] = new System.Windows.Media.SolidColorBrush(_originalTheme.TextPrimaryColor);
                resources["BorderBrush"] = new System.Windows.Media.SolidColorBrush(_originalTheme.BorderColor);
                resources["ErrorBrush"] = new System.Windows.Media.SolidColorBrush(_originalTheme.ErrorColor);
                resources["WarningBrush"] = new System.Windows.Media.SolidColorBrush(_originalTheme.WarningColor);
                resources["SuccessBrush"] = new System.Windows.Media.SolidColorBrush(_originalTheme.SuccessColor);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restoring theme: {ex.Message}");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }
        }
    }
}
