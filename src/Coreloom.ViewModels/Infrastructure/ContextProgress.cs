namespace Coreloom.ViewModels.Infrastructure;

/// <summary>
/// Like <see cref="Progress{T}"/>: reports are handled through the synchronization context of the thread that created it
/// (the UI thread in the app). Without one (tests, tools) a report is handled at once on the reporting thread instead of
/// being queued to the thread pool, so it can never be handled after the work it belongs to has finished.
/// </summary>
public sealed class ContextProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    public ContextProgress(Action<T> handler) => _handler = handler;

    public void Report(T value)
    {
        if (_context is null)
        {
            _handler(value);
            return;
        }

        _context.Post(static state =>
        {
            var (handler, report) = ((Action<T>, T))state!;
            handler(report);
        }, (_handler, value));
    }
}
