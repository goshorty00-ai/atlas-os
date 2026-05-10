using System;
using System.Collections.Generic;

namespace AtlasAI.DJ
{
    internal sealed class DjRuntimeSession : IDisposable
    {
        private readonly IReadOnlyList<IDisposable> _resources;

        public DjRuntimeSession(AudioEngine engine, DjControllerManager controllers)
        {
            Engine = engine ?? throw new ArgumentNullException(nameof(engine));
            Controllers = controllers ?? throw new ArgumentNullException(nameof(controllers));
            _resources = new IDisposable[] { controllers, engine };
        }

        internal DjRuntimeSession(params IDisposable[] resources)
        {
            _resources = resources ?? Array.Empty<IDisposable>();
        }

        public AudioEngine? Engine { get; }

        public DjControllerManager? Controllers { get; }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            for (var index = _resources.Count - 1; index >= 0; index--)
            {
                try
                {
                    _resources[index]?.Dispose();
                }
                catch
                {
                }
            }
        }
    }
}