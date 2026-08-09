namespace TaskFlow.Infrastructure.Persistence.Interceptors;

// Ambient switch to turn OFF audit-log writing for a unit of work (e.g. the Paymo
// bulk import, which would otherwise write hundreds of thousands of audit rows).
// AsyncLocal flows down the async call chain, so wrapping the work is enough:
//   using (AuditScope.Suppress()) { ...bulk work... }
public static class AuditScope
{
    private static readonly AsyncLocal<int> _depth = new();

    public static bool IsSuppressed => _depth.Value > 0;

    public static IDisposable Suppress()
    {
        _depth.Value++;
        return new Popper();
    }

    private sealed class Popper : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            if (_depth.Value > 0) _depth.Value--;
        }
    }
}
