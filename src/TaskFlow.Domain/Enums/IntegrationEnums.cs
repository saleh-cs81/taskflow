namespace TaskFlow.Domain.Enums;

public enum PaymoConnectionStatus
{
    Disconnected = 0,
    Connected = 1,
    Invalid = 2
}

public enum MigrationJobType
{
    Full = 0,
    Incremental = 1
}

public enum MigrationJobStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    CompletedWithErrors = 3,
    Failed = 4
}
