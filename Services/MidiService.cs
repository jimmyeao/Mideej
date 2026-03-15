using Mideej.Models;
using NAudio.Midi;

namespace Mideej.Services;

/// <summary>
/// Service for managing MIDI device connections and input/output
/// </summary>
public class MidiService : IMidiService, IDisposable
{
    private MidiIn? _midiIn;
    private MidiOut? _midiOut;
    private MidiDeviceInfo? _currentDevice;
    private bool _isMappingMode;
    private int _errorCount;
    private const int MAX_ERRORS_BEFORE_DISCONNECT = 10;

    // Reconnection state
    private System.Timers.Timer? _reconnectTimer;
    private string? _lastDeviceName;
    private int _reconnectAttempts;
    private bool _isReconnecting;
    private const int RECONNECT_INITIAL_DELAY_MS = 2000;
    private const int RECONNECT_MAX_DELAY_MS = 30000;
    private const int RECONNECT_MAX_ATTEMPTS = 0; // 0 = unlimited

    public event EventHandler<MidiControlChangeEventArgs>? ControlChangeReceived;
    public event EventHandler<MidiNoteEventArgs>? NoteOnReceived;
    public event EventHandler<MidiNoteEventArgs>? NoteOffReceived;
    public event EventHandler<MidiPitchBendEventArgs>? PitchBendReceived;
    public event EventHandler<MidiDeviceEventArgs>? DeviceStateChanged;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<bool>? ReconnectionAttempted;

    public bool IsConnected => _midiIn != null;
    public MidiDeviceInfo? CurrentDevice => _currentDevice;
    public bool IsMappingMode => _isMappingMode;

