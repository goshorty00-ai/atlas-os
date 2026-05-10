using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace AtlasAI.DJ
{
    public class TrackAnalysis
    {
        public int Bpm { get; set; }
        public double Confidence { get; set; }
        public double BeatIntervalSeconds { get; set; }
        public double GridOffsetSeconds { get; set; }
        public List<double> BeatMarkers { get; set; } = new List<double>();
        public List<double> PhraseMarkers { get; set; } = new List<double>();
        public List<double> WaveformMin { get; set; } = new List<double>();
        public List<double> WaveformMax { get; set; } = new List<double>();
        public List<double> WaveformRms { get; set; } = new List<double>();
    }

    public class DjControllerBinding
    {
        public string ControlType { get; set; } = "cc";
        public int ControlNumber { get; set; }
        public string Command { get; set; } = string.Empty;
        public string Deck { get; set; } = string.Empty;
        public double Scale { get; set; } = 1;
        public double Offset { get; set; }
        public bool IsRelative { get; set; }
    }

    public class DjControllerProfile
    {
        public string Id { get; set; } = "generic-2deck";
        public string Name { get; set; } = "Generic 2-Deck MIDI";
        public string Protocol { get; set; } = "MIDI";
        public List<DjControllerBinding> Bindings { get; set; } = new List<DjControllerBinding>();
    }

    public class DjControllerDevice
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string InputName { get; set; } = string.Empty;
        public string OutputName { get; set; } = string.Empty;
        public bool Connected { get; set; }
        public bool SupportsInput { get; set; }
        public bool SupportsOutput { get; set; }
        public string Protocol { get; set; } = "MIDI";
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    }

    public class DjControllerInputEvent
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string ControlType { get; set; } = string.Empty;
        public int Channel { get; set; }
        public int ControlNumber { get; set; }
        public int RawValue { get; set; }
        public double NormalizedValue { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }

    public class DjControllerAction
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string Deck { get; set; } = string.Empty;
        public double Value { get; set; }
        public bool IsRelative { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    }

    public class Track
    {
        public string Name { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int Bpm { get; set; }
        public int OriginalBpm { get; set; }
        public int DurationSeconds { get; set; }
    }

    public class CuePoint
    {
        public double Time { get; set; }
        public string Label { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
    }

    public class AutoMixState : INotifyPropertyChanged
    {
        private bool _enabled;
        private string _status = string.Empty;
        private string _sourceDeck = string.Empty;
        private string _targetDeck = string.Empty;
        private double _progress;
        private double _transitionBeats = 16;
        private double _remainingBeats;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, string name)
        {
            if (!Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        public bool Enabled { get => _enabled; set => Set(ref _enabled, value, nameof(Enabled)); }
        public string Status { get => _status; set => Set(ref _status, value ?? string.Empty, nameof(Status)); }
        public string SourceDeck { get => _sourceDeck; set => Set(ref _sourceDeck, value ?? string.Empty, nameof(SourceDeck)); }
        public string TargetDeck { get => _targetDeck; set => Set(ref _targetDeck, value ?? string.Empty, nameof(TargetDeck)); }
        public double Progress { get => _progress; set => Set(ref _progress, Math.Clamp(value, 0, 1), nameof(Progress)); }
        public double TransitionBeats { get => _transitionBeats; set => Set(ref _transitionBeats, Math.Clamp(value, 4, 64), nameof(TransitionBeats)); }
        public double RemainingBeats { get => _remainingBeats; set => Set(ref _remainingBeats, Math.Max(0, value), nameof(RemainingBeats)); }
    }

    public class DeckState : INotifyPropertyChanged
    {
        private bool _isPlaying;
        private bool _loopActive;
        private bool _isCued;
        private bool _syncEnabled;
        private bool _isSyncMaster;
        private bool _cueMonitoring;
        private bool _waveformVisible = true;
        private Track _track = new Track();
        private double _volume = 80;
        private double _gain = 50;
        private double _eqHigh = 50;
        private double _eqMid = 50;
        private double _eqLow = 50;
        private double _filter = 50;
        private double _tempo;
        private double _pitchRange = 16;
        private double _currentTime;
        private double _currentTimeSeconds;
        private double _remainingTimeSeconds;
        private double _cuePosition;
        private double? _loopStart;
        private double? _loopEnd;
        private string _loopSize = "1";
        private string _syncSourceDeck = string.Empty;
        private double _syncTargetBpm;
        private double _syncTempoDelta;
        private double _syncBeatPhaseOffsetSeconds;
        private bool _syncAligned;
        private double _effectiveBpm;
        private double _beatIntervalSeconds;
        private double _beatPhaseFraction;
        private double _beatPhaseSeconds;
        private double _beatPosition;
        private int _barIndex;
        private int _beatInBar;
        private int _phraseIndex;
        private List<CuePoint> _cuePoints = new List<CuePoint>();

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, string name)
        {
            if (!Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        public bool IsPlaying { get => _isPlaying; set => Set(ref _isPlaying, value, nameof(IsPlaying)); }
        public bool LoopActive { get => _loopActive; set => Set(ref _loopActive, value, nameof(LoopActive)); }
        public bool IsCued { get => _isCued; set => Set(ref _isCued, value, nameof(IsCued)); }
        public bool SyncEnabled { get => _syncEnabled; set => Set(ref _syncEnabled, value, nameof(SyncEnabled)); }
        public bool IsSyncMaster { get => _isSyncMaster; set => Set(ref _isSyncMaster, value, nameof(IsSyncMaster)); }
        public bool CueMonitoring { get => _cueMonitoring; set => Set(ref _cueMonitoring, value, nameof(CueMonitoring)); }
        public bool WaveformVisible { get => _waveformVisible; set => Set(ref _waveformVisible, value, nameof(WaveformVisible)); }
        public Track Track { get => _track; set => Set(ref _track, value, nameof(Track)); }
        public double Volume { get => _volume; set => Set(ref _volume, Math.Clamp(value, 0, 100), nameof(Volume)); }
        public double Gain { get => _gain; set => Set(ref _gain, Math.Clamp(value, 0, 100), nameof(Gain)); }
        public double EqHigh { get => _eqHigh; set => Set(ref _eqHigh, Math.Clamp(value, 0, 100), nameof(EqHigh)); }
        public double EqMid { get => _eqMid; set => Set(ref _eqMid, Math.Clamp(value, 0, 100), nameof(EqMid)); }
        public double EqLow { get => _eqLow; set => Set(ref _eqLow, Math.Clamp(value, 0, 100), nameof(EqLow)); }
        public double Filter { get => _filter; set => Set(ref _filter, Math.Clamp(value, 0, 100), nameof(Filter)); }
        public double Tempo { get => _tempo; set => Set(ref _tempo, Math.Clamp(value, -50, 50), nameof(Tempo)); }
        public double PitchRange { get => _pitchRange; set => Set(ref _pitchRange, Math.Clamp(value, 4, 50), nameof(PitchRange)); }
        public double CurrentTime { get => _currentTime; set => Set(ref _currentTime, Math.Clamp(value, 0, 100), nameof(CurrentTime)); }
        public double CurrentTimeSeconds { get => _currentTimeSeconds; set => Set(ref _currentTimeSeconds, Math.Max(0, value), nameof(CurrentTimeSeconds)); }
        public double RemainingTimeSeconds { get => _remainingTimeSeconds; set => Set(ref _remainingTimeSeconds, Math.Max(0, value), nameof(RemainingTimeSeconds)); }
        public double CuePosition { get => _cuePosition; set => Set(ref _cuePosition, Math.Clamp(value, 0, 100), nameof(CuePosition)); }
        public double? LoopStart { get => _loopStart; set => Set(ref _loopStart, value, nameof(LoopStart)); }
        public double? LoopEnd { get => _loopEnd; set => Set(ref _loopEnd, value, nameof(LoopEnd)); }
        public string LoopSize { get => _loopSize; set => Set(ref _loopSize, string.IsNullOrWhiteSpace(value) ? "1" : value, nameof(LoopSize)); }
        public string SyncSourceDeck { get => _syncSourceDeck; set => Set(ref _syncSourceDeck, value ?? string.Empty, nameof(SyncSourceDeck)); }
        public double SyncTargetBpm { get => _syncTargetBpm; set => Set(ref _syncTargetBpm, value, nameof(SyncTargetBpm)); }
        public double SyncTempoDelta { get => _syncTempoDelta; set => Set(ref _syncTempoDelta, value, nameof(SyncTempoDelta)); }
        public double SyncBeatPhaseOffsetSeconds { get => _syncBeatPhaseOffsetSeconds; set => Set(ref _syncBeatPhaseOffsetSeconds, value, nameof(SyncBeatPhaseOffsetSeconds)); }
        public bool SyncAligned { get => _syncAligned; set => Set(ref _syncAligned, value, nameof(SyncAligned)); }
        public double EffectiveBpm { get => _effectiveBpm; set => Set(ref _effectiveBpm, Math.Max(0, value), nameof(EffectiveBpm)); }
        public double BeatIntervalSeconds { get => _beatIntervalSeconds; set => Set(ref _beatIntervalSeconds, Math.Max(0, value), nameof(BeatIntervalSeconds)); }
        public double BeatPhaseFraction { get => _beatPhaseFraction; set => Set(ref _beatPhaseFraction, Math.Clamp(value, 0, 1), nameof(BeatPhaseFraction)); }
        public double BeatPhaseSeconds { get => _beatPhaseSeconds; set => Set(ref _beatPhaseSeconds, Math.Max(0, value), nameof(BeatPhaseSeconds)); }
        public double BeatPosition { get => _beatPosition; set => Set(ref _beatPosition, Math.Max(0, value), nameof(BeatPosition)); }
        public int BarIndex { get => _barIndex; set => Set(ref _barIndex, Math.Max(0, value), nameof(BarIndex)); }
        public int BeatInBar { get => _beatInBar; set => Set(ref _beatInBar, Math.Max(0, value), nameof(BeatInBar)); }
        public int PhraseIndex { get => _phraseIndex; set => Set(ref _phraseIndex, Math.Max(0, value), nameof(PhraseIndex)); }
        public List<CuePoint> CuePoints { get => _cuePoints; set => Set(ref _cuePoints, value ?? new List<CuePoint>(), nameof(CuePoints)); }
    }
}
