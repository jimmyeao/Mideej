namespace Mideej.Models;

/// <summary>
/// Configuration for a single audio channel
/// </summary>
public class ChannelConfiguration
{
    /// <summary>
    /// Index of the channel
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Display name for the channel
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// IDs of audio sessions assigned to this channel
    /// </summary>
    public List<string> AssignedSessionIds { get; set; } = new();

    /// <summary>
    /// Whether the channel is muted
    /// </summary>
    public bool IsMuted { get; set; }

    /// <summary>
    /// Whether the channel is soloed
    /// </summary>
    public bool IsSoloed { get; set; }

    /// <summary>
    /// Current volume (0.0 to 1.0)
    /// </summary>
    public float Volume { get; set; } = 1f;

    /// <summary>
    /// Filter configuration for this channel
    /// </summary>
    public FilterConfiguration? Filter { get; set; }

    /// <summary>
    /// Color for the channel (hex format)
    /// </summary>
    public string Color { get; set; } = "#3B82F6";

    /// <summary>
    /// Type of the assigned session (if any)
    /// </summary>
    public AudioSessionType? SessionType { get; set; }
}

/// <summary>
/// Audio filter configuration
/// </summary>
public class FilterConfiguration
{
    /// <summary>
    /// Whether the filter is enabled
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Type of filter
    /// </summary>
    public FilterType Type { get; set; } = FilterType.LowPass;

    /// <summary>
    /// Cutoff frequency in Hz
    /// </summary>
    public float Cutoff { get; set; } = 1000f;

    /// <summary>
    /// Filter resonance (Q factor)
    /// </summary>
    public float Resonance { get; set; } = 1f;
}

/// <summary>
/// Types of audio filters
/// </summary>
public enum FilterType
{
    LowPass,
    HighPass,
    BandPass,
    Notch
}
