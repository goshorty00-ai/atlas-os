using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Midi;

namespace AtlasAI.DJ
{
    internal sealed class DjControllerManager : IDisposable
    {
        private readonly List<MidiIn> _inputs = new List<MidiIn>();
        private readonly List<DjControllerDevice> _devices = new List<DjControllerDevice>();
        private readonly List<DjControllerProfile> _profiles = new List<DjControllerProfile>();

        public event EventHandler<DjControllerInputEvent>? InputReceived;
        public event EventHandler<DjControllerAction>? ActionReceived;

        public IReadOnlyList<DjControllerDevice> Devices => _devices;
        public IReadOnlyList<DjControllerProfile> Profiles => _profiles;

        public DjControllerManager()
        {
            SeedProfiles();
            RefreshDevices();
        }

        public void RefreshDevices()
        {
            DisposeInputs();
            _devices.Clear();

            var outputNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < MidiOut.NumberOfDevices; index++)
            {
                try
                {
                    var info = MidiOut.DeviceInfo(index);
                    outputNames[info.ProductName] = info.ProductName;
                }
                catch
                {
                }
            }

            for (var index = 0; index < MidiIn.NumberOfDevices; index++)
            {
                try
                {
                    var info = MidiIn.DeviceInfo(index);
                    var device = new DjControllerDevice
                    {
                        Id = $"midi:{index}:{info.ProductName}",
                        Name = info.ProductName,
                        InputName = info.ProductName,
                        OutputName = outputNames.TryGetValue(info.ProductName, out var output) ? output : string.Empty,
                        Connected = true,
                        SupportsInput = true,
                        SupportsOutput = outputNames.ContainsKey(info.ProductName),
                        Protocol = "MIDI",
                        LastSeenUtc = DateTime.UtcNow
                    };

                    _devices.Add(device);

                    var midiIn = new MidiIn(index);
                    midiIn.MessageReceived += (_, args) => HandleMessage(device, args.MidiEvent);
                    midiIn.ErrorReceived += (_, __) => { };
                    midiIn.Start();
                    _inputs.Add(midiIn);
                }
                catch
                {
                }
            }
        }

        private void HandleMessage(DjControllerDevice device, MidiEvent midiEvent)
        {
            var evt = Translate(device, midiEvent);
            if (evt == null)
                return;

            device.LastSeenUtc = evt.TimestampUtc;
            InputReceived?.Invoke(this, evt);

            var action = TranslateAction(evt);
            if (action != null)
                ActionReceived?.Invoke(this, action);
        }

        internal DjControllerAction? TranslateAction(DjControllerInputEvent evt)
        {
            if (evt == null)
                return null;

            foreach (var profile in _profiles)
            {
                var binding = profile.Bindings?.FirstOrDefault(candidate =>
                    string.Equals(candidate.ControlType, evt.ControlType, StringComparison.OrdinalIgnoreCase) &&
                    candidate.ControlNumber == evt.ControlNumber);

                if (binding == null)
                    continue;

                var value = binding.IsRelative
                    ? evt.RawValue - 64
                    : evt.NormalizedValue * binding.Scale + binding.Offset;

                return new DjControllerAction
                {
                    DeviceId = evt.DeviceId,
                    DeviceName = evt.DeviceName,
                    Command = binding.Command,
                    Deck = DjDeckRouting.Normalize(binding.Deck) ?? string.Empty,
                    Value = value,
                    IsRelative = binding.IsRelative,
                    TimestampUtc = evt.TimestampUtc,
                };
            }

            return null;
        }

