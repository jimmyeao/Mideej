using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mideej.Models;
using Mideej.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace Mideej.ViewModels;

/// <summary>
/// Main window ViewModel
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IMidiService? _midiService;
    private readonly IAudioSessionManager? _audioSessionManager;
    private readonly IConfigurationService? _configurationService;

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
    private string _controlTypeToMap = "Volume"; // Volume, Mute, Solo, Record, Select

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private AppTheme _currentTheme = AppTheme.Dark;

    [ObservableProperty]
    private double _windowWidth = 1200;

    [ObservableProperty]
    private double _windowHeight = 700;

    public ObservableCollection<ThemeOption> AvailableThemes { get; } = new()
{
    new ThemeOption { Name = "DarkTheme", DisplayName = "Dark 🌙" },
    new ThemeOption { Name = "LightTheme", DisplayName = "Light ☀️" },
    new ThemeOption { Name = "CyberpunkTheme", DisplayName = "Cyberpunk 🌈" },
    new ThemeOption { Name = "ForestTheme", DisplayName = "Forest 🌿" },
    new ThemeOption { Name = "ArcticTheme", DisplayName = "Arctic ❄️" }
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
        IConfigurationService configurationService)
    {
        _midiService = midiService;
        _audioSessionManager = audioSessionManager;
        _configurationService = configurationService;

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
            _audioSessionManager.StartMonitoring();
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
        SelectedTheme = AvailableThemes.FirstOrDefault(t => t.Name == "DarkTheme");
        ApplyTheme(SelectedTheme);
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
    private void ToggleTheme()
    {
        CurrentTheme = CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        // Theme change logic will be handled by the view
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
        WindowWidth = (channelCount * channelWidth) + windowChrome;
    }

    private void SubscribeToChannelEvents(ChannelViewModel channel)
    {
        channel.VolumeChanged += OnChannelVolumeChanged;
        channel.MuteChanged += OnChannelMuteChanged;
        channel.SoloChanged += OnChannelSoloChanged;
        channel.MappingModeRequested += OnChannelMappingModeRequested;
        channel.SessionAssignmentRequested += OnChannelSessionAssignmentRequested;
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
        }
    }

    private void OnChannelSoloChanged(object? sender, EventArgs e)
    {
        if (sender is ChannelViewModel soloedChannel && soloedChannel.IsSoloed)
        {
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
        var dialog = new SessionAssignmentDialog(AvailableSessions, mappedSessionIds)
        {
            Owner = Application.Current.MainWindow
        };

        if (dialog.ShowDialog() == true && dialog.SelectedSessions.Count > 0)
        {
            // Clear existing session assignments
            channel.AssignedSessions.Clear();

            // Assign all selected sessions
            foreach (var session in dialog.SelectedSessions)
            {
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
                // Multiple sessions assigned
                channel.Name = $"{dialog.SelectedSessions.Count} Sessions";
                channel.SessionType = dialog.SelectedSessions[0].SessionType;
            }

            StatusMessage = $"Assigned {dialog.SelectedSessions.Count} session(s) to Channel {channel.Index + 1}";

            // Save configuration
            _ = SaveConfigurationAsync();
        }
    }

    [RelayCommand]
    private void RemoveChannel(ChannelViewModel channel)
    {
        Channels.Remove(channel);
        // Re-index remaining channels
        for (int i = 0; i < Channels.Count; i++)
        {
            Channels[i].Index = i;
        }
        UpdateWindowSize();
    }

    private void OnMidiControlChange(object? sender, MidiControlChangeEventArgs e)
    {
        // Debug: Show we received a MIDI message
        Console.WriteLine($"MIDI CC: Ch{e.Channel} CC{e.Controller} Val{e.Value}");

        // Handle MIDI CC messages
        if (IsMappingModeActive && ChannelAwaitingMapping != null)
        {
            // Only map CC to Volume if that's what the user selected
            if (ControlTypeToMap == "Volume")
            {
                Console.WriteLine($"  Mapping CC to Volume");
                CreateMapping(e.Channel, e.Controller, ChannelAwaitingMapping);
                CancelMappingMode();
            }
            else
            {
                StatusMessage = $"Expecting a button press for {ControlTypeToMap}, but received CC message. Try a fader/knob for Volume mapping.";
                Console.WriteLine($"  Ignoring CC - expecting {ControlTypeToMap}");
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
        if (IsMappingModeActive && ChannelAwaitingMapping != null)
        {
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

        // In Mackie mode, the fader channel directly corresponds to the mixer channel (1-8)
        // So channel 0 = Channel 1, channel 1 = Channel 2, etc.

        if (IsMappingModeActive && ChannelAwaitingMapping != null)
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
            // Apply fader value to the mapped channel
            ApplyMidiFader(e.Channel, e.Value);
        }
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
            _configurationService.CurrentSettings.MidiMappings = _midiMappings.Values.ToList();
            _ = _configurationService.SaveCurrentSettingsAsync();
        }

        StatusMessage = $"Mapped MIDI CH{midiChannel + 1} Note{noteNumber} to {channel.Name} {controlType}";
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
            _configurationService.CurrentSettings.MidiMappings = _midiMappings.Values.ToList();
            _ = _configurationService.SaveCurrentSettingsAsync();
        }

        StatusMessage = $"Mapped Fader on MIDI CH{midiChannel + 1} to {channel.Name}";
        Console.WriteLine($"Created fader mapping: MIDI CH{midiChannel} -> {channel.Name}");
    }

    private void ApplyMidiFader(int midiChannel, int pitchBendValue)
    {
        var key = (midiChannel, -1); // Look for pitch bend mapping
        if (!_midiMappings.TryGetValue(key, out var mapping))
        {
            // No mapping for this fader yet
            return;
        }

        if (mapping.TargetChannelIndex >= Channels.Count)
        {
            return;
        }

        var channel = Channels[mapping.TargetChannelIndex];

        // Convert pitch bend value (0-16383) to volume (0.0-1.0)
        float volume = pitchBendValue / 16383f;
        channel.Volume = volume;

        // Apply to audio sessions
        ApplyVolumeToSessions(channel);

        // Send feedback to motorized fader
        _midiService?.SendPitchBend(midiChannel, pitchBendValue);
    }

    private void ApplyMidiNote(int midiChannel, int noteNumber)
    {
        var key = (midiChannel, noteNumber);
        if (!_midiMappings.TryGetValue(key, out var mapping))
        {
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
                channel.ToggleRecord();
                // Send LED feedback
                _midiService?.SendNoteOn(midiChannel, noteNumber, channel.IsRecording ? 127 : 0);
                StatusMessage = $"{channel.Name} - Recording: {(channel.IsRecording ? "ON" : "OFF")}";
                Console.WriteLine($"Record toggled for {channel.Name}: {channel.IsRecording}");
                break;

            case MidiControlType.Select:
                channel.ToggleSelect();
                // Send LED feedback
                _midiService?.SendNoteOn(midiChannel, noteNumber, channel.IsSelected ? 127 : 0);
                StatusMessage = $"{channel.Name} - Selected: {(channel.IsSelected ? "ON" : "OFF")}";
                Console.WriteLine($"Select toggled for {channel.Name}: {channel.IsSelected}");
                break;
        }
    }

    private void ApplyMuteToSessions(ChannelViewModel channel)
    {
        if (_audioSessionManager == null) return;

        // Check if any channel is currently soloed
        var soloedChannel = Channels.FirstOrDefault(c => c.IsSoloed);

        if (soloedChannel != null)
        {
            // Solo mode is active - only allow mute changes on the soloed channel
            // Non-soloed channels must remain muted regardless of their IsMuted flag
            if (channel != soloedChannel)
            {
                // Don't apply mute changes to non-soloed channels
                // They should stay muted due to solo mode
                Console.WriteLine($"Mute toggle ignored for {channel.Name} - {soloedChannel.Name} is soloed");
                return;
            }
        }

        // Apply the mute change
        foreach (var session in channel.AssignedSessions)
        {
            _audioSessionManager.SetSessionMute(session.SessionId, channel.IsMuted);
        }
    }

    private void ApplySoloLogic()
    {
        // If any channel is soloed, mute all non-soloed channels
        var soloedChannels = Channels.Where(c => c.IsSoloed).ToList();

        if (soloedChannels.Any())
        {
            // Mute all non-soloed channels
            foreach (var channel in Channels)
            {
                bool shouldBeMuted = !channel.IsSoloed;
                ApplyEffectiveMute(channel, shouldBeMuted);
            }
        }
        else
        {
            // No solo active, restore user mute states
            foreach (var channel in Channels)
            {
                ApplyEffectiveMute(channel, channel.IsMuted);
            }
        }
    }

    private void ApplyEffectiveMute(ChannelViewModel channel, bool isMuted)
    {
        if (_audioSessionManager == null) return;

        foreach (var session in channel.AssignedSessions)
        {
            // Don't mute master volume during solo
            if (session.SessionId == "master_output")
            {
                continue;
            }

            _audioSessionManager.SetSessionMute(session.SessionId, isMuted);
        }
    }

    private void OnMidiDeviceStateChanged(object? sender, MidiDeviceEventArgs e)
    {
        IsMidiConnected = e.IsConnected;
        StatusMessage = e.IsConnected ? $"Connected to {e.Device.Name}" : "MIDI device disconnected";
    }

    private void OnAudioSessionsChanged(object? sender, AudioSessionChangedEventArgs e)
    {
        AvailableSessions.Clear();
        foreach (var session in e.Sessions)
        {
            AvailableSessions.Add(session);
        }

        // Relink channel sessions if we have pending configs
        if (_pendingChannelConfigs.Count > 0)
        {
            RelinkChannelSessions();
        }
    }

    /// <summary>
    /// Relinks channel sessions from saved configuration to actual audio session objects
    /// </summary>
    private void RelinkChannelSessions()
    {
        for (int i = 0; i < Channels.Count && i < _pendingChannelConfigs.Count; i++)
        {
            var channel = Channels[i];
            var config = _pendingChannelConfigs[i];

            if (config.AssignedSessionIds.Count > 0)
            {
                channel.RelinkSessions(config.AssignedSessionIds, AvailableSessions);
                Console.WriteLine($"Relinked {channel.AssignedSessions.Count} sessions to {channel.Name}");
            }
        }
    }

    private void OnPeakLevelsUpdated(object? sender, PeakLevelEventArgs e)
    {
        // Update VU meters
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
        }
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
            _configurationService.CurrentSettings.MidiMappings = _midiMappings.Values.ToList();
            _ = _configurationService.SaveCurrentSettingsAsync();
        }

        StatusMessage = $"Mapped MIDI CH{midiChannel + 1} CC{controller} to {channel.Name}";
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

    private async void LoadConfiguration()
    {
        if (_configurationService == null) return;

        try
        {
            var settings = await _configurationService.LoadSettingsAsync();
            CurrentTheme = settings.Theme;

            // Load channels from configuration
            Channels.Clear();
            _pendingChannelConfigs = settings.Channels;

            foreach (var channelConfig in settings.Channels)
            {
                var channelVm = new ChannelViewModel();
                channelVm.LoadConfiguration(channelConfig);
                SubscribeToChannelEvents(channelVm);
                Channels.Add(channelVm);
            }

            // Relink sessions if available
            if (AvailableSessions.Count > 0)
            {
                RelinkChannelSessions();
            }

            // Update window size based on loaded channels
            UpdateWindowSize();

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

        var settings = _configurationService.CurrentSettings;
        settings.Theme = CurrentTheme;
        settings.SelectedMidiDevice = SelectedMidiDevice?.Name;
        settings.Channels = Channels.Select(c => c.ToConfiguration()).ToList();
        settings.MidiMappings = _midiMappings.Values.ToList();

        await _configurationService.SaveSettingsAsync(settings);
    }
}
