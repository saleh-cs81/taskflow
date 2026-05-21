namespace TaskFlow.Application.Common.Interfaces;

public interface ICurrentUser
{
    long? UserId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    IReadOnlyCollection<string> Permissions { get; }
}
