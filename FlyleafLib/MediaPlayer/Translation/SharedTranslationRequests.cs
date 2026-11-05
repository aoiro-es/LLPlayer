using System.Linq;

#nullable enable
namespace FlyleafLib.MediaPlayer.Translation;

// Owned by one player configuration. No subtitle text or generated results are written to disk.
internal sealed class SharedTranslationRequests<T>(int capacity = 512) where T : class
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = [];
    private long _sequence;
    internal int StartedRequests { get; private set; }

    public async Task<T> GetAsync(string key, Func<CancellationToken, Task<T>> generate, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Entry entry;
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var previous) && previous.Task.IsCompleted && !previous.Task.IsCompletedSuccessfully)
                _entries.Remove(key);
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry(generate);
                _entries.Add(key, entry);
                StartedRequests++;
            }
            entry.Waiters++;
            entry.LastUsed = ++_sequence;
            // In-flight work is never evicted. Keep the most recently used completed results.
            foreach (var old in _entries.Where(p => p.Value.Waiters == 0 && p.Value.Task.IsCompleted)
                .OrderBy(p => p.Value.LastUsed).Take(Math.Max(0, _entries.Count - Math.Max(1, capacity))).ToArray())
                _entries.Remove(old.Key);
        }
        try { return await entry.Task.WaitAsync(token).ConfigureAwait(false); }
        finally
        {
            bool cancel = false;
            lock (_lock)
            {
                entry.Waiters--;
                if (entry.Waiters == 0 && !entry.Task.IsCompletedSuccessfully)
                {
                    if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry)) _entries.Remove(key);
                    cancel = !entry.Task.IsCompleted;
                }
            }
            // One track leaving must not cancel a request still needed by another track.
            if (cancel)
            {
                try { entry.Cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private sealed class Entry
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public Task<T> Task { get; }
        public int Waiters;
        public long LastUsed;
        public Entry(Func<CancellationToken, Task<T>> generate)
        {
            var token = Cancellation.Token;
            Task = System.Threading.Tasks.Task.Run(() => generate(token));
            _ = Task.ContinueWith(completed =>
            {
                _ = completed.Exception; // Observe faults even when every waiter was canceled.
                Cancellation.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
