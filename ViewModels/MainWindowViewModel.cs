using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mideej.Models;
using Mideej.Services;

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
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private AppTheme _currentTheme = AppTheme.Dark;

    /// <summary>
    /// MIDI mappings: Key is (channel, controller), Value is the mapping
    /// </summary>
    private readonly Dictionary<(int channel, int controller), MidiMapping> _midiMappings = new();

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
        var success = await _midiService.ConnectAsync(SelectedMidiDevice.DeviceId);

        if (success)
        {
            IsMidiConnected = true;
            StatusMessage = $"Connected to {SelectedMidiDevice.Name}";
        }
        else
        {
            StatusMessage = $"Failed to connect to {SelectedMidiDevice.Name}";
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
        foreach (var device in devices)
        {
            AvailableMidiDevices.Add(device);
        }
    }

    [RelayCommand]
    private void StartMappingMode(ChannelViewModel channel)
    {
        if (_midiService == null) return;

        ChannelAwaitingMapping = channel;
        channel.IsInMappingMode = true;
        IsMappingModeActive = true;
        _midiService.StartMappingMode();
        StatusMessage = $"Move a MIDI control to map to {channel.Name}...";
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
        StatusMessage = "Mapping cancelled";
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
    }

    private void SubscribeToChannelEvents(ChannelViewModel channel)
    {
        channel.VolumeChanged += OnChannelVolumeChanged;
        channel.MuteChanged += OnChannelMuteChanged;
        channel.SoloChanged += OnChannelSoloChanged;
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
        ApplySoloLogic();
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
    }

    private void OnMidiControlChange(object? sender, MidiControlChangeEventArgs e)
    {
        // Debug: Show we received a MIDI message
        Console.WriteLine($"MIDI CC: Ch{e.Channel} CC{e.Controller} Val{e.Value}");

        // Handle MIDI CC messages
        if (IsMappingModeActive && ChannelAwaitingMapping != null)
        {
            // Create new mapping
            CreateMapping(e.Channel, e.Controller, ChannelAwaitingMapping);
            CancelMappingMode();
        }
        else
        {
            // Apply existing mapping
            ApplyMidiControl(e.Channel, e.Controller, e.Value);
        }
    }

    private void OnMidiNoteOn(object? sender, MidiNoteEventArgs e)
    {
        // Handle MIDI note messages (can be used for mute/solo buttons)
        if (IsMappingModeActive && ChannelAwaitingMapping != null)
        {
            // Create mapping for note-based control (mute button)
            CreateNoteMapping(e.Channel, e.NoteNumber, ChannelAwaitingMapping, MidiControlType.Mute);
            CancelMappingMode();
        }
        else
        {
            // Apply existing note mapping
            ApplyMidiNote(e.Channel, e.NoteNumber);
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
                break;

            case MidiControlType.Solo:
                channel.ToggleSolo();
                ApplySoloLogic();
                break;
        }
    }

    private void ApplyMuteToSessions(ChannelViewModel channel)
    {
        if (_audioSessionManager == null) return;

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
            foreach (var channelConfig in settings.Channels)
            {
                var channelVm = new ChannelViewModel();
                channelVm.LoadConfiguration(channelConfig);
                SubscribeToChannelEvents(channelVm);
                Channels.Add(channelVm);
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
