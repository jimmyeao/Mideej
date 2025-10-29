namespace Mideej.Models;

/// <summary>
/// Represents a mapping between a MIDI control and an application action
/// </summary>
public class MidiMapping
{
    /// <summary>
    /// MIDI channel (0-15)
    /// </summary>
    public int Channel { get; set; }

    /// <summary>
    /// MIDI CC number (0-127)
    /// </summary>
    public int ControlNumber { get; set; }

    /// <summary>
    /// Target channel index in the application
    /// Use -1 for global (non-channel) actions like transport controls
    /// </summary>
    public int TargetChannelIndex { get; set; }

    /// <summary>
    /// Type of control action
    /// </summary>
    public MidiControlType ControlType { get; set; }

    /// <summary>
    /// Minimum value for scaling (default 0)
    /// </summary>
    public float MinValue { get; set; } = 0f;

    /// <summary>
    /// Maximum value for scaling (default 1)
    /// </summary>
    public float MaxValue { get; set; } = 1f;

    /// <summary>
    /// Whether this mapping is inverted (max becomes min)
    /// </summary>
    public bool IsInverted { get; set; }
}

/// <summary>
/// Types of MIDI control actions
/// </summary>
public enum MidiControlType
{
    Volume,
    Mute,
    Solo,
    Record,
    Select,
    CycleSession,
    FilterCutoff,
    FilterResonance,
    FilterType,
    Pan,
    // Global transport controls (limited set)
    TransportPlay,
    TransportPause,
    TransportNext,
    TransportPrevious
}
