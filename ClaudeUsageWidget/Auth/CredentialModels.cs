using System.Text.Json.Serialization;

namespace ClaudeUsageWidget.Auth;

internal sealed class CredentialsFile
{
    [JsonPropertyName("claudeAiOauth")]
    public ClaudeAiOAuthSection? ClaudeAiOauth { get; set; }
}

internal sealed class ClaudeAiOAuthSection
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expiresAt")]
    public long ExpiresAt { get; set; }
}

internal sealed class CachedToken
{
    public string AccessToken { get; set; } = "";
    public string? RefreshToken { get; set; }
    public long ExpiresAt { get; set; }
}

internal sealed class RefreshTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expires_in")]
    public long ExpiresIn { get; set; }
}

internal sealed class OAuthErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

public sealed class CredentialException : Exception
{
    public CredentialException(string message) : base(message)
    {
    }
}
