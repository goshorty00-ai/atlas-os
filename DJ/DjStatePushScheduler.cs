using System;
using System.Threading;
using System.Threading.Tasks;

namespace AtlasAI.DJ
{
    internal sealed class DjStatePushScheduler : IDisposable
    {
        private readonly Func<Task> _callback;
        private readonly TimeSpan _delay;
        private readonly CancellationTokenSource _cts = new();
        private int _pending;
        private int _running;

        public DjStatePushScheduler(Func<Task> callback, TimeSpan? delay = null)
        {
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
            _delay = delay ?? TimeSpan.FromMilliseconds(75);
        }

        public void Request()
        {
            if (_cts.IsCancellationRequested)
                return;

            Interlocked.Exchange(ref _pending, 1);
            if (Interlocked.CompareExchange(ref _running, 1, 0) == 0)
                _ = PumpAsync();
        }

        private async Task PumpAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    await Task.Delay(_delay, _cts.Token).ConfigureAwait(false);
                    if (_cts.IsCancellationRequested)
                        break;

                    if (Interlocked.Exchange(ref _pending, 0) == 0)
                        break;

                    await _callback().ConfigureAwait(false);

                    if (Interlocked.CompareExchange(ref _pending, 0, 0) == 0)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
                if (!_cts.IsCancellationRequested && Interlocked.CompareExchange(ref _pending, 0, 0) == 1)
                    Request();
            }
        }

        public void Dispose()
        {
            if (_cts.IsCancellationRequested)
                return;

            _cts.Cancel();
            _cts.Dispose();
        }
    }
}