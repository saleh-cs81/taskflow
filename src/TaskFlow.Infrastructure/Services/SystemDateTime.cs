using TaskFlow.Application.Common.Interfaces;

namespace TaskFlow.Infrastructure.Services;

public class SystemDateTime : IDateTime
{
    public DateTime UtcNow => DateTime.UtcNow;
}
