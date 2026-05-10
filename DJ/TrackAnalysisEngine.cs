using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;

namespace AtlasAI.DJ
{
    internal static class TrackAnalysisEngine
    {
        private const int WaveformBins = 1800;
        private const double AnalysisWindowSeconds = 0.008;

        public static TrackAnalysis Analyze(string path)
        {
            var analysis = new TrackAnalysis();

            try
            {
                using var reader = new AudioFileReader(path);
                var sampleRate = reader.WaveFormat.SampleRate;
                var channels = Math.Max(1, reader.WaveFormat.Channels);
                var durationSeconds = Math.Max(0.001, reader.TotalTime.TotalSeconds);
                var totalFrames = Math.Max(1L, (long)Math.Round(durationSeconds * sampleRate));

                var waveformMin = Enumerable.Repeat(1d, WaveformBins).ToArray();
                var waveformMax = Enumerable.Repeat(-1d, WaveformBins).ToArray();
                var waveformSquare = new double[WaveformBins];
                var waveformCounts = new int[WaveformBins];

                var onsetWindowFrames = Math.Max(256, (int)Math.Round(sampleRate * AnalysisWindowSeconds));
                var onsetSquares = new List<double>(4096);
                var buffer = new float[Math.Max(sampleRate * channels, 8192)];
                var onsetAccumulator = 0d;
                var onsetFrameCount = 0;
                var frameIndex = 0L;

                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (var index = 0; index < read; index += channels)
                    {
                        double mono = 0;
                        for (var channel = 0; channel < channels && index + channel < read; channel++)
                            mono += buffer[index + channel];

                        mono /= channels;

                        var waveformIndex = (int)Math.Clamp(frameIndex * WaveformBins / totalFrames, 0, WaveformBins - 1);
                        waveformMin[waveformIndex] = Math.Min(waveformMin[waveformIndex], mono);
                        waveformMax[waveformIndex] = Math.Max(waveformMax[waveformIndex], mono);
                        waveformSquare[waveformIndex] += mono * mono;
                        waveformCounts[waveformIndex]++;

                        onsetAccumulator += mono * mono;
                        onsetFrameCount++;
                        if (onsetFrameCount >= onsetWindowFrames)
                        {
                            onsetSquares.Add(onsetAccumulator / onsetFrameCount);
                            onsetAccumulator = 0;
                            onsetFrameCount = 0;
                        }

                        frameIndex++;
                    }
                }

                if (onsetFrameCount > 0)
                    onsetSquares.Add(onsetAccumulator / Math.Max(1, onsetFrameCount));

                NormalizeWaveform(waveformMin, waveformMax, waveformSquare, waveformCounts, analysis);
                PopulateBeatgrid(onsetSquares.ToArray(), durationSeconds, analysis);
            }
            catch
            {
                return analysis;
            }

            return analysis;
        }

        private static void NormalizeWaveform(double[] waveformMin, double[] waveformMax, double[] waveformSquare, int[] waveformCounts, TrackAnalysis analysis)
        {
            double maxAbs = 0;
            for (var index = 0; index < waveformMin.Length; index++)
            {
                if (waveformCounts[index] == 0)
                {
                    waveformMin[index] = 0;
                    waveformMax[index] = 0;
                    continue;
                }

                maxAbs = Math.Max(maxAbs, Math.Abs(waveformMin[index]));
                maxAbs = Math.Max(maxAbs, Math.Abs(waveformMax[index]));
            }

            var scale = maxAbs > 0 ? 1d / maxAbs : 1d;

            for (var index = 0; index < waveformMin.Length; index++)
            {
                var rms = waveformCounts[index] > 0
                    ? Math.Sqrt(waveformSquare[index] / waveformCounts[index])
                    : 0;

                analysis.WaveformMin.Add(Math.Clamp(waveformMin[index] * scale, -1, 1));
                analysis.WaveformMax.Add(Math.Clamp(waveformMax[index] * scale, -1, 1));
                analysis.WaveformRms.Add(Math.Clamp(rms * scale, 0, 1));
            }
        }

