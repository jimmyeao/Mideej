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
    /// Event fired when a MIDI pitch bend message is received (used for faders in Mackie mode)
    /// </summary>
    event EventHandler<MidiPitchBendEventArgs>? PitchBendReceived;

    /// <summary>
    /// Event fired when a MIDI device is connected or disconnected
    /// </summary>
    event EventHandler<MidiDeviceEventArgs>? DeviceStateChanged;

    /// <summary>
    /// Event fired when a MIDI error occurs
    /// </summary>
    event EventHandler<string>? ErrorOccurred;

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

    /// <summary>
    /// Sends a control change message to the MIDI device
    /// </summary>
    void SendControlChange(int channel, int controller, int value);

    /// <summary>
    /// Sends a note on message to the MIDI device (for LED buttons)
    /// </summary>
    void SendNoteOn(int channel, int noteNumber, int velocity);

    /// <summary>
    /// Sends a note off message to the MIDI device
    /// </summary>
    void SendNoteOff(int channel, int noteNumber);

    /// <summary>
    /// Sends a pitch bend message (for motorized faders in some protocols)
    /// </summary>
    void SendPitchBend(int channel, int value);
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
/// Event args for MIDI pitch bend messages
/// </summary>
public class MidiPitchBendEventArgs : EventArgs
{
    public int Channel { get; set; }
    public int Value { get; set; } // 0-16383, center is 8192
}

/// <summary>
/// Event args for MIDI device state changes
/// </summary>
public class MidiDeviceEventArgs : EventArgs
{
    public MidiDeviceInfo Device { get; set; } = null!;
    public bool IsConnected { get; set; }
}
