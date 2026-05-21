using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaskFlow.Application.Common.Interfaces;

namespace TaskFlow.Infrastructure.Integration.Paymo;

// Talks to the real Paymo REST API (https://app.paymoapp.com/api).
// Auth is HTTP Basic with the API key as the username. Includes exponential
// backoff retry for transient failures (429 / 5xx / network).
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
        catch { return false; }
    }

    public async Task<IReadOnlyList<PaymoProject>> GetProjectsAsync(string apiKey, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
    {
        var root = await GetJsonAsync(apiKey, "projects", ct);
        return Parse(root, "projects", el => new PaymoProject(
            GetLong(el, "id"), GetString(el, "name") ?? "", GetString(el, "description"), GetBool(el, "active")));
    }

    public async Task<IReadOnlyList<PaymoTaskList>> GetTaskListsAsync(string apiKey, long paymoProjectId, CancellationToken ct = default)
    {
        var root = await GetJsonAsync(apiKey, $"tasklists?where=project_id={paymoProjectId}", ct);
        return Parse(root, "tasklists", el => new PaymoTaskList(
            GetLong(el, "id"), GetLong(el, "project_id"), GetString(el, "name") ?? ""));
    }

    public async Task<IReadOnlyList<PaymoTask>> GetTasksAsync(string apiKey, long paymoProjectId, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
    {
        var root = await GetJsonAsync(apiKey, $"tasks?where=project_id={paymoProjectId}", ct);
        return Parse(root, "tasks", el => new PaymoTask(
            GetLong(el, "id"), GetLong(el, "project_id"), GetNullableLong(el, "tasklist_id"),
            GetString(el, "name") ?? "", GetString(el, "description"), GetBool(el, "complete"), GetDate(el, "due_date")));
    }

    public async Task<IReadOnlyList<PaymoTimeEntry>> GetTimeEntriesAsync(string apiKey, long paymoProjectId, DateTime? modifiedSinceUtc = null, CancellationToken ct = default)
    {
        var root = await GetJsonAsync(apiKey, $"entries?where=project_id={paymoProjectId}", ct);
        return Parse(root, "entries", el =>
        {
            var start = GetDate(el, "start_time") ?? DateTime.UtcNow;
            var end = GetDate(el, "end_time") ?? start;
            var dur = GetInt(el, "duration");
            return new PaymoTimeEntry(GetLong(el, "id"), GetLong(el, "project_id"), GetNullableLong(el, "task_id"),
                start, end, dur, GetString(el, "description"), GetBool(el, "billable"));
        });
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
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:X"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            try
            {
                var res = await http.SendAsync(request, ct);
                if (!IsTransient(res.StatusCode) || attempt >= 4) return res;
                logger.LogWarning("Paymo {Path} returned {Status}, retry {Attempt}", path, res.StatusCode, attempt);
                res.Dispose();
            }
            catch (HttpRequestException ex) when (attempt < 4)
            {
                logger.LogWarning(ex, "Paymo {Path} network error, retry {Attempt}", path, attempt);
            }

            await Task.Delay(delayMs, ct);
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
    private static int GetInt(JsonElement el, string p) => el.TryGetProperty(p, out var v) && v.TryGetInt32(out var n) ? n : 0;
    private static string? GetString(JsonElement el, string p) => el.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static bool GetBool(JsonElement el, string p) => el.TryGetProperty(p, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && v.GetString() == "1"));
    private static DateTime? GetDate(JsonElement el, string p)
        => el.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
}
