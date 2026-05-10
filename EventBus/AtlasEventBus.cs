using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.EventBus
{
    /// <summary>
    /// Central event bus for Atlas AI. Supports publish/subscribe, filtering, and broadcasting.
    /// Thread-safe and designed for high-throughput event processing.
    /// </summary>
    public sealed class AtlasEventBus : IDisposable
    {
        // ── Singleton ────────────────────────────────────────────────────────
        private static AtlasEventBus? _instance;
        private static readonly object _lock = new();

        public static AtlasEventBus Instance
        {
            get
            {
                lock (_lock) { return _instance ??= new AtlasEventBus(); }
            }
        }

        // ── State ────────────────────────────────────────────────────────────
        private readonly ConcurrentDictionary<string, ConcurrentBag<Subscription>> _subscriptions = new();
        private readonly ConcurrentQueue<(AtlasEvent evt, DateTime queued)> _eventQueue = new();
        private readonly ConcurrentDictionary<string, EventStatistics> _stats = new();
        
        private CancellationTokenSource? _cts;
        private Task? _processingTask;
        private bool _running;
        private bool _disposed;

        // ── Configuration ────────────────────────────────────────────────────
        public int MaxQueueSize { get; set; } = 10000;
        public int ProcessingDelayMs { get; set; } = 10;
        public bool EnableStatistics { get; set; } = true;
        public bool EnableLogging { get; set; } = false;

        // ── Events ───────────────────────────────────────────────────────────
        public event Action<AtlasEvent>? EventPublished;
        public event Action<string, Exception>? EventError;

        private AtlasEventBus() { }

        // ── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Start the event bus processing loop.
        /// </summary>
        public void Start()
        {
            if (_running) return;
            _running = true;
            _cts = new CancellationTokenSource();
            _processingTask = Task.Run(() => ProcessEventQueueAsync(_cts.Token));
            Log("Event bus started");
        }

        /// <summary>
        /// Stop the event bus and wait for pending events to process.
        /// </summary>
        public void Stop()
        {
            if (!_running) return;
            _running = false;
            _cts?.Cancel();
            _processingTask?.Wait(TimeSpan.FromSeconds(5));
            Log("Event bus stopped");
        }

        /// <summary>
        /// Publish an event to all subscribers.
        /// </summary>
        public void Publish(AtlasEvent evt)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AtlasEventBus));
            if (evt == null) throw new ArgumentNullException(nameof(evt));

            if (_eventQueue.Count >= MaxQueueSize)
            {
                Log($"Event queue full ({MaxQueueSize}), dropping event: {evt.EventType}");
                return;
            }

            _eventQueue.Enqueue((evt, DateTime.UtcNow));
            EventPublished?.Invoke(evt);

            if (EnableStatistics)
                RecordStat(evt.EventType, "published");
        }

        /// <summary>
        /// Publish an event and wait for all handlers to complete (synchronous).
        /// </summary>
        public async Task PublishAsync(AtlasEvent evt)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AtlasEventBus));
            if (evt == null) throw new ArgumentNullException(nameof(evt));

            await DispatchEventAsync(evt);
        }

        /// <summary>
        /// Subscribe to a specific event type.
        /// </summary>
        public IDisposable Subscribe<T>(Action<T> handler) where T : AtlasEvent
        {
            return Subscribe<T>(handler, null);
        }

        /// <summary>
        /// Subscribe to a specific event type with a filter predicate.
        /// </summary>
        public IDisposable Subscribe<T>(Action<T> handler, Func<T, bool>? filter) where T : AtlasEvent
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var eventType = GetEventType<T>();
            var subscription = new Subscription
            {
                Id = Guid.NewGuid().ToString("N"),
                EventType = eventType,
                Handler = evt =>
                {
                    if (evt is T typedEvent)
                    {
                        if (filter == null || filter(typedEvent))
                            handler(typedEvent);
                    }
                },
                Filter = filter != null ? evt => evt is T t && filter(t) : null
            };

            var bag = _subscriptions.GetOrAdd(eventType, _ => new ConcurrentBag<Subscription>());
            bag.Add(subscription);

            Log($"Subscribed to {eventType} (id: {subscription.Id})");

            return new SubscriptionToken(this, subscription);
        }

        /// <summary>
        /// Subscribe to all events (wildcard subscription).
        /// </summary>
        public IDisposable SubscribeAll(Action<AtlasEvent> handler)
        {
            return SubscribeAll(handler, null);
        }

        /// <summary>
        /// Subscribe to all events with a filter.
        /// </summary>
        public IDisposable SubscribeAll(Action<AtlasEvent> handler, Func<AtlasEvent, bool>? filter)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var subscription = new Subscription
            {
                Id = Guid.NewGuid().ToString("N"),
                EventType = "*",
                Handler = evt =>
                {
                    if (filter == null || filter(evt))
                        handler(evt);
                },
                Filter = filter
            };

            var bag = _subscriptions.GetOrAdd("*", _ => new ConcurrentBag<Subscription>());
            bag.Add(subscription);

            Log($"Subscribed to all events (id: {subscription.Id})");

            return new SubscriptionToken(this, subscription);
        }

        /// <summary>
        /// Broadcast an event to all subscribers immediately (bypasses queue).
        /// </summary>
        public void Broadcast(AtlasEvent evt)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AtlasEventBus));
            if (evt == null) throw new ArgumentNullException(nameof(evt));

            _ = Task.Run(() => DispatchEventAsync(evt));
        }

        /// <summary>
        /// Get statistics for a specific event type.
        /// </summary>
        public EventStatistics? GetStatistics(string eventType)
        {
            return _stats.TryGetValue(eventType, out var stats) ? stats : null;
        }

        /// <summary>
        /// Get all event statistics.
        /// </summary>
        public Dictionary<string, EventStatistics> GetAllStatistics()
        {
            return _stats.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        /// <summary>
        /// Clear all statistics.
        /// </summary>
        public void ClearStatistics()
        {
            _stats.Clear();
        }

        /// <summary>
        /// Get current queue size.
        /// </summary>
        public int GetQueueSize() => _eventQueue.Count;

        /// <summary>
        /// Get subscriber count for an event type.
        /// </summary>
        public int GetSubscriberCount(string eventType)
        {
            return _subscriptions.TryGetValue(eventType, out var bag) ? bag.Count : 0;
        }

        // ── Internal ─────────────────────────────────────────────────────────

        private async Task ProcessEventQueueAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_eventQueue.TryDequeue(out var item))
                    {
                        var (evt, queued) = item;
                        var queueTime = (DateTime.UtcNow - queued).TotalMilliseconds;

                        if (EnableStatistics)
                            RecordStat(evt.EventType, "dequeued", queueTime);

                        await DispatchEventAsync(evt);
                    }
                    else
                    {
                        await Task.Delay(ProcessingDelayMs, ct);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error processing event queue: {ex.Message}");
                }
            }
        }

        private async Task DispatchEventAsync(AtlasEvent evt)
        {
            var tasks = new List<Task>();

            // Dispatch to specific event type subscribers
            if (_subscriptions.TryGetValue(evt.EventType, out var specificBag))
            {
                foreach (var sub in specificBag)
                {
                    tasks.Add(Task.Run(() => InvokeHandler(sub, evt)));
                }
            }

            // Dispatch to wildcard subscribers
            if (_subscriptions.TryGetValue("*", out var wildcardBag))
            {
                foreach (var sub in wildcardBag)
                {
                    tasks.Add(Task.Run(() => InvokeHandler(sub, evt)));
                }
            }

            if (tasks.Any())
            {
                await Task.WhenAll(tasks);
            }

            if (EnableStatistics)
                RecordStat(evt.EventType, "dispatched");
        }

        private void InvokeHandler(Subscription sub, AtlasEvent evt)
        {
            try
            {
                sub.Handler(evt);

                if (EnableStatistics)
                    RecordStat(evt.EventType, "handled");
            }
            catch (Exception ex)
            {
                Log($"Error in event handler for {evt.EventType}: {ex.Message}");
                EventError?.Invoke(evt.EventType, ex);

                if (EnableStatistics)
                    RecordStat(evt.EventType, "error");
            }
        }

        private void Unsubscribe(Subscription subscription)
        {
            if (_subscriptions.TryGetValue(subscription.EventType, out var bag))
            {
                // ConcurrentBag doesn't support removal, so we mark it as inactive
                subscription.IsActive = false;
                Log($"Unsubscribed from {subscription.EventType} (id: {subscription.Id})");
            }
        }

        private void RecordStat(string eventType, string action, double? value = null)
        {
            var stats = _stats.GetOrAdd(eventType, _ => new EventStatistics { EventType = eventType });

            lock (stats)
            {
                switch (action)
                {
                    case "published":
                        stats.PublishedCount++;
                        break;
                    case "dequeued":
                        stats.DequeuedCount++;
                        if (value.HasValue)
                        {
                            stats.TotalQueueTimeMs += value.Value;
                            stats.AvgQueueTimeMs = stats.TotalQueueTimeMs / stats.DequeuedCount;
                        }
                        break;
                    case "dispatched":
                        stats.DispatchedCount++;
                        break;
                    case "handled":
                        stats.HandledCount++;
                        break;
                    case "error":
                        stats.ErrorCount++;
                        break;
                }

                stats.LastEventTime = DateTime.UtcNow;
            }
        }

        private static string GetEventType<T>() where T : AtlasEvent
        {
            var instance = Activator.CreateInstance<T>();
            return instance.EventType;
        }

        private void Log(string message)
        {
            if (EnableLogging)
                System.Diagnostics.Debug.WriteLine($"[EventBus] {message}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _subscriptions.Clear();
            _stats.Clear();
        }

        // ── Helper Classes ───────────────────────────────────────────────────

        private class Subscription
        {
            public string Id { get; set; } = "";
            public string EventType { get; set; } = "";
            public Action<AtlasEvent> Handler { get; set; } = _ => { };
            public Func<AtlasEvent, bool>? Filter { get; set; }
            public bool IsActive { get; set; } = true;
        }

        private class SubscriptionToken : IDisposable
        {
            private readonly AtlasEventBus _bus;
            private readonly Subscription _subscription;

            public SubscriptionToken(AtlasEventBus bus, Subscription subscription)
            {
                _bus = bus;
                _subscription = subscription;
            }

            public void Dispose()
            {
                _bus.Unsubscribe(_subscription);
            }
        }
    }

    // ── Statistics ───────────────────────────────────────────────────────────

    public class EventStatistics
    {
        public string EventType { get; set; } = "";
        public long PublishedCount { get; set; }
        public long DequeuedCount { get; set; }
        public long DispatchedCount { get; set; }
        public long HandledCount { get; set; }
        public long ErrorCount { get; set; }
        public double TotalQueueTimeMs { get; set; }
        public double AvgQueueTimeMs { get; set; }
        public DateTime? LastEventTime { get; set; }
    }
}
