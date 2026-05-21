using Microsoft.Extensions.Logging;
using TaskFlow.Application.Common.Interfaces;

namespace TaskFlow.Infrastructure.Services;

// Dev stub: logs the email instead of sending. Replace with SendGrid/SES in production.
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        logger.LogInformation("EMAIL → {To} | {Subject}", toEmail, subject);
        return Task.CompletedTask;
    }
}
