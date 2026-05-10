using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AtlasAI.Services
{
    public sealed class HardwareMetricsService : IDisposable
    {
        private PerformanceCounter? _cpu;
        private PerformanceCounter? _ram;
        private PerformanceCounter? _disk;

        private DateTime _lastGpuRefreshUtc = DateTime.MinValue;
        private List<PerformanceCounter>? _gpuCounters;

        public HardwareMetricsService()
        {
            try { _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total"); _cpu.NextValue(); } catch { }
            try { _ram = new PerformanceCounter("Memory", "% Committed Bytes In Use"); _ram.NextValue(); } catch { }
            try { _disk = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total"); _disk.NextValue(); } catch { }
        }

        public MetricsSnapshot GetSnapshot()
        {
            var cpu = ReadCounter(_cpu);
            var ram = ReadCounter(_ram);
            var disk = ReadCounter(_disk);
            var gpu = ReadGpu();

            cpu = Clamp0To100(cpu);
            ram = Clamp0To100(ram);
            disk = Clamp0To100(disk);
            gpu = Clamp0To100(gpu);

            return new MetricsSnapshot(cpu, gpu, ram, disk);
        }

        private static double ReadCounter(PerformanceCounter? counter)
        {
            try
            {
                if (counter == null) return 0;
                return counter.NextValue();
            }
            catch
            {
                return 0;
            }
        }

        private double ReadGpu()
        {
            try
            {
                var now = DateTime.UtcNow;
                if (_gpuCounters == null || (now - _lastGpuRefreshUtc).TotalSeconds > 10)
                {
                    _gpuCounters = BuildGpuCounters();
                    _lastGpuRefreshUtc = now;
                }

                if (_gpuCounters == null || _gpuCounters.Count == 0) return 0;

                double total = 0;
                foreach (var c in _gpuCounters)
                {
                    try { total += c.NextValue(); } catch { }
                }

                return total;
            }
            catch
            {
                return 0;
            }
        }

        private static List<PerformanceCounter> BuildGpuCounters()
        {
            var counters = new List<PerformanceCounter>();
            try
            {
                if (!PerformanceCounterCategory.Exists("GPU Engine"))
                    return counters;

                var cat = new PerformanceCounterCategory("GPU Engine");
                var instances = cat.GetInstanceNames();
                foreach (var inst in instances)
                {
                    if (inst == null) continue;
                    if (inst.IndexOf("engtype_3d", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    try
                    {
                        var pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst);
                        pc.NextValue();
                        counters.Add(pc);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return counters;
        }

        private static double Clamp0To100(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            if (value < 0) return 0;
            if (value > 100) return 100;
            return value;
        }

        public void Dispose()
        {
            try { _cpu?.Dispose(); } catch { }
            try { _ram?.Dispose(); } catch { }
            try { _disk?.Dispose(); } catch { }
            _cpu = null;
            _ram = null;
            _disk = null;

            try
            {
                if (_gpuCounters != null)
                {
                    foreach (var c in _gpuCounters)
                    {
                        try { c.Dispose(); } catch { }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                _gpuCounters = null;
            }
        }

        public readonly record struct MetricsSnapshot(double Cpu, double Gpu, double Ram, double Disk);
    }
}

