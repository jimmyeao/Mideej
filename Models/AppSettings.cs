namespace Mideej.Models;

/// <summary>
/// Application settings and configuration
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Selected MIDI device name
    /// </summary>
    public string? SelectedMidiDevice { get; set; }

    /// <summary>
    /// List of channel configurations
    /// </summary>
    public List<ChannelConfiguration> Channels { get; set; } = new();

    /// <summary>
    /// MIDI mappings
    /// </summary>
    public List<MidiMapping> MidiMappings { get; set; } = new();

    /// <summary>
    /// Active profile name
    /// </summary>
    public string? ActiveProfile { get; set; }

    /// <summary>
    /// All saved profiles
    /// </summary>
    public Dictionary<string, Profile> Profiles { get; set; } = new();

    /// <summary>
    /// Application theme (Light/Dark)
    /// </summary>
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>
    /// Whether to start with Windows
    /// </summary>
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Whether to start minimized
    /// </summary>
    public bool StartMinimized { get; set; }

    /// <summary>
    /// VU meter refresh rate in milliseconds
    /// </summary>
    public int VuMeterRefreshRate { get; set; } = 30;
}

/// <summary>
/// Profile containing saved settings
/// </summary>
public class Profile
{
    /// <summary>
    /// Profile name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Channel configurations
    /// </summary>
    public List<ChannelConfiguration> Channels { get; set; } = new();

    /// <summary>
    /// MIDI mappings
    /// </summary>
    public List<MidiMapping> MidiMappings { get; set; } = new();
}

/// <summary>
/// Application theme options
/// </summary>
public enum AppTheme
{
    Light,
    Dark,
    System
}
