using System.Collections.Concurrent;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// A single-threaded synchronization context that stands in for a WPF dispatcher: any work posted to it
/// runs on one dedicated thread, so a test can assert that operator calls were marshalled there.
/// </summary>
internal sealed class SingleThreadSynchronizationContext : SynchronizationContext, IDisposable {

    readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    readonly Thread _thread;
    readonly ManualResetEventSlim _ready = new(false);

    public SingleThreadSynchronizationContext() {
        _thread = new Thread(Loop) { IsBackground = true, Name = "bridge-test-ui" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>The id of the thread every callback runs on.</summary>
    public int ThreadId { get; private set; }

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    void Loop() {
        ThreadId = Environment.CurrentManagedThreadId;
        SetSynchronizationContext(this);
        _ready.Set();
        foreach (var item in _queue.GetConsumingEnumerable()) {
            try { item.Callback(item.State); } catch { /* a test failure surfaces through the awaited task */ }
        }
    }

    public void Dispose() {
        _queue.CompleteAdding();
        _ready.Dispose();
    }
}
