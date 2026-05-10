using System;
using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Provides optional audio cues for wake word detection.
    /// Plays a short, non-intrusive beep when wake word is detected.
    /// </summary>
    public class WakeWordAudioCues : IDisposable
    {
        private static WakeWordAudioCues? _instance;
        private static readonly object _lock = new object();

        public static WakeWordAudioCues Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new WakeWordAudioCues();
                    }
                }
                return _instance;
            }
        }

        private WaveOutEvent? _waveOut;
        private bool _enabled = false;
        private bool _isDisposed = false;

        private WakeWordAudioCues()
        {
            // Initialize on demand
        }

        /// <summary>
        /// Enable or disable audio cues
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        /// <summary>
        /// Play a short, satisfying "pop" acknowledgment sound.
        /// Two-tone (high→low) with fast attack and smooth decay.
        /// </summary>
        public void PlayAcknowledgment()
        {
            if (!_enabled || _isDisposed) return;

            try
            {
                var sampleRate = 44100;
                var durationSec = 0.09; // 90ms — snappy pop
                var samples = (int)(sampleRate * durationSec);
                var buffer = new float[samples];
                var amplitude = 0.35f;

                for (int i = 0; i < samples; i++)
                {
                    double t = (double)i / sampleRate;
                    // Frequency glide from 1400 Hz → 900 Hz gives a satisfying "pop" feel
                    double freq = 1400.0 - (500.0 * (t / durationSec));
                    double envelope;
                    double pos = (double)i / samples;
                    if (pos < 0.05)
                        envelope = pos / 0.05;          // 5% attack
                    else
                        envelope = Math.Pow(1.0 - ((pos - 0.05) / 0.95), 2.0); // quadratic decay
                    buffer[i] = (float)(amplitude * envelope * Math.Sin(2.0 * Math.PI * freq * t));
                }

                var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
                var provider = new NAudio.Wave.RawSourceWaveStream(
                    new System.IO.MemoryStream(BufferToBytes(buffer)), waveFormat);

                _waveOut?.Dispose();
                _waveOut = new WaveOutEvent();
                _waveOut.Init(provider);
                _waveOut.Play();

                Debug.WriteLine("[WakeWordAudioCues] Played pop acknowledgment");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWordAudioCues] Failed to play pop: {ex.Message}");
            }
        }

        private static byte[] BufferToBytes(float[] buffer)
        {
            var bytes = new byte[buffer.Length * 4];
            Buffer.BlockCopy(buffer, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        /// <summary>
        /// Play a short error tone (100ms, 400Hz)
        /// </summary>
        public void PlayError()
        {
            if (!_enabled || _isDisposed) return;

            try
            {
                var sampleRate = 44100;
                var frequency = 400; // Lower frequency for error
                var duration = 0.15; // Slightly longer
                var amplitude = 0.2f;

                var signalGenerator = new SignalGenerator(sampleRate, 1)
                {
                    Gain = amplitude,
                    Frequency = frequency,
                    Type = SignalGeneratorType.Sin
                };

                var fadeIn = new FadeInOutSampleProvider(signalGenerator.Take(TimeSpan.FromSeconds(duration)));
                fadeIn.BeginFadeIn(30);
                fadeIn.BeginFadeOut(30);

                _waveOut?.Dispose();
                _waveOut = new WaveOutEvent();
                _waveOut.Init(fadeIn);
                _waveOut.Play();

                Debug.WriteLine("[WakeWordAudioCues] Played error tone");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWordAudioCues] Failed to play error tone: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                _waveOut?.Stop();
                _waveOut?.Dispose();
                _waveOut = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WakeWordAudioCues] Dispose error: {ex.Message}");
            }
        }
    }
}
