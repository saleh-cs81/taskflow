namespace TaskFlow.Domain.Enums;

public enum AttachmentTargetType
{
    Task = 0,
    Project = 1,
    Comment = 2
}

public enum NotificationType
{
    Mention = 0,
    TaskAssigned = 1,
    TaskCommented = 2,
    TaskDueSoon = 3,
    TimesheetSubmitted = 4,
    TimesheetReviewed = 5,
    ProjectInvite = 6,
    TaskCompleted = 7,
    Generic = 100
}
