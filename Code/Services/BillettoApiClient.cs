// [CHANGE: Billetto sales dashboard] Related: Code/Services/BillettoTicketService.cs, Code/Services/BillettoSalesService.cs
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace Code.Services;

// Shared HTTP machinery for the Billetto organiser API, extracted from
// BillettoTicketService so both dashboards paginate and throttle the same way.
// Not DI-registered: instantiated per fetch with callbacks into the caller's
// own progress state.
internal sealed class BillettoApiClient
{
    private readonly ILogger _logger;
    private readonly Action? _onPageFetched;
    private readonly Action<int?, int?>? _onRatelimit;
    private readonly Action<double?>? _onThrottleWait;

    public BillettoApiClient(
        ILogger logger,
        Action? onPageFetched = null,
        Action<int?, int?>? onRatelimit = null,
        Action<double?>? onThrottleWait = null)
    {
        _logger = logger;
        _onPageFetched = onPageFetched;
        _onRatelimit = onRatelimit;
        _onThrottleWait = onThrottleWait;
    }

    public static HttpClient CreateClient(IHttpClientFactory factory, string keypair)
    {
        var client = factory.CreateClient("Billetto");
        client.DefaultRequestHeaders.Remove("Api-Keypair");
        client.DefaultRequestHeaders.Add("Api-Keypair", keypair);
        return client;
    }

    public async Task FetchPagedAsync(HttpClient client, string baseUrl, string url, Action<JsonNode> handleItem, Action<int>? onNewItems = null)
    {
        var pageGuard = 0;
        string? nextUrl = url;

        // Billetto's has_more is broken: it stays true past the last page, serves an
        // empty page, then loops the cursor back to the start. Track seen ids and stop
        // on an empty page or a page with no new items.
        var seenIds = new HashSet<string>();

        while (!string.IsNullOrEmpty(nextUrl) && pageGuard++ < 200)
        {
            var response = await GetWithThrottleRetryAsync(client, nextUrl);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Billetto API svarede {(int)response.StatusCode}: {body}");
            }

            var hasRemaining = TryGetHeaderInt(response, "X-Ratelimit-Remaining", out var remaining);
            var hasLimit = TryGetHeaderInt(response, "X-Ratelimit-Limit", out var limit);
            _onPageFetched?.Invoke();
            _onRatelimit?.Invoke(hasRemaining ? remaining : null, hasLimit ? limit : null);

            var root = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            var data = root?["data"]?.AsArray();
            if (data == null || data.Count == 0) return;

            var newItems = 0;
            foreach (var item in data)
            {
                if (item == null) continue;
                var id = item["id"]?.GetValue<string>();
                if (id != null && !seenIds.Add(id)) continue;
                newItems++;
                handleItem(item);
            }
            if (newItems == 0) return;

            onNewItems?.Invoke(newItems);

            var hasMore = root?["has_more"]?.GetValue<bool>() ?? false;
            var next = root?["next_url"]?.GetValue<string>();
            if (!hasMore || string.IsNullOrWhiteSpace(next))
            {
                nextUrl = null;
            }
            else
            {
                nextUrl = next.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? next : baseUrl + next;
                // Gentle pacing between pages instead of reactive long pauses
                await Task.Delay(300);
            }
        }
    }

    // Billetto throttles aggressively; on 429 wait the announced time and retry
    public async Task<HttpResponseMessage> GetWithThrottleRetryAsync(HttpClient client, string url)
    {
        for (var attempt = 0; ; attempt++)
        {
            var response = await client.GetAsync(url);
            if ((int)response.StatusCode != 429 || attempt >= 3)
            {
                return response;
            }

            var waitSeconds = response.Headers.RetryAfter?.Delta?.TotalSeconds;
            if (waitSeconds == null)
            {
                var body = await response.Content.ReadAsStringAsync();
                var match = System.Text.RegularExpressions.Regex.Match(body, @"(\d+)\s*second");
                waitSeconds = match.Success ? int.Parse(match.Groups[1].Value) : 30;
            }

            var wait = TimeSpan.FromSeconds(Math.Clamp(waitSeconds.Value + 1, 2, 90));
            _logger.LogWarning("Billetto: throttled (429), waiting {Wait}s before retry {Attempt}/3", wait.TotalSeconds, attempt + 1);
            _onRatelimit?.Invoke(0, null);
            _onThrottleWait?.Invoke(wait.TotalSeconds);
            await Task.Delay(wait);
            _onThrottleWait?.Invoke(null);
        }
    }

    private static bool TryGetHeaderInt(HttpResponseMessage response, string header, out int value)
    {
        value = 0;
        return response.Headers.TryGetValues(header, out var values)
            && int.TryParse(values.FirstOrDefault(), out value);
    }
}
