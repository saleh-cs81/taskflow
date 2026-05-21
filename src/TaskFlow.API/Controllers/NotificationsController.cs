using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Features.Notifications;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public class NotificationsController(INotificationService notifications) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> List([FromQuery] bool unreadOnly = false, CancellationToken ct = default)
        => Ok(await notifications.ListAsync(unreadOnly, ct));

    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> UnreadCount(CancellationToken ct)
        => Ok(new { count = await notifications.UnreadCountAsync(ct) });

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await notifications.MarkAllReadAsync(ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/activity")]
[Authorize]
public class ActivityController(IActivityService activity) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ActivityDto>>> Feed([FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await activity.GetFeedAsync(take, ct));
}
