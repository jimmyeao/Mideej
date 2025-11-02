using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mideej.Models;
using Mideej.Services;
using Mideej.Views;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace Mideej.ViewModels;

/// <summary>
/// ViewModel for the Settings window
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IConfigurationService? _configurationService;
    private readonly MainWindowViewModel _mainViewModel;
    private readonly ThemeService _themeService;

    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private double _fontSizeScale = 1.0;

    [ObservableProperty]
    private int _selectedFontSizeIndex = 1; // Default to Normal (100%)

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    public ObservableCollection<FontSizeOption> FontSizeOptions { get; } = new()
    {
        new FontSizeOption { DisplayName = "Small (80%)", Scale = 0.8 },
        new FontSizeOption { DisplayName = "Normal (100%)", Scale = 1.0 },
        new FontSizeOption { DisplayName = "Large (120%)", Scale = 1.2 },
        new FontSizeOption { DisplayName = "Extra Large (140%)", Scale = 1.4 }
    };

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<ControllerPresetOption> ControllerPresets { get; } = new();

    public ObservableCollection<ThemeOption> AvailableThemes => _mainViewModel.AvailableThemes;

    [ObservableProperty]
    private ControllerPresetOption? _selectedPreset;

    public SettingsViewModel(MainWindowViewModel mainViewModel, IConfigurationService? configurationService)
    {
        _mainViewModel = mainViewModel;
        _configurationService = configurationService;
        _themeService = new ThemeService();

        // Load current settings from main view model
        MinimizeToTray = mainViewModel.MinimizeToTray;
        StartWithWindows = mainViewModel.StartWithWindows;
        StartMinimized = mainViewModel.StartMinimized;
        FontSizeScale = mainViewModel.FontSizeScale;
        SelectedTheme = mainViewModel.SelectedTheme;

        // Set the selected font size index
        var matchingOption = FontSizeOptions.FirstOrDefault(o => Math.Abs(o.Scale - FontSizeScale) < 0.01);
        if (matchingOption != null)
        {
            SelectedFontSizeIndex = FontSizeOptions.IndexOf(matchingOption);
        }

        // Load available controller presets
        LoadControllerPresets();
    }

    private void LoadControllerPresets()
    {
        try
        {
            var presetsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ControllerPresets");
            
            if (Directory.Exists(presetsFolder))
            {
                var jsonFiles = Directory.GetFiles(presetsFolder, "*.json");
                
                foreach (var file in jsonFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    ControllerPresets.Add(new ControllerPresetOption
                    {
                        DisplayName = fileName.Replace("-", " ").Replace("_", " "),
                        FilePath = file
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading controller presets: {ex.Message}");
        }
    }

    partial void OnSelectedPresetChanged(ControllerPresetOption? value)
    {
        if (value != null)
        {
            _ = LoadPresetAsync(value.FilePath);
        }
    }

    private async Task LoadPresetAsync(string filePath)
    {
        try
        {
            if (_configurationService == null) return;

            var config = await _configurationService.ImportControllerConfigAsync(filePath);
            
            var result = MessageBox.Show(
                $"Load preset '{config.ControllerName}'?\n\n" +
                $"MIDI Mappings: {config.MidiMappings.Count}\n" +
                $"Channels: {config.Channels?.Count ?? 0}\n\n" +
                "Replace existing mappings?",
                "Load Controller Preset",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel)
            {
                SelectedPreset = null; // Reset selection
                return;
            }

            bool replaceExisting = result == MessageBoxResult.Yes;
            
            bool applyChannels = false;
            if (config.Channels != null && config.Channels.Count > 0)
            {
                var channelResult = MessageBox.Show(
                    "Load channel configurations too?",
                    "Load Channels",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                applyChannels = channelResult == MessageBoxResult.Yes;
            }

            Console.WriteLine($"Applying config with {config.MidiMappings.Count} mappings");
            _configurationService.ApplyControllerConfig(config, replaceExisting, applyChannels);
            
            Console.WriteLine($"Config applied. CurrentSettings has {_configurationService.CurrentSettings.MidiMappings.Count} mappings");
            
            // Reload mappings and channels in main view model on UI thread
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _mainViewModel.ReloadFromConfiguration(applyChannels);
            });
            
            // Save the configuration
            await _mainViewModel.SaveConfigurationAsync();
            
            Console.WriteLine("Preset loading complete");
            StatusMessage = $"✓ Loaded preset '{config.ControllerName}' - {config.MidiMappings.Count} mappings";
            SelectedPreset = null; // Reset selection
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Failed to load preset: {ex.Message}";
            SelectedPreset = null; // Reset selection
        }
    }

    partial void OnSelectedFontSizeIndexChanged(int value)
    {
        if (value >= 0 && value < FontSizeOptions.Count)
        {
            FontSizeScale = FontSizeOptions[value].Scale;
            // Apply immediately to main view model for preview
            _mainViewModel.FontSizeScale = FontSizeScale;
        }
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value != null)
        {
            // Apply immediately to main view model for preview
            _mainViewModel.SelectedTheme = value;
        }
    }

    [RelayCommand]
    private async Task ExportControllerConfig()
    {
        try
        {
            var controllerName = _configurationService?.CurrentSettings.SelectedMidiDevice ?? "Unknown-Controller";
            var dateString = DateTime.Now.ToString("dd-MM-yyyy");
            var fileName = $"{controllerName}-{dateString}.json";
            
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Controller Config (*.json)|*.json",
                DefaultExt = "json",
                FileName = fileName
            };

            if (dialog.ShowDialog() == true)
            {
                await _configurationService.ExportControllerConfigAsync(dialog.FileName, controllerName, includeChannels: true);
                StatusMessage = $"✓ Exported to {System.IO.Path.GetFileName(dialog.FileName)}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportControllerConfig()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Controller Config (*.json)|*.json|All Files (*.*)|*.*",
                DefaultExt = "json"
            };

            if (dialog.ShowDialog() == true)
            {
                var config = await _configurationService.ImportControllerConfigAsync(dialog.FileName);
                
                var result = MessageBox.Show(
                    $"Import controller config '{config.ControllerName}'?\n\n" +
                    $"MIDI Mappings: {config.MidiMappings.Count}\n" +
                    $"Channels: {config.Channels?.Count ?? 0}\n\n" +
                    "Replace existing mappings?",
                    "Import Controller Config",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;

                bool replaceExisting = result == MessageBoxResult.Yes;
                
                bool applyChannels = false;
                if (config.Channels != null && config.Channels.Count > 0)
                {
                    var channelResult = MessageBox.Show(
                        "Import channel configurations too?",
                        "Import Channels",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    applyChannels = channelResult == MessageBoxResult.Yes;
                }

                _configurationService.ApplyControllerConfig(config, replaceExisting, applyChannels);
                
                // Notify main view model to reload
                await _mainViewModel.SaveConfigurationAsync();
                
                StatusMessage = $"✓ Imported from '{config.ControllerName}'";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Import failed: {ex.Message}";
            MessageBox.Show($"Failed to import controller config:\n{ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ManageMappings()
    {
        // Delegate to main view model
        _mainViewModel.ManageMappingsCommand.Execute(null);
    }

    [RelayCommand]
    private async Task Apply()
    {
        // Apply settings to main view model
        _mainViewModel.MinimizeToTray = MinimizeToTray;
        _mainViewModel.StartWithWindows = StartWithWindows;
        _mainViewModel.StartMinimized = StartMinimized;
        _mainViewModel.FontSizeScale = FontSizeScale;
        if (SelectedTheme != null)
        {
            _mainViewModel.SelectedTheme = SelectedTheme;
        }

        // Save configuration
        await _mainViewModel.SaveConfigurationAsync();

        StatusMessage = "✓ Settings applied";
    }

    [RelayCommand]
    private void Close(Window? window)
    {
        window?.Close();
    }

    [RelayCommand]
    private void CreateNewTheme()
    {
        try
        {
            // Create a new theme based on the current theme
            var currentTheme = _themeService.LoadThemeFromFile(SelectedTheme?.Name ?? "DarkTheme");
            if (currentTheme == null)
            {
                currentTheme = new Theme();
            }

            currentTheme.Name = "CustomTheme";
            currentTheme.DisplayName = "Custom";

            var editor = new ThemeEditorWindow(currentTheme, _themeService, () =>
            {
                // Refresh available themes in main view model
                _mainViewModel.RefreshAvailableThemes();
                StatusMessage = "Theme created successfully";
            }, isNewTheme: true);

            if (editor.ShowDialog() == true)
            {
                // After successful save, refresh the theme list
                _mainViewModel.RefreshAvailableThemes();
                StatusMessage = "Theme created successfully";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error creating theme: {ex.Message}";
        }
    }

    [RelayCommand]
    private void EditTheme()
    {
        try
        {
            if (SelectedTheme == null)
            {
                StatusMessage = "Please select a theme to edit";
                return;
            }

            var theme = _themeService.LoadThemeFromFile(SelectedTheme.Name);
            if (theme == null)
            {
                StatusMessage = $"Could not load theme '{SelectedTheme.Name}'";
                return;
            }

            var currentThemeName = SelectedTheme.Name;

            var editor = new ThemeEditorWindow(theme, _themeService, () =>
            {
                // Refresh available themes in main view model
                _mainViewModel.RefreshAvailableThemes();
            }, isNewTheme: false);

            var result = editor.ShowDialog();

            // After closing the editor (whether saved or cancelled)
            // Refresh the theme list and reapply the current theme
            _mainViewModel.RefreshAvailableThemes();

            // Reapply the theme to ensure it loads from the updated file
            var updatedTheme = _mainViewModel.AvailableThemes.FirstOrDefault(t => t.Name == currentThemeName);
            if (updatedTheme != null)
            {
                _mainViewModel.SelectedTheme = updatedTheme;
            }

            if (result == true)
            {
                StatusMessage = "Theme updated successfully";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error editing theme: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ImportTheme()
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Theme",
                Filter = "XAML files (*.xaml)|*.xaml|All files (*.*)|*.*",
                DefaultExt = ".xaml"
            };

            if (dialog.ShowDialog() == true)
            {
                var theme = _themeService.ImportTheme(dialog.FileName);
                if (theme != null)
                {
                    // Refresh available themes in main view model
                    _mainViewModel.RefreshAvailableThemes();

                    StatusMessage = $"Theme '{theme.DisplayName}' imported successfully";

                    // Select the newly imported theme
                    var importedTheme = _mainViewModel.AvailableThemes.FirstOrDefault(t => t.Name == theme.Name);
                    if (importedTheme != null)
                    {
                        SelectedTheme = importedTheme;
                        _mainViewModel.SelectedTheme = importedTheme;
                    }
                }
                else
                {
                    StatusMessage = "Failed to import theme";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import error: {ex.Message}";
        }
    }
}

public class FontSizeOption
{
    public string DisplayName { get; set; } = string.Empty;
    public double Scale { get; set; }
}

public class ControllerPresetOption
{
    public string DisplayName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}
