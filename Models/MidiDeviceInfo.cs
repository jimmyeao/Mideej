namespace Mideej.Models;

/// <summary>
/// Information about a MIDI device
/// </summary>
public class MidiDeviceInfo
{
    /// <summary>
    /// Device ID
    /// </summary>
    public int DeviceId { get; set; }

    /// <summary>
    /// Device name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether the device is currently connected
    /// </summary>
    public bool IsConnected { get; set; }

    /// <summary>
    /// Manufacturer name
    /// </summary>
    public string? Manufacturer { get; set; }

    public override string ToString() => Name;
}
