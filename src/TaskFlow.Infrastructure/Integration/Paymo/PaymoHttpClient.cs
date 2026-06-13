using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaskFlow.Application.Common.Interfaces;

namespace TaskFlow.Infrastructure.Integration.Paymo;

// Talks to the real Paymo REST API (https://app.paymoapp.com/api).
// Auth: HTTP Basic with the API key as the username and any non-empty password.
// Paymo API v1 does NOT paginate — a list GET returns the full matching set —
// and we always scope list calls per project, so volume stays bounded.
public class PaymoHttpClient(HttpClient http, ILogger<PaymoHttpClient> logger) : IPaymoClient
{
    private const string BaseUrl = "https://app.paymoapp.com/api/";

    public async Task<bool> ValidateKeyAsync(string apiKey, CancellationToken ct = default)
    {
        try
        {
            using var res = await SendAsync(apiKey, "me", ct);
            return res.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Paymo key validation failed");
            return false;
        }
    }

    public async Task<IReadOnlyList<PaymoUser>> GetUsersAsync(string apiKey, CancellationToken ct = default)
    {
        var root = await GetJsonAsync(apiKey, "users", ct);
        return Parse(root, "users", el => new PaymoUser(
            GetLong(el, "id"), GetString(el, "name") ?? "", GetString(el, "email"), GetBool(el, "active"), GetString(el, "type")));
    }

