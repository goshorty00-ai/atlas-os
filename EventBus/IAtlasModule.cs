using System;
using System.Collections.Generic;

namespace AtlasAI.EventBus
{
    /// <summary>
    /// Interface for Atlas modules that can publish and subscribe to events.
    /// </summary>
    public interface IAtlasModule : IDisposable
    {
        /// <summary>
        /// Unique module identifier.
        /// </summary>
        string ModuleId { get; }

        /// <summary>
        /// Human-readable module name.
        /// </summary>
        string ModuleName { get; }

        /// <summary>
        /// Module version.
        /// </summary>
        string Version { get; }

        /// <summary>
        /// Initialize the module and register event subscriptions.
        /// </summary>
        void Initialize(AtlasEventBus eventBus);

        /// <summary>
        /// Start the module.
        /// </summary>
        void Start();

        /// <summary>
        /// Stop the module.
        /// </summary>
        void Stop();

        /// <summary>
        /// Get module status.
        /// </summary>
        ModuleStatus GetStatus();
    }

    public enum ModuleStatus
    {
        Uninitialized,
        Initialized,
        Running,
        Stopped,
        Error
    }

    /// <summary>
    /// Base class for Atlas modules with common event bus functionality.
    /// </summary>
    public abstract class AtlasModuleBase : IAtlasModule
    {
        protected AtlasEventBus? EventBus { get; private set; }
        protected List<IDisposable> Subscriptions { get; } = new();
        protected ModuleStatus Status { get; set; } = ModuleStatus.Uninitialized;

        public abstract string ModuleId { get; }
        public abstract string ModuleName { get; }
        public virtual string Version => "1.0.0";

        public virtual void Initialize(AtlasEventBus eventBus)
        {
            EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            RegisterEventHandlers();
            Status = ModuleStatus.Initialized;
            Log($"{ModuleName} initialized");
        }

        public virtual void Start()
        {
            if (Status != ModuleStatus.Initialized && Status != ModuleStatus.Stopped)
                throw new InvalidOperationException($"Cannot start module in {Status} state");

            OnStart();
            Status = ModuleStatus.Running;
            Log($"{ModuleName} started");
        }

        public virtual void Stop()
        {
            if (Status != ModuleStatus.Running)
                return;

            OnStop();
            Status = ModuleStatus.Stopped;
            Log($"{ModuleName} stopped");
        }

        public ModuleStatus GetStatus() => Status;

        public virtual void Dispose()
        {
            Stop();
            foreach (var sub in Subscriptions)
            {
                try { sub.Dispose(); } catch { }
            }
            Subscriptions.Clear();
            OnDispose();
        }

        /// <summary>
        /// Register event handlers. Override to subscribe to events.
        /// </summary>
        protected abstract void RegisterEventHandlers();

        /// <summary>
        /// Called when module starts. Override for custom start logic.
        /// </summary>
        protected virtual void OnStart() { }

        /// <summary>
        /// Called when module stops. Override for custom stop logic.
        /// </summary>
        protected virtual void OnStop() { }

        /// <summary>
        /// Called when module is disposed. Override for custom cleanup.
        /// </summary>
        protected virtual void OnDispose() { }

        /// <summary>
        /// Publish an event to the event bus.
        /// </summary>
        protected void Publish(AtlasEvent evt)
        {
            if (EventBus == null)
                throw new InvalidOperationException("Module not initialized");

            evt.Source = ModuleId;
            EventBus.Publish(evt);
        }

        /// <summary>
        /// Subscribe to an event type.
        /// </summary>
        protected void Subscribe<T>(Action<T> handler) where T : AtlasEvent
        {
            if (EventBus == null)
                throw new InvalidOperationException("Module not initialized");

            var subscription = EventBus.Subscribe(handler);
            Subscriptions.Add(subscription);
        }

        /// <summary>
        /// Subscribe to an event type with a filter.
        /// </summary>
        protected void Subscribe<T>(Action<T> handler, Func<T, bool> filter) where T : AtlasEvent
        {
            if (EventBus == null)
                throw new InvalidOperationException("Module not initialized");

            var subscription = EventBus.Subscribe(handler, filter);
            Subscriptions.Add(subscription);
        }

        protected void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[{ModuleName}] {message}");
        }
    }
}
