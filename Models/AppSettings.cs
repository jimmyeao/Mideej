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
    /// Application theme name (DarkTheme, LightTheme, etc.)
    /// </summary>
    public string SelectedTheme { get; set; } = "DarkTheme";

    /// <summary>
    /// Whether to start with Windows
    /// </summary>
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Whether to start minimized
    /// </summary>
    public bool StartMinimized { get; set; }

    /// <summary>
    /// Whether to minimize to tray instead of taskbar
    /// </summary>
    public bool MinimizeToTray { get; set; }

    /// <summary>
    /// VU meter refresh rate in milliseconds
    /// </summary>
    public int VuMeterRefreshRate { get; set; } = 30;

    /// <summary>
    /// Window width
    /// </summary>
    public double WindowWidth { get; set; } = 1200;

    /// <summary>
    /// Window height
    /// </summary>
    public double WindowHeight { get; set; } = 700;

    /// <summary>
    /// Window left position
    /// </summary>
    public double WindowLeft { get; set; } = double.NaN;

    /// <summary>
    /// Window top position
    /// </summary>
    public double WindowTop { get; set; } = double.NaN;

    /// <summary>
    /// Window state (Normal, Maximized, Minimized)
    /// </summary>
    public int WindowState { get; set; } = 0; // 0 = Normal

    /// <summary>
    /// Font size scale (0.8 = Small, 1.0 = Normal, 1.2 = Large, 1.4 = Extra Large)
    /// </summary>
    public double FontSizeScale { get; set; } = 1.0;
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