        private static DjControllerInputEvent? Translate(DjControllerDevice device, MidiEvent midiEvent)
        {
            switch (midiEvent)
            {
                case ControlChangeEvent cc:
                    return new DjControllerInputEvent
                    {
                        DeviceId = device.Id,
                        DeviceName = device.Name,
                        ControlType = "cc",
                        Channel = cc.Channel,
                        ControlNumber = (int)cc.Controller,
                        RawValue = cc.ControllerValue,
                        NormalizedValue = cc.ControllerValue / 127d,
                        TimestampUtc = DateTime.UtcNow
                    };
                case NoteEvent note when note.CommandCode == MidiCommandCode.NoteOn:
                    return new DjControllerInputEvent
                    {
                        DeviceId = device.Id,
                        DeviceName = device.Name,
                        ControlType = "note",
                        Channel = note.Channel,
                        ControlNumber = note.NoteNumber,
                        RawValue = note.Velocity,
                        NormalizedValue = note.Velocity / 127d,
                        TimestampUtc = DateTime.UtcNow
                    };
                case PitchWheelChangeEvent pitch:
                    return new DjControllerInputEvent
                    {
                        DeviceId = device.Id,
                        DeviceName = device.Name,
                        ControlType = "pitch",
                        Channel = pitch.Channel,
                        ControlNumber = 0,
                        RawValue = pitch.Pitch,
                        NormalizedValue = (pitch.Pitch - 8192d) / 8192d,
                        TimestampUtc = DateTime.UtcNow
                    };
                default:
                    return null;
            }
        }

        private void SeedProfiles()
        {
            if (_profiles.Count > 0)
                return;

            _profiles.Add(new DjControllerProfile
            {
                Id = "generic-2deck-midi",
                Name = "Generic 2-Deck MIDI",
                Protocol = "MIDI",
                Bindings = new List<DjControllerBinding>
                {
                    new DjControllerBinding { ControlType = "cc", ControlNumber = 0, Command = "tempo", Deck = "A", Scale = 100, Offset = -50 },
                    new DjControllerBinding { ControlType = "cc", ControlNumber = 1, Command = "tempo", Deck = "B", Scale = 100, Offset = -50 },
                    new DjControllerBinding { ControlType = "cc", ControlNumber = 2, Command = "crossfader", Scale = 100 },
                    new DjControllerBinding { ControlType = "cc", ControlNumber = 3, Command = "volume", Deck = "A", Scale = 100 },
                    new DjControllerBinding { ControlType = "cc", ControlNumber = 4, Command = "volume", Deck = "B", Scale = 100 },
                    new DjControllerBinding { ControlType = "pitch", ControlNumber = 0, Command = "bend", Deck = "A", Scale = 0.08, IsRelative = true },
                    new DjControllerBinding { ControlType = "note", ControlNumber = 36, Command = "playPause", Deck = "A" },
                    new DjControllerBinding { ControlType = "note", ControlNumber = 37, Command = "cue", Deck = "A" },
                    new DjControllerBinding { ControlType = "note", ControlNumber = 38, Command = "playPause", Deck = "B" },
                    new DjControllerBinding { ControlType = "note", ControlNumber = 39, Command = "cue", Deck = "B" },
                    new DjControllerBinding { ControlType = "note", ControlNumber = 40, Command = "sync", Deck = "A" },
                    new DjControllerBinding { ControlType = "note", ControlNumber = 41, Command = "sync", Deck = "B" },
                    new DjControllerBinding { ControlType = "note", ControlNumber = 42, Command = "hotCue1", Deck = "A" },
                    new DjControllerBinding { ControlType = "note", ControlNumber = 43, Command = "hotCue2", Deck = "A" },
                    new DjControllerBinding { ControlType = "note", ControlNumber = 44, Command = "hotCue1", Deck = "B" },
                    new DjControllerBinding { ControlType = "note", ControlNumber = 45, Command = "hotCue2", Deck = "B" },
                }
            });
        }

        private void DisposeInputs()
        {
            foreach (var input in _inputs)
            {
                try
                {
                    input.Stop();
                    input.Dispose();
                }
                catch
                {
                }
            }

            _inputs.Clear();
        }

        public void Dispose()
        {
            DisposeInputs();
        }
    }
}