    public async Task<IReadOnlyList<PaymoClient>> GetClientsAsync(string apiKey, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
    {
        var root = await GetJsonAsync(apiKey, "clients" + WhereSuffix(null, modifiedSinceUtc), ct);
        return Parse(root, "clients", el => new PaymoClient(
            GetLong(el, "id"), GetString(el, "name") ?? "", GetString(el, "email"), GetString(el, "phone"),
            GetString(el, "address"), GetString(el, "city"), GetString(el, "country"), GetString(el, "website"), GetBool(el, "active")));
    }

    public async Task<IReadOnlyList<PaymoClientContact>> GetClientContactsAsync(string apiKey, CancellationToken ct = default)
    {
        var root = await GetJsonAsync(apiKey, "clientcontacts", ct);
        return Parse(root, "clientcontacts", el => new PaymoClientContact(
            GetLong(el, "id"), GetLong(el, "client_id"), GetString(el, "name") ?? "",
            GetString(el, "email"), GetString(el, "phone"), GetString(el, "position"), GetBool(el, "is_main")));
    }

    public async Task<IReadOnlyList<PaymoProjectStatus>> GetProjectStatusesAsync(string apiKey, CancellationToken ct = default)
    {
        var root = await GetJsonAsync(apiKey, "projectstatuses", ct);
        return Parse(root, "projectstatuses", el => new PaymoProjectStatus(GetLong(el, "id"), GetString(el, "name") ?? ""));
    }

    public async Task<IReadOnlyList<PaymoProject>> GetProjectsAsync(string apiKey, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
    {
        var root = await GetJsonAsync(apiKey, "projects" + WhereSuffix(null, modifiedSinceUtc), ct);
        return Parse(root, "projects", el => new PaymoProject(
            GetLong(el, "id"), GetString(el, "name") ?? "", GetString(el, "description"), GetBool(el, "active"),
            GetNullableLong(el, "client_id"), GetNullableLong(el, "status_id"), GetString(el, "code"),
            GetString(el, "color"), GetNullableDecimal(el, "budget_hours"), GetBool(el, "billable")));
    }

    public async Task<IReadOnlyList<PaymoTaskList>> GetTaskListsAsync(string apiKey, long paymoProjectId, CancellationToken ct = default)
    {
        var root = await GetJsonAsync(apiKey, "tasklists" + WhereSuffix($"project_id={paymoProjectId}", null), ct);
        return Parse(root, "tasklists", el => new PaymoTaskList(
            GetLong(el, "id"), GetLong(el, "project_id"), GetString(el, "name") ?? "",
            GetInt(el, "seq"), GetNullableLong(el, "milestone_id")));
    }

    public async Task<IReadOnlyList<PaymoMilestone>> GetMilestonesAsync(string apiKey, long paymoProjectId, CancellationToken ct = default)
    {
        var root = await GetJsonAsync(apiKey, "milestones" + WhereSuffix($"project_id={paymoProjectId}", null), ct);
        return Parse(root, "milestones", el => new PaymoMilestone(
            GetLong(el, "id"), GetLong(el, "project_id"), GetString(el, "name") ?? "", GetDate(el, "due_date"), GetBool(el, "complete")));
    }

    public async Task<IReadOnlyList<PaymoTask>> GetTasksAsync(string apiKey, long paymoProjectId, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
    {
        var root = await GetJsonAsync(apiKey, "tasks" + WhereSuffix($"project_id={paymoProjectId}", modifiedSinceUtc), ct);
        return Parse(root, "tasks", el => new PaymoTask(
            GetLong(el, "id"), GetLong(el, "project_id"), GetNullableLong(el, "tasklist_id"),
            GetString(el, "name") ?? "", GetString(el, "description"), GetBool(el, "complete"),
            GetDate(el, "due_date"), GetDate(el, "start_date"), GetDate(el, "completed_on"),
            GetInt(el, "priority"), GetInt(el, "seq"), GetString(el, "code"),
            AllUserIds(el, "users")));
    }

    public async Task<IReadOnlyList<PaymoSubtask>> GetSubtasksAsync(string apiKey, long paymoProjectId, CancellationToken ct = default)
    {
        var root = await GetJsonAsync(apiKey, "subtasks" + WhereSuffix($"project_id={paymoProjectId}", null), ct);
        return Parse(root, "subtasks", el => new PaymoSubtask(
            GetLong(el, "id"), GetLong(el, "task_id"), GetString(el, "name") ?? "", GetBool(el, "complete"), GetInt(el, "seq")));
    }

    // Paymo task 'users' is an array of assigned user ids.
    private static IReadOnlyList<long> AllUserIds(JsonElement el, string prop)
    {
        var ids = new List<long>();
        if (el.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var item in arr.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var id)) ids.Add(id);
        return ids;
    }

    public async Task<IReadOnlyList<PaymoTimeEntry>> GetTimeEntriesAsync(string apiKey, long paymoProjectId, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
    {
        var root = await GetJsonAsync(apiKey, "entries" + WhereSuffix($"project_id={paymoProjectId}", modifiedSinceUtc), ct);
        if (!root.TryGetProperty("entries", out var arr) || arr.ValueKind != JsonValueKind.Array) return [];

        var list = new List<PaymoTimeEntry>();
        foreach (var el in arr.EnumerateArray())
        {
            var duration = GetInt(el, "duration");                 // seconds
            var startTime = GetDate(el, "start_time");
            var endTime = GetDate(el, "end_time");

            // A running timer (no end_time, no duration) — skip; nothing meaningful to import.
            if (endTime is null && duration <= 0) continue;

            DateTime start, end;
            if (startTime is not null)
            {
                start = startTime.Value;
                end = endTime ?? start.AddSeconds(duration);
            }
            else
            {
                // Bulk "date + duration" entry: no timestamps, just a day and a length.
                var day = GetDate(el, "date") ?? DateTime.UtcNow.Date;
                start = DateTime.SpecifyKind(day.Date, DateTimeKind.Utc);
                end = start.AddSeconds(duration);
            }
            if (duration <= 0) duration = (int)(end - start).TotalSeconds;

            list.Add(new PaymoTimeEntry(
                GetLong(el, "id"), GetLong(el, "project_id"), GetNullableLong(el, "task_id"),
                GetNullableLong(el, "user_id"),
                start, end, duration, GetString(el, "description"),
                GetBool(el, "billable"), GetBool(el, "billed")));   // real billable flag + invoiced flag
        }
        return list;
    }

    // --- where-clause builder (URL-encoded value; optional incremental filter) ---

    private static string WhereSuffix(string? filter, DateTime? modifiedSinceUtc)
    {
        var clauses = new List<string>();
        if (!string.IsNullOrEmpty(filter)) clauses.Add(filter);
        if (modifiedSinceUtc is { } since)
            clauses.Add($"updated_on>={((DateTimeOffset)DateTime.SpecifyKind(since, DateTimeKind.Utc)).ToUnixTimeSeconds()}");
        if (clauses.Count == 0) return string.Empty;
        return "?where=" + Uri.EscapeDataString(string.Join(" and ", clauses));
    }

    // --- HTTP plumbing with retry ---

    private async Task<JsonElement> GetJsonAsync(string apiKey, string path, CancellationToken ct)
    {
        using var res = await SendAsync(apiKey, path, ct);
        res.EnsureSuccessStatusCode();
        var stream = await res.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    private async Task<HttpResponseMessage> SendAsync(string apiKey, string path, CancellationToken ct)
    {
        var delayMs = 500;
        for (var attempt = 1; ; attempt++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + path);
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:x"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            try
            {
                var res = await http.SendAsync(request, ct);
                if (!IsTransient(res.StatusCode) || attempt >= 4) return res;

                // Honor Retry-After on 429; otherwise exponential backoff.
                var wait = res.Headers.RetryAfter?.Delta
                    ?? (res.Headers.RetryAfter?.Date is { } d ? (TimeSpan?)(d - DateTimeOffset.UtcNow) : null)
                    ?? TimeSpan.FromMilliseconds(delayMs);
                if (wait < TimeSpan.Zero) wait = TimeSpan.FromMilliseconds(delayMs);
                logger.LogWarning("Paymo {Path} returned {Status}, retry {Attempt} after {Wait}s", path, res.StatusCode, attempt, wait.TotalSeconds);
                res.Dispose();
                await Task.Delay(wait, ct);
            }
            catch (HttpRequestException ex) when (attempt < 4)
            {
                logger.LogWarning(ex, "Paymo {Path} network error, retry {Attempt}", path, attempt);
                await Task.Delay(delayMs, ct);
            }
            delayMs *= 2;
        }
    }

    private static bool IsTransient(HttpStatusCode code)
        => code == HttpStatusCode.TooManyRequests || (int)code >= 500;

    private static IReadOnlyList<T> Parse<T>(JsonElement root, string collection, Func<JsonElement, T> map)
    {
        if (!root.TryGetProperty(collection, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];
        return arr.EnumerateArray().Select(map).ToList();
    }

    private static long GetLong(JsonElement el, string p) => el.TryGetProperty(p, out var v) && v.TryGetInt64(out var n) ? n : 0;
    private static long? GetNullableLong(JsonElement el, string p) => el.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
    private static int GetInt(JsonElement el, string p) => el.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
    private static decimal? GetNullableDecimal(JsonElement el, string p) => el.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n) ? n : null;
    private static string? GetString(JsonElement el, string p) => el.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool GetBool(JsonElement el, string p)
    {
        if (!el.TryGetProperty(p, out var v)) return false;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => v.TryGetInt32(out var n) && n == 1,
            JsonValueKind.String => v.GetString() is "1" or "true" or "True" or "yes",
            _ => false
        };
    }

    private static DateTime? GetDate(JsonElement el, string p)
        => el.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String
           && DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
}