        private static void PopulateBeatgrid(double[] energy, double durationSeconds, TrackAnalysis analysis)
        {
            if (energy.Length < 128)
                return;

            var onset = new double[energy.Length - 1];
            for (var index = 0; index < onset.Length; index++)
                onset[index] = Math.Max(0, energy[index + 1] - energy[index]);

            var novelty = BuildNoveltyCurve(onset);

            var minLag = (int)(60.0 / (175.0 * AnalysisWindowSeconds));
            var maxLag = Math.Min((int)(60.0 / (80.0 * AnalysisWindowSeconds)), novelty.Length / 2);
            if (minLag >= maxLag)
                return;

            var correlation = new double[maxLag - minLag + 1];
            double maxCorrelation = 0;
            for (var lag = minLag; lag <= maxLag; lag++)
            {
                double sum = 0;
                var limit = Math.Min(novelty.Length - lag, 4000);
                for (var index = 0; index < limit; index++)
                    sum += novelty[index] * novelty[index + lag];

                correlation[lag - minLag] = sum;
                maxCorrelation = Math.Max(maxCorrelation, sum);
            }

            if (maxCorrelation <= 0)
                return;

            var bestLag = minLag;
            var bestScore = 0d;
            for (var lag = minLag + 1; lag < maxLag; lag++)
            {
                var local = correlation[lag - minLag];
                if (local < correlation[lag - minLag - 1] || local < correlation[lag - minLag + 1])
                    continue;

                var baseScore = correlation[lag - minLag] / maxCorrelation;
                var doubleIdx = lag * 2 - minLag;
                var halfIdx = lag / 2 - minLag;
                var tripleIdx = lag * 3 - minLag;
                if (doubleIdx >= 0 && doubleIdx < correlation.Length)
                    baseScore += (correlation[doubleIdx] / maxCorrelation) * 0.25;
                if (halfIdx >= 0 && halfIdx < correlation.Length)
                    baseScore += (correlation[halfIdx] / maxCorrelation) * 0.25;
                if (tripleIdx >= 0 && tripleIdx < correlation.Length)
                    baseScore += (correlation[tripleIdx] / maxCorrelation) * 0.12;

                var bpmCandidate = 60.0 / (lag * AnalysisWindowSeconds);
                while (bpmCandidate < 80) bpmCandidate *= 2;
                while (bpmCandidate > 175) bpmCandidate /= 2;
                if (bpmCandidate >= 118 && bpmCandidate <= 132)
                    baseScore += 0.05;

                if (baseScore > bestScore)
                {
                    bestScore = baseScore;
                    bestLag = lag;
                }
            }

            var bpm = 60.0 / (bestLag * AnalysisWindowSeconds);
            while (bpm < 85) bpm *= 2;
            while (bpm > 175) bpm /= 2;

            var beatInterval = 60.0 / bpm;
            var peaks = CollectOnsetPeaks(novelty, beatInterval);
            if (peaks.Count == 0)
                return;

            var bestOffset = FindBestGridOffset(peaks, beatInterval);

            analysis.Bpm = (int)Math.Round(bpm);
            analysis.Confidence = Math.Clamp(bestScore / 1.5, 0, 1);
            analysis.BeatIntervalSeconds = beatInterval;
            analysis.GridOffsetSeconds = bestOffset;

            var snappedMarkers = BuildBeatMarkers(peaks, bestOffset, beatInterval, durationSeconds);
            for (var beatIndex = 0; beatIndex < snappedMarkers.Count; beatIndex++)
            {
                var marker = snappedMarkers[beatIndex];
                analysis.BeatMarkers.Add(marker);
                if (beatIndex % 16 == 0)
                    analysis.PhraseMarkers.Add(marker);
            }
        }

