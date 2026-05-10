using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SoundTouch;

namespace AtlasAI.DJ
{
    public class AudioEngine : IDisposable
    {
        /* Single output — MixingSampleProvider combines two lock-free DeckProviders */
        private WaveOutEvent? _masterPlayer;
        private readonly DeckProvider _deckProviderA;
        private readonly DeckProvider _deckProviderB;

        /* Readers — volatile so timer/UI can read CurrentTime without locks */
        private volatile AudioFileReader? _readerA;
        private volatile AudioFileReader? _readerB;
        private Timer? _timer;

        private const int MixRate = 44100;
        private const int MixChannels = 2;
        private const double SyncAlignedToleranceSeconds = 0.024;
        private const double SyncResyncThresholdSeconds = 0.07;
        private const double PlayingSyncResyncThresholdSeconds = 0.12;
        private const double SyncHardResyncBeatWindow = 0.42;
        private const double SyncTempoNudgeStrength = 26.0;
        private const double SyncMaxTempoNudgePercent = 4.5;
        private const double SyncMaxAutoPitchRange = 16.0;
        private const int EngineTickMs = 33;

        public DeckState DeckA { get; } = new DeckState();
        public DeckState DeckB { get; } = new DeckState();
        public double Crossfader { get; private set; } = 50;
        public double MasterVolume { get; set; } = 80;
        public double MasterLevel { get; private set; }
        public double CueMix { get; private set; } = 50;
        public double HeadphoneVolume { get; private set; } = 74;
        public double MicVolume { get; private set; } = 35;
        public string CrossfaderCurve { get; private set; } = "sharp";
        public string EffectMode { get; private set; } = "Punch";
        public double[] DeckAWaveformBars { get; private set; } = Array.Empty<double>();
        public double[] DeckBWaveformBars { get; private set; } = Array.Empty<double>();
        public TrackAnalysis? DeckAAnalysis { get; private set; }
        public TrackAnalysis? DeckBAnalysis { get; private set; }
        public bool EffectEcho { get; set; }
        public bool EffectReverb { get; set; }
        public bool EffectFlanger { get; set; }
        public bool EffectDelay { get; set; }
        public bool EffectFilter { get; set; }
        public bool EffectPhaser { get; set; }
        public AutoMixState AutoMix { get; } = new AutoMixState();

        private MixingSampleProvider? _mixer;
        private RecordingTap? _recordingTap;
        private double _autoMixStartBeatPosition;

        public AudioEngine()
        {
            ResetDeckDefaults(DeckA);
            ResetDeckDefaults(DeckB);
            _deckProviderA = new DeckProvider(this, DeckA, true);
            _deckProviderB = new DeckProvider(this, DeckB, false);
            InitMasterOutput();
        }

        private void InitMasterOutput()
        {
            _mixer = new MixingSampleProvider(
                WaveFormat.CreateIeeeFloatWaveFormat(MixRate, MixChannels))
            { ReadFully = true };
            _mixer.AddMixerInput(_deckProviderA);
            _mixer.AddMixerInput(_deckProviderB);

            _recordingTap = new RecordingTap(_mixer, () => EffectMode);

            _masterPlayer = new WaveOutEvent
            {
                DesiredLatency = 150,
                NumberOfBuffers = 3
            };
            _masterPlayer.Init(_recordingTap);
            _masterPlayer.Play();
        }

        /// <summary>Wrap a reader, resampling / up-mixing to stereo 44100 Hz.
        /// Stays entirely in IEEE-float sample domain.</summary>
        private static ISampleProvider WrapReader(AudioFileReader reader)
        {
            ISampleProvider src = reader;
            if (src.WaveFormat.Channels == 1)
                src = new MonoToStereoSampleProvider(src);
            else if (src.WaveFormat.Channels > 2)
                src = new StereoFoldDownProvider(src);
            if (src.WaveFormat.SampleRate != MixRate)
                src = new WdlResamplingSampleProvider(src, MixRate);
            return src;
        }

        public void LoadA(string path, int bpm, string key, string name, string artist)
        {
            DeckA.IsPlaying = false;
            var reader = new AudioFileReader(path) { Volume = 1.0f };
            var wrapped = WrapReader(reader);
            var old = _readerA;
            _readerA = reader;
            _deckProviderA.SetSource(reader, wrapped);
            DelayDispose(old);

            ResetDeckDefaults(DeckA);
            DeckA.Track = new Track { FilePath = path, Bpm = bpm, OriginalBpm = bpm, Key = key, Name = name, Artist = artist };
            DeckA.IsCued = true;
            DeckA.Track.DurationSeconds = (int)reader.TotalTime.TotalSeconds;
            DeckA.Track.Duration = TimeSpan.FromSeconds(DeckA.Track.DurationSeconds).ToString(@"m\:ss");
            DeckAAnalysis = AnalyzeTrack(path, bpm);
            if (DeckAAnalysis?.Bpm > 0)
            {
                DeckA.Track.Bpm = DeckAAnalysis.Bpm;
                DeckA.Track.OriginalBpm = DeckAAnalysis.Bpm;
            }
            DeckA.CuePosition = GetDefaultCuePosition(DeckAAnalysis, DeckA.Track.DurationSeconds);
            DeckAWaveformBars = DeckAAnalysis?.WaveformRms.ToArray() ?? Array.Empty<double>();
            SeekA(DeckA.CuePosition);
            UpdateDeckTelemetry("A", DeckA, reader);
            SetVolumes();
        }

        public void LoadB(string path, int bpm, string key, string name, string artist)
        {
            DeckB.IsPlaying = false;
            var reader = new AudioFileReader(path) { Volume = 1.0f };
            var wrapped = WrapReader(reader);
            var old = _readerB;
            _readerB = reader;
            _deckProviderB.SetSource(reader, wrapped);
            DelayDispose(old);

            ResetDeckDefaults(DeckB);
            DeckB.Track = new Track { FilePath = path, Bpm = bpm, OriginalBpm = bpm, Key = key, Name = name, Artist = artist };
            DeckB.IsCued = true;
            DeckB.Track.DurationSeconds = (int)reader.TotalTime.TotalSeconds;
            DeckB.Track.Duration = TimeSpan.FromSeconds(DeckB.Track.DurationSeconds).ToString(@"m\:ss");
            DeckBAnalysis = AnalyzeTrack(path, bpm);
            if (DeckBAnalysis?.Bpm > 0)
            {
                DeckB.Track.Bpm = DeckBAnalysis.Bpm;
                DeckB.Track.OriginalBpm = DeckBAnalysis.Bpm;
            }
            DeckB.CuePosition = GetDefaultCuePosition(DeckBAnalysis, DeckB.Track.DurationSeconds);
            DeckBWaveformBars = DeckBAnalysis?.WaveformRms.ToArray() ?? Array.Empty<double>();
            SeekB(DeckB.CuePosition);
            UpdateDeckTelemetry("B", DeckB, reader);
            SetVolumes();
        }

        public bool LoadDeck(string deckLabel, string path, int bpm, string key, string name, string artist)
        {
            var normalizedDeck = DjDeckRouting.Normalize(deckLabel);
            if (normalizedDeck == null)
                return false;

            if (normalizedDeck == "A")
                LoadA(path, bpm, key, name, artist);
            else
                LoadB(path, bpm, key, name, artist);

            return true;
        }

