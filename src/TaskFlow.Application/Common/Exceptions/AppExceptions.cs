namespace TaskFlow.Application.Common.Exceptions;

// Maps to 400. Carries a localization key so the API can translate the message (ar/en).
public class ValidationAppException(string messageKey, IDictionary<string, string[]>? errors = null)
    : Exception(messageKey)
{
    public string MessageKey { get; } = messageKey;
    public IDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
}

// Maps to 401.
public class UnauthorizedAppException(string messageKey) : Exception(messageKey)
{
    public string MessageKey { get; } = messageKey;
}

// Maps to 404.
public class NotFoundAppException(string messageKey) : Exception(messageKey)
{
    public string MessageKey { get; } = messageKey;
}

// Maps to 409.
public class ConflictAppException(string messageKey) : Exception(messageKey)
{
    public string MessageKey { get; } = messageKey;
}