        private static double[] BuildNoveltyCurve(double[] onset)
        {
            if (onset.Length == 0)
                return Array.Empty<double>();

            var novelty = new double[onset.Length];
            for (var index = 0; index < onset.Length; index++)
            {
                var start = Math.Max(0, index - 8);
                var end = Math.Min(onset.Length - 1, index + 8);
                double localMean = 0;
                for (var cursor = start; cursor <= end; cursor++)
                    localMean += onset[cursor];

                localMean /= Math.Max(1, end - start + 1);
                novelty[index] = Math.Max(0, onset[index] - localMean * 0.82);
            }

            var max = novelty.Max();
            if (max > 0)
            {
                for (var index = 0; index < novelty.Length; index++)
                    novelty[index] /= max;
            }

            return novelty;
        }

        private static List<(double Time, double Weight)> CollectOnsetPeaks(double[] onset, double beatInterval)
        {
            var peaks = new List<(double Time, double Weight)>();
            if (onset.Length < 8)
                return peaks;

            var maxOnset = onset.Max();
            if (maxOnset <= 0)
                return peaks;

            var minDistance = Math.Max(1, (int)Math.Round((beatInterval / 4) / AnalysisWindowSeconds));
            var lastPeak = -minDistance;

            for (var index = 2; index < onset.Length - 2; index++)
            {
                var value = onset[index];
                if (value < maxOnset * 0.22)
                    continue;
                if (value < onset[index - 1] || value < onset[index + 1])
                    continue;
                if (index - lastPeak < minDistance && peaks.Count > 0 && peaks[^1].Weight >= value)
                    continue;

                if (index - lastPeak < minDistance && peaks.Count > 0)
                    peaks.RemoveAt(peaks.Count - 1);

                peaks.Add((index * AnalysisWindowSeconds, value));
                lastPeak = index;
            }

            return peaks;
        }

        private static double FindBestGridOffset(List<(double Time, double Weight)> peaks, double beatInterval)
        {
            var candidates = new HashSet<double>();
            foreach (var peak in peaks.OrderByDescending(peak => peak.Weight).Take(64))
            {
                var baseOffset = Mod(peak.Time, beatInterval);
                candidates.Add(baseOffset);
                candidates.Add(Mod(baseOffset + beatInterval * 0.25, beatInterval));
                candidates.Add(Mod(baseOffset + beatInterval * 0.5, beatInterval));
                candidates.Add(Mod(baseOffset + beatInterval * 0.75, beatInterval));
            }

            var bestOffset = 0d;
            var bestScore = double.MinValue;
            foreach (var candidate in candidates)
            {
                double score = 0;
                foreach (var peak in peaks)
                {
                    var nearest = Math.Round((peak.Time - candidate) / beatInterval);
                    var snapped = candidate + nearest * beatInterval;
                    var distance = Math.Abs(peak.Time - snapped);
                    var tolerance = Math.Max(0.035, beatInterval * 0.18);
                    if (distance <= tolerance)
                        score += peak.Weight * (1 - distance / tolerance);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestOffset = candidate;
                }
            }

            return bestOffset;
        }

        private static List<double> BuildBeatMarkers(List<(double Time, double Weight)> peaks, double offset, double beatInterval, double durationSeconds)
        {
            var markers = new List<double>();
            var tolerance = Math.Max(0.03, beatInterval * 0.16);
            var sortedPeaks = peaks.OrderBy(peak => peak.Time).ToList();

            for (var marker = offset; marker < durationSeconds; marker += beatInterval)
            {
                var snapped = marker;
                var bestDistance = tolerance;

                foreach (var peak in sortedPeaks)
                {
                    var distance = Math.Abs(peak.Time - marker);
                    if (distance > bestDistance)
                        continue;

                    bestDistance = distance;
                    snapped = peak.Time;
                }

                if (markers.Count == 0 || Math.Abs(snapped - markers[^1]) >= beatInterval * 0.4)
                    markers.Add(Math.Clamp(snapped, 0, durationSeconds));
            }

            return markers;
        }

        private static double Mod(double value, double modulus)
        {
            if (modulus <= 0)
                return 0;

            var result = value % modulus;
            return result < 0 ? result + modulus : result;
        }
    }
}