using System.Threading.Channels;
using TaskFlow.Application.Common.Interfaces;

namespace TaskFlow.Infrastructure.Integration.Paymo;

// In-process queue of migration runs (singleton). The hosted runner drains it.
public class MigrationQueue : IMigrationQueue
{
    private readonly Channel<MigrationWorkItem> _channel =
        Channel.CreateUnbounded<MigrationWorkItem>();

    public void Enqueue(MigrationWorkItem item) => _channel.Writer.TryWrite(item);

    public IAsyncEnumerable<MigrationWorkItem> DequeueAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
