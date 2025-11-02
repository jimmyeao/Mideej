using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mideej.Models;
using Mideej.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.IO;

namespace Mideej.ViewModels;

/// <summary>
/// Main window ViewModel
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IMidiService? _midiService;
    private readonly IAudioSessionManager? _audioSessionManager;
    private readonly IConfigurationService? _configurationService;
    private readonly IMediaControlService? _mediaControlService;

    // Track simple transport state for LED feedback
    private bool _isPlaying;

    [ObservableProperty]
    private string _title = "Mideej - MIDI Audio Mixer";

    [ObservableProperty]
    private bool _isMidiConnected;

    [ObservableProperty]
    private MidiDeviceInfo? _selectedMidiDevice;

    [ObservableProperty]
    private bool _isMappingModeActive;

    [ObservableProperty]
    private ChannelViewModel? _channelAwaitingMapping;

    [ObservableProperty]
    private string _controlTypeToMap = "Volume"; // Volume, Mute, Solo, Record, Select, TransportPlay, TransportPause, TransportNext, TransportPrevious

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private double _windowWidth = 1200;

    [ObservableProperty]
    private double _windowHeight = 700;

    [ObservableProperty]
    private double _windowLeft = double.NaN;

    [ObservableProperty]
    private double _windowTop = double.NaN;

    [ObservableProperty]
    private System.Windows.WindowState _windowState = System.Windows.WindowState.Normal;

    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _startMinimized;

    partial void OnStartMinimizedChanged(bool value)
    {
        // Keep the startup registry value in sync when this changes
        // Only update if StartWithWindows is enabled so we don't create an entry unintentionally
        UpdateStartupRegistry(StartWithWindows);
    }

    [ObservableProperty]
    private double _fontSizeScale = 1.0;

    partial void OnFontSizeScaleChanged(double value)
    {
        // Notify the main window to apply font size
        FontSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? FontSizeChanged;

    [ObservableProperty]
    private double _minWindowWidth = 600;

    [ObservableProperty]
    private bool _isFullScreenMode;

    partial void OnStartWithWindowsChanged(bool value)
    {
        UpdateStartupRegistry(value);
    }

    public ObservableCollection<ThemeOption> AvailableThemes { get; } = new()
{
    new ThemeOption { Name = "DarkTheme", DisplayName = "Dark 🌙" },
    new ThemeOption { Name = "LightTheme", DisplayName = "Light ☀️" },
    new ThemeOption { Name = "NordTheme", DisplayName = "Nord 🌊" },
    new ThemeOption { Name = "DraculaTheme", DisplayName = "Dracula 🦇" },
    new ThemeOption { Name = "OceanTheme", DisplayName = "Ocean 🌅" },
    new ThemeOption { Name = "SunsetTheme", DisplayName = "Sunset 🌇" },
    new ThemeOption { Name = "CyberpunkTheme", DisplayName = "Cyberpunk 🌈" },
    new ThemeOption { Name = "ForestTheme", DisplayName = "Forest 🌿" },
    new ThemeOption { Name = "ArcticTheme", DisplayName = "Arctic ❄️" },
    // Holiday themes
    new ThemeOption { Name = "HalloweenTheme", DisplayName = "Halloween 🎃" },
    new ThemeOption { Name = "ChristmasTheme", DisplayName = "Christmas 🎄" },
    new ThemeOption { Name = "DiwaliTheme", DisplayName = "Diwali 🪔" },
    new ThemeOption { Name = "HanukkahTheme", DisplayName = "Hanukkah 🕎" },
    new ThemeOption { Name = "EidTheme", DisplayName = "Eid 🌙" },
    new ThemeOption { Name = "LunarNewYearTheme", DisplayName = "Lunar New Year 🧧" },
    new ThemeOption { Name = "EasterTheme", DisplayName = "Easter 🐣" },
    new ThemeOption { Name = "NowruzTheme", DisplayName = "Nowruz 🌱" },
    new ThemeOption { Name = "RamadanTheme", DisplayName = "Ramadan 🌙" },
    new ThemeOption { Name = "PrideTheme", DisplayName = "Pride 🏳️‍🌈" }
};

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    /// <summary>
    /// MIDI mappings: Key is (channel, controller), Value is the mapping
    /// </summary>
    private readonly Dictionary<(int channel, int controller), MidiMapping> _midiMappings = new();

    /// <summary>
    /// Stores channel configurations for relinking sessions after they're loaded
    /// </summary>
    private List<ChannelConfiguration> _pendingChannelConfigs = new();

    /// <summary>
    /// Available MIDI devices
    /// </summary>
    public ObservableCollection<MidiDeviceInfo> AvailableMidiDevices { get; } = new();

    /// <summary>
    /// Audio channels
    /// </summary>
    public ObservableCollection<ChannelViewModel> Channels { get; } = new();

    /// <summary>
    /// All available audio sessions
    /// </summary>
    public ObservableCollection<AudioSessionInfo> AvailableSessions { get; } = new();

    public MainWindowViewModel()
    {
        // Design-time constructor
        if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(new System.Windows.DependencyObject()))
        {
            InitializeDesignData();
        }
    }

    public MainWindowViewModel(
        IMidiService midiService,
        IAudioSessionManager audioSessionManager,
        IConfigurationService configurationService,
        IMediaControlService mediaControlService)
    {
        _midiService = midiService;
        _audioSessionManager = audioSessionManager;
        _configurationService = configurationService;
        _mediaControlService = mediaControlService;

        Initialize();
    }

    private void Initialize()
    {
        if (_midiService != null)
        {
            _midiService.ControlChangeReceived += OnMidiControlChange;
            _midiService.NoteOnReceived += OnMidiNoteOn;
            _midiService.PitchBendReceived += OnMidiPitchBend;
            _midiService.DeviceStateChanged += OnMidiDeviceStateChanged;
            RefreshMidiDevices();
        }

        if (_audioSessionManager != null)
        {
            _audioSessionManager.SessionsChanged += OnAudioSessionsChanged;
            _audioSessionManager.PeakLevelsUpdated += OnPeakLevelsUpdated;
            _audioSessionManager.MasterMuteChanged += OnMasterMuteChanged;
            _audioSessionManager.StartMonitoring();
        }

        if (_mediaControlService != null)
        {
            _mediaControlService.PlaybackStateChanged += OnPlaybackStateChanged;
        }

        // Load configuration
        LoadConfiguration();

        // Initialize with default channels if none exist
        if (Channels.Count == 0)
        {
            for (int i = 0; i < 8; i++)
            {
                var channel = new ChannelViewModel
                {
                    Index = i,
                    Name = $"Channel {i + 1}"
                };
                SubscribeToChannelEvents(channel);
                Channels.Add(channel);
            }
        }
        // Theme will be loaded from config or default to DarkTheme
        if (SelectedTheme == null)
        {
            SelectedTheme = AvailableThemes.FirstOrDefault(t => t.Name == "DarkTheme");
            ApplyTheme(SelectedTheme);
        }
        UpdateWindowSize();
    }
    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value != null)
            ApplyTheme(value);
    }

    private void ApplyTheme(ThemeOption theme)
    {
        try
        {
            Application.Current.Resources.MergedDictionaries.Clear();

            // Base styles
            var baseStyles = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Themes/Styles.xaml", UriKind.Absolute)
            };
            Application.Current.Resources.MergedDictionaries.Add(baseStyles);

            // Selected theme
            var themeDict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Themes/{theme.Name}.xaml", UriKind.Absolute)
            };
            Application.Current.Resources.MergedDictionaries.Add(themeDict);

            StatusMessage = $"Theme applied: {theme.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error applying theme: {ex.Message}";
        }
    }

    private void InitializeDesignData()
    {
        // Add sample channels for design-time
        for (int i = 0; i < 4; i++)
        {
            Channels.Add(new ChannelViewModel
            {
                Index = i,
                Name = $"Channel {i + 1}",
                Volume = 0.7f,
                PeakLevel = 0.5f
            });
        }
    }

    [RelayCommand]
    private async Task ConnectMidiDevice()
    {
        if (SelectedMidiDevice == null || _midiService == null) return;

        StatusMessage = $"Connecting to {SelectedMidiDevice.Name}...";
        Console.WriteLine($"Attempting to connect to MIDI device: {SelectedMidiDevice.Name} (ID: {SelectedMidiDevice.DeviceId})");

        var success = await _midiService.ConnectAsync(SelectedMidiDevice.DeviceId);

        if (success)
        {
            IsMidiConnected = true;
            StatusMessage = $"✓ Connected to {SelectedMidiDevice.Name}";
            Console.WriteLine($"Successfully connected to {SelectedMidiDevice.Name}");
            Console.WriteLine("Listening for MIDI messages...");
            
            // Play startup animation if it's an M-Vave SMC mixer
            if (SelectedMidiDevice.Name.Contains("SMC", StringComparison.OrdinalIgnoreCase) ||
                SelectedMidiDevice.Name.Contains("M-Vave", StringComparison.OrdinalIgnoreCase) ||
                SelectedMidiDevice.Name.Contains("SINCO", StringComparison.OrdinalIgnoreCase))
            {
                _ = PlayStartupAnimationAndRestoreLeds();
            }
        }
        else
        {
            IsMidiConnected = false;
            StatusMessage = $"✗ Failed to connect to {SelectedMidiDevice.Name} - May be in use by another app";
            Console.WriteLine($"FAILED to connect to {SelectedMidiDevice.Name}");
            Console.WriteLine("Make sure the device is not open in another application (DAW, etc.)");
        }
    }

    [RelayCommand]
    private void DisconnectMidiDevice()
    {
        _midiService?.Disconnect();
        IsMidiConnected = false;
        StatusMessage = "MIDI device disconnected";
    }

    [RelayCommand]
    private void RefreshMidiDevices()
    {
        if (_midiService == null) return;

        AvailableMidiDevices.Clear();
        var devices = _midiService.GetAvailableDevices();

        Console.WriteLine($"Found {devices.Count} MIDI device(s):");
        foreach (var device in devices)
        {
            Console.WriteLine($"  - [{device.DeviceId}] {device.Name}");
            AvailableMidiDevices.Add(device);
        }

        if (devices.Count > 0)
        {
            Console.WriteLine("TIP: If you see multiple devices, choose the one with 'MIDIIN' in the name for input.");
        }
    }

    [RelayCommand]
    private void StartMappingMode(ChannelViewModel channel)
    {
        if (_midiService == null)
        {
            StatusMessage = "MIDI service not available";
            return;
        }

        if (!IsMidiConnected)
        {
            StatusMessage = "Please connect a MIDI device first";
            return;
        }

        ChannelAwaitingMapping = channel;
        channel.IsInMappingMode = true;
        IsMappingModeActive = true;
        _midiService.StartMappingMode();
        StatusMessage = $"MAPPING MODE: Move a MIDI control to map to {channel.Name}";
        Console.WriteLine($"=== MAPPING MODE STARTED for {channel.Name} ===");
    }

    [RelayCommand]
    private void StartTransportMapping(string controlType)
    {
        if (_midiService == null)
        {
            StatusMessage = "MIDI service not available";
            return;
        }
        if (!IsMidiConnected)
        {
            StatusMessage = "Please connect a MIDI device first";
            return;
        }
        ControlTypeToMap = controlType; // e.g., TransportPlay
        ChannelAwaitingMapping = null; // global mapping
        IsMappingModeActive = true;
        _midiService.StartMappingMode();
        var label = controlType switch
        {
            "TransportPlay" => "Play",
            "TransportPause" => "Pause",
            "TransportNext" => "Next",
            "TransportPrevious" => "Previous",
            _ => controlType
        };
        StatusMessage = $"MAPPING MODE: Press a MIDI control to map to {label}";
        Console.WriteLine($"=== GLOBAL MAPPING MODE STARTED for {label} ===");
    }

    [RelayCommand]
    private void CancelMappingMode()
    {
        if (ChannelAwaitingMapping != null)
        {
            ChannelAwaitingMapping.IsInMappingMode = false;
        }
        ChannelAwaitingMapping = null;
        IsMappingModeActive = false;
        _midiService?.StopMappingMode();
        StatusMessage = "Mapping cancelled - Ready";
        Console.WriteLine("=== MAPPING MODE CANCELLED ===");
    }

    [RelayCommand]
    private void AddChannel()
    {
        var newChannel = new ChannelViewModel
        {
            Index = Channels.Count,
            Name = $"Channel {Channels.Count + 1}"
        };
        SubscribeToChannelEvents(newChannel);
        Channels.Add(newChannel);
        UpdateWindowSize();
    }

    [RelayCommand]
    private void ToggleFullScreenMode()
    {
        IsFullScreenMode = !IsFullScreenMode;
        StatusMessage = IsFullScreenMode ? "Full Screen Mode" : "Normal Mode";
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var settingsViewModel = new SettingsViewModel(this, _configurationService);
        var settingsWindow = new SettingsWindow(settingsViewModel)
        {
            Owner = Application.Current.MainWindow
        };
        settingsWindow.ShowDialog();
    }

    [RelayCommand]
    public void ManageMappings()
    {
        var channelNames = Channels.Select(c => c.Name).ToList();
        var mappingsList = _midiMappings.Values.ToList();
        
        var dialog = new MidiMappingsDialog(mappingsList, channelNames)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true)
        {
            // Update mappings based on what's left in the dialog
            _midiMappings.Clear();
            foreach (var mappingVm in dialog.Mappings)
            {
                var key = (mappingVm.Original.Channel, mappingVm.Original.ControlNumber);
                _midiMappings[key] = mappingVm.Original;
            }

            // Save to configuration
            if (_configurationService != null)
            {
                SyncConfigurationState();
                _ = _configurationService.SaveCurrentSettingsAsync();
            }

            StatusMessage = $"Mappings updated: {_midiMappings.Count} mappings active";
        }
    }

    // Transport action commands (invoke immediately from UI)
    [RelayCommand]
    private void TransportPlay() => PerformTransportAction(MidiControlType.TransportPlay);

    [RelayCommand]
    private void TransportPause() => PerformTransportAction(MidiControlType.TransportPause);

    [RelayCommand]
    private void TransportNext() => PerformTransportAction(MidiControlType.TransportNext);

    [RelayCommand]
    private void TransportPrevious() => PerformTransportAction(MidiControlType.TransportPrevious);

    [RelayCommand]
    private async Task ExportControllerConfig()
    {
        try
        {
            var controllerName = SelectedMidiDevice?.Name ?? "Unknown-Controller";
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
                StatusMessage = $"✓ Controller config exported to {System.IO.Path.GetFileName(dialog.FileName)}";
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
                
                // Ask about channels if config contains them
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
                
                // Reload mappings and channels from updated settings
                _midiMappings.Clear();
                foreach (var mapping in _configurationService.CurrentSettings.MidiMappings)
                {
                    var key = (mapping.Channel, mapping.ControlNumber);
                    _midiMappings[key] = mapping;
                }

                if (applyChannels && config.Channels != null)
                {
                    Channels.Clear();
                    foreach (var channelConfig in _configurationService.CurrentSettings.Channels)
                    {
                        var channelVm = new ChannelViewModel();
                        channelVm.LoadConfiguration(channelConfig);
                        SubscribeToChannelEvents(channelVm);
                        Channels.Add(channelVm);
                    }
                    UpdateWindowSize();
                }

                await _configurationService.SaveCurrentSettingsAsync();
                
                StatusMessage = $"✓ Imported {_midiMappings.Count} mappings from '{config.ControllerName}'";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ Import failed: {ex.Message}";
            MessageBox.Show($"Failed to import controller config:\n{ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // helper to open the context menu from Map button (bind via EventSetter in XAML if needed)
    [RelayCommand]
    private void ShowTransportMapMenu(object parameter)
    {
        // Handled in XAML via ContextMenu on the button, this exists to satisfy binding
    }

    /// <summary>
    /// Calculates and updates window size based on number of channels
    /// </summary>
    private void UpdateWindowSize()
    {
        const int channelWidth = 144; // 140px channel + 4px margin
        const int windowChrome = 60; // Extra space for window padding and borders
        const int minChannels = 4;
        const int maxChannels = 16;

        int channelCount = Math.Max(minChannels, Math.Min(Channels.Count, maxChannels));
        double calculatedWidth = (channelCount * channelWidth) + windowChrome;
        
        // Update minimum width to fit all channels (use actual channel count, not clamped)
        MinWindowWidth = Math.Max(600, (Channels.Count * channelWidth) + windowChrome);
        
        // Only update window width if it would be larger than current or if current is smaller than min
        if (WindowWidth < MinWindowWidth)
        {
            WindowWidth = MinWindowWidth;
        }
        else if (calculatedWidth > WindowWidth)
        {
            WindowWidth = calculatedWidth;
        }
    }

    private void SubscribeToChannelEvents(ChannelViewModel channel)
    {
        channel.VolumeChanged += OnChannelVolumeChanged;
        channel.MuteChanged += OnChannelMuteChanged;
        channel.SoloChanged += OnChannelSoloChanged;
        channel.MappingModeRequested += OnChannelMappingModeRequested;
        channel.SessionAssignmentRequested += OnChannelSessionAssignmentRequested;
        channel.SessionCleared += OnChannelSessionCleared;
        channel.CycleSessionRequested += OnChannelCycleSessionRequested;
        channel.PropertyChanged += OnChannelPropertyChanged;
    }

    private void UnsubscribeFromChannelEvents(ChannelViewModel channel)
    {
        channel.VolumeChanged -= OnChannelVolumeChanged;
        channel.MuteChanged -= OnChannelMuteChanged;
        channel.PropertyChanged -= OnChannelPropertyChanged;
        channel.SoloChanged -= OnChannelSoloChanged;
        channel.MappingModeRequested -= OnChannelMappingModeRequested;
        channel.SessionAssignmentRequested -= OnChannelSessionAssignmentRequested;
        channel.SessionCleared -= OnChannelSessionCleared;
        channel.CycleSessionRequested -= OnChannelCycleSessionRequested;
    }

    private void OnChannelMappingModeRequested(object? sender, MappingTypeRequestedEventArgs e)
    {
        if (sender is ChannelViewModel channel)
        {
            ControlTypeToMap = e.ControlType;
            Console.WriteLine($"=== Mapping mode requested for {channel.Name} - Control Type: {ControlTypeToMap} ===");
            StartMappingMode(channel);
        }
    }

    private void OnChannelVolumeChanged(object? sender, EventArgs e)
    {
        if (sender is ChannelViewModel channel)
        {
            ApplyVolumeToSessions(channel);
        }
    }

    private void OnChannelMuteChanged(object? sender, EventArgs e)
    {
        if (sender is ChannelViewModel channel)
        {
            ApplyMuteToSessions(channel);
            SendMuteLedFeedback(channel, channel.IsMuted);
        }
    }

    private void OnChannelSoloChanged(object? sender, EventArgs e)
    {
        if (sender is ChannelViewModel soloedChannel && soloedChannel.IsSoloed)
        {
            // Prevent soloing system devices (master volume, input/output devices)
            if (soloedChannel.AssignedSessions.Any(s => 
                s.SessionId == "master_output" ||
                s.SessionId.StartsWith("input_") ||
                s.SessionId.StartsWith("output_")))
            {
                soloedChannel.IsSoloed = false;
                StatusMessage = "Cannot solo system audio devices";
                return;
            }

            // Exclusive solo - unsolo all other channels
            foreach (var channel in Channels)
            {
                if (channel != soloedChannel && channel.IsSoloed)
                {
                    channel.IsSoloed = false;

                    // Turn off the LED for the unsoloed channel
                    SendSoloLedFeedback(channel, false);
                }
            }

            // Turn on the LED for the newly soloed channel
            SendSoloLedFeedback(soloedChannel, true);
        }
        else if (sender is ChannelViewModel unsuloedChannel && !unsuloedChannel.IsSoloed)
        {
            // User manually turned off solo - turn off LED
            SendSoloLedFeedback(unsuloedChannel, false);
        }

        ApplySoloLogic();
    }
    private void SendMuteLedFeedback(ChannelViewModel channel, bool isOn)
    {
        if (_midiService == null) return;

        // Find the MIDI mapping for this channel's mute button
        foreach (var kvp in _midiMappings)
        {
            var mapping = kvp.Value;
            if (mapping.TargetChannelIndex == channel.Index && mapping.ControlType == MidiControlType.Mute)
            {
                // Send LED feedback (on=127, off=0)
                _midiService.SendNoteOn(mapping.Channel, mapping.ControlNumber, isOn ? 127 : 0);
                Console.WriteLine($"Mute LED feedback sent for {channel.Name}: {(isOn ? "ON" : "OFF")}");
                break;
            }
        }
    }
    /// <summary>
    /// Sends LED feedback to MIDI controller for solo button state
    /// </summary>
    private void SendSoloLedFeedback(ChannelViewModel channel, bool isOn)
    {
        if (_midiService == null) return;

        // Find the MIDI mapping for this channel's solo button
        foreach (var kvp in _midiMappings)
        {
            var mapping = kvp.Value;
            if (mapping.TargetChannelIndex == channel.Index && mapping.ControlType == MidiControlType.Solo)
            {
                // Send LED feedback (on=127, off=0)
                _midiService.SendNoteOn(mapping.Channel, mapping.ControlNumber, isOn ? 127 : 0);
                Console.WriteLine($"Solo LED feedback sent for {channel.Name}: {(isOn ? "ON" : "OFF")}");
                break;
            }
        }
    }

    private void OnChannelSessionAssignmentRequested(object? sender, EventArgs e)
    {
        if (sender is not ChannelViewModel channel) return;

        // Build list of already mapped session IDs
        var mappedSessionIds = new HashSet<string>();
        foreach (var ch in Channels)
        {
            foreach (var session in ch.AssignedSessions)
            {
                mappedSessionIds.Add(session.SessionId);
            }
        }

        // Open the session assignment dialog with multi-selection support
        var dialog = new SessionAssignmentDialog(AvailableSessions, mappedSessionIds, channel.AssignedSessions.Select(s => s.SessionId))
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true && dialog.SelectedSessions.Count > 0)
        {
            // Update intended assignments from dialog selection
            channel.SetIntendedAssignments(dialog.SelectedSessions);

            // Clear and set live assigned sessions
            channel.AssignedSessions.Clear();
            foreach (var session in dialog.SelectedSessions)
            {
                // Remove this session from all OTHER channels (exclusive mapping)
                foreach (var otherChannel in Channels)
                {
                    if (otherChannel != channel)
                    {
                        var existingSession = otherChannel.AssignedSessions.FirstOrDefault(s => s.SessionId == session.SessionId);
                        if (existingSession != null)
                        {
                            otherChannel.AssignedSessions.Remove(existingSession);
                            Console.WriteLine($"Removed '{session.DisplayName}' from {otherChannel.Name} (exclusive mapping)");

                            if (otherChannel.AssignedSessions.Count == 0)
                            {
                                otherChannel.Name = $"Channel {otherChannel.Index + 1}";
                                otherChannel.SessionType = null;
                            }
                        }
                    }
                }

                channel.AssignedSessions.Add(session);
            }

            // Update channel name based on selection
            if (dialog.SelectedSessions.Count == 1)
            {
                var session = dialog.SelectedSessions[0];
                channel.Name = session.DisplayName;
                channel.SessionType = session.SessionType;
            }
            else
            {
                channel.Name = $"{dialog.SelectedSessions.Count} Sessions";
                channel.SessionType = dialog.SelectedSessions[0].SessionType;
            }

            StatusMessage = $"Assigned {dialog.SelectedSessions.Count} session(s) to Channel {channel.Index + 1}";

            // Apply solo logic to ensure newly assigned sessions respect current solo state
            ApplySoloLogic();

            // Save configuration
            _ = SaveConfigurationAsync();
        }
    }

    private void OnChannelSessionCleared(object? sender, EventArgs e)
    {
        if (sender is ChannelViewModel channel)
        {
            StatusMessage = $"Cleared sessions from Channel {channel.Index + 1}";
            // Save configuration
            _ = SaveConfigurationAsync();
        }
    }

    private void OnChannelCycleSessionRequested(object? sender, EventArgs e)
    {
        if (sender is not ChannelViewModel channel) return;

        // Existing feature retained, but no longer mappable via MIDI.
        // Get all available non-system sessions (exclude master, input/output devices)
        var availableSessions = AvailableSessions
            .Where(s => s.SessionType == AudioSessionType.Application)
            .ToList();

        if (availableSessions.Count == 0)
        {
            StatusMessage = "No application sessions available to cycle";
            return;
        }

        // Find current session index
        int currentIndex = -1;
        if (channel.AssignedSessions.Count > 0)
        {
            var currentSession = channel.AssignedSessions[0];
            currentIndex = availableSessions.FindIndex(s => s.SessionId == currentSession.SessionId);
        }

        // Cycle to next session
        int nextIndex = (currentIndex + 1) % availableSessions.Count;
        var nextSession = availableSessions[nextIndex];

        // Remove this session from all OTHER channels (exclusive mapping)
        foreach (var otherChannel in Channels)
        {
            if (otherChannel != channel)
            {
                var existingSession = otherChannel.AssignedSessions.FirstOrDefault(s => s.SessionId == nextSession.SessionId);
                if (existingSession != null)
                {
                    otherChannel.AssignedSessions.Remove(existingSession);
                    otherChannel.IntendedAssignments.RemoveAll(r => r.SessionId == nextSession.SessionId);

                    if (otherChannel.AssignedSessions.Count == 0)
                    {
                        otherChannel.Name = $"Channel {otherChannel.Index + 1}";
                        otherChannel.SessionType = null;
                    }
                }
            }
        }

        // Assign to this channel
        channel.AssignedSessions.Clear();
        channel.IntendedAssignments.Clear();
        channel.AssignedSessions.Add(nextSession);
        channel.SetIntendedAssignments(new List<AudioSessionInfo> { nextSession });
        channel.Name = nextSession.DisplayName;
        channel.SessionType = nextSession.SessionType;

        StatusMessage = $"Channel {channel.Index + 1} cycled to: {nextSession.DisplayName}";
        Console.WriteLine($"Channel {channel.Index + 1} cycled to: {nextSession.DisplayName}");

        // Apply solo logic
        ApplySoloLogic();

        // Save configuration
        _ = SaveConfigurationAsync();
    }

    [RelayCommand]
    private void RemoveChannel(ChannelViewModel channel)
    {
        Console.WriteLine($"Removing channel: {channel.Name} (Index: {channel.Index})");
        
        // Unsubscribe from events
        UnsubscribeFromChannelEvents(channel);
        
        Channels.Remove(channel);
        
        // Re-index remaining channels
        for (int i = 0; i < Channels.Count; i++)
        {
            Channels[i].Index = i;
        }
        
        UpdateWindowSize();
        StatusMessage = $"Channel removed. {Channels.Count} channels remaining.";
        
        // Save configuration
        _ = SaveConfigurationAsync();
    }

    private void OnMidiControlChange(object? sender, MidiControlChangeEventArgs e)
    {
        // Debug: Show we received a MIDI message
        Console.WriteLine($"MIDI CC: Ch{e.Channel} CC{e.Controller} Val{e.Value}");

        // Handle MIDI CC messages
        if (IsMappingModeActive)
        {
            if (ChannelAwaitingMapping != null)
            {
                // Map CC to Volume only
                if (ControlTypeToMap == "Volume")
                {
                    Console.WriteLine($" Mapping CC to Volume");
                    CreateMapping(e.Channel, e.Controller, ChannelAwaitingMapping);
                    CancelMappingMode();
                }
                else
                {
                    StatusMessage = $"Expecting a button press for {ControlTypeToMap}, but received CC message. Try a fader/knob for Volume mapping.";
                    Console.WriteLine($" Ignoring CC - expecting {ControlTypeToMap}");
                }
            }
            else
            {
                // Global transport mapping via CC
                var controlType = ResolveTransportControlType(ControlTypeToMap);
                if (controlType != null)
                {
                    CreateGlobalCcMapping(e.Channel, e.Controller, controlType.Value);
                    CancelMappingMode();
                }
            }
        }
        else
        {
            // Apply existing mapping
            ApplyMidiControl(e.Channel, e.Controller, e.Value);
        }
    }

    private void OnMidiNoteOn(object? sender, MidiNoteEventArgs e)
    {
        // Debug: Show we received a MIDI note message
        Console.WriteLine($"MIDI Note On: Ch{e.Channel} Note#{e.NoteNumber} Vel{e.Velocity}");

        // Handle MIDI note messages (buttons)
        if (IsMappingModeActive)
        {
            if (ChannelAwaitingMapping != null)
            {
                if (ControlTypeToMap == "CycleSession")
                {
                    StatusMessage = "Cycle Session mapping has been removed and is no longer supported.";
                    Console.WriteLine("  Ignoring mapping request for CycleSession");
                    CancelMappingMode();
                    return;
                }

                // Use the control type selected by the user
                MidiControlType buttonType = ControlTypeToMap switch
                {
                    "Mute" => MidiControlType.Mute,
                    "Solo" => MidiControlType.Solo,
                    "Record" => MidiControlType.Record,
                    "Select" => MidiControlType.Select,
                    _ => MidiControlType.Mute
                };

                Console.WriteLine($"  Mapping button to: {buttonType}");
                CreateNoteMapping(e.Channel, e.NoteNumber, ChannelAwaitingMapping, buttonType);
                CancelMappingMode();
            }
            else
            {
                // Global transport mapping via Note
                var controlType = ResolveTransportControlType(ControlTypeToMap);
                if (controlType != null)
                {
                    CreateGlobalNoteMapping(e.Channel, e.NoteNumber, controlType.Value);
                    CancelMappingMode();
                }
            }
        }
        else
        {
            // Apply existing note mapping
            ApplyMidiNote(e.Channel, e.NoteNumber);
        }
    }

    private void OnMidiPitchBend(object? sender, MidiPitchBendEventArgs e)
    {
        // Debug: Show we received a pitch bend message (used for faders in Mackie mode)
        // Convert 0-16383 to 0-127 for display
        int value127 = e.Value / 128;
        float volumePercent = (e.Value / 16383f) * 100f;

        Console.WriteLine($"MIDI Pitch Bend: Ch{e.Channel} RawValue={e.Value} Display={value127}/127 Volume={volumePercent:F1}%");
        Console.WriteLine($"  IsMappingModeActive={IsMappingModeActive}, ChannelAwaitingMapping={ChannelAwaitingMapping?.Name ?? "null"}");

        if (IsMappingModeActive)
        {
            if (ChannelAwaitingMapping != null)
            {
                // Only map pitch bend to Volume if that's what the user selected
                if (ControlTypeToMap == "Volume")
                {
                    Console.WriteLine($"  Creating fader mapping for channel {ChannelAwaitingMapping.Name}");
                    CreateFaderMapping(e.Channel, ChannelAwaitingMapping);
                    CancelMappingMode();
                }
                else
                {
                    StatusMessage = $"Expecting a button press for {ControlTypeToMap}, but received fader. Try pressing a button.";
                    Console.WriteLine($"  Ignoring pitch bend - expecting {ControlTypeToMap}");
                }
            }
            else
            {
                // Global mapping doesn't use pitch bend
                StatusMessage = "Pitch bend is not supported for transport controls. Press a button instead.";
                Console.WriteLine("  Ignoring pitch bend for transport mapping");
            }
        }
        else
        {
            // Apply fader value to the mapped channel
            ApplyMidiFader(e.Channel, e.Value);
        }
    }

    private void CreateGlobalNoteMapping(int midiChannel, int noteNumber, MidiControlType controlType)
    {
        var mapping = new MidiMapping
        {
            Channel = midiChannel,
            ControlNumber = noteNumber,
            TargetChannelIndex = -1, // global
            ControlType = controlType
        };
        var key = (midiChannel, noteNumber);
        _midiMappings[key] = mapping;
        if (_configurationService != null)
        {
            SyncConfigurationState();
            _ = _configurationService.SaveCurrentSettingsAsync();
        }
        StatusMessage = $"Mapped MIDI CH{midiChannel + 1} Note{noteNumber} to {controlType}";
    }

    private void CreateGlobalCcMapping(int midiChannel, int controller, MidiControlType controlType)
    {
        var mapping = new MidiMapping
        {
            Channel = midiChannel,
            ControlNumber = controller,
            TargetChannelIndex = -1,
            ControlType = controlType
        };
        var key = (midiChannel, controller);
        _midiMappings[key] = mapping;
        if (_configurationService != null)
        {
            SyncConfigurationState();
            _ = _configurationService.SaveCurrentSettingsAsync();
        }
        StatusMessage = $"Mapped MIDI CH{midiChannel + 1} CC{controller} to {controlType}";
    }

    private MidiControlType? ResolveTransportControlType(string controlType)
    {
        return controlType switch
        {
            "TransportPlay" => MidiControlType.TransportPlay,
            "TransportPause" => MidiControlType.TransportPause,
            "TransportNext" => MidiControlType.TransportNext,
            "TransportPrevious" => MidiControlType.TransportPrevious,
            _ => null
        };
    }

    private void CreateNoteMapping(int midiChannel, int noteNumber, ChannelViewModel channel, MidiControlType controlType)
    {
        var mapping = new MidiMapping
        {
            Channel = midiChannel,
            ControlNumber = noteNumber, // Use note number as control number
            TargetChannelIndex = channel.Index,
            ControlType = controlType
        };

        var key = (midiChannel, noteNumber);
        _midiMappings[key] = mapping;

        if (_configurationService != null)
        {
            SyncConfigurationState();
            _ = _configurationService.SaveCurrentSettingsAsync();
        }

        StatusMessage = $"Mapped MIDI CH{midiChannel + 1} Note{noteNumber} to {channel.Name} {controlType}";
    }

    private void CreateCcButtonMapping(int midiChannel, int controller, ChannelViewModel channel, MidiControlType controlType)
    {
        var mapping = new MidiMapping
        {
            Channel = midiChannel,
            ControlNumber = controller,
            TargetChannelIndex = channel.Index,
            ControlType = controlType
        };

        var key = (midiChannel, controller);
        _midiMappings[key] = mapping;

        if (_configurationService != null)
        {
            SyncConfigurationState();
            _ = _configurationService.SaveCurrentSettingsAsync();
        }

        StatusMessage = $"Mapped MIDI CH{midiChannel + 1} CC{controller} to {channel.Name} {controlType}";
        Console.WriteLine($"Created CC button mapping: MIDI CH{midiChannel} CC{controller} -> {channel.Name} {controlType}");
    }

    private void CreateFaderMapping(int midiChannel, ChannelViewModel channel)
    {
        // For faders, we use -1 as a special indicator that this is a pitch bend mapping
        var mapping = new MidiMapping
        {
            Channel = midiChannel,
            ControlNumber = -1, // Special value for pitch bend
            TargetChannelIndex = channel.Index,
            ControlType = MidiControlType.Volume
        };

        var key = (midiChannel, -1); // Use -1 as the "control number" for pitch bend
        _midiMappings[key] = mapping;

        if (_configurationService != null)
        {
            SyncConfigurationState();
            _ = _configurationService.SaveCurrentSettingsAsync();
        }

        StatusMessage = $"Mapped Fader on MIDI CH{midiChannel + 1} to {channel.Name}";
        Console.WriteLine($"Created fader mapping: MIDI CH{midiChannel} -> {channel.Name}");
    }

    private readonly Dictionary<int, System.Timers.Timer> _faderDebounceTimers = new();
    private readonly Dictionary<int, (ChannelViewModel channel, int pitchBendValue)> _pendingFaderUpdates = new();
    private const int FaderDebounceMs = 30; // Batch audio updates every 30ms

    private void ApplyMidiFader(int midiChannel, int pitchBendValue)
    {
        var key = (midiChannel, -1); // Look for pitch bend mapping
        if (!_midiMappings.TryGetValue(key, out var mapping))
        {
            // No mapping for this fader yet
            return;
        }

        if (mapping.TargetChannelIndex >= Channels.Count || mapping.TargetChannelIndex < 0)
        {
            return;
        }

        var channel = Channels[mapping.TargetChannelIndex];

        // Convert pitch bend value (0-16383) to volume (0.0-1.0)
        float volume = pitchBendValue / 16383f;
        channel.Volume = volume; // Update UI immediately

        // Store pending update and debounce audio session writes
        _pendingFaderUpdates[midiChannel] = (channel, pitchBendValue);

        if (!_faderDebounceTimers.TryGetValue(midiChannel, out var timer))
        {
            timer = new System.Timers.Timer(FaderDebounceMs);
            timer.AutoReset = false;
            timer.Elapsed += (s, e) =>
            {
                if (_pendingFaderUpdates.TryGetValue(midiChannel, out var pending))
                {
                    // Apply batched update to audio sessions
                    ApplyVolumeToSessions(pending.channel);
                }
            };
            _faderDebounceTimers[midiChannel] = timer;
        }

        timer.Stop();
        timer.Start();

        // Send feedback to motorized fader immediately
        _midiService?.SendPitchBend(midiChannel, pitchBendValue);
    }

    private void ApplyMidiNote(int midiChannel, int noteNumber)
    {
        var key = (midiChannel, noteNumber);
        if (!_midiMappings.TryGetValue(key, out var mapping))
        {
            return;
        }

        Console.WriteLine($"[ApplyMidiNote] Received note - Ch{midiChannel} Note{noteNumber} -> ControlType: {mapping.ControlType}, TargetChannel: {mapping.TargetChannelIndex}");

        if (mapping.TargetChannelIndex == -1)
        {
            // Global transport buttons handled centrally (LED feedback included)
            PerformTransportAction(mapping.ControlType);
            return;
        }

        if (mapping.TargetChannelIndex >= Channels.Count)
        {
            return;
        }

        var channel = Channels[mapping.TargetChannelIndex];

        switch (mapping.ControlType)
        {
            case MidiControlType.Mute:
                channel.ToggleMute();
                ApplyMuteToSessions(channel);
                // Send LED feedback (on=127, off=0)
                _midiService?.SendNoteOn(midiChannel, noteNumber, channel.IsMuted ? 127 : 0);
                Console.WriteLine($"Mute toggled for {channel.Name}: {channel.IsMuted}");
                break;

            case MidiControlType.Solo:
                channel.ToggleSolo();
                ApplySoloLogic();
                // Send LED feedback
                _midiService?.SendNoteOn(midiChannel, noteNumber, channel.IsSoloed ? 127 : 0);
                Console.WriteLine($"Solo toggled for {channel.Name}: {channel.IsSoloed}");
                break;

            case MidiControlType.Record:
                // Handle default device switching for input/output devices
                HandleRecordButtonPress(channel, midiChannel, noteNumber);
                break;

            case MidiControlType.Select:
                // Select button is now read-only (audio activity indicator)
                // Ignore button presses from MIDI controller
                Console.WriteLine($"Select button press ignored for {channel.Name} (read-only audio activity indicator)");
                break;

            case MidiControlType.CycleSession:
                // No longer supported via MIDI mapping
                StatusMessage = "Cycle Session mapping is no longer supported; input ignored.";
                Console.WriteLine($"Ignored CycleSession mapping for {channel.Name}");
                break;
        }
    }

    private void ApplyMidiControl(int midiChannel, int controller, int value)
    {
        // Look up the mapping
        var key = (midiChannel, controller);
        if (!_midiMappings.TryGetValue(key, out var mapping))
        {
            // No mapping found - ignore this MIDI message
            return;
        }

        if (mapping.TargetChannelIndex == -1)
        {
            PerformTransportAction(mapping.ControlType);
            return;
        }

        // Get the target channel
        if (mapping.TargetChannelIndex >= Channels.Count)
        {
            return;
        }

        var channel = Channels[mapping.TargetChannelIndex];

        // Convert MIDI value (0-127) to application value based on mapping range
        float normalizedValue = value / 127f;
        float scaledValue = mapping.MinValue + (normalizedValue * (mapping.MaxValue - mapping.MinValue));

        if (mapping.IsInverted)
        {
            scaledValue = mapping.MaxValue - (normalizedValue * (mapping.MaxValue - mapping.MinValue));
        }

        // Apply based on control type
        switch (mapping.ControlType)
        {
            case MidiControlType.Volume:
                channel.Volume = scaledValue;
                // Apply to all assigned audio sessions
                ApplyVolumeToSessions(channel);
                // Send feedback to MIDI controller (motorized fader)
                SendVolumeFeedback(midiChannel, controller, value);
                break;

            case MidiControlType.CycleSession:
                // No longer supported via MIDI mapping
                StatusMessage = "Cycle Session mapping is no longer supported; input ignored.";
                Console.WriteLine($"Ignored CycleSession CC for {channel.Name}");
                return;

            case MidiControlType.Pan:
                // Pan control (future enhancement)
                break;

            case MidiControlType.FilterCutoff:
                if (channel.Filter != null)
                {
                    channel.Filter.Cutoff = scaledValue * 20000f; // 0-20kHz
                }
                break;

            case MidiControlType.FilterResonance:
                if (channel.Filter != null)
                {
                    channel.Filter.Resonance = scaledValue * 10f; // 0-10 Q factor
                }
                break;
        }
    }

    private void PerformTransportAction(MidiControlType controlType)
    {
        switch (controlType)
        {
            case MidiControlType.TransportPlay:
                // Toggle play/pause - if playing, pause it; if paused, play it
                if (_isPlaying)
                {
                    _mediaControlService?.Pause();
                    _isPlaying = false;
                    StatusMessage = "Media: Paused";
                }
                else
                {
                    _mediaControlService?.Play();
                    _isPlaying = true;
                    StatusMessage = "Media: Playing";
                }
                UpdatePlayPauseLeds();
                break;
            case MidiControlType.TransportPause:
                // Toggle play/pause - if playing, pause it; if paused, play it
                if (_isPlaying)
                {
                    _mediaControlService?.Pause();
                    _isPlaying = false;
                    StatusMessage = "Media: Paused";
                }
                else
                {
                    _mediaControlService?.Play();
                    _isPlaying = true;
                    StatusMessage = "Media: Playing";
                }
                UpdatePlayPauseLeds();
                break;
            case MidiControlType.TransportNext:
                _mediaControlService?.NextTrack();
                StatusMessage = "Media: Next";
                BlinkTransportLed(MidiControlType.TransportNext);
                break;
            case MidiControlType.TransportPrevious:
                _mediaControlService?.PreviousTrack();
                StatusMessage = "Media: Previous";
                BlinkTransportLed(MidiControlType.TransportPrevious);
                break;
        }
    }

    private void SendVolumeFeedback(int midiChannel, int controller, int value)
    {
        // Send the same value back to the controller for motorized faders
        _midiService?.SendControlChange(midiChannel, controller, value);
    }

    private void ApplyVolumeToSessions(ChannelViewModel channel)
    {
        if (_audioSessionManager == null) return;

        foreach (var session in channel.AssignedSessions)
        {
            _audioSessionManager.SetSessionVolume(session.SessionId, channel.Volume);
        }
    }

    // --- Transport LED helpers ---
    private void UpdatePlayPauseLeds()
    {
        // Play LED on when playing; Pause LED on when paused
        SetTransportLed(MidiControlType.TransportPlay, _isPlaying);
        SetTransportLed(MidiControlType.TransportPause, !_isPlaying);
    }

    private void SetTransportLed(MidiControlType controlType, bool on)
    {
        if (_midiService == null) return;
        foreach (var kvp in _midiMappings)
        {
            var mapping = kvp.Value;
            if (mapping.TargetChannelIndex == -1 && mapping.ControlType == controlType)
            {
                int ch = mapping.Channel;
                int num = mapping.ControlNumber;
                int val = on ? 127 : 0;

                // Try Note LED
                _midiService.SendNoteOn(ch, num, val);
                // Also try CC LED for controllers using CC for buttons
                _midiService.SendControlChange(ch, num, val);
            }
        }
    }

    private void BlinkTransportLed(MidiControlType controlType)
    {
        if (_midiService == null) return;
        _ = Task.Run(async () =>
        {
            foreach (var kvp in _midiMappings)
            {
                var mapping = kvp.Value;
                if (mapping.TargetChannelIndex == -1 && mapping.ControlType == controlType)
                {
                    int ch = mapping.Channel;
                    int num = mapping.ControlNumber;
                    // ON
                    _midiService.SendNoteOn(ch, num, 127);
                    _midiService.SendControlChange(ch, num, 127);
                }
            }
            await Task.Delay(120);
            foreach (var kvp in _midiMappings)
            {
                var mapping = kvp.Value;
                if (mapping.TargetChannelIndex == -1 && mapping.ControlType == controlType)
                {
                    int ch = mapping.Channel;
                    int num = mapping.ControlNumber;
                    // OFF
                    _midiService.SendNoteOn(ch, num, 0);
                    _midiService.SendControlChange(ch, num, 0);
                }
            }
        });
    }

    private async void LoadConfiguration()
    {
        if (_configurationService == null) return;

        try
        {
            var settings = await _configurationService.LoadSettingsAsync();
            
            // Load theme
            var savedTheme = AvailableThemes.FirstOrDefault(t => t.Name == settings.SelectedTheme);
            if (savedTheme != null)
            {
                SelectedTheme = savedTheme;
                ApplyTheme(savedTheme);
            }

            // Detect corrupted/empty config and attempt restore from backup
            if (settings.Channels.Count == 0 && settings.MidiMappings.Count == 0)
            {
                Console.WriteLine("Warning: Loaded config is empty. Attempting to restore from backup...");
                if (await _configurationService.RestoreFromBackupAsync())
                {
                    settings = _configurationService.CurrentSettings;
                    StatusMessage = "✓ Restored configuration from backup";
                    Console.WriteLine("Successfully restored from backup");
                }
                else
                {
                    Console.WriteLine("No backup available or backup restore failed. Using defaults.");
                    StatusMessage = "No saved configuration found - using defaults";
                }
            }

            // Load channels from configuration
            Channels.Clear();
            _pendingChannelConfigs = settings.Channels;

            if (settings.Channels.Count > 0)
            {
                foreach (var channelConfig in settings.Channels)
                {
                    var channelVm = new ChannelViewModel();
                    channelVm.LoadConfiguration(channelConfig);
                    SubscribeToChannelEvents(channelVm);
                    Channels.Add(channelVm);
                }
            }
            else
            {
                // Create default channels if none in config
                for (int i = 0; i < 8; i++)
                {
                    var channel = new ChannelViewModel
                    {
                        Index = i,
                        Name = $"Channel {i + 1}"
                    };
                    SubscribeToChannelEvents(channel);
                    Channels.Add(channel);
                }
            }

            // Relink sessions if available
            if (AvailableSessions.Count > 0)
            {
                RelinkChannelSessions();
            }

            // Update window size based on loaded channels
            UpdateWindowSize();

            // Restore window position and size
            WindowWidth = settings.WindowWidth;
            WindowHeight = settings.WindowHeight;
            WindowLeft = settings.WindowLeft;
            WindowTop = settings.WindowTop;
            WindowState = (System.Windows.WindowState)settings.WindowState;
            MinimizeToTray = settings.MinimizeToTray;
            StartWithWindows = settings.StartWithWindows;
            StartMinimized = settings.StartMinimized;
            FontSizeScale = settings.FontSizeScale;

            // Scrub unsupported CycleSession mappings
            int removedCycle = settings.MidiMappings.RemoveAll(m => m.ControlType == MidiControlType.CycleSession);
            if (removedCycle > 0)
            {
                Console.WriteLine($"Removed {removedCycle} unsupported CycleSession mappings from settings");
                try
                {
                    await _configurationService.SaveSettingsAsync(settings);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to persist removal of CycleSession mappings: {ex.Message}");
                }
            }

            // Load MIDI mappings
            _midiMappings.Clear();
            foreach (var mapping in settings.MidiMappings)
            {
                var key = (mapping.Channel, mapping.ControlNumber);
                _midiMappings[key] = mapping;
            }

            // Select saved MIDI device
            if (!string.IsNullOrEmpty(settings.SelectedMidiDevice))
            {
                SelectedMidiDevice = AvailableMidiDevices.FirstOrDefault(d => d.Name == settings.SelectedMidiDevice);

                // Auto-connect if device is available
                if (SelectedMidiDevice != null)
                {
                    await ConnectMidiDevice();
                    // Initialize Record LEDs for default devices
                    InitializeDefaultDeviceRecordLeds();
                }
            }

            StatusMessage = $"Loaded {_midiMappings.Count} MIDI mappings";
        }
        catch
        {
            // Use defaults if loading fails
        }
    }

    public async Task SaveConfigurationAsync()
    {
        if (_configurationService == null) return;

        SyncConfigurationState();
        await _configurationService.SaveSettingsAsync(_configurationService.CurrentSettings);
    }

    /// <summary>
    /// Reloads mappings and optionally channels from the configuration service
    /// </summary>
    public void ReloadFromConfiguration(bool reloadChannels)
    {
        if (_configurationService == null) return;

        var settings = _configurationService.CurrentSettings;

        // Scrub unsupported CycleSession mappings on reload too
        int removedCycle = settings.MidiMappings.RemoveAll(m => m.ControlType == MidiControlType.CycleSession);
        if (removedCycle > 0)
        {
            Console.WriteLine($"Removed {removedCycle} unsupported CycleSession mappings during reload");
            _ = _configurationService.SaveCurrentSettingsAsync();
        }

        // Reload MIDI mappings
        _midiMappings.Clear();
        foreach (var mapping in settings.MidiMappings)
        {
            // Explicitly name tuple elements to match Dictionary<(int channel, int controller), ...>
            var key = (channel: mapping.Channel, controller: mapping.ControlNumber);
            _midiMappings[key] = mapping;
        }

        // Reload channels if requested
        if (reloadChannels && settings.Channels.Count > 0)
        {
            Channels.Clear();
            foreach (var channelConfig in settings.Channels)
            {
                var channelVm = new ChannelViewModel();
                channelVm.LoadConfiguration(channelConfig);
                SubscribeToChannelEvents(channelVm);
                Channels.Add(channelVm);
            }
            UpdateWindowSize();

            // Relink sessions if available
            if (AvailableSessions.Count > 0)
            {
                RelinkChannelSessions();
            }
        }

        StatusMessage = $"Loaded {_midiMappings.Count} MIDI mappings";
        Console.WriteLine($"Reloaded {_midiMappings.Count} mappings from configuration");
    }

    /// <summary>
    /// Syncs all UI state (channels, mappings, theme, device) to CurrentSettings in memory.
    /// Call this before any save operation to ensure CurrentSettings is up-to-date.
    /// </summary>
    private void SyncConfigurationState()
    {
        if (_configurationService == null) return;

        var settings = _configurationService.CurrentSettings;
        settings.SelectedTheme = SelectedTheme?.Name ?? "DarkTheme";
        settings.SelectedMidiDevice = SelectedMidiDevice?.Name;
        settings.Channels = Channels.Select(c => c.ToConfiguration()).ToList();
        // Ensure we never persist CycleSession mappings going forward
        settings.MidiMappings = _midiMappings.Values.Where(m => m.ControlType != MidiControlType.CycleSession).ToList();
        
        // Save window position and size
        settings.WindowWidth = WindowWidth;
        settings.WindowHeight = WindowHeight;
        settings.WindowLeft = WindowLeft;
        settings.WindowTop = WindowTop;
        settings.WindowState = (int)WindowState;
        settings.MinimizeToTray = MinimizeToTray;
        settings.StartWithWindows = StartWithWindows;
        settings.StartMinimized = StartMinimized;
        settings.FontSizeScale = FontSizeScale;

        Console.WriteLine($"[Config Sync] Synced {settings.Channels.Count} channels, {settings.MidiMappings.Count} mappings to CurrentSettings");
    }

    private void OnMidiDeviceStateChanged(object? sender, MidiDeviceEventArgs e)
    {
        IsMidiConnected = e.IsConnected;
        StatusMessage = e.IsConnected ? $"Connected to {e.Device.Name}" : "MIDI device disconnected";
    }

    private void OnMasterMuteChanged(object? sender, MasterMuteChangedEventArgs e)
    {
        // Ensure we run UI updates on the UI thread
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            // Fallback: run inline
            HandleMasterMuteChanged(e);
            return;
        }

        if (dispatcher.CheckAccess())
        {
            HandleMasterMuteChanged(e);
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => HandleMasterMuteChanged(e)));
        }
    }

    private void HandleMasterMuteChanged(MasterMuteChangedEventArgs e)
    {
        // Find the channel assigned to master_output and update its state
        var masterChannel = Channels.FirstOrDefault(ch => 
            ch.AssignedSessions.Any(s => s.SessionId == "master_output"));
        
        if (masterChannel != null)
        {
            StatusMessage = e.IsMuted ? "Master: Muted" : "Master: Unmuted";
            
            // Update the channel's internal mute state (fixes UI not updating)
            masterChannel.IsMuted = e.IsMuted;
            
            // Update the mute LED for the master channel
            SendMuteLedFeedback(masterChannel, e.IsMuted);
            
            Console.WriteLine($"Master mute changed: {e.IsMuted} (Volume={e.Volume:F2})");
        }
    }

    private void OnAudioSessionsChanged(object? sender, AudioSessionChangedEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            // No dispatcher available - update inline
            UpdateAvailableSessionsAndRelink(e);
            return;
        }

        if (dispatcher.CheckAccess())
        {
            UpdateAvailableSessionsAndRelink(e);
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => UpdateAvailableSessionsAndRelink(e)));
        }
    }

    private void UpdateAvailableSessionsAndRelink(AudioSessionChangedEventArgs e)
    {
        AvailableSessions.Clear();
        foreach (var session in e.Sessions)
        {
            AvailableSessions.Add(session);
        }

        // Always relink channel sessions on session changes
        RelinkChannelSessions();

        // Update Record LEDs for default devices after relinking
        if (IsMidiConnected)
        {
            InitializeDefaultDeviceRecordLeds();
        }
    }

    private void OnPeakLevelsUpdated(object? sender, PeakLevelEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if ( dispatcher == null)
        {
            UpdatePeakLevels(e);
            return;
        }

        if (dispatcher.CheckAccess())
        {
            UpdatePeakLevels(e);
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => UpdatePeakLevels(e)));
        }
    }

    private void UpdatePeakLevels(PeakLevelEventArgs e)
    {
        // Update VU meters and audio activity
        foreach (var channel in Channels)
        {
            float maxPeak = 0;
            foreach (var session in channel.AssignedSessions)
            {
                if (e.PeakLevels.TryGetValue(session.SessionId, out float peak))
                {
                    maxPeak = Math.Max(maxPeak, peak);
                }
            }
            channel.PeakLevel = maxPeak;

            // Update audio activity indicator with 500ms delayed off
            channel.UpdateAudioActivity(maxPeak);
        }
    }

    private void OnPlaybackStateChanged(Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus status)
    {
        // Ensure UI updates happen on UI thread
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            HandlePlaybackStateChanged(status);
            return;
        }

        if (dispatcher.CheckAccess())
        {
            HandlePlaybackStateChanged(status);
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => HandlePlaybackStateChanged(status)));
        }
    }

    private void HandlePlaybackStateChanged(Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus status)
    {
        // Update internal state and LEDs based on Windows media playback state
        _isPlaying = status == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        
        StatusMessage = status switch
        {
            Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "Media: Playing",
            Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "Media: Paused",
            Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => "Media: Stopped",
            _ => StatusMessage
        };
        
        // Update LEDs to reflect current state
        UpdatePlayPauseLeds();
        
        Console.WriteLine($"Playback state changed: {status} (isPlaying={_isPlaying})");
    }

    private void CreateMapping(int midiChannel, int controller, ChannelViewModel channel)
    {
        // Create a new MIDI mapping for volume control
        var mapping = new MidiMapping
        {
            Channel = midiChannel,
            ControlNumber = controller,
            TargetChannelIndex = channel.Index,
            ControlType = MidiControlType.Volume
        };

        // Store the mapping
        var key = (midiChannel, controller);
        _midiMappings[key] = mapping;

        // Update configuration
        if (_configurationService != null)
        {
            SyncConfigurationState();
            _ = _configurationService.SaveCurrentSettingsAsync();
        }

        StatusMessage = $"Mapped MIDI CH{midiChannel + 1} CC{controller} to {channel.Name}";
    }

    private void ApplySoloLogic()
    {
        // If any channel is soloed, mute all non-soloed channels
        var soloedChannels = Channels.Where(c => c.IsSoloed).ToList();

        if (soloedChannels.Any())
        {
            // Mute all non-soloed channels (skip system devices during solo)
            foreach (var channel in Channels)
            {
                bool shouldBeMuted = !channel.IsSoloed;
                ApplyEffectiveMute(channel, shouldBeMuted, isFromSolo: true);
            }
        }
        else
        {
            // No solo active, restore user mute states (skip system devices)
            foreach (var channel in Channels)
            {
                ApplyEffectiveMute(channel, channel.IsMuted, isFromSolo: true);
            }
        }
    }

    private void ApplyEffectiveMute(ChannelViewModel channel, bool isMuted, bool isFromSolo = false)
    {
        if (_audioSessionManager == null) return;

        foreach (var session in channel.AssignedSessions)
        {
            // During solo operations, don't mute system devices (master, input, output)
            // But allow manual mute to work on all devices
            if (isFromSolo && 
                (session.SessionId == "master_output" || 
                 session.SessionId.StartsWith("input_") || 
                 session.SessionId.StartsWith("output_")))
            {
                continue;
            }

            _audioSessionManager.SetSessionMute(session.SessionId, isMuted);
        }
    }

    private void ApplyMuteToSessions(ChannelViewModel channel)
    {
        if (_audioSessionManager == null) return;

        // Check if this channel contains only system devices (master, input, output)
        bool isSystemDeviceChannel = channel.AssignedSessions.All(s => 
            s.SessionId == "master_output" || 
            s.SessionId.StartsWith("input_") || 
            s.SessionId.StartsWith("output_"));

        // Check if any channel is currently soloed
        var soloedChannel = Channels.FirstOrDefault(c => c.IsSoloed);

        if (soloedChannel != null && !isSystemDeviceChannel)
        {
            // Solo mode is active - only allow mute changes on application channels that are soloed
            // But always allow system devices to be muted
            if (channel != soloedChannel)
            {
                // Don't apply mute changes to non-soloed application channels
                // They should stay muted due to solo mode
                return;
            }
        }

        // Apply the mute change
        foreach (var session in channel.AssignedSessions)
        {
            _audioSessionManager.SetSessionMute(session.SessionId, channel.IsMuted);
        }
    }
    
    /// <summary>
    /// Plays startup animation and then restores LED states
    /// </summary>
    private async Task PlayStartupAnimationAndRestoreLeds()
    {
        await PlayStartupAnimation();
        // Wait a bit for animation to settle
        await Task.Delay(200);
        // Restore all LED states based on current mute/solo states
        RestoreAllLedStates();
    }

    /// <summary>
    /// Restores all mute and solo LED states for all channels
    /// </summary>
    private void RestoreAllLedStates()
    {
        foreach (var channel in Channels)
        {
            // Restore mute LED
            SendMuteLedFeedback(channel, channel.IsMuted);
            
            // Restore solo LED
            SendSoloLedFeedback(channel, channel.IsSoloed);
        }
    }

    /// <summary>
    /// Plays a cool LED startup animation on M-Vave SMC mixer
    /// </summary>
    private async Task PlayStartupAnimation()
    {
        if (_midiService == null || !IsMidiConnected)
            return;
        
        await Task.Run(async () =>
        {
            try
            {
                Console.WriteLine("🎨 Playing startup animation...");
                
                // M-Vave SMC button layout (based on Mixxx XML):
                // Row 1: Notes 0x00-0x07 (top row buttons)
                // Row 2: Notes 0x08-0x0F (second row)
                // Row 3: Notes 0x10-0x17 (third row)
                
                // Animation: Sweep across all channels
                for (int channel = 0; channel < 8; channel++)
                {
                    // Light up all buttons in this channel
                    _midiService.SendNoteOn(0, 0x00 + channel, 127);
                    _midiService.SendNoteOn(0, 0x08 + channel, 127);
                    _midiService.SendNoteOn(0, 0x10 + channel, 127);
                    
                    await Task.Delay(50);
                    
                    // Turn off previous channel (trailing effect)
                    if (channel > 0)
                    {
                        _midiService.SendNoteOn(0, 0x00 + (channel - 1), 0);
                        _midiService.SendNoteOn(0, 0x08 + (channel - 1), 0);
                        _midiService.SendNoteOn(0, 0x10 + (channel - 1), 0);
                    }
                }
                
                // Turn off last channel
                await Task.Delay(50);
                _midiService.SendNoteOn(0, 0x07, 0);
                _midiService.SendNoteOn(0, 0x0F, 0);
                _midiService.SendNoteOn(0, 0x17, 0);
                
                // Flash all LEDs twice
                await Task.Delay(100);
                for (int flash = 0; flash < 2; flash++)
                {
                    // All on
                    for (int note = 0x00; note <= 0x17; note++)
                    {
                        _midiService.SendNoteOn(0, note, 127);
                    }
                    await Task.Delay(80);
                    
                    // All off
                    for (int note = 0x00; note <= 0x17; note++)
                    {
                        _midiService.SendNoteOn(0, note, 0);
                    }
                    await Task.Delay(80);
                }
                
                Console.WriteLine("✨ Startup animation complete!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing startup animation: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Updates the Windows startup registry entry
    /// </summary>
    private void UpdateStartupRegistry(bool enable)
    {
        try
        {
            const string appName = "Mideej";
            // Ensure the Run key exists; create if missing
            var startupKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true)
                ?? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);

            if (startupKey == null)
            {
                Console.WriteLine("Could not access or create startup registry key");
                return;
            }

            if (enable)
            {
                var target = ResolveStartupTargetPath();
                if (!string.IsNullOrEmpty(target))
                {
                    // Use dfshim for ClickOnce appref-ms for reliability; otherwise run the exe directly
                    string startupValue = target.EndsWith(".appref-ms", StringComparison.OrdinalIgnoreCase)
                        ? $"rundll32.exe dfshim.dll,ShOpenVerbShortcut \"{target}\""
                        : $"\"{target}\"";
                    startupKey.SetValue(appName, startupValue);
                    Console.WriteLine($"Added to Windows startup: {startupValue}");
                }
            }
            else
            {
                startupKey.DeleteValue(appName, false);
                Console.WriteLine("Removed from Windows startup");
            }

            startupKey.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating startup registry: {ex.Message}");
            StatusMessage = $"Error updating startup: {ex.Message}";
        }
    }

    private string? ResolveStartupTargetPath()
    {
        try
        {
            // Try to locate a ClickOnce .appref-ms shortcut first (stable across updates)
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (Directory.Exists(programs))
            {
                var candidates = Directory.GetFiles(programs, "*.appref-ms", SearchOption.AllDirectories);
                var appref = candidates.FirstOrDefault(p =>
                    string.Equals(Path.GetFileNameWithoutExtension(p), "Mideej", StringComparison.OrdinalIgnoreCase))
                    ?? candidates.FirstOrDefault(p => p.IndexOf("Mideej", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!string.IsNullOrEmpty(appref))
                    return appref;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ResolveStartupTargetPath appref-ms probe failed: {ex.Message}");
        }

        try
        {
            // Fallback to current executable path
            return System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ResolveStartupTargetPath exe probe failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Handles Record button press for switching default audio devices
    /// </summary>
    private void HandleRecordButtonPress(ChannelViewModel channel, int midiChannel, int noteNumber)
    {
        Console.WriteLine($"[HandleRecordButtonPress] Called for {channel.Name}");

        if (_audioSessionManager == null)
        {
            Console.WriteLine($"[HandleRecordButtonPress] AudioSessionManager is null!");
            return;
        }

        // Check if this channel has any assigned sessions
        if (channel.AssignedSessions.Count == 0)
        {
            StatusMessage = $"{channel.Name} - No device assigned";
            Console.WriteLine($"[HandleRecordButtonPress] {channel.Name} has no sessions assigned");
            return;
        }

        // Get the first session to check its type
        var session = channel.AssignedSessions[0];
        Console.WriteLine($"[HandleRecordButtonPress] {channel.Name} - SessionId: {session.SessionId}, Type: {session.SessionType}, DisplayName: {session.DisplayName}");

        // Ignore Record button on application sessions
        if (session.SessionType == AudioSessionType.Application ||
            session.SessionType == AudioSessionType.SystemSounds)
        {
            Console.WriteLine($"[HandleRecordButtonPress] Ignored - application/system session: {channel.Name}");
            return;
        }

        // Handle output device switching
        if (session.SessionType == AudioSessionType.Output && session.SessionId.StartsWith("output_"))
        {
            Console.WriteLine($"[HandleRecordButtonPress] Handling output device switch for {session.DisplayName}");

            // Get the current default output device
            var currentDefaultOutputId = _audioSessionManager.GetDefaultPlaybackDeviceId();
            Console.WriteLine($"[HandleRecordButtonPress] Current default output: {currentDefaultOutputId}");

            // If this device is already the default, provide feedback
            if (session.SessionId == currentDefaultOutputId)
            {
                Console.WriteLine($"[HandleRecordButtonPress] Device {session.DisplayName} is already the default output");
                StatusMessage = $"{session.DisplayName} is already default output";
                // Still send LED feedback to confirm it's the default
                SendRecordLedFeedback(channel, true);
                return;
            }

            // Switch default output device
            Console.WriteLine($"[HandleRecordButtonPress] Attempting to switch default output to: {session.SessionId}");
            if (_audioSessionManager.SetDefaultPlaybackDevice(session.SessionId))
            {
                // Turn off Record LED on the old default channel (if any)
                if (currentDefaultOutputId != null)
                {
                    var oldDefaultChannel = Channels.FirstOrDefault(ch =>
                        ch.AssignedSessions.Any(s => s.SessionId == currentDefaultOutputId));
                    if (oldDefaultChannel != null)
                    {
                        SendRecordLedFeedback(oldDefaultChannel, false);
                    }
                }

                // Turn on Record LED on the new default channel
                SendRecordLedFeedback(channel, true);

                StatusMessage = $"Default output: {session.DisplayName}";
                Console.WriteLine($"Switched default output to: {session.DisplayName}");
            }
            else
            {
                StatusMessage = $"Failed to set default output";
                Console.WriteLine($"Failed to switch default output to: {session.DisplayName}");
            }
        }
        // Handle input device switching
        else if (session.SessionType == AudioSessionType.Input && session.SessionId.StartsWith("input_"))
        {
            Console.WriteLine($"[HandleRecordButtonPress] Handling input device switch for {session.DisplayName}");

            // Get the current default input device
            var currentDefaultInputId = _audioSessionManager.GetDefaultRecordingDeviceId();
            Console.WriteLine($"[HandleRecordButtonPress] Current default input: {currentDefaultInputId}");

            // If this device is already the default, provide feedback
            if (session.SessionId == currentDefaultInputId)
            {
                Console.WriteLine($"[HandleRecordButtonPress] Device {session.DisplayName} is already the default input");
                StatusMessage = $"{session.DisplayName} is already default input";
                // Still send LED feedback to confirm it's the default
                SendRecordLedFeedback(channel, true);
                return;
            }

            // Switch default input device
            Console.WriteLine($"[HandleRecordButtonPress] Attempting to switch default input to: {session.SessionId}");
            if (_audioSessionManager.SetDefaultRecordingDevice(session.SessionId))
            {
                // Turn off Record LED on the old default channel (if any)
                if (currentDefaultInputId != null)
                {
                    var oldDefaultChannel = Channels.FirstOrDefault(ch =>
                        ch.AssignedSessions.Any(s => s.SessionId == currentDefaultInputId));
                    if (oldDefaultChannel != null)
                    {
                        SendRecordLedFeedback(oldDefaultChannel, false);
                    }
                }

                // Turn on Record LED on the new default channel
                SendRecordLedFeedback(channel, true);

                StatusMessage = $"Default input: {session.DisplayName}";
                Console.WriteLine($"Switched default input to: {session.DisplayName}");
            }
            else
            {
                StatusMessage = $"Failed to set default input";
                Console.WriteLine($"Failed to switch default input to: {session.DisplayName}");
            }
        }
        else
        {
            // Unexpected session type or ID format
            Console.WriteLine($"[HandleRecordButtonPress] Unexpected session - Type: {session.SessionType}, SessionId: {session.SessionId}");
            StatusMessage = $"{channel.Name} - Cannot switch (invalid session type)";
        }
    }

    /// <summary>
    /// Sends LED feedback to MIDI controller for record button state
    /// </summary>
    private void SendRecordLedFeedback(ChannelViewModel channel, bool isOn)
    {
        if (_midiService == null) return;

        // Find the MIDI mapping for this channel's record button
        foreach (var kvp in _midiMappings)
        {
            var mapping = kvp.Value;
            if (mapping.TargetChannelIndex == channel.Index && mapping.ControlType == MidiControlType.Record)
            {
                // Send LED feedback (on=127, off=0)
                _midiService.SendNoteOn(mapping.Channel, mapping.ControlNumber, isOn ? 127 : 0);
                Console.WriteLine($"Record LED feedback sent for {channel.Name}: {(isOn ? "ON" : "OFF")}");
                break;
            }
        }
    }

    /// <summary>
    /// Sends LED feedback to MIDI controller for select button (audio activity indicator)
    /// </summary>
    private void SendSelectLedFeedback(ChannelViewModel channel, bool isOn)
    {
        if (_midiService == null) return;

        // Find the MIDI mapping for this channel's select button
        foreach (var kvp in _midiMappings)
        {
            var mapping = kvp.Value;
            if (mapping.TargetChannelIndex == channel.Index && mapping.ControlType == MidiControlType.Select)
            {
                // Send LED feedback (on=127, off=0)
                _midiService.SendNoteOn(mapping.Channel, mapping.ControlNumber, isOn ? 127 : 0);
                break;
            }
        }
    }

    /// <summary>
    /// Handles property changes on channels (primarily for audio activity LED feedback)
    /// </summary>
    private void OnChannelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not ChannelViewModel channel) return;

        // Send LED feedback when audio activity state changes
        if (e.PropertyName == nameof(ChannelViewModel.IsAudioActive))
        {
            SendSelectLedFeedback(channel, channel.IsAudioActive);
        }
    }

    /// <summary>
    /// Initializes Record LEDs for channels with default devices on startup
    /// </summary>
    private void InitializeDefaultDeviceRecordLeds()
    {
        if (_audioSessionManager == null)
            return;

        try
        {
            // Get default playback device
            var defaultOutputId = _audioSessionManager.GetDefaultPlaybackDeviceId();
            if (defaultOutputId != null)
            {
                var outputChannel = Channels.FirstOrDefault(ch =>
                    ch.AssignedSessions.Any(s => s.SessionId == defaultOutputId));
                if (outputChannel != null)
                {
                    SendRecordLedFeedback(outputChannel, true);
                    Console.WriteLine($"Lit Record LED for default output: {outputChannel.Name}");
                }
            }

            // Get default recording device
            var defaultInputId = _audioSessionManager.GetDefaultRecordingDeviceId();
            if (defaultInputId != null)
            {
                var inputChannel = Channels.FirstOrDefault(ch =>
                    ch.AssignedSessions.Any(s => s.SessionId == defaultInputId));
                if (inputChannel != null)
                {
                    SendRecordLedFeedback(inputChannel, true);
                    Console.WriteLine($"Lit Record LED for default input: {inputChannel.Name}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error initializing default device Record LEDs: {ex.Message}");
        }
    }

    /// <summary>
    /// Relinks channel sessions from saved configuration to actual audio session objects
    /// </summary>
    private void RelinkChannelSessions()
    {
        if (_pendingChannelConfigs.Count > 0)
        {
            for (int i = 0; i < Channels.Count && i < _pendingChannelConfigs.Count; i++)
            {
                var channel = Channels[i];
                var config = _pendingChannelConfigs[i];

                channel.RelinkSessions(config, AvailableSessions);
                Console.WriteLine($"Relinked {channel.AssignedSessions.Count} sessions to {channel.Name} (from pending config)");
            }

            _pendingChannelConfigs.Clear();
            return;
        }

        // Otherwise, use channels' intended assignments to relink dynamically
        // First collect all intended session assignments (even if not yet matched) to know what's claimed
        var claimedSessions = new HashSet<string>();
        var claimedProcesses = new HashSet<int>();
        var claimedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var ch in Channels)
        {
            foreach (var intent in ch.IntendedAssignments)
            {
                // Skip special markers
                if (intent.SessionId == "unmapped_apps" || intent.SessionId == "focused_app")
                    continue;
                    
                // Claim by exact session ID
                if (!string.IsNullOrEmpty(intent.SessionId))
                    claimedSessions.Add(intent.SessionId);
                    
                // Claim by process ID
                if (intent.ProcessId.HasValue)
                    claimedProcesses.Add(intent.ProcessId.Value);
                    
                // Claim by process name
                if (!string.IsNullOrWhiteSpace(intent.ProcessName))
                    claimedProcessNames.Add(intent.ProcessName);
            }
        }

        // Now relink each channel
        foreach (var channel in Channels)
        {
            channel.RelinkFromIntended(AvailableSessions);
            
            // Handle special "unmapped_apps" marker
            if (channel.IntendedAssignments.Any(r => r.SessionId == "unmapped_apps"))
            {
                // Add all application sessions that aren't claimed by any other channel
                var unmappedApps = AvailableSessions
                    .Where(s => s.SessionType == AudioSessionType.Application && 
                               !claimedSessions.Contains(s.SessionId) &&
                               !claimedProcesses.Contains(s.ProcessId) &&
                               !claimedProcessNames.Contains(s.ProcessName ?? "") &&
                               !channel.AssignedSessions.Any(existing => existing.SessionId == s.SessionId))
                    .ToList();
                
                foreach (var app in unmappedApps)
                {
                    channel.AssignedSessions.Add(app);
                }
                
                Console.WriteLine($"Added {unmappedApps.Count} unmapped applications to {channel.Name}");
            }
            
            // TODO: Handle special "focused_app" marker (requires active window tracking)
            
            Console.WriteLine($"Relinked {channel.AssignedSessions.Count} sessions to {channel.Name} (dynamic refresh)");
            
            // Sync initial state from audio session to UI
            SyncChannelStateFromSession(channel);
        }
    }

    /// <summary>
    /// Syncs the channel's UI state with the actual audio session state
    /// </summary>
    private void SyncChannelStateFromSession(ChannelViewModel channel)
    {
        // Find master_output session and sync its mute state
        var masterSession = channel.AssignedSessions.FirstOrDefault(s => s.SessionId == "master_output");
        if (masterSession != null)
        {
            channel.IsMuted = masterSession.IsMuted;
            Console.WriteLine($"Synced {channel.Name} mute state: {masterSession.IsMuted}");
        }
    }
}
