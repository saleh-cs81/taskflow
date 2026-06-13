using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Entities;

// A team-scheduling allocation: a user booked to a project (optionally a specific task)
// for a date range at a fixed number of hours per day. Mirrors Paymo's "bookings".
public class Booking : TenantEntity
{
    public long UserId { get; set; }
    public long ProjectId { get; set; }
    public long? TaskId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int HoursPerDay { get; set; }
    public string? Description { get; set; }

    public User? User { get; set; }
    public Project? Project { get; set; }
    public TaskItem? Task { get; set; }
}
