using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mideej.Models;
using Mideej.Services;
using System;
using System.Windows;
using System.Windows.Media;

namespace Mideej.ViewModels
{
    public partial class ThemeEditorViewModel : ObservableObject
    {
        private readonly ThemeService _themeService;
        private readonly Theme _originalTheme;
        private readonly Action? _onThemeChanged;

        [ObservableProperty]
        private string _themeName = "CustomTheme";

        [ObservableProperty]
        private string _themeDisplayName = "Custom Theme";

        [ObservableProperty]
        private Color _backgroundColor;

        [ObservableProperty]
        private Color _surfaceColor;

        [ObservableProperty]
        private Color _surfaceHighlightColor;

        [ObservableProperty]
        private Color _primaryColor;

        [ObservableProperty]
        private Color _primaryDarkColor;

        [ObservableProperty]
        private Color _accentColor;

        [ObservableProperty]
        private Color _textPrimaryColor;

        [ObservableProperty]
        private Color _textSecondaryColor;

        [ObservableProperty]
        private Color _borderColor;

        [ObservableProperty]
        private Color _errorColor;

        [ObservableProperty]
        private Color _warningColor;

        [ObservableProperty]
        private Color _successColor;

        [ObservableProperty]
        private bool _isNewTheme;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public ThemeEditorViewModel(Theme theme, ThemeService themeService, Action? onThemeChanged = null, bool isNewTheme = false)
        {
            _themeService = themeService;
            _originalTheme = theme.Clone();
            _onThemeChanged = onThemeChanged;
            _isNewTheme = isNewTheme;

            // Load theme properties
            ThemeName = theme.Name;
            ThemeDisplayName = theme.DisplayName;
            BackgroundColor = theme.BackgroundColor;
            SurfaceColor = theme.SurfaceColor;
            SurfaceHighlightColor = theme.SurfaceHighlightColor;
            PrimaryColor = theme.PrimaryColor;
            PrimaryDarkColor = theme.PrimaryDarkColor;
            AccentColor = theme.AccentColor;
            TextPrimaryColor = theme.TextPrimaryColor;
            TextSecondaryColor = theme.TextSecondaryColor;
            BorderColor = theme.BorderColor;
            ErrorColor = theme.ErrorColor;
            WarningColor = theme.WarningColor;
            SuccessColor = theme.SuccessColor;

            // Subscribe to color changes for live preview
            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName?.EndsWith("Color") == true)
                {
                    ApplyLivePreview();
                }
            };
        }

        partial void OnThemeNameChanged(string value)
        {
            // Auto-generate display name from theme name if not manually set
            if (string.IsNullOrWhiteSpace(ThemeDisplayName) || ThemeDisplayName == _originalTheme.DisplayName)
            {
                ThemeDisplayName = value.Replace("Theme", "").Trim();
            }
        }

        [RelayCommand]
        private void SaveTheme()
        {
            try
            {
                // Validate theme name
                if (string.IsNullOrWhiteSpace(ThemeName))
                {
                    StatusMessage = "Theme name cannot be empty";
                    return;
                }

                // Sanitize theme name - remove spaces and special characters
                ThemeName = ThemeName.Trim();
                ThemeName = System.Text.RegularExpressions.Regex.Replace(ThemeName, @"[^a-zA-Z0-9]", "");

                if (string.IsNullOrWhiteSpace(ThemeName))
                {
                    StatusMessage = "Theme name must contain letters or numbers";
                    return;
                }

                // Ensure theme name ends with "Theme"
                if (!ThemeName.EndsWith("Theme", StringComparison.OrdinalIgnoreCase))
                {
                    ThemeName += "Theme";
                }

                // Check if name is available (excluding original theme if editing)
                if (!_themeService.IsThemeNameAvailable(ThemeName, IsNewTheme ? null : _originalTheme.Name))
                {
                    StatusMessage = $"Theme name '{ThemeName}' already exists";
                    return;
                }

                var theme = new Theme
                {
                    Name = ThemeName,
                    DisplayName = ThemeDisplayName,
                    BackgroundColor = BackgroundColor,
                    SurfaceColor = SurfaceColor,
                    SurfaceHighlightColor = SurfaceHighlightColor,
                    PrimaryColor = PrimaryColor,
                    PrimaryDarkColor = PrimaryDarkColor,
                    AccentColor = AccentColor,
                    TextPrimaryColor = TextPrimaryColor,
                    TextSecondaryColor = TextSecondaryColor,
                    BorderColor = BorderColor,
                    ErrorColor = ErrorColor,
                    WarningColor = WarningColor,
                    SuccessColor = SuccessColor
                };

                if (_themeService.SaveTheme(theme))
                {
                    StatusMessage = $"Theme '{ThemeDisplayName}' saved successfully";
                    _onThemeChanged?.Invoke();
                }
                else
                {
                    StatusMessage = "Failed to save theme";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        [RelayCommand]
        private void ResetToOriginal()
        {
            BackgroundColor = _originalTheme.BackgroundColor;
            SurfaceColor = _originalTheme.SurfaceColor;
            SurfaceHighlightColor = _originalTheme.SurfaceHighlightColor;
            PrimaryColor = _originalTheme.PrimaryColor;
            PrimaryDarkColor = _originalTheme.PrimaryDarkColor;
            AccentColor = _originalTheme.AccentColor;
            TextPrimaryColor = _originalTheme.TextPrimaryColor;
            TextSecondaryColor = _originalTheme.TextSecondaryColor;
            BorderColor = _originalTheme.BorderColor;
            ErrorColor = _originalTheme.ErrorColor;
            WarningColor = _originalTheme.WarningColor;
            SuccessColor = _originalTheme.SuccessColor;

            StatusMessage = "Reset to original colors";
        }

        [RelayCommand]
        private void ExportTheme()
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export Theme",
                    Filter = "XAML files (*.xaml)|*.xaml|All files (*.*)|*.*",
                    FileName = $"{ThemeName}.xaml",
                    DefaultExt = ".xaml"
                };

                if (dialog.ShowDialog() == true)
                {
                    var theme = new Theme
                    {
                        Name = ThemeName,
                        DisplayName = ThemeDisplayName,
                        BackgroundColor = BackgroundColor,
                        SurfaceColor = SurfaceColor,
                        SurfaceHighlightColor = SurfaceHighlightColor,
                        PrimaryColor = PrimaryColor,
                        PrimaryDarkColor = PrimaryDarkColor,
                        AccentColor = AccentColor,
                        TextPrimaryColor = TextPrimaryColor,
                        TextSecondaryColor = TextSecondaryColor,
                        BorderColor = BorderColor,
                        ErrorColor = ErrorColor,
                        WarningColor = WarningColor,
                        SuccessColor = SuccessColor
                    };

                    if (_themeService.ExportTheme(theme, dialog.FileName))
                    {
                        StatusMessage = "Theme exported successfully";
                    }
                    else
                    {
                        StatusMessage = "Failed to export theme";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Export error: {ex.Message}";
            }
        }

        private void ApplyLivePreview()
        {
            try
            {
                var resources = Application.Current.Resources;

                // Update colors
                resources["BackgroundColor"] = BackgroundColor;
                resources["SurfaceColor"] = SurfaceColor;
                resources["SurfaceHighlightColor"] = SurfaceHighlightColor;
                resources["PrimaryColor"] = PrimaryColor;
                resources["PrimaryDarkColor"] = PrimaryDarkColor;
                resources["AccentColor"] = AccentColor;
                resources["TextPrimaryColor"] = TextPrimaryColor;
                resources["TextSecondaryColor"] = TextSecondaryColor;
                resources["BorderColor"] = BorderColor;
                resources["ErrorColor"] = ErrorColor;
                resources["WarningColor"] = WarningColor;
                resources["SuccessColor"] = SuccessColor;

                // Update brushes
                resources["BackgroundBrush"] = new SolidColorBrush(BackgroundColor);
                resources["SurfaceBrush"] = new SolidColorBrush(SurfaceColor);
                resources["SurfaceHighlightBrush"] = new SolidColorBrush(SurfaceHighlightColor);
                resources["PrimaryBrush"] = new SolidColorBrush(PrimaryColor);
                resources["PrimaryDarkBrush"] = new SolidColorBrush(PrimaryDarkColor);
                resources["AccentBrush"] = new SolidColorBrush(AccentColor);
                resources["TextPrimaryBrush"] = new SolidColorBrush(TextPrimaryColor);
                resources["TextSecondaryBrush"] = new SolidColorBrush(TextSecondaryColor);
                resources["TextBrush"] = new SolidColorBrush(TextPrimaryColor);
                resources["BorderBrush"] = new SolidColorBrush(BorderColor);
                resources["ErrorBrush"] = new SolidColorBrush(ErrorColor);
                resources["WarningBrush"] = new SolidColorBrush(WarningColor);
                resources["SuccessBrush"] = new SolidColorBrush(SuccessColor);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying live preview: {ex.Message}");
            }
        }
    }
}