        /// <summary>Dispose an old reader on the thread-pool after a delay,
        /// so any in-flight audio Read() finishes first.</summary>
        private static void DelayDispose(AudioFileReader? old)
        {
            if (old != null)
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Thread.Sleep(500);
                    try { old.Dispose(); } catch { }
                });
        }

        public void PlayA()
        {
            if (_readerA == null) return;
            if (DeckA.CurrentTime >= 99.95)
                SeekA(DeckA.CuePosition > 0 ? DeckA.CuePosition : 0);
            DeckA.IsPlaying = true;
            DeckA.IsCued = false;
            StartTimer();
            SetVolumes();
        }

        public void PlayB()
        {
            if (_readerB == null) return;
            if (DeckB.CurrentTime >= 99.95)
                SeekB(DeckB.CuePosition > 0 ? DeckB.CuePosition : 0);
            DeckB.IsPlaying = true;
            DeckB.IsCued = false;
            StartTimer();
            SetVolumes();
        }

        public void PauseA()
        {
            DeckA.IsPlaying = false;
            SetVolumes();
        }

        public void PauseB()
        {
            DeckB.IsPlaying = false;
            SetVolumes();
        }

        public void SeekA(double pct)
        {
            var reader = _readerA;
            if (reader == null) return;
            pct = Math.Clamp(pct, 0, 100);
            reader.CurrentTime = TimeSpan.FromSeconds(reader.TotalTime.TotalSeconds * pct / 100.0);
            _deckProviderA.SyncPlaybackPosition(reader.CurrentTime.TotalSeconds);
            DeckA.CurrentTime = pct;
        }

        public void SeekB(double pct)
        {
            var reader = _readerB;
            if (reader == null) return;
            pct = Math.Clamp(pct, 0, 100);
            reader.CurrentTime = TimeSpan.FromSeconds(reader.TotalTime.TotalSeconds * pct / 100.0);
            _deckProviderB.SyncPlaybackPosition(reader.CurrentTime.TotalSeconds);
            DeckB.CurrentTime = pct;
        }

        public void SetVolumes()
        {
            var master = MasterVolume / 100.0;
            var deckAOutput = Math.Clamp(GetDeckOutputLevel(DeckA, true) * master, 0, 1);
            var deckBOutput = Math.Clamp(GetDeckOutputLevel(DeckB, false) * master, 0, 1);
            var cueA = DeckA.CueMonitoring ? GetDeckSourceLevel(DeckA) * (1 - CueMix / 100.0) : 0;
            var cueB = DeckB.CueMonitoring ? GetDeckSourceLevel(DeckB) * (CueMix / 100.0) : 0;
            var blendedMaster = Math.Clamp(deckAOutput + deckBOutput, 0, 1);
            MasterLevel = Math.Clamp(blendedMaster + ((cueA + cueB) * (HeadphoneVolume / 250.0)), 0, 1);
        }

        private void StartTimer()
        {
            _timer ??= new Timer(_ => TimerTick(), null, TimeSpan.FromMilliseconds(EngineTickMs), TimeSpan.FromMilliseconds(EngineTickMs));
        }

        private void TimerTick()
        {
            try
            {
                UpdateProgress(DeckA, _readerA);
                UpdateProgress(DeckB, _readerB);
                MaintainSync();
                UpdateDeckTelemetry("A", DeckA, _readerA);
                UpdateDeckTelemetry("B", DeckB, _readerB);
                AdvanceAutomix();
            }
            catch { /* swallow timer exceptions */ }
        }

        private void UpdateProgress(DeckState deck, AudioFileReader? reader)
        {
            try
            {
                if (deck == null || reader == null) return;

                var provider = ReferenceEquals(deck, DeckA) ? _deckProviderA : _deckProviderB;
                var playbackSeconds = provider.GetPlaybackSeconds();
                deck.CurrentTime = playbackSeconds / Math.Max(1, reader.TotalTime.TotalSeconds) * 100.0;
            }
            catch { /* ignore transient errors */ }
        }

        private void UpdateDeckTelemetry(string deckLabel, DeckState deck, AudioFileReader? reader)
        {
            if (deck == null)
                return;

            var durationSeconds = deck.Track?.DurationSeconds ?? 0;
            if (durationSeconds <= 0 && reader != null)
                durationSeconds = (int)reader.TotalTime.TotalSeconds;

            var currentSeconds = durationSeconds > 0
                ? Math.Clamp(deck.CurrentTime, 0, 100) / 100.0 * durationSeconds
                : 0;

            deck.CurrentTimeSeconds = currentSeconds;
            deck.RemainingTimeSeconds = Math.Max(0, durationSeconds - currentSeconds);
            deck.EffectiveBpm = GetEffectiveBpm(deckLabel);
            deck.BeatIntervalSeconds = GetSyncBeatIntervalSeconds(deckLabel);
            deck.BeatPhaseSeconds = GetBeatPhaseSeconds(deckLabel);
            deck.BeatPhaseFraction = GetBeatPhaseFraction(deckLabel);
            deck.WaveformVisible = deck.Track != null && !string.IsNullOrWhiteSpace(deck.Track.FilePath);

            if (deck.BeatIntervalSeconds > 0)
            {
                var analysis = GetAnalysis(deckLabel);
                var offset = analysis?.GridOffsetSeconds ?? 0;
                var beatPosition = Math.Max(0, (currentSeconds - offset) / deck.BeatIntervalSeconds);
                deck.BeatPosition = beatPosition;
                deck.BarIndex = (int)Math.Floor(beatPosition / 4d) + 1;
                deck.BeatInBar = ((int)Math.Floor(beatPosition) % 4) + 1;
                deck.PhraseIndex = (int)Math.Floor(beatPosition / 16d) + 1;
            }
            else
            {
                deck.BeatPosition = 0;
                deck.BarIndex = 0;
                deck.BeatInBar = 0;
                deck.PhraseIndex = 0;
            }
        }

        public void UpdateTempo()
        {
            // Tempo is applied in real-time by DeckProvider.Read() via linear resampling.
            // Just refresh volume levels here.
            MaintainSync();
            SetVolumes();
        }

        public void SetDeckTempo(string deckLabel, double tempo)
        {
            var deck = GetDeck(deckLabel);
            if (deck == null)
                return;

            deck.Tempo = Math.Clamp(tempo, -deck.PitchRange, deck.PitchRange);

            if (deck.SyncEnabled && !deck.IsSyncMaster)
                DisableDeckSync(deckLabel, preserveOtherDeck: true);

            MaintainSync();
            SetVolumes();
        }

        public bool PlayDeck(string deckLabel)
        {
            var normalizedDeck = DjDeckRouting.Normalize(deckLabel);
            if (normalizedDeck == null)
                return false;

            if (normalizedDeck == "A")
                PlayA();
            else
                PlayB();

            return true;
        }

        public bool PauseDeck(string deckLabel)
        {
            var normalizedDeck = DjDeckRouting.Normalize(deckLabel);
            if (normalizedDeck == null)
                return false;

            if (normalizedDeck == "A")
                PauseA();
            else
                PauseB();

            return true;
        }

        public void CueDeck(string deckLabel)
        {
            var normalizedDeck = DjDeckRouting.Normalize(deckLabel);
            var deck = GetDeck(normalizedDeck);
            if (normalizedDeck == null || deck == null)
                return;

            JumpToCue(normalizedDeck);
            PauseDeck(normalizedDeck);

            deck.IsCued = true;
        }

        public void BendDeck(string deckLabel, double deltaSeconds)
        {
            var normalizedDeck = DjDeckRouting.Normalize(deckLabel);
            var deck = GetDeck(normalizedDeck);
            if (normalizedDeck == null || deck?.Track == null || deck.Track.DurationSeconds <= 0)
                return;

            var currentSeconds = deck.CurrentTime / 100.0 * deck.Track.DurationSeconds;
            var nextSeconds = Math.Clamp(currentSeconds + deltaSeconds, 0, deck.Track.DurationSeconds);
            Seek(normalizedDeck, nextSeconds / Math.Max(1, deck.Track.DurationSeconds) * 100.0);
        }

        public void SetLoopIn(string deckLabel)
        {
            var normalizedDeck = DjDeckRouting.Normalize(deckLabel);
            var deck = GetDeck(normalizedDeck);
            if (normalizedDeck == null || deck == null)
                return;

            deck.LoopStart = QuantizePercentToBeat(normalizedDeck, deck.CurrentTime);
            deck.LoopEnd = null;
            deck.LoopActive = false;
        }

        public void SetLoopOut(string deckLabel)
        {
            var normalizedDeck = DjDeckRouting.Normalize(deckLabel);
            var deck = GetDeck(normalizedDeck);
            if (normalizedDeck == null || deck == null)
                return;

            var analysis = GetAnalysis(normalizedDeck);
            if (analysis != null && analysis.BeatIntervalSeconds > 0 && deck.Track.DurationSeconds > 0)
            {
                var loopStart = deck.LoopStart ?? QuantizePercentToBeat(normalizedDeck, deck.CurrentTime);
                var beats = DjTimingMath.ParseLoopSizeBeats(deck.LoopSize);
                var startSeconds = loopStart / 100.0 * deck.Track.DurationSeconds;
                var endSeconds = Math.Min(deck.Track.DurationSeconds, startSeconds + analysis.BeatIntervalSeconds * beats);
                deck.LoopStart = loopStart;
                deck.LoopEnd = endSeconds / deck.Track.DurationSeconds * 100.0;
            }
            else
            {
                deck.LoopEnd = QuantizePercentToBeat(normalizedDeck, deck.CurrentTime);
            }

            deck.LoopActive = deck.LoopStart.HasValue && deck.LoopEnd.HasValue && deck.LoopEnd > deck.LoopStart;
        }

        public void ClearLoop(string deckLabel)
        {
            var deck = GetDeck(DjDeckRouting.Normalize(deckLabel));
            if (deck == null)
                return;

            deck.LoopStart = null;
            deck.LoopEnd = null;
            deck.LoopActive = false;
        }

        public void SetHotCue(string deckLabel, int cueIndex, double percent)
        {
            var normalizedDeck = DjDeckRouting.Normalize(deckLabel);
            var deck = GetDeck(normalizedDeck);
            if (normalizedDeck == null || deck == null || cueIndex < 0)
                return;

            var cues = deck.CuePoints?.ToList() ?? new List<CuePoint>();
            while (cues.Count <= cueIndex)
            {
                cues.Add(new CuePoint
                {
                    Label = $"C{cues.Count + 1}",
                    Color = normalizedDeck == "A" ? "cyan" : "amber",
                    Time = -1
                });
            }

            cues[cueIndex].Time = QuantizePercentToBeat(normalizedDeck, percent);
            deck.CuePoints = cues;
        }

        public void TriggerHotCue(string deckLabel, int cueIndex)
        {
            var normalizedDeck = DjDeckRouting.Normalize(deckLabel);
            var deck = GetDeck(normalizedDeck);
            if (normalizedDeck == null || deck == null || cueIndex < 0)
                return;

            var cues = deck.CuePoints?.ToList() ?? new List<CuePoint>();
            while (cues.Count <= cueIndex)
            {
                cues.Add(new CuePoint
                {
                    Label = $"C{cues.Count + 1}",
                    Color = normalizedDeck == "A" ? "cyan" : "amber",
                    Time = -1
                });
            }

            if (cues[cueIndex].Time >= 0)
            {
                Seek(normalizedDeck, cues[cueIndex].Time);
                SetCuePosition(normalizedDeck, cues[cueIndex].Time);
            }
            else
            {
                cues[cueIndex].Time = QuantizePercentToBeat(normalizedDeck, deck.CurrentTime);
                deck.CuePoints = cues;
            }
        }

        public bool StartAutomixTransition(string sourceDeckLabel, string targetDeckLabel, double transitionBeats = 16)
        {
            var sourceLabel = DjDeckRouting.Normalize(sourceDeckLabel);
            var targetLabel = DjDeckRouting.Normalize(targetDeckLabel);
            if (sourceLabel == null || targetLabel == null || sourceLabel == targetLabel)
                return false;

            var source = GetDeck(sourceLabel);
            var target = GetDeck(targetLabel);
            if (!HasSyncableTrack(source) || !HasSyncableTrack(target))
                return false;

            if (!source!.IsPlaying)
                PlayDeck(sourceLabel);

            if (!source.IsSyncMaster)
                PromoteDeckToMaster(sourceLabel);

            EnableFollower(targetLabel, sourceLabel, snapPhase: true);
            if (!target!.IsPlaying)
                PlayDeck(targetLabel);

            AutoMix.Enabled = true;
            AutoMix.SourceDeck = sourceLabel;
            AutoMix.TargetDeck = targetLabel;
            AutoMix.TransitionBeats = transitionBeats;
            AutoMix.Progress = 0;
            AutoMix.RemainingBeats = transitionBeats;
            AutoMix.Status = $"Mixing {sourceLabel} -> {targetLabel}";
            _autoMixStartBeatPosition = source.BeatPosition;

            SetCrossfader(sourceLabel == "A" ? 0 : 100);
            return true;
        }

        public void StopAutomix(string status = "")
        {
            AutoMix.Enabled = false;
            AutoMix.Progress = 0;
            AutoMix.RemainingBeats = 0;
            AutoMix.SourceDeck = string.Empty;
            AutoMix.TargetDeck = string.Empty;
            AutoMix.Status = status;
        }

        public void ToggleDeckSync(string deckLabel)
        {
            var deck = GetDeck(deckLabel);
            if (deck == null || deck.Track == null || string.IsNullOrWhiteSpace(deck.Track.FilePath))
                return;

            if (deck.IsSyncMaster)
            {
                DisableDeckSync(deckLabel, preserveOtherDeck: false);
                MaintainSync();
                return;
            }

            if (deck.SyncEnabled)
            {
                DisableDeckSync(deckLabel, preserveOtherDeck: true);
                MaintainSync();
                return;
            }

            var otherLabel = GetOppositeDeckLabel(deckLabel);
            var otherDeck = GetDeck(otherLabel);
            if (otherDeck != null && otherDeck.SyncEnabled && otherDeck.IsSyncMaster && HasSyncableTrack(otherDeck))
            {
                EnableFollower(deckLabel, otherLabel, snapPhase: !(deck.IsPlaying && otherDeck.IsPlaying));
                MaintainSync();
                return;
            }

            if (otherDeck != null && HasSyncableTrack(otherDeck) && otherDeck.IsPlaying)
            {
                PromoteDeckToMaster(otherLabel);
                EnableFollower(deckLabel, otherLabel, snapPhase: !deck.IsPlaying);
                MaintainSync();
                return;
            }

            PromoteDeckToMaster(deckLabel);
            MaintainSync();
        }

        /* ── Recording ─────────────────────────────────────────── */

        public bool IsRecording => _recordingTap?.IsRecording ?? false;
        public string? RecordingPath => _recordingTap?.RecordingPath;

        public string StartRecording()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Atlas DJ Recordings");
            Directory.CreateDirectory(dir);
            var filename = $"Mix_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
            var path = Path.Combine(dir, filename);
            _recordingTap?.Start(path);
            return path;
        }

        public void StopRecording()
        {
            _recordingTap?.Stop();
        }

        /* ── Sample playback ─────────────────────────────────── */

        public void PlaySample(string path, float volume = 1.0f)
        {
            if (_mixer == null || !File.Exists(path)) return;
            try
            {
                var reader = new AudioFileReader(path) { Volume = volume };
                var wrapped = WrapReader(reader);
                _mixer.AddMixerInput(new AutoDisposeSampleProvider(wrapped, reader));
            }
            catch { }
        }

        public void SetCrossfader(double value)
        {
            Crossfader = Math.Clamp(value, 0, 100);
            SetVolumes();
        }

        public void SetCrossfaderCurve(string curve)
        {
            CrossfaderCurve = string.IsNullOrWhiteSpace(curve) ? "sharp" : curve.Trim().ToLowerInvariant();
            SetVolumes();
        }

        public void SetMasterVolume(double value)
        {
            MasterVolume = Math.Clamp(value, 0, 100);
            SetVolumes();
        }

        public void SetCueMix(double value)
        {
            CueMix = Math.Clamp(value, 0, 100);
            SetVolumes();
        }

        public void SetHeadphoneVolume(double value)
        {
            HeadphoneVolume = Math.Clamp(value, 0, 100);
            SetVolumes();
        }

        public void SetMicVolume(double value)
        {
            MicVolume = Math.Clamp(value, 0, 100);
        }

        public void SetDeckVolume(string deckLabel, double value)
        {
            var deck = GetDeck(deckLabel);
            if (deck == null) return;
            deck.Volume = value;
            SetVolumes();
        }

        public void SetDeckGain(string deckLabel, double value)
        {
            var deck = GetDeck(deckLabel);
            if (deck == null) return;
            deck.Gain = value;
            SetVolumes();
        }

        public void SetDeckEq(string deckLabel, string band, double value)
        {
            var deck = GetDeck(deckLabel);
            if (deck == null) return;

            switch ((band ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "high": deck.EqHigh = value; break;
                case "mid": deck.EqMid = value; break;
                case "low": deck.EqLow = value; break;
                default: return;
            }
            SetVolumes();
        }

        public void SetDeckFilter(string deckLabel, double value)
        {
            var deck = GetDeck(deckLabel);
            if (deck == null) return;
            deck.Filter = value;
            SetVolumes();
        }

        public void SetDeckPitchRange(string deckLabel, double value)
        {
            var deck = GetDeck(deckLabel);
            if (deck == null) return;
            deck.PitchRange = value;
            deck.Tempo = Math.Clamp(deck.Tempo, -deck.PitchRange, deck.PitchRange);
            SetVolumes();
        }

        public void ToggleDeckCueMonitor(string deckLabel)
        {
            var deck = GetDeck(deckLabel);
            if (deck == null) return;
            deck.CueMonitoring = !deck.CueMonitoring;
            SetVolumes();
        }

        public void SetEffectMode(string effectMode)
        {
            EffectMode = string.IsNullOrWhiteSpace(effectMode) ? "Punch" : effectMode;
        }

        public void SetCuePosition(string deckLabel, double pct)
        {
            var deck = GetDeck(deckLabel);
            if (deck == null) return;
            deck.CuePosition = Math.Clamp(pct, 0, 100);
            deck.IsCued = true;
        }

        public void JumpToCue(string deckLabel)
        {
            var deck = GetDeck(deckLabel);
            if (deck == null) return;
            Seek(deckLabel, deck.CuePosition);
            deck.IsCued = true;
        }

        public void Seek(string deckLabel, double pct)
        {
            if (string.Equals(deckLabel, "A", StringComparison.OrdinalIgnoreCase))
                SeekA(pct);
            else if (string.Equals(deckLabel, "B", StringComparison.OrdinalIgnoreCase))
                SeekB(pct);
            SetVolumes();
        }

        public DeckState? GetDeck(string? deckLabel)
        {
            if (string.Equals(deckLabel, "A", StringComparison.OrdinalIgnoreCase)) return DeckA;
            if (string.Equals(deckLabel, "B", StringComparison.OrdinalIgnoreCase)) return DeckB;
            return null;
        }

        public double GetBaseBpm(string? deckLabel)
        {
            var analysis = GetAnalysis(deckLabel);
            if (analysis?.Bpm > 0)
                return analysis.Bpm;

            var track = GetDeck(deckLabel)?.Track;
            if (track == null)
                return 0;

            return track.OriginalBpm > 0 ? track.OriginalBpm : track.Bpm;
        }

        public double GetEffectiveBpm(string? deckLabel)
        {
            var deck = GetDeck(deckLabel);
            var baseBpm = GetBaseBpm(deckLabel);
            if (deck == null || baseBpm <= 0)
                return 0;

            return baseBpm * (1 + deck.Tempo / 100.0);
        }

        private static void ResetDeckDefaults(DeckState deck)
        {
            deck.IsPlaying = false;
            deck.LoopActive = false;
            deck.IsCued = false;
            deck.SyncEnabled = false;
            deck.IsSyncMaster = false;
            deck.CueMonitoring = false;
            deck.Volume = 80;
            deck.Gain = 50;
            deck.EqHigh = 50;
            deck.EqMid = 50;
            deck.EqLow = 50;
            deck.Filter = 50;
            deck.Tempo = 0;
            deck.PitchRange = 12;
            deck.CurrentTime = 0;
            deck.CuePosition = 0;
            deck.LoopStart = null;
            deck.LoopEnd = null;
            deck.LoopSize = "1";
            deck.SyncSourceDeck = string.Empty;
            deck.SyncTargetBpm = 0;
            deck.SyncTempoDelta = 0;
            deck.SyncBeatPhaseOffsetSeconds = 0;
            deck.SyncAligned = false;
            deck.CurrentTimeSeconds = 0;
            deck.RemainingTimeSeconds = 0;
            deck.EffectiveBpm = 0;
            deck.BeatIntervalSeconds = 0;
            deck.BeatPhaseFraction = 0;
            deck.BeatPhaseSeconds = 0;
            deck.BeatPosition = 0;
            deck.BarIndex = 0;
            deck.BeatInBar = 0;
            deck.PhraseIndex = 0;
            deck.WaveformVisible = true;
            deck.CuePoints = new System.Collections.Generic.List<CuePoint>();
        }

        private void AdvanceAutomix()
        {
            if (!AutoMix.Enabled)
                return;

            var sourceLabel = DjDeckRouting.Normalize(AutoMix.SourceDeck);
            var targetLabel = DjDeckRouting.Normalize(AutoMix.TargetDeck);
            var source = GetDeck(sourceLabel);
            var target = GetDeck(targetLabel);
            if (sourceLabel == null || targetLabel == null || !HasSyncableTrack(source) || !HasSyncableTrack(target))
            {
                StopAutomix("Automix stopped");
                return;
            }

            if (!source!.IsPlaying)
                PlayDeck(sourceLabel);
            if (!target!.IsPlaying)
                PlayDeck(targetLabel);

            if (!source.IsSyncMaster)
                PromoteDeckToMaster(sourceLabel);
            if (!target.SyncEnabled || !string.Equals(target.SyncSourceDeck, sourceLabel, StringComparison.OrdinalIgnoreCase))
                EnableFollower(targetLabel, sourceLabel, snapPhase: false);

            var beatsElapsed = Math.Max(0, source.BeatPosition - _autoMixStartBeatPosition);
            var progress = AutoMix.TransitionBeats > 0
                ? Math.Clamp(beatsElapsed / AutoMix.TransitionBeats, 0, 1)
                : 1;

            AutoMix.Progress = progress;
            AutoMix.RemainingBeats = Math.Max(0, AutoMix.TransitionBeats - beatsElapsed);
            AutoMix.Status = $"Crossfading {sourceLabel} -> {targetLabel}";

            SetCrossfader(sourceLabel == "A"
                ? progress * 100.0
                : 100.0 - (progress * 100.0));

            if (progress >= 1)
            {
                PauseDeck(sourceLabel);
                ClearSyncState(source);
                PromoteDeckToMaster(targetLabel);
                AutoMix.Status = "Mix complete";
                AutoMix.Enabled = false;
                AutoMix.RemainingBeats = 0;
                AutoMix.Progress = 1;
            }
        }

        private double QuantizePercentToBeat(string deckLabel, double percent)
        {
            var deck = GetDeck(deckLabel);
            var analysis = GetAnalysis(deckLabel);
            if (deck?.Track == null || deck.Track.DurationSeconds <= 0 || analysis == null || analysis.BeatMarkers.Count == 0)
                return Math.Clamp(percent, 0, 100);

            var currentSeconds = Math.Clamp(percent, 0, 100) / 100.0 * deck.Track.DurationSeconds;
            var snapped = analysis.BeatMarkers.OrderBy(marker => Math.Abs(marker - currentSeconds)).FirstOrDefault();
            return Math.Clamp(snapped / deck.Track.DurationSeconds * 100.0, 0, 100);
        }

        private double GetCrossfadeAmount(bool isDeckA)
        {
            var position = Crossfader / 100.0;
            return CrossfaderCurve switch
            {
                "smooth" => isDeckA ? Math.Cos(position * Math.PI * 0.5) : Math.Sin(position * Math.PI * 0.5),
                "sharp" => isDeckA
                    ? (position <= 0.5 ? 1 : Math.Clamp(2 - (position * 2), 0, 1))
                    : (position >= 0.5 ? 1 : Math.Clamp(position * 2, 0, 1)),
                _ => isDeckA ? 1 - position : position,
            };
        }

        public double GetDeckMixAmount(string? deckLabel)
        {
            return string.Equals(deckLabel, "A", StringComparison.OrdinalIgnoreCase)
                ? GetCrossfadeAmount(true)
                : string.Equals(deckLabel, "B", StringComparison.OrdinalIgnoreCase)
                    ? GetCrossfadeAmount(false)
                    : 0;
        }

        public double GetDeckSourceLevel(DeckState deck) => GetDeckSignal(deck);

        public double GetDeckOutputLevel(DeckState deck, bool isDeckA)
        {
            return Math.Clamp(GetDeckSignal(deck) * GetCrossfadeAmount(isDeckA), 0, 1);
        }

        private static double GetDeckSignal(DeckState deck)
        {
            var channel = deck.Volume / 100.0;
            var gain = 0.5 + (deck.Gain / 100.0);
            var eqHigh = 0.5 + (deck.EqHigh / 100.0);
            var eqMid  = 0.5 + (deck.EqMid  / 100.0);
            var eqLow  = 0.5 + (deck.EqLow  / 100.0);
            var eq = (eqHigh + eqMid + eqLow) / 3.0;
            var filterDistance = Math.Abs(deck.Filter - 50) / 50.0;
            var filter = 1 - (filterDistance * 0.55);
            return Math.Clamp(channel * gain * eq * filter, 0, 1.5);
        }

        private static TrackAnalysis AnalyzeTrack(string path, int fallbackBpm)
        {
            try
            {
                var analysis = TrackAnalysisEngine.Analyze(path);
                if (analysis.Bpm <= 0 && fallbackBpm > 0)
                    analysis.Bpm = fallbackBpm;
                return analysis;
            }
            catch
            {
                return new TrackAnalysis { Bpm = fallbackBpm };
            }
        }

        /// <summary>Detect BPM via bass-filtered onset autocorrelation with harmonic scoring.</summary>
        public static int DetectBpm(string path)
        {
            try
            {
                return TrackAnalysisEngine.Analyze(path).Bpm;
            }
            catch { return 0; }
        }

        public TrackAnalysis? GetAnalysis(string? deckLabel)
        {
            if (string.Equals(deckLabel, "A", StringComparison.OrdinalIgnoreCase)) return DeckAAnalysis;
            if (string.Equals(deckLabel, "B", StringComparison.OrdinalIgnoreCase)) return DeckBAnalysis;
            return null;
        }

        public double GetDeckCurrentSeconds(string? deckLabel)
        {
            var deck = GetDeck(deckLabel);
            if (deck?.Track == null || deck.Track.DurationSeconds <= 0)
                return 0;

            return deck.CurrentTime / 100.0 * deck.Track.DurationSeconds;
        }

        public double GetBeatPhaseSeconds(string? deckLabel)
        {
            var beatInterval = GetSyncBeatIntervalSeconds(deckLabel);
            if (beatInterval <= 0)
                return 0;

            return GetBeatPhaseSecondsInternal(deckLabel, beatInterval);
        }

        public double GetBeatPhaseFraction(string? deckLabel)
        {
            var beatInterval = GetSyncBeatIntervalSeconds(deckLabel);
            if (beatInterval <= 0)
                return 0;

            return GetBeatPhaseFractionInternal(deckLabel, beatInterval);
        }

        public double GetBeatPhaseOffsetSeconds(string? deckLabel, string? referenceDeckLabel)
        {
            var deckBeatInterval = GetSyncBeatIntervalSeconds(deckLabel);
            var referenceBeatInterval = GetSyncBeatIntervalSeconds(referenceDeckLabel);
            if (deckBeatInterval <= 0 || referenceBeatInterval <= 0)
                return 0;

            var beatOffset = GetBeatPhaseOffsetBeats(deckLabel, deckBeatInterval, referenceDeckLabel, referenceBeatInterval);
            return beatOffset * deckBeatInterval;
        }

        public double AlignDeckToPhaseFraction(string deckLabel, double sourcePhaseFraction)
        {
            var deck = GetDeck(deckLabel);
            var analysis = GetAnalysis(deckLabel);
            var beatInterval = GetSyncBeatIntervalSeconds(deckLabel);
            if (deck?.Track == null || analysis == null || beatInterval <= 0 || deck.Track.DurationSeconds <= 0)
                return deck?.CurrentTime ?? 0;

            var normalizedPhaseFraction = sourcePhaseFraction % 1.0;
            if (normalizedPhaseFraction < 0)
                normalizedPhaseFraction += 1.0;

            var currentSeconds = GetDeckCurrentSeconds(deckLabel);
            var nearestBeatIndex = Math.Round((currentSeconds - analysis.GridOffsetSeconds) / beatInterval);
            var alignedSeconds = analysis.GridOffsetSeconds + nearestBeatIndex * beatInterval + normalizedPhaseFraction * beatInterval;
            alignedSeconds = Math.Clamp(alignedSeconds, 0, Math.Max(0, deck.Track.DurationSeconds - 0.05));
            var pct = alignedSeconds / deck.Track.DurationSeconds * 100.0;
            Seek(deckLabel, pct);
            return pct;
        }

        public double AlignDeckToPhase(string deckLabel, double sourcePhaseSeconds)
        {
            var beatInterval = GetSyncBeatIntervalSeconds(deckLabel);
            if (beatInterval <= 0)
                return GetDeck(deckLabel)?.CurrentTime ?? 0;

            return AlignDeckToPhaseFraction(deckLabel, sourcePhaseSeconds / beatInterval);
        }

        public double GetRequiredTempoPercent(double sourceEffectiveBpm, double targetBaseBpm)
        {
            if (sourceEffectiveBpm <= 0 || targetBaseBpm <= 0)
                return 0;

            return ((sourceEffectiveBpm / targetBaseBpm) - 1) * 100.0;
        }

        private double GetDeckBeatIntervalSeconds(string? deckLabel, double baseBpmOverride = 0)
        {
            var analysis = GetAnalysis(deckLabel);
            var fallbackBaseBpm = baseBpmOverride > 0 ? baseBpmOverride : GetBaseBpm(deckLabel);
            if (analysis == null || analysis.BeatIntervalSeconds <= 0)
                return fallbackBaseBpm > 0 ? 60.0 / fallbackBaseBpm : 0;

            var analysisBpm = analysis.Bpm > 0 ? analysis.Bpm : GetBaseBpm(deckLabel);
            var effectiveBaseBpm = baseBpmOverride > 0 ? baseBpmOverride : analysisBpm;
            if (analysisBpm <= 0 || effectiveBaseBpm <= 0)
                return analysis.BeatIntervalSeconds;

            return analysis.BeatIntervalSeconds * (analysisBpm / effectiveBaseBpm);
        }

        private double GetSyncBeatIntervalSeconds(string? deckLabel)
        {
            var deck = GetDeck(deckLabel);
            if (deck != null && deck.SyncEnabled && !deck.IsSyncMaster && deck.SyncTargetBpm > 0)
            {
                var baseBpm = GetBaseBpm(deckLabel);
                if (baseBpm > 0)
                {
                    var adjustedBaseBpm = DjTimingMath.ResolveSyncBaseBpm(deck.SyncTargetBpm, baseBpm, deck.PitchRange);
                    return GetDeckBeatIntervalSeconds(deckLabel, adjustedBaseBpm);
                }
            }

            return GetDeckBeatIntervalSeconds(deckLabel);
        }

        private double GetBeatPhaseSecondsInternal(string? deckLabel, double beatInterval)
        {
            var analysis = GetAnalysis(deckLabel);
            if (analysis == null || beatInterval <= 0)
                return 0;

            var currentSeconds = GetDeckCurrentSeconds(deckLabel);
            var phase = (currentSeconds - analysis.GridOffsetSeconds) % beatInterval;
            if (phase < 0)
                phase += beatInterval;
            return phase;
        }

        private double GetBeatPhaseFractionInternal(string? deckLabel, double beatInterval)
        {
            if (beatInterval <= 0)
                return 0;

            return GetBeatPhaseSecondsInternal(deckLabel, beatInterval) / beatInterval;
        }

        private double GetBeatPhaseOffsetBeats(string? deckLabel, double deckBeatInterval, string? referenceDeckLabel, double referenceBeatInterval)
        {
            if (deckBeatInterval <= 0 || referenceBeatInterval <= 0)
                return 0;

            var deckPhaseFraction = GetBeatPhaseFractionInternal(deckLabel, deckBeatInterval);
            var referencePhaseFraction = GetBeatPhaseFractionInternal(referenceDeckLabel, referenceBeatInterval);
            return DjTimingMath.NormalizePhaseBeatOffset(deckPhaseFraction - referencePhaseFraction);
        }

        private static bool HasSyncableTrack(DeckState? deck)
        {
            return deck?.Track != null
                && !string.IsNullOrWhiteSpace(deck.Track.FilePath)
                && deck.Track.DurationSeconds > 0;
        }

        private static string GetOppositeDeckLabel(string deckLabel)
        {
            return string.Equals(deckLabel, "A", StringComparison.OrdinalIgnoreCase) ? "B" : "A";
        }

        private void PromoteDeckToMaster(string deckLabel)
        {
            var deck = GetDeck(deckLabel);
            if (deck == null)
                return;

            var otherDeck = GetDeck(GetOppositeDeckLabel(deckLabel));
            deck.SyncEnabled = true;
            deck.IsSyncMaster = true;
            deck.SyncSourceDeck = string.Empty;
            deck.SyncTargetBpm = GetEffectiveBpm(deckLabel);
            deck.SyncTempoDelta = 0;
            deck.SyncBeatPhaseOffsetSeconds = 0;
            deck.SyncAligned = true;

            if (otherDeck != null && otherDeck.IsSyncMaster)
                ClearSyncState(otherDeck);
        }

        private void EnableFollower(string followerLabel, string masterLabel, bool snapPhase)
        {
            var follower = GetDeck(followerLabel);
            var master = GetDeck(masterLabel);
            if (!HasSyncableTrack(follower) || !HasSyncableTrack(master))
                return;

            master!.SyncEnabled = true;
            master.IsSyncMaster = true;
            follower!.SyncEnabled = true;
            follower.IsSyncMaster = false;
            follower.SyncSourceDeck = masterLabel;
            SynchronizeFollowerToMaster(followerLabel, masterLabel, snapPhase);
        }

        private void DisableDeckSync(string deckLabel, bool preserveOtherDeck)
        {
            var deck = GetDeck(deckLabel);
            if (deck == null)
                return;

            var otherLabel = GetOppositeDeckLabel(deckLabel);
            var otherDeck = GetDeck(otherLabel);
            var disableOtherDeck = deck.IsSyncMaster || !preserveOtherDeck;

            ClearSyncState(deck);

            if (disableOtherDeck && otherDeck != null && otherDeck.SyncEnabled)
                ClearSyncState(otherDeck);
            else if (otherDeck != null && otherDeck.IsSyncMaster)
                UpdateMasterTelemetry(otherLabel);
        }

        private static void ClearSyncState(DeckState deck)
        {
            deck.SyncEnabled = false;
            deck.IsSyncMaster = false;
            deck.SyncSourceDeck = string.Empty;
            deck.SyncTargetBpm = 0;
            deck.SyncTempoDelta = 0;
            deck.SyncBeatPhaseOffsetSeconds = 0;
            deck.SyncAligned = false;
        }

        private void MaintainSync()
        {
            var masterLabel = DeckA.SyncEnabled && DeckA.IsSyncMaster ? "A"
                : DeckB.SyncEnabled && DeckB.IsSyncMaster ? "B"
                : null;

            if (masterLabel == null)
            {
                if (!DeckA.SyncEnabled) ClearSyncState(DeckA);
                if (!DeckB.SyncEnabled) ClearSyncState(DeckB);
                return;
            }

            UpdateMasterTelemetry(masterLabel);

            var followerLabel = GetOppositeDeckLabel(masterLabel);
            var follower = GetDeck(followerLabel);
            if (follower == null)
                return;

            if (!follower.SyncEnabled || follower.IsSyncMaster)
            {
                if (!follower.SyncEnabled)
                    ClearSyncState(follower);
                return;
            }

            var master = GetDeck(masterLabel);
            var bothPlaying = follower.IsPlaying && master?.IsPlaying == true;
            SynchronizeFollowerToMaster(followerLabel, masterLabel, snapPhase: !bothPlaying);
        }

        private void UpdateMasterTelemetry(string masterLabel)
        {
            var master = GetDeck(masterLabel);
            if (master == null)
                return;

            master.SyncEnabled = true;
            master.IsSyncMaster = true;
            master.SyncSourceDeck = string.Empty;
            master.SyncTargetBpm = GetEffectiveBpm(masterLabel);
            master.SyncTempoDelta = 0;
            master.SyncBeatPhaseOffsetSeconds = 0;
            master.SyncAligned = true;
        }

        private void SynchronizeFollowerToMaster(string followerLabel, string masterLabel, bool snapPhase)
        {
            var follower = GetDeck(followerLabel);
            var master = GetDeck(masterLabel);
            if (master == null)
                return;

            if (!HasSyncableTrack(follower) || !HasSyncableTrack(master))
            {
                if (follower != null)
                    ClearSyncState(follower);
                return;
            }

            var masterEffectiveBpm = GetEffectiveBpm(masterLabel);
            var followerBaseBpm = GetBaseBpm(followerLabel);
            if (masterEffectiveBpm <= 0 || followerBaseBpm <= 0)
                return;

            var adjustedFollowerBaseBpm = DjTimingMath.ResolveSyncBaseBpm(masterEffectiveBpm, followerBaseBpm, follower.PitchRange);
            var requiredTempo = GetRequiredTempoPercent(masterEffectiveBpm, adjustedFollowerBaseBpm);
            if (Math.Abs(requiredTempo) > follower.PitchRange)
                follower.PitchRange = Math.Max(follower.PitchRange, Math.Min(SyncMaxAutoPitchRange, Math.Ceiling(Math.Abs(requiredTempo) / 4.0) * 4.0));

            follower.SyncEnabled = true;
            follower.IsSyncMaster = false;
            follower.SyncSourceDeck = masterLabel;
            follower.SyncTargetBpm = masterEffectiveBpm;

            var followerBeatInterval = GetDeckBeatIntervalSeconds(followerLabel, adjustedFollowerBaseBpm);
            var masterBeatInterval = GetDeckBeatIntervalSeconds(masterLabel);
            var phaseBeatOffset = GetBeatPhaseOffsetBeats(followerLabel, followerBeatInterval, masterLabel, masterBeatInterval);
            var phaseOffset = phaseBeatOffset * followerBeatInterval;
            var followerTrack = follower.Track;
            if (followerTrack == null)
                return;

            var bothPlaying = follower.IsPlaying && master.IsPlaying;
            var phaseAlignedTolerance = followerBeatInterval > 0
                ? Math.Min(SyncAlignedToleranceSeconds, followerBeatInterval * 0.09)
                : SyncAlignedToleranceSeconds;
            var resyncThreshold = bothPlaying
                ? Math.Max(PlayingSyncResyncThresholdSeconds, followerBeatInterval * SyncHardResyncBeatWindow)
                : Math.Max(SyncResyncThresholdSeconds, followerBeatInterval * 0.22);

            if (snapPhase && Math.Abs(phaseOffset) >= resyncThreshold)
            {
                var currentSeconds = GetDeckCurrentSeconds(followerLabel);
                var targetSeconds = Math.Clamp(currentSeconds - phaseOffset, 0, Math.Max(0, followerTrack.DurationSeconds - 0.05));
                var targetPercent = targetSeconds / Math.Max(1, followerTrack.DurationSeconds) * 100.0;
                Seek(followerLabel, targetPercent);
                phaseBeatOffset = GetBeatPhaseOffsetBeats(followerLabel, followerBeatInterval, masterLabel, masterBeatInterval);
                phaseOffset = phaseBeatOffset * followerBeatInterval;
            }

            var phaseTempoCorrection = 0.0;
            if (bothPlaying && followerBeatInterval > 0)
            {
                var nudgeStrength = Math.Abs(phaseBeatOffset) > 0.2
                    ? SyncTempoNudgeStrength * 1.35
                    : SyncTempoNudgeStrength;
                phaseTempoCorrection = Math.Clamp(
                    -phaseBeatOffset * nudgeStrength,
                    -SyncMaxTempoNudgePercent,
                    SyncMaxTempoNudgePercent);
            }

            follower.Tempo = Math.Clamp(requiredTempo + phaseTempoCorrection, -follower.PitchRange, follower.PitchRange);
            follower.SyncTempoDelta = masterEffectiveBpm - GetEffectiveBpm(followerLabel);

            follower.SyncBeatPhaseOffsetSeconds = phaseOffset;
            follower.SyncAligned = Math.Abs(phaseOffset) <= phaseAlignedTolerance;
        }

        private static double GetDefaultCuePosition(TrackAnalysis? analysis, int durationSeconds)
        {
            if (analysis == null || durationSeconds <= 0)
                return 0;

            var launchPoint = analysis.PhraseMarkers.FirstOrDefault();
            if (launchPoint <= 0 && analysis.BeatMarkers.Count > 0)
                launchPoint = analysis.BeatMarkers[0];
            if (launchPoint <= 0)
                return 0;

            return Math.Clamp(launchPoint / Math.Max(1, durationSeconds) * 100.0, 0, 100);
        }

        private static int DetectBpmFromOnset(double[] energy, int count, double windowDuration)
        {
            var onset = new double[count - 1];
            for (int i = 0; i < onset.Length; i++)
                onset[i] = Math.Max(0, energy[i + 1] - energy[i]);

            // Autocorrelation for BPM range 70–180
            var minLag = (int)(60.0 / (180.0 * windowDuration));
            var maxLag = (int)(60.0 / (70.0 * windowDuration));
            maxLag = Math.Min(maxLag, onset.Length / 2);
            if (minLag >= maxLag) return 0;

            var corr = new double[maxLag - minLag + 1];
            double maxCorr = 0;
            for (int lag = minLag; lag <= maxLag; lag++)
            {
                double sum = 0;
                int len = Math.Min(onset.Length - lag, 800);
                for (int i = 0; i < len; i++)
                    sum += onset[i] * onset[i + lag];
                corr[lag - minLag] = sum;
                if (sum > maxCorr) maxCorr = sum;
            }
            if (maxCorr <= 0) return 0;

            // Find all local peaks above 40% of max
            var peaks = new List<(int lag, double corr)>();
            for (int i = 1; i < corr.Length - 1; i++)
            {
                if (corr[i] > corr[i - 1] && corr[i] >= corr[i + 1] && corr[i] > maxCorr * 0.4)
                    peaks.Add((minLag + i, corr[i]));
            }
            if (peaks.Count == 0)
            {
                int bestIdx = 0;
                for (int i = 1; i < corr.Length; i++)
                    if (corr[i] > corr[bestIdx]) bestIdx = i;
                peaks.Add((minLag + bestIdx, corr[bestIdx]));
            }

            // Score each peak: correlation strength + harmonic consistency
            double bestScore = 0;
            int bestLag = peaks[0].lag;
            foreach (var peak in peaks)
            {
                double score = peak.corr / maxCorr;
                // Bonus if 2× lag (half-BPM harmonic) also has correlation
                int lag2Idx = peak.lag * 2 - minLag;
                if (lag2Idx >= 0 && lag2Idx < corr.Length)
                    score += (corr[lag2Idx] / maxCorr) * 0.3;
                // Bonus if lag/2 (double-BPM harmonic) also has correlation
                int lagHalfIdx = peak.lag / 2 - minLag;
                if (lagHalfIdx >= 0 && lagHalfIdx < corr.Length)
                    score += (corr[lagHalfIdx] / maxCorr) * 0.3;
                // Bonus if 3× lag also correlates (triplet consistency)
                int lag3Idx = peak.lag * 3 - minLag;
                if (lag3Idx >= 0 && lag3Idx < corr.Length)
                    score += (corr[lag3Idx] / maxCorr) * 0.15;

                if (score > bestScore) { bestScore = score; bestLag = peak.lag; }
            }

            var detectedBpm = 60.0 / (bestLag * windowDuration);
            // Normalise to DJ-friendly range 85-170 (covers most dance music)
            while (detectedBpm > 170) detectedBpm /= 2;
            while (detectedBpm < 85) detectedBpm *= 2;
            return (int)Math.Round(detectedBpm);
        }

        public void Dispose()
        {
            StopAutomix();
            _timer?.Dispose();
            _recordingTap?.Stop();
            try { _masterPlayer?.Stop(); } catch { }
            _masterPlayer?.Dispose();
            _readerA?.Dispose();
            _readerB?.Dispose();
        }

        /* ── Lock-free per-deck provider — runs on the audio thread ── */
        private sealed class DeckProvider : ISampleProvider
        {
            private const double TimeStretchThreshold = 0.0015;
            private const int ProcessorChunkFrames = 2048;
            private const double LoopBoundaryToleranceSeconds = 0.0005;

            private readonly AudioEngine _engine;
            private readonly DeckState _deck;
            private readonly bool _isDeckA;
            private volatile ISampleProvider? _source;
            private volatile AudioFileReader? _reader;
            private SoundTouchProcessor? _timeStretch;
            private float[]? _processorInputBuffer;
            private bool _sourceExhausted;
            private bool _usingTimeStretch;
            private double _playbackSeconds;

            public WaveFormat WaveFormat { get; }

            public DeckProvider(AudioEngine engine, DeckState deck, bool isDeckA)
            {
                _engine = engine;
                _deck = deck;
                _isDeckA = isDeckA;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(MixRate, MixChannels);
            }

            /// <summary>Swap the audio source. Called from UI thread;
            /// old reader disposal is handled by the caller.</summary>
            public void SetSource(AudioFileReader reader, ISampleProvider wrapped)
            {
                _reader = reader;
                _source = wrapped;
                ResetProcessingState();
                SyncPlaybackPosition(reader.CurrentTime.TotalSeconds);
            }

            public void SyncPlaybackPosition(double seconds)
            {
                Interlocked.Exchange(ref _playbackSeconds, Math.Max(0, seconds));
                ResetProcessingState();
            }

            public double GetPlaybackSeconds()
            {
                return Math.Max(0, Interlocked.CompareExchange(ref _playbackSeconds, 0, 0));
            }

            private void ResetProcessingState()
            {
                _sourceExhausted = false;
                _timeStretch?.Clear();
            }

            private SoundTouchProcessor GetOrCreateTimeStretch(double tempo)
            {
                if (_timeStretch == null)
                {
                    _timeStretch = new SoundTouchProcessor
                    {
                        SampleRate = MixRate,
                        Channels = MixChannels,
                        Rate = 1.0,
                        Pitch = 1.0,
                        Tempo = tempo,
                    };
                    _timeStretch.SetSetting(SettingId.UseQuickSeek, 1);
                    _timeStretch.SetSetting(SettingId.SequenceDurationMs, 40);
                    _timeStretch.SetSetting(SettingId.SeekWindowDurationMs, 15);
                    _timeStretch.SetSetting(SettingId.OverlapDurationMs, 8);
                }
                else
                {
                    _timeStretch.Rate = 1.0;
                    _timeStretch.Pitch = 1.0;
                    _timeStretch.Tempo = tempo;
                }

                return _timeStretch;
            }

            public int Read(float[] buffer, int offset, int count)
            {
                Array.Clear(buffer, offset, count);

                var source = _source;
                var reader = _reader;
                if (source == null || reader == null || !_deck.IsPlaying)
                    return count;

                double speed = Math.Clamp(1.0 + (_deck.Tempo / 100.0), 0.5, 1.5);
                var useTimeStretch = Math.Abs(speed - 1.0) >= TimeStretchThreshold;
                if (useTimeStretch != _usingTimeStretch)
                {
                    ResetProcessingState();
                    _usingTimeStretch = useTimeStretch;
                }

                var requestedFrames = count / MixChannels;
                var framesWritten = 0;

                while (framesWritten < requestedFrames)
                {
                    if (IsAtOrPastLoopBoundary(reader))
                    {
                        if (!JumpToLoopStart(reader))
                        {
                            CompleteTrack();
                            break;
                        }

                        source = _source;
                        if (source == null)
                            break;
                    }

                    var remainingFrames = requestedFrames - framesWritten;
                    var maxFramesThisPass = GetOutputFramesUntilLoop(reader, speed, remainingFrames);
                    if (maxFramesThisPass <= 0)
                    {
                        if (JumpToLoopStart(reader))
                            continue;

                        CompleteTrack();
                        break;
                    }

                    var requestedSamples = maxFramesThisPass * MixChannels;
                    var samplesRead = useTimeStretch
                        ? ReadTimeStretchSegment(source, reader, buffer, offset + framesWritten * MixChannels, requestedSamples, speed)
                        : ReadDirectSegment(source, buffer, offset + framesWritten * MixChannels, requestedSamples);

                    if (samplesRead <= 0)
                    {
                        if (JumpToLoopStart(reader))
                        {
                            source = _source;
                            if (source == null)
                                break;

                            continue;
                        }

                        CompleteTrack();
                        break;
                    }

                    framesWritten += samplesRead / MixChannels;
                }

                var vol = (float)Math.Clamp(
                    _engine.GetDeckOutputLevel(_deck, _isDeckA)
                    * (_engine.MasterVolume / 100.0), 0, 2);

                for (int i = 0; i < count; i++)
                    buffer[offset + i] *= vol;

                return count;
            }

            private int ReadDirectSegment(ISampleProvider source, float[] buffer, int offset, int requestedSamples)
            {
                try
                {
                    var read = source.Read(buffer, offset, requestedSamples);
                    if (read > 0)
                    {
                        var playbackSeconds = GetPlaybackSeconds() + ((read / (double)MixChannels) / MixRate);
                        SyncPlaybackCursor(playbackSeconds);
                    }

                    return read;
                }
                catch
                {
                    return 0;
                }
            }

            private int ReadTimeStretchSegment(ISampleProvider source, AudioFileReader reader, float[] buffer, int offset, int requestedSamples, double speed)
            {
                var processor = GetOrCreateTimeStretch(speed);
                var outputFrames = requestedSamples / MixChannels;
                var framesWritten = 0;

                if (_processorInputBuffer == null || _processorInputBuffer.Length < ProcessorChunkFrames * MixChannels)
                    _processorInputBuffer = new float[ProcessorChunkFrames * MixChannels];

                while (framesWritten < outputFrames)
                {
                    var remainingFrames = outputFrames - framesWritten;
                    if (processor.AvailableSamples > 0)
                    {
                        var receiveSpan = new Span<float>(buffer, offset + framesWritten * MixChannels, remainingFrames * MixChannels);
                        var receivedFrames = processor.ReceiveSamples(ref receiveSpan, remainingFrames);
                        if (receivedFrames > 0)
                        {
                            framesWritten += receivedFrames;
                            continue;
                        }
                    }

                    if (_sourceExhausted)
                        break;

                    var inputFramesUntilLoop = GetInputFramesUntilLoop(reader);
                    if (inputFramesUntilLoop == 0)
                        break;

                    var inputFramesRequested = Math.Min(ProcessorChunkFrames, inputFramesUntilLoop);
                    int sourceRead;
                    try
                    {
                        sourceRead = source.Read(_processorInputBuffer, 0, inputFramesRequested * MixChannels);
                    }
                    catch
                    {
                        return 0;
                    }

                    if (sourceRead > 0)
                    {
                        var inputFrames = sourceRead / MixChannels;
                        var inputSpan = new ReadOnlySpan<float>(_processorInputBuffer, 0, sourceRead);
                        processor.PutSamples(ref inputSpan, inputFrames);
                        continue;
                    }

                    _sourceExhausted = true;
                    processor.Flush();
                }

                if (framesWritten > 0)
                {
                    var playbackSeconds = GetPlaybackSeconds() + ((framesWritten / (double)MixRate) * speed);
                    SyncPlaybackCursor(playbackSeconds);
                }

                return framesWritten * MixChannels;
            }

            private int GetOutputFramesUntilLoop(AudioFileReader reader, double speed, int remainingFrames)
            {
                if (!TryGetLoopRangeSeconds(reader, out _, out var loopEndSeconds))
                    return remainingFrames;

                var playbackSeconds = Math.Max(GetPlaybackSeconds(), reader.CurrentTime.TotalSeconds);
                var secondsRemaining = Math.Max(0, loopEndSeconds - playbackSeconds);
                if (secondsRemaining <= LoopBoundaryToleranceSeconds)
                    return 0;

                var framesUntilLoop = (int)Math.Floor(secondsRemaining / Math.Max(speed, 0.001) * MixRate);
                if (framesUntilLoop <= 0)
                    return 1;

                return Math.Min(remainingFrames, framesUntilLoop);
            }

            private int GetInputFramesUntilLoop(AudioFileReader reader)
            {
                if (!TryGetLoopRangeSeconds(reader, out _, out var loopEndSeconds))
                    return ProcessorChunkFrames;

                var secondsRemaining = Math.Max(0, loopEndSeconds - reader.CurrentTime.TotalSeconds);
                if (secondsRemaining <= LoopBoundaryToleranceSeconds)
                    return 0;

                return Math.Max(1, (int)Math.Floor(secondsRemaining * MixRate));
            }

            private bool IsAtOrPastLoopBoundary(AudioFileReader reader)
            {
                if (!TryGetLoopRangeSeconds(reader, out _, out var loopEndSeconds))
                    return false;

                var playbackSeconds = Math.Max(GetPlaybackSeconds(), reader.CurrentTime.TotalSeconds);
                return playbackSeconds >= loopEndSeconds - LoopBoundaryToleranceSeconds;
            }

            private bool JumpToLoopStart(AudioFileReader reader)
            {
                if (!TryGetLoopRangeSeconds(reader, out var loopStartSeconds, out _))
                    return false;

                reader.CurrentTime = TimeSpan.FromSeconds(loopStartSeconds);
                SyncPlaybackPosition(loopStartSeconds);
                return true;
            }

            private bool TryGetLoopRangeSeconds(AudioFileReader reader, out double loopStartSeconds, out double loopEndSeconds)
            {
                loopStartSeconds = 0;
                loopEndSeconds = 0;

                if (!_deck.LoopActive || !_deck.LoopStart.HasValue || !_deck.LoopEnd.HasValue)
                    return false;

                var totalSeconds = reader.TotalTime.TotalSeconds;
                if (totalSeconds <= 0)
                    return false;

                loopStartSeconds = Math.Clamp(_deck.LoopStart.Value / 100.0 * totalSeconds, 0, totalSeconds);
                loopEndSeconds = Math.Clamp(_deck.LoopEnd.Value / 100.0 * totalSeconds, 0, totalSeconds);
                return loopEndSeconds > loopStartSeconds + LoopBoundaryToleranceSeconds;
            }

            private void CompleteTrack()
            {
                _deck.IsPlaying = false;
                _deck.IsCued = false;
                _sourceExhausted = false;
            }

            private void SyncPlaybackCursor(double seconds)
            {
                Interlocked.Exchange(ref _playbackSeconds, Math.Max(0, seconds));
            }
        }

        /* ── Recording tap — passes audio through and optionally writes to file ── */
        private sealed class RecordingTap : ISampleProvider
        {
            private readonly ISampleProvider _source;
            private readonly Func<string> _effectModeProvider;
            private volatile WaveFileWriter? _writer;
            private float[] _echoBuffer = new float[MixRate * 2];
            private int _echoIndex;
            private float _filterL;
            private float _filterR;
            private float _washL;
            private float _washR;

            public WaveFormat WaveFormat => _source.WaveFormat;
            public bool IsRecording => _writer != null;
            public string? RecordingPath { get; private set; }

            public RecordingTap(ISampleProvider source, Func<string> effectModeProvider)
            {
                _source = source;
                _effectModeProvider = effectModeProvider;
            }

            public void Start(string path)
            {
                Stop();
                RecordingPath = path;
                _writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(
                    _source.WaveFormat.SampleRate, _source.WaveFormat.Channels));
            }

            public void Stop()
            {
                var w = _writer;
                _writer = null;
                RecordingPath = null;
                try { w?.Dispose(); } catch { }
            }

            public int Read(float[] buffer, int offset, int count)
            {
                int read = _source.Read(buffer, offset, count);
                ApplyEffect(buffer, offset, read);
                try { _writer?.WriteSamples(buffer, offset, read); } catch { }
                return read;
            }

            private void ApplyEffect(float[] buffer, int offset, int count)
            {
                var effect = (_effectModeProvider.Invoke() ?? string.Empty).Trim().ToLowerInvariant();
                if (count <= 0)
                    return;

                switch (effect)
                {
                    case "echo":
                        ApplyEcho(buffer, offset, count, 0.34f, 0.28f);
                        break;
                    case "filter":
                        ApplyFilter(buffer, offset, count);
                        break;
                    case "wash":
                        ApplyWash(buffer, offset, count);
                        break;
                    case "punch":
                    default:
                        ApplyPunch(buffer, offset, count);
                        break;
                }
            }

            private void ApplyPunch(float[] buffer, int offset, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    var sample = buffer[offset + i] * 1.18f;
                    buffer[offset + i] = (float)Math.Tanh(sample * 1.1f);
                }
            }

            private void ApplyEcho(float[] buffer, int offset, int count, float feedback, float wet)
            {
                for (int i = 0; i < count; i += 2)
                {
                    var delayedL = _echoBuffer[_echoIndex];
                    var delayedR = _echoBuffer[_echoIndex + 1];
                    var inputL = buffer[offset + i];
                    var inputR = i + 1 < count ? buffer[offset + i + 1] : inputL;

                    buffer[offset + i] = inputL + delayedL * wet;
                    if (i + 1 < count)
                        buffer[offset + i + 1] = inputR + delayedR * wet;

                    _echoBuffer[_echoIndex] = inputL + delayedL * feedback;
                    _echoBuffer[_echoIndex + 1] = inputR + delayedR * feedback;
                    _echoIndex += 2;
                    if (_echoIndex >= _echoBuffer.Length)
                        _echoIndex = 0;
                }
            }

            private void ApplyFilter(float[] buffer, int offset, int count)
            {
                const float alpha = 0.18f;
                for (int i = 0; i < count; i += 2)
                {
                    var inL = buffer[offset + i];
                    var inR = i + 1 < count ? buffer[offset + i + 1] : inL;
                    _filterL += alpha * (inL - _filterL);
                    _filterR += alpha * (inR - _filterR);
                    buffer[offset + i] = _filterL;
                    if (i + 1 < count)
                        buffer[offset + i + 1] = _filterR;
                }
            }

            private void ApplyWash(float[] buffer, int offset, int count)
            {
                ApplyEcho(buffer, offset, count, 0.48f, 0.22f);
                const float alpha = 0.08f;
                for (int i = 0; i < count; i += 2)
                {
                    var inL = buffer[offset + i];
                    var inR = i + 1 < count ? buffer[offset + i + 1] : inL;
                    _washL += alpha * (inL - _washL);
                    _washR += alpha * (inR - _washR);
                    buffer[offset + i] = inL * 0.72f + _washL * 0.28f;
                    if (i + 1 < count)
                        buffer[offset + i + 1] = inR * 0.72f + _washR * 0.28f;
                }
            }
        }

        /* ── One-shot sample provider that disposes the reader when done ── */
        private sealed class AutoDisposeSampleProvider : ISampleProvider
        {
            private readonly ISampleProvider _source;
            private readonly IDisposable _disposable;
            private bool _finished;

            public WaveFormat WaveFormat => _source.WaveFormat;

            public AutoDisposeSampleProvider(ISampleProvider source, IDisposable disposable)
            {
                _source = source;
                _disposable = disposable;
            }

            public int Read(float[] buffer, int offset, int count)
            {
                if (_finished) return 0;
                int read = _source.Read(buffer, offset, count);
                if (read == 0)
                {
                    _finished = true;
                    ThreadPool.QueueUserWorkItem(_ => { try { _disposable.Dispose(); } catch { } });
                }
                return read;
            }
        }

        /* ── Multi-channel → stereo fold-down (stays in IEEE-float) ── */
        private sealed class StereoFoldDownProvider : ISampleProvider
        {
            private readonly ISampleProvider _source;
            private readonly int _ch;
            private float[]? _buf;

            public WaveFormat WaveFormat { get; }

            public StereoFoldDownProvider(ISampleProvider source)
            {
                _source = source;
                _ch = source.WaveFormat.Channels;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
            }

            public int Read(float[] buffer, int offset, int count)
            {
                int frames = count / 2;
                int srcCount = frames * _ch;
                if (_buf == null || _buf.Length < srcCount)
                    _buf = new float[srcCount];

                int read = _source.Read(_buf, 0, srcCount);
                int framesRead = read / _ch;

                for (int i = 0; i < framesRead; i++)
                {
                    int s = i * _ch, d = offset + i * 2;
                    buffer[d]     = _buf[s];
                    buffer[d + 1] = _buf[s + 1];
                }

                return framesRead * 2;
            }
        }
    }
}
