namespace TaskFlow.Application.Common.Authorization;

// Central catalogue of permission codes. Seeded into the Permissions table.
public static class Permissions
{
    public static class Projects
    {
        public const string View = "projects.view";
        public const string Create = "projects.create";
        public const string Update = "projects.update";
        public const string Delete = "projects.delete";
    }

    public static class Tasks
    {
        public const string View = "tasks.view";
        public const string Create = "tasks.create";
        public const string Update = "tasks.update";
        public const string Delete = "tasks.delete";
        public const string Assign = "tasks.assign";
    }

    public static class TimeTracking
    {
        public const string View = "time.view";
        public const string Track = "time.track";
        public const string Approve = "time.approve";
    }

    public static class Users
    {
        public const string View = "users.view";
        public const string Invite = "users.invite";
        public const string Manage = "users.manage";
    }

    public static class Reports
    {
        public const string View = "reports.view";
        public const string Export = "reports.export";
    }

    public static IReadOnlyList<(string Code, string Group)> All { get; } = new List<(string, string)>
    {
        (Projects.View, "Projects"), (Projects.Create, "Projects"), (Projects.Update, "Projects"), (Projects.Delete, "Projects"),
        (Tasks.View, "Tasks"), (Tasks.Create, "Tasks"), (Tasks.Update, "Tasks"), (Tasks.Delete, "Tasks"), (Tasks.Assign, "Tasks"),
        (TimeTracking.View, "TimeTracking"), (TimeTracking.Track, "TimeTracking"), (TimeTracking.Approve, "TimeTracking"),
        (Users.View, "Users"), (Users.Invite, "Users"), (Users.Manage, "Users"),
        (Reports.View, "Reports"), (Reports.Export, "Reports"),
    };
}
