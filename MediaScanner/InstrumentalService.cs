using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Dsp;
using NAudio.Wave;

namespace AtlasAI.MediaScanner
{
    public sealed class InstrumentalService
    {
        public static InstrumentalService Instance { get; } = new InstrumentalService();

        private readonly string _cacheDir;
        private readonly object _lock = new();

        private InstrumentalService()
        {
            _cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasAI", "Instrumentals");
            try { Directory.CreateDirectory(_cacheDir); } catch { }
        }

        public string GetCachePath(string sourcePath)
        {
            var p = (sourcePath ?? "").Trim();
            long ticks = 0;
            try { if (File.Exists(p)) ticks = File.GetLastWriteTimeUtc(p).Ticks; } catch { }
            var key = $"{p}|{ticks}";

            using var sha1 = SHA1.Create();
            var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(key));
            var hex = BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            return Path.Combine(_cacheDir, $"{hex}.wav");
        }

        public async Task<string?> GetOrCreateInstrumentalAsync(string sourcePath, CancellationToken ct)
        {
            var p = (sourcePath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(p) || !File.Exists(p)) return null;

            var dest = GetCachePath(p);
            if (File.Exists(dest)) return dest;

            lock (_lock)
            {
                if (File.Exists(dest)) return dest;
            }

            try
            {
                await Task.Run(() => GenerateCenterCancelWav(p, dest, ct), ct).ConfigureAwait(false);
                return File.Exists(dest) ? dest : null;
            }
            catch
            {
                try { if (File.Exists(dest)) File.Delete(dest); } catch { }
                return null;
            }
        }

        private static void GenerateCenterCancelWav(string sourcePath, string destPath, CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            using var reader = new AudioFileReader(sourcePath);
            var inFormat = reader.WaveFormat;

            var outFormat = WaveFormat.CreateIeeeFloatWaveFormat(inFormat.SampleRate, 2);
            using var writer = new WaveFileWriter(destPath, outFormat);

            var sampleRate = inFormat.SampleRate;
            var cutoffHz = 180f;
            var lowL = BiQuadFilter.LowPassFilter(sampleRate, cutoffHz, 0.707f);
            var lowR = BiQuadFilter.LowPassFilter(sampleRate, cutoffHz, 0.707f);
            var highL = BiQuadFilter.HighPassFilter(sampleRate, cutoffHz, 0.707f);
            var highR = BiQuadFilter.HighPassFilter(sampleRate, cutoffHz, 0.707f);

            var buffer = new float[reader.WaveFormat.SampleRate * inFormat.Channels];
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var read = reader.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;

                if (inFormat.Channels == 1)
                {
                    for (var i = 0; i < read; i++)
                    {
                        var s = buffer[i];
                        writer.WriteSample(s);
                        writer.WriteSample(s);
                    }
                    continue;
                }

                var frames = read / inFormat.Channels;
                var idx = 0;
                for (var f = 0; f < frames; f++)
                {
                    var l = buffer[idx++];
                    var r = buffer[idx++];
                    var mid = (l + r) * 0.5f;
                    var side = (l - r) * 0.5f;

                    var lowMidL = lowL.Transform(mid);
                    var lowMidR = lowR.Transform(mid);
                    var highSideL = highL.Transform(side);
                    var highSideR = highR.Transform(side);

                    var outL = lowMidL + highSideL;
                    var outR = lowMidR - highSideR;

                    if (outL > 1f) outL = 1f;
                    else if (outL < -1f) outL = -1f;
                    if (outR > 1f) outR = 1f;
                    else if (outR < -1f) outR = -1f;

                    writer.WriteSample(outL);
                    writer.WriteSample(outR);
                }
            }

            writer.Flush();
        }
    }
}
