using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClaudeUsageWidget.Auth;

/// <summary>
/// Reuses Claude Code's own login instead of asking the user to sign in again.
/// Reads the OAuth token pair Claude Code stores in ~/.claude/.credentials.json,
/// refreshes it via Anthropic's OAuth endpoint when needed, and caches the
/// refreshed token in this app's own local app-data folder. Never writes back
/// to Claude Code's credentials file, so there is no risk of racing with or
/// corrupting Claude Code's own credential state.
/// </summary>
public sealed class CredentialStore
{
    // Reverse-engineered from the installed Claude Code CLI binary (claude.exe):
    // this is the same client id and endpoint Claude Code itself uses to
    // refresh its stored OAuth token.
    private const string TokenUrl = "https://platform.claude.com/v1/oauth/token";
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    private static readonly string SourceCredentialsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeUsageWidget");

    private static readonly string CachePath = Path.Combine(CacheDirectory, "token-cache.json");

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private CachedToken? _current;

    public CredentialStore(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> GetValidAccessTokenAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var token = _current ?? LoadCached() ?? LoadFromSource()
                ?? throw new CredentialException(
                    "Claude Code isn't signed in on this machine (no credentials found).");

            if (IsExpiredOrExpiringSoon(token))
            {
                token = await RefreshAsync(token, ct);
            }

            _current = token;
            return token.AccessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static bool IsExpiredOrExpiringSoon(CachedToken token)
    {
        const long bufferMs = 60_000;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= token.ExpiresAt - bufferMs;
    }

    private static CachedToken? LoadCached()
    {
        try
        {
            if (!File.Exists(CachePath))
                return null;

            var json = File.ReadAllText(CachePath);
            return JsonSerializer.Deserialize<CachedToken>(json);
        }
        catch
        {
            return null;
        }
    }

    private static CachedToken? LoadFromSource()
    {
        try
        {
            if (!File.Exists(SourceCredentialsPath))
                return null;

            var json = File.ReadAllText(SourceCredentialsPath);
            var file = JsonSerializer.Deserialize<CredentialsFile>(json);
            var oauth = file?.ClaudeAiOauth;
            if (oauth is null || string.IsNullOrEmpty(oauth.AccessToken))
                return null;

            return new CachedToken
            {
                AccessToken = oauth.AccessToken,
                RefreshToken = oauth.RefreshToken,
                ExpiresAt = oauth.ExpiresAt,
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<CachedToken> RefreshAsync(CachedToken token, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token.RefreshToken))
            throw new CredentialException("Claude Code's login has expired; sign in again in Claude Code.");

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = JsonContent.Create(new
            {
                grant_type = "refresh_token",
                refresh_token = token.RefreshToken,
                client_id = ClientId,
            }),
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new CredentialException($"Couldn't reach the token refresh endpoint: {ex.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new CredentialException(
                    $"Token refresh was rejected ({(int)response.StatusCode} {response.ReasonPhrase}).");
            }

            var parsed = await response.Content.ReadFromJsonAsync<RefreshTokenResponse>(cancellationToken: ct)
                ?? throw new CredentialException("Token refresh returned an unreadable response.");

            if (string.IsNullOrEmpty(parsed.AccessToken))
                throw new CredentialException("Token refresh response was missing an access token.");

            var refreshed = new CachedToken
            {
                AccessToken = parsed.AccessToken,
                RefreshToken = string.IsNullOrEmpty(parsed.RefreshToken) ? token.RefreshToken : parsed.RefreshToken,
                ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + parsed.ExpiresIn * 1000,
            };

            SaveCache(refreshed);
            return refreshed;
        }
    }

    private static void SaveCache(CachedToken token)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var json = JsonSerializer.Serialize(token);
            File.WriteAllText(CachePath, json);
        }
        catch
        {
            // Caching the refreshed token is a best-effort optimization; if it fails,
            // the next poll simply refreshes again from the source credentials.
        }
    }
}
