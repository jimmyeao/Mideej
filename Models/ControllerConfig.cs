namespace Mideej.Models;

/// <summary>
/// Portable configuration for a MIDI controller that can be exported/imported
/// </summary>
public class ControllerConfig
{
    /// <summary>
    /// Name of the controller configuration (e.g., "Akai APC40", "Behringer X-Touch Mini")
    /// </summary>
    public string ControllerName { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the controller setup
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// MIDI mappings for this controller
    /// </summary>
    public List<MidiMapping> MidiMappings { get; set; } = new();

    /// <summary>
    /// Optional channel configurations (if you want to export channel layout with the controller)
    /// </summary>
    public List<ChannelConfiguration>? Channels { get; set; }

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last modified timestamp
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Version of the config format (for future compatibility)
    /// </summary>
    public int Version { get; set; } = 1;
}
