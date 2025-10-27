using Mideej.Models;
using NAudio.Midi;

namespace Mideej.Services;

/// <summary>
/// Service for managing MIDI device connections and input
/// </summary>
public class MidiService : IMidiService, IDisposable
{
    private MidiIn? _midiIn;
    private MidiDeviceInfo? _currentDevice;
    private bool _isMappingMode;

    public event EventHandler<MidiControlChangeEventArgs>? ControlChangeReceived;
    public event EventHandler<MidiNoteEventArgs>? NoteOnReceived;
    public event EventHandler<MidiNoteEventArgs>? NoteOffReceived;
    public event EventHandler<MidiDeviceEventArgs>? DeviceStateChanged;

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
                // Disconnect if already connected
                if (_midiIn != null)
                {
                    Disconnect();
                }

                // Create new MIDI input
                _midiIn = new MidiIn(deviceId);
                _midiIn.MessageReceived += OnMidiMessageReceived;
                _midiIn.ErrorReceived += OnMidiErrorReceived;

                // Get device info
                var caps = MidiIn.DeviceInfo(deviceId);
                _currentDevice = new MidiDeviceInfo
                {
                    DeviceId = deviceId,
                    Name = caps.ProductName,
                    Manufacturer = caps.Manufacturer.ToString(),
                    IsConnected = true
                };

                // Start receiving MIDI messages
                _midiIn.Start();

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
                _currentDevice = null;
                return false;
            }
        });
    }

    public void Disconnect()
    {
        if (_midiIn != null)
        {
            try
            {
                _midiIn.Stop();
                _midiIn.MessageReceived -= OnMidiMessageReceived;
                _midiIn.ErrorReceived -= OnMidiErrorReceived;
                _midiIn.Dispose();

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
                        ControlChangeReceived?.Invoke(this, args);
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
                            NoteOnReceived?.Invoke(this, args);
                        }
                        else
                        {
                            var args = new MidiNoteEventArgs
                            {
                                Channel = message.Channel - 1,
                                NoteNumber = noteOnEvent.NoteNumber,
                                Velocity = 0
                            };
                            NoteOffReceived?.Invoke(this, args);
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
                        NoteOffReceived?.Invoke(this, args);
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
        Console.WriteLine($"MIDI Error: {e.MidiEvent}");
    }

    public void Dispose()
    {
        Disconnect();
    }
}