    public List<MidiDeviceInfo> GetAvailableDevices()
    {
        var devices = new List<MidiDeviceInfo>();

        try
        {
            for (int i = 0; i < MidiIn.NumberOfDevices; i++)
            {
                var caps = MidiIn.DeviceInfo(i);
                devices.Add(new MidiDeviceInfo
                {
                    DeviceId = i,
                    Name = caps.ProductName,
                    Manufacturer = caps.Manufacturer.ToString(),
                    IsConnected = false
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error enumerating MIDI devices: {ex.Message}");
        }

        return devices;
    }

    public async Task<bool> ConnectAsync(int deviceId)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Stop any in-progress reconnection since we're connecting explicitly
                if (!_isReconnecting)
                {
                    StopReconnection();
                }

                // Disconnect if already connected
                if (_midiIn != null)
                {
                    Disconnect();
                }

                // Create new MIDI input
                _midiIn = new MidiIn(deviceId);
                _midiIn.MessageReceived += OnMidiMessageReceived;
                _midiIn.ErrorReceived += OnMidiErrorReceived;

                // Resolve matching MIDI output by name
                string? inName = null;
                try
                {
                    var inCaps = MidiIn.DeviceInfo(deviceId);
                    inName = inCaps.ProductName;
                }
                catch { }

                int outIndex = -1;
                if (!string.IsNullOrEmpty(inName))
                {
                    for (int i = 0; i < MidiOut.NumberOfDevices; i++)
                    {
                        var outCaps = MidiOut.DeviceInfo(i);
                        if (string.Equals(outCaps.ProductName, inName, StringComparison.OrdinalIgnoreCase))
                        {
                            outIndex = i;
                            break;
                        }
                        // Some drivers add IN/OUT suffixes; try contains
                        if (outCaps.ProductName.Contains(inName, StringComparison.OrdinalIgnoreCase) || inName.Contains(outCaps.ProductName, StringComparison.OrdinalIgnoreCase))
                        {
                            outIndex = i;
                            // keep searching for exact match, but remember this candidate
                        }
                    }
                }

                if (outIndex >= 0)
                {
                    _midiOut = new MidiOut(outIndex);
                    Console.WriteLine($"MIDI Output initialized for '{inName}' at index {outIndex}");
                }
                else
                {
                    // Fallback: try using the same index
                    try
                    {
                        _midiOut = new MidiOut(deviceId);
                        Console.WriteLine($"MIDI Output initialized (fallback) at index {deviceId}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Could not open MIDI output (fallback): {ex.Message}");
                    }
                }

                // Get device info
                var caps = MidiIn.DeviceInfo(deviceId);
                _currentDevice = new MidiDeviceInfo
                {
                    DeviceId = deviceId,
                    Name = caps.ProductName,
                    Manufacturer = caps.Manufacturer.ToString(),
                    IsConnected = true
                };

                // Reset error count on successful connection
                _errorCount = 0;

                // Start receiving MIDI messages
                _midiIn.Start();

                Console.WriteLine($"MIDI Input started for device {deviceId} ('{_currentDevice.Name}')");

                // Raise device state changed event
                DeviceStateChanged?.Invoke(this, new MidiDeviceEventArgs
                {
                    Device = _currentDevice,
                    IsConnected = true
                });

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error connecting to MIDI device: {ex.Message}");
                _midiIn?.Dispose();
                _midiIn = null;
                _midiOut?.Dispose();
                _midiOut = null;
                _currentDevice = null;
                return false;
            }
        });
    }

    public void Disconnect()
    {
        if (_midiIn != null || _midiOut != null)
        {
            try
            {
                if (_midiIn != null)
                {
                    _midiIn.Stop();
                    _midiIn.MessageReceived -= OnMidiMessageReceived;
                    _midiIn.ErrorReceived -= OnMidiErrorReceived;
                    _midiIn.Dispose();
                }

                if (_midiOut != null)
                {
                    _midiOut.Dispose();
                }

                var device = _currentDevice;
                if (device != null)
                {
                    device.IsConnected = false;
                    DeviceStateChanged?.Invoke(this, new MidiDeviceEventArgs
                    {
                        Device = device,
                        IsConnected = false
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error disconnecting MIDI device: {ex.Message}");
            }
            finally
            {
                _midiIn = null;
                _midiOut = null;
                _currentDevice = null;
            }
        }
    }

    public void StartMappingMode()
    {
        _isMappingMode = true;
    }

    public void StopMappingMode()
    {
        _isMappingMode = false;
    }

    private void OnMidiMessageReceived(object? sender, MidiInMessageEventArgs e)
    {
        try
        {
            // Reset error count when we successfully receive messages (connection is working)
            if (_errorCount > 0)
            {
                _errorCount = 0;
            }

            var message = e.MidiEvent;

            switch (message.CommandCode)
            {
                case MidiCommandCode.ControlChange:
                    if (message is ControlChangeEvent ccEvent)
                    {
                        var args = new MidiControlChangeEventArgs
                        {
                            Channel = message.Channel - 1, // NAudio uses 1-based channels
                            Controller = (int)ccEvent.Controller,
                            Value = ccEvent.ControllerValue
                        };

                        // Dispatch to UI thread
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            ControlChangeReceived?.Invoke(this, args);
                        });
                    }
                    break;

                case MidiCommandCode.NoteOn:
                    if (message is NoteOnEvent noteOnEvent)
                    {
                        // Some devices send NoteOn with velocity 0 as NoteOff
                        if (noteOnEvent.Velocity > 0)
                        {
                            var args = new MidiNoteEventArgs
                            {
                                Channel = message.Channel - 1,
                                NoteNumber = noteOnEvent.NoteNumber,
                                Velocity = noteOnEvent.Velocity
                            };

                            // Dispatch to UI thread
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                NoteOnReceived?.Invoke(this, args);
                            });
                        }
                        else
                        {
                            var args = new MidiNoteEventArgs
                            {
                                Channel = message.Channel - 1,
                                NoteNumber = noteOnEvent.NoteNumber,
                                Velocity = 0
                            };

                            // Dispatch to UI thread
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                NoteOffReceived?.Invoke(this, args);
                            });
                        }
                    }
                    break;

                case MidiCommandCode.NoteOff:
                    if (message is NoteEvent noteOffEvent)
                    {
                        var args = new MidiNoteEventArgs
                        {
                            Channel = message.Channel - 1,
                            NoteNumber = noteOffEvent.NoteNumber,
                            Velocity = noteOffEvent.Velocity
                        };

                        // Dispatch to UI thread
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            NoteOffReceived?.Invoke(this, args);
                        });
                    }
                    break;

                case MidiCommandCode.PitchWheelChange:
                    if (message is PitchWheelChangeEvent pitchEvent)
                    {
                        var args = new MidiPitchBendEventArgs
                        {
                            Channel = message.Channel - 1,
                            Value = pitchEvent.Pitch // 0-16383, center is 8192
                        };

                        // Dispatch to UI thread
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        {
                            PitchBendReceived?.Invoke(this, args);
                        });
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing MIDI message: {ex.Message}");
        }
    }

    private void OnMidiErrorReceived(object? sender, MidiInMessageEventArgs e)
    {
        _errorCount++;
        string errorMsg = $"MIDI Error ({_errorCount}/{MAX_ERRORS_BEFORE_DISCONNECT}): {e.MidiEvent}";
        Console.WriteLine(errorMsg);

        // Notify listeners about the error
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            ErrorOccurred?.Invoke(this, errorMsg);
        });

        // If we've accumulated too many errors, the device is likely disconnected
        if (_errorCount >= MAX_ERRORS_BEFORE_DISCONNECT)
        {
            Console.WriteLine($"Too many MIDI errors ({_errorCount}). Device may be disconnected. Starting automatic reconnection...");

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ErrorOccurred?.Invoke(this, "MIDI device appears to be disconnected. Attempting automatic reconnection...");
            });

            // Remember the device name before disconnecting
            _lastDeviceName = _currentDevice?.Name;

            // Disconnect the broken connection and start reconnection
            Disconnect();
            StartReconnection();
        }
    }

    private void StartReconnection()
    {
        if (_isReconnecting || string.IsNullOrEmpty(_lastDeviceName))
            return;

        _isReconnecting = true;
        _reconnectAttempts = 0;

        Console.WriteLine($"[Reconnect] Will attempt to reconnect to '{_lastDeviceName}'");

        _reconnectTimer?.Dispose();
        _reconnectTimer = new System.Timers.Timer(RECONNECT_INITIAL_DELAY_MS);
        _reconnectTimer.AutoReset = false;
        _reconnectTimer.Elapsed += OnReconnectTimerElapsed;
        _reconnectTimer.Start();
    }

    private void StopReconnection()
    {
        _isReconnecting = false;
        _reconnectAttempts = 0;
        _reconnectTimer?.Stop();
        _reconnectTimer?.Dispose();
        _reconnectTimer = null;
    }

    private async void OnReconnectTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!_isReconnecting || string.IsNullOrEmpty(_lastDeviceName))
            return;

        _reconnectAttempts++;
        Console.WriteLine($"[Reconnect] Attempt {_reconnectAttempts} to reconnect to '{_lastDeviceName}'...");

        try
        {
            // Scan for the device by name (device IDs can change after unplug/replug)
            int deviceId = -1;
            for (int i = 0; i < MidiIn.NumberOfDevices; i++)
            {
                try
                {
                    var caps = MidiIn.DeviceInfo(i);
                    if (string.Equals(caps.ProductName, _lastDeviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        deviceId = i;
                        break;
                    }
                }
                catch
                {
                    // Device enumeration can fail for individual devices
                }
            }

            if (deviceId >= 0)
            {
                bool success = await ConnectAsync(deviceId);

                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    ReconnectionAttempted?.Invoke(this, success);
                });

                if (success)
                {
                    Console.WriteLine($"[Reconnect] Successfully reconnected to '{_lastDeviceName}' (attempt {_reconnectAttempts})");
                    StopReconnection();
                    return;
                }
                else
                {
                    Console.WriteLine($"[Reconnect] Found device but failed to connect (attempt {_reconnectAttempts})");
                }
            }
            else
            {
                Console.WriteLine($"[Reconnect] Device '{_lastDeviceName}' not found (attempt {_reconnectAttempts})");

                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    ReconnectionAttempted?.Invoke(this, false);
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Reconnect] Error during reconnection attempt: {ex.Message}");
        }

        // Check if we've exceeded max attempts (0 = unlimited)
        if (RECONNECT_MAX_ATTEMPTS > 0 && _reconnectAttempts >= RECONNECT_MAX_ATTEMPTS)
        {
            Console.WriteLine($"[Reconnect] Max reconnection attempts ({RECONNECT_MAX_ATTEMPTS}) reached. Giving up.");
            StopReconnection();

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ErrorOccurred?.Invoke(this, $"Failed to reconnect after {RECONNECT_MAX_ATTEMPTS} attempts. Please reconnect manually.");
            });
            return;
        }

        // Schedule next attempt with exponential backoff
        int delay = Math.Min(
            RECONNECT_INITIAL_DELAY_MS * (int)Math.Pow(2, Math.Min(_reconnectAttempts - 1, 10)),
            RECONNECT_MAX_DELAY_MS);
        Console.WriteLine($"[Reconnect] Next attempt in {delay}ms");

        if (_reconnectTimer != null)
        {
            _reconnectTimer.Interval = delay;
            _reconnectTimer.Start();
        }
    }

    private void CheckSendErrorThreshold()
    {
        if (_errorCount >= MAX_ERRORS_BEFORE_DISCONNECT && !_isReconnecting)
        {
            Console.WriteLine($"[Send] Too many send errors ({_errorCount}). Starting automatic reconnection...");
            _lastDeviceName = _currentDevice?.Name;

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ErrorOccurred?.Invoke(this, "MIDI device appears to be disconnected. Attempting automatic reconnection...");
            });

            Disconnect();
            StartReconnection();
        }
    }

    public void SendControlChange(int channel, int controller, int value)
    {
        if (_midiOut == null) return;

        try
        {
            var message = new ControlChangeEvent(0, channel + 1, (MidiController)controller, value);
            _midiOut.Send(message.GetAsShortMessage());
            Console.WriteLine($"Sent MIDI CC: Ch{channel} CC{controller} Val{value}");
        }
        catch (Exception ex)
        {
            _errorCount++;
            Console.WriteLine($"Error sending MIDI CC: {ex.Message}");
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ErrorOccurred?.Invoke(this, $"Error sending MIDI: {ex.Message}");
            });
            CheckSendErrorThreshold();
        }
    }

    public void SendNoteOn(int channel, int noteNumber, int velocity)
    {
        if (_midiOut == null) return;

        try
        {
            var message = new NoteOnEvent(0, channel + 1, noteNumber, velocity, 0);
            _midiOut.Send(message.GetAsShortMessage());
            Console.WriteLine($"Sent MIDI NoteOn: Ch{channel} Note{noteNumber} Vel{velocity}");
        }
        catch (Exception ex)
        {
            _errorCount++;
            Console.WriteLine($"Error sending MIDI Note On: {ex.Message}");
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ErrorOccurred?.Invoke(this, $"Error sending MIDI: {ex.Message}");
            });
            CheckSendErrorThreshold();
        }
    }

    public void SendNoteOff(int channel, int noteNumber)
    {
        if (_midiOut == null) return;

        try
        {
            var message = new NoteEvent(0, channel + 1, MidiCommandCode.NoteOff, noteNumber, 0);
            _midiOut.Send(message.GetAsShortMessage());
            Console.WriteLine($"Sent MIDI NoteOff: Ch{channel} Note{noteNumber}");
        }
        catch (Exception ex)
        {
            _errorCount++;
            Console.WriteLine($"Error sending MIDI Note Off: {ex.Message}");
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ErrorOccurred?.Invoke(this, $"Error sending MIDI: {ex.Message}");
            });
            CheckSendErrorThreshold();
        }
    }

    public void SendPitchBend(int channel, int value)
    {
        if (_midiOut == null) return;

        try
        {
            var message = new PitchWheelChangeEvent(0, channel + 1, value);
            _midiOut.Send(message.GetAsShortMessage());
            Console.WriteLine($"Sent MIDI PitchBend: Ch{channel} Val{value}");
        }
        catch (Exception ex)
        {
            _errorCount++;
            Console.WriteLine($"Error sending MIDI Pitch Bend: {ex.Message}");
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                ErrorOccurred?.Invoke(this, $"Error sending MIDI: {ex.Message}");
            });
            CheckSendErrorThreshold();
        }
    }

    public void Dispose()
    {
        StopReconnection();
        Disconnect();
    }
}
