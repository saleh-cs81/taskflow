namespace TaskFlow.Domain.Enums;

public enum SubscriptionStatus
{
    Trial = 0,
    Active = 1,
    PastDue = 2,
    Cancelled = 3,
    Suspended = 4
}

// System role keys. Custom per-tenant roles may also exist in the Roles table.
public enum SystemRole
{
    SuperAdmin = 0,
    CompanyAdmin = 1,
    ProjectManager = 2,
    TeamLeader = 3,
    Employee = 4,
    Client = 5
}

public enum InvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Expired = 2,
    Revoked = 3
}

public enum AuditChangeType
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    // Auth / session events (stored in the same AuditLog table; column is an int so adding values needs no migration).
    Login = 3,
    Logout = 4,
    LoginFailed = 5,
    PasswordChanged = 6
}
