using System;
using NAudio.Wave;

namespace AtlasAI.Voice
{
    /// <summary>
    /// Allocation-free RMS metering sample provider.
    /// Wraps an underlying ISampleProvider and computes RMS over a sliding window.
    /// Calls onRms callback when each window completes.
    /// </summary>
    public class RmsMeteringSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly Action<float> _onRms;
        private readonly int _windowSize;
        
        // Pre-allocated accumulator - no per-read allocations
        private double _sumOfSquares;
        private int _sampleCount;
        
        /// <summary>
        /// Create a metering sample provider
        /// </summary>
        /// <param name="source">Source sample provider to wrap</param>
        /// <param name="onRms">Callback invoked with RMS value (0..1+) when window completes</param>
        /// <param name="windowSize">Number of samples per RMS window (default 1024, ~23ms at 44.1kHz)</param>
        public RmsMeteringSampleProvider(ISampleProvider source, Action<float> onRms, int windowSize = 1024)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _onRms = onRms ?? throw new ArgumentNullException(nameof(onRms));
            _windowSize = Math.Max(64, windowSize);
            
            _sumOfSquares = 0;
            _sampleCount = 0;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        /// <summary>
        /// Read samples from source and compute RMS.
        /// Zero allocations per call - reuses accumulator.
        /// </summary>
        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = _source.Read(buffer, offset, count);
            
            if (samplesRead > 0)
            {
                // Process all samples (handles mono and stereo)
                for (int i = 0; i < samplesRead; i++)
                {
                    float sample = buffer[offset + i];
                    _sumOfSquares += sample * sample;
                    _sampleCount++;
                    
                    // Window complete - compute and report RMS
                    if (_sampleCount >= _windowSize)
                    {
                        float rms = (float)Math.Sqrt(_sumOfSquares / _sampleCount);
                        
                        // Reset for next window
                        _sumOfSquares = 0;
                        _sampleCount = 0;
                        
                        // Invoke callback (typically ~10-20ms intervals)
                        try
                        {
                            _onRms(rms);
                        }
                        catch
                        {
                            // Don't let callback errors break audio playback
                        }
                    }
                }
            }
            
            return samplesRead;
        }

        /// <summary>
        /// Reset the accumulator (call when starting new playback)
        /// </summary>
        public void Reset()
        {
            _sumOfSquares = 0;
            _sampleCount = 0;
        }
    }
}
