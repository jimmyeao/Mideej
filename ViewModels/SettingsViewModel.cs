using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mideej.Models;
using Mideej.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace Mideej.ViewModels;

/// <summary>
/// ViewModel for the Settings window
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IConfigurationService? _configurationService;
    private readonly MainWindowViewModel _mainViewModel;

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

    public ObservableCollection<FontSizeOption> FontSizeOptions { get; } = new()
    {
        new FontSizeOption { DisplayName = "Small (80%)", Scale = 0.8 },
        new FontSizeOption { DisplayName = "Normal (100%)", Scale = 1.0 },
        new FontSizeOption { DisplayName = "Large (120%)", Scale = 1.2 },
        new FontSizeOption { DisplayName = "Extra Large (140%)", Scale = 1.4 }
    };

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public SettingsViewModel(MainWindowViewModel mainViewModel, IConfigurationService? configurationService)
    {
        _mainViewModel = mainViewModel;
        _configurationService = configurationService;

        // Load current settings from main view model
        MinimizeToTray = mainViewModel.MinimizeToTray;
        StartWithWindows = mainViewModel.StartWithWindows;
        StartMinimized = mainViewModel.StartMinimized;
        FontSizeScale = mainViewModel.FontSizeScale;

        // Set the selected font size index
        var matchingOption = FontSizeOptions.FirstOrDefault(o => Math.Abs(o.Scale - FontSizeScale) < 0.01);
        if (matchingOption != null)
        {
            SelectedFontSizeIndex = FontSizeOptions.IndexOf(matchingOption);
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

        // Save configuration
        await _mainViewModel.SaveConfigurationAsync();

        StatusMessage = "✓ Settings applied";
    }

    [RelayCommand]
    private void Close(Window? window)
    {
        window?.Close();
    }
}

public class FontSizeOption
{
    public string DisplayName { get; set; } = string.Empty;
    public double Scale { get; set; }
}
