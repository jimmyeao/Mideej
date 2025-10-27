using NAudio.Midi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mideej
{
    public enum ChannelButton { Record, Solo, Mute, Select }

    public static class SmcMixerMap
    {
        // === CHANNEL CONTROLS ==========================================================

        /// <summary>Pitch-bend MIDI channel (1-8) for each fader.</summary>
        public static int GetFaderChannel(int channel1to8)
            => channel1to8 switch { >= 1 and <= 8 => channel1to8, _ => throw new ArgumentOutOfRangeException(nameof(channel1to8)) };

        /// <summary>CC number (0x10-0x17) for each rotary knob.</summary>
        public static int GetKnobCc(int channel1to8)
            => 0x10 + (channel1to8 - 1);

        /// <summary>Note number (0x00-0x1F) for Record/Solo/Mute/Select buttons.</summary>
        public static int GetButtonNote(int channel1to8, ChannelButton button)
        {
            if (channel1to8 is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(channel1to8));
            int baseNote = button switch
            {
                ChannelButton.Record => 0x00,
                ChannelButton.Solo => 0x08,
                ChannelButton.Mute => 0x10,
                ChannelButton.Select => 0x18,
                _ => throw new ArgumentOutOfRangeException(nameof(button))
            };
            return baseNote + (channel1to8 - 1);
        }

        // === TRANSPORT (standard MCU layout, may vary slightly) =========================
        public const int NoteRewind = 0x5B;
        public const int NoteFwd = 0x5C;
        public const int NoteStop = 0x5D;
        public const int NotePlay = 0x5E;
        public const int NoteRecord = 0x5F;
        public const int NoteLoop = 0x60;
        public const int NotePunch = 0x61;
        public const int NoteMetronome = 0x62;
        public const int NoteUndo = 0x63;

        // === LED FEEDBACK ==============================================================
        private static MidiOut? _ledOut;

        /// <summary>Opens the feedback port (MIDIIN2 SMC Mixer) if not already open.</summary>
        public static void EnsureLedPort()
        {
            _ledOut ??= new MidiOut(Enumerable.Range(0, MidiOut.NumberOfDevices)
                .First(i => MidiOut.DeviceInfo(i).ProductName.Contains("MIDIIN2", StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>Turns a channel LED on/off.</summary>
        public static void SetButtonLed(int channel1to8, ChannelButton button, bool on)
        {
            EnsureLedPort();
            int note = GetButtonNote(channel1to8, button);
            _ledOut!.Send(MidiMessage.StartNote(note, on ? 127 : 0, 1).RawData);
        }

        /// <summary>Turns a transport LED on/off (e.g. Play/Stop).</summary>
        public static void SetTransportLed(int noteNumber, bool on)
        {
            EnsureLedPort();
            _ledOut!.Send(MidiMessage.StartNote(noteNumber, on ? 127 : 0, 1).RawData);
        }

        /// <summary>Close LED feedback port when done.</summary>
        public static void CloseLedPort()
        {
            _ledOut?.Dispose();
            _ledOut = null;
        }
    }
}
