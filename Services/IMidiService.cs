using Mideej.Models;

namespace Mideej.Services;

/// <summary>
/// Service for managing MIDI device connections and input
/// </summary>
public interface IMidiService
{
    /// <summary>
    /// Event fired when a MIDI control change message is received
    /// </summary>
    event EventHandler<MidiControlChangeEventArgs>? ControlChangeReceived;

    /// <summary>
    /// Event fired when a MIDI note on message is received
    /// </summary>
    event EventHandler<MidiNoteEventArgs>? NoteOnReceived;

    /// <summary>
    /// Event fired when a MIDI note off message is received
    /// </summary>
    event EventHandler<MidiNoteEventArgs>? NoteOffReceived;

    /// <summary>
    /// Event fired when a MIDI device is connected or disconnected
    /// </summary>
    event EventHandler<MidiDeviceEventArgs>? DeviceStateChanged;

    /// <summary>
    /// Gets all available MIDI input devices
    /// </summary>
    List<MidiDeviceInfo> GetAvailableDevices();

    /// <summary>
    /// Connects to a MIDI device
    /// </summary>
    Task<bool> ConnectAsync(int deviceId);

    /// <summary>
    /// Disconnects from the current MIDI device
    /// </summary>
    void Disconnect();

    /// <summary>
    /// Gets whether a MIDI device is currently connected
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets the currently connected device info
    /// </summary>
    MidiDeviceInfo? CurrentDevice { get; }

    /// <summary>
    /// Starts listening for mapping mode (captures the next MIDI message)
    /// </summary>
    void StartMappingMode();

    /// <summary>
    /// Stops mapping mode
    /// </summary>
    void StopMappingMode();

    /// <summary>
    /// Whether mapping mode is active
    /// </summary>
    bool IsMappingMode { get; }
}

/// <summary>
/// Event args for MIDI control change messages
/// </summary>
public class MidiControlChangeEventArgs : EventArgs
{
    public int Channel { get; set; }
    public int Controller { get; set; }
    public int Value { get; set; }
}

/// <summary>
/// Event args for MIDI note messages
/// </summary>
public class MidiNoteEventArgs : EventArgs
{
    public int Channel { get; set; }
    public int NoteNumber { get; set; }
    public int Velocity { get; set; }
}

/// <summary>
/// Event args for MIDI device state changes
/// </summary>
public class MidiDeviceEventArgs : EventArgs
{
    public MidiDeviceInfo Device { get; set; } = null!;
    public bool IsConnected { get; set; }
}
