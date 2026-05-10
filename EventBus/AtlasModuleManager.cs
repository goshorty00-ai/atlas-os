using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace AtlasAI.EventBus
{
    /// <summary>
    /// Manages Atlas modules and their lifecycle.
    /// </summary>
    public sealed class AtlasModuleManager : IDisposable
    {
        private static AtlasModuleManager? _instance;
        private static readonly object _lock = new();

        public static AtlasModuleManager Instance
        {
            get
            {
                lock (_lock) { return _instance ??= new AtlasModuleManager(); }
            }
        }

        private readonly ConcurrentDictionary<string, IAtlasModule> _modules = new();
        private readonly AtlasEventBus _eventBus;
        private bool _disposed;

        private AtlasModuleManager()
        {
            _eventBus = AtlasEventBus.Instance;
        }

        public void RegisterModule(IAtlasModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (_modules.ContainsKey(module.ModuleId))
                throw new InvalidOperationException($"Module {module.ModuleId} already registered");

            _modules[module.ModuleId] = module;
            module.Initialize(_eventBus);
            Log($"Registered module: {module.ModuleName}");
        }

        public void UnregisterModule(string moduleId)
        {
            if (_modules.TryRemove(moduleId, out var module))
            {
                module.Stop();
                module.Dispose();
                Log($"Unregistered module: {module.ModuleName}");
            }
        }

        public void StartAll()
        {
            _eventBus.Start();
            foreach (var module in _modules.Values)
            {
                try { module.Start(); }
                catch (Exception ex) { Log($"Error starting {module.ModuleName}: {ex.Message}"); }
            }
            Log("All modules started");
        }

        public void StopAll()
        {
            foreach (var module in _modules.Values)
            {
                try { module.Stop(); }
                catch (Exception ex) { Log($"Error stopping {module.ModuleName}: {ex.Message}"); }
            }
            _eventBus.Stop();
            Log("All modules stopped");
        }

        public IAtlasModule? GetModule(string moduleId)
        {
            return _modules.TryGetValue(moduleId, out var module) ? module : null;
        }

        public IEnumerable<IAtlasModule> GetAllModules() => _modules.Values;

        public Dictionary<string, ModuleStatus> GetModuleStatuses()
        {
            return _modules.ToDictionary(kv => kv.Key, kv => kv.Value.GetStatus());
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopAll();
            foreach (var module in _modules.Values)
            {
                try { module.Dispose(); } catch { }
            }
            _modules.Clear();
        }

        private void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[ModuleManager] {message}");
        }
    }
}
