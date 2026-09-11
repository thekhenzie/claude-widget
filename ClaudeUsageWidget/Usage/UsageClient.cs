using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClaudeUsageWidget.Auth;

namespace ClaudeUsageWidget.Usage;

/// <summary>
/// Claude Code has no dedicated "get my usage" endpoint - its own /usage command
/// reads the anthropic-ratelimit-unified-* response headers that come back on a
/// real /v1/messages call. The free /v1/messages/count_tokens endpoint does NOT
/// carry these headers (verified during bring-up), so this makes the smallest
/// possible real inference call (max_tokens=1) purely to read them off the
/// response - a negligible sliver of quota (a few tokens) spent to check the
/// quota, at a deliberately slow poll interval.
/// </summary>
public sealed class UsageClient
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    // Required for OAuth-bearer requests (as opposed to a raw x-api-key); this is
    // the exact beta flag the Claude Code CLI sends for claude.ai-login requests.
    private const string OAuthBetaFlag = "oauth-2025-04-20";

    // Cheapest current model; keeps the per-poll cost as close to zero as possible.
    private const string ProbeModel = "claude-haiku-4-5-20251001";

    private readonly HttpClient _http;
    private readonly CredentialStore _credentials;

    public UsageClient(HttpClient http, CredentialStore credentials)
    {
        _http = http;
        _credentials = credentials;
    }

    public async Task<UsageSnapshot> GetUsageAsync(CancellationToken ct)
    {
        var accessToken = await _credentials.GetValidAccessTokenAsync(ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new
            {
                model = ProbeModel,
                max_tokens = 1,
                messages = new[] { new { role = "user", content = "hi" } },
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Headers.Add("anthropic-beta", OAuthBetaFlag);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new UsageUnavailableException($"Couldn't reach the Anthropic API: {ex.Message}");
        }

        using (response)
        {
            var sessionUtilization = ReadUtilization(response, "anthropic-ratelimit-unified-5h-utilization");
            var sessionResetsAt = ReadUnixSeconds(response, "anthropic-ratelimit-unified-5h-reset");
            var weeklyUtilization = ReadUtilization(response, "anthropic-ratelimit-unified-7d-utilization");
            var weeklyResetsAt = ReadUnixSeconds(response, "anthropic-ratelimit-unified-7d-reset");
            var status = ReadHeader(response, "anthropic-ratelimit-unified-status");

            if (sessionUtilization is null && weeklyUtilization is null)
            {
                throw new UsageUnavailableException(
                    $"Usage headers were missing from the response ({(int)response.StatusCode} {response.ReasonPhrase}).");
            }

            return new UsageSnapshot(
                sessionUtilization,
                sessionResetsAt,
                weeklyUtilization,
                weeklyResetsAt,
                status,
                DateTimeOffset.UtcNow);
        }
    }

    private static string? ReadHeader(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static double? ReadUtilization(HttpResponseMessage response, string name)
    {
        var raw = ReadHeader(response, name);
        if (raw is null || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return null;

        // Defensive: normalize to a 0-1 fraction whether the header is
        // "0.42"-style or "42"-style.
        return value > 1.0 ? value / 100.0 : value;
    }

    private static DateTimeOffset? ReadUnixSeconds(HttpResponseMessage response, string name)
    {
        var raw = ReadHeader(response, name);
        if (raw is null || !long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return null;

        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }
}
