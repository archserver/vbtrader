using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http;

namespace VBTrader.Infrastructure.Schwab;

public class SchwabTokenManager
{
    private readonly ILogger<SchwabTokenManager> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _tokensFilePath;

    public string? AccessToken { get; private set; }
    public string? RefreshToken { get; private set; }
    public string? IdToken { get; private set; }

    private DateTime _accessTokenIssued = DateTime.MinValue;
    private DateTime _refreshTokenIssued = DateTime.MinValue;

    private const int AccessTokenTimeoutSeconds = 1800; // 30 minutes
    private const int RefreshTokenTimeoutSeconds = 7 * 24 * 60 * 60; // 7 days

    public bool IsAccessTokenValid =>
        !string.IsNullOrEmpty(AccessToken) &&
        (DateTime.UtcNow - _accessTokenIssued).TotalSeconds < (AccessTokenTimeoutSeconds - 60);

    public bool IsRefreshTokenValid =>
        !string.IsNullOrEmpty(RefreshToken) &&
        (DateTime.UtcNow - _refreshTokenIssued).TotalSeconds < (RefreshTokenTimeoutSeconds - 1800);

    public SchwabTokenManager(ILogger<SchwabTokenManager> logger, HttpClient httpClient, string tokensFilePath = "schwab_tokens.json")
    {
        _logger = logger;
        _httpClient = httpClient;
        _tokensFilePath = tokensFilePath;
        LoadTokensFromFile();
    }

    private void LoadTokensFromFile()
    {
        try
        {
            if (!File.Exists(_tokensFilePath))
            {
                _logger.LogInformation("Token file does not exist: {FilePath}", _tokensFilePath);
                return;
            }

            var json = File.ReadAllText(_tokensFilePath);
            var tokenData = JsonSerializer.Deserialize<TokenData>(json);

            if (tokenData != null)
            {
                AccessToken = tokenData.TokenDictionary?.AccessToken;
                RefreshToken = tokenData.TokenDictionary?.RefreshToken;
                IdToken = tokenData.TokenDictionary?.IdToken;

                if (DateTime.TryParse(tokenData.AccessTokenIssued, out var accessIssued))
                    _accessTokenIssued = accessIssued.ToUniversalTime();

                if (DateTime.TryParse(tokenData.RefreshTokenIssued, out var refreshIssued))
                    _refreshTokenIssued = refreshIssued.ToUniversalTime();

                var accessDelta = AccessTokenTimeoutSeconds - (DateTime.UtcNow - _accessTokenIssued).TotalSeconds;
                var refreshDelta = RefreshTokenTimeoutSeconds - (DateTime.UtcNow - _refreshTokenIssued).TotalSeconds;

                _logger.LogInformation("Access token expires in {AccessDelta:F0} seconds", accessDelta);
                _logger.LogInformation("Refresh token expires in {RefreshDelta:F0} seconds", refreshDelta);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading tokens from file: {FilePath}", _tokensFilePath);
        }
    }

    private void SaveTokensToFile()
    {
        try
        {
            var tokenData = new TokenData
            {
                AccessTokenIssued = _accessTokenIssued.ToString("O"),
                RefreshTokenIssued = _refreshTokenIssued.ToString("O"),
                TokenDictionary = new TokenDictionary
                {
                    AccessToken = AccessToken,
                    RefreshToken = RefreshToken,
                    IdToken = IdToken
                }
            };

            var json = JsonSerializer.Serialize(tokenData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_tokensFilePath, json);

            _logger.LogInformation("Tokens saved to file: {FilePath}", _tokensFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving tokens to file: {FilePath}", _tokensFilePath);
        }
    }

    public async Task<bool> GetTokensFromAuthorizationCodeAsync(string appKey, string appSecret, string callbackUrl, string authorizationCode)
    {
        try
        {
            Console.WriteLine($"🔄 Token Exchange Request:");
            Console.WriteLine($"   Code: {authorizationCode}");
            Console.WriteLine($"   Callback URL: {callbackUrl}");

            // Create headers exactly like Python code
            var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{appKey}:{appSecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.schwabapi.com/v1/oauth/token");
            request.Headers.Add("Authorization", $"Basic {authString}");

            // Create form data exactly like Python code
            var formData = new List<KeyValuePair<string, string>>
            {
                new("grant_type", "authorization_code"),
                new("code", authorizationCode),
                new("redirect_uri", callbackUrl)
            };

            request.Content = new FormUrlEncodedContent(formData);
            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded");

            Console.WriteLine($"🚀 Sending token request to Schwab...");

            var response = await _httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"📡 Response Status: {response.StatusCode}");
            Console.WriteLine($"📄 Response Content: {responseContent}");

            if (response.IsSuccessStatusCode)
            {
                // Parse JSON response like Python: tD = response.json()
                using var jsonDoc = JsonDocument.Parse(responseContent);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("access_token", out var accessTokenElement) &&
                    root.TryGetProperty("refresh_token", out var refreshTokenElement))
                {
                    var accessToken = accessTokenElement.GetString();
                    var refreshToken = refreshTokenElement.GetString();

                    Console.WriteLine($"✅ Access Token received: {accessToken?[..20]}...");
                    Console.WriteLine($"✅ Refresh Token received: {refreshToken?[..20]}...");

                    var now = DateTime.UtcNow;
                    var tokenResponse = new TokenResponse
                    {
                        AccessToken = accessToken ?? "",
                        RefreshToken = refreshToken,
                        TokenType = root.TryGetProperty("token_type", out var typeElement) ? typeElement.GetString() ?? "Bearer" : "Bearer",
                        ExpiresIn = root.TryGetProperty("expires_in", out var expiresElement) ? expiresElement.GetInt32() : 1800,
                        Scope = root.TryGetProperty("scope", out var scopeElement) ? scopeElement.GetString() ?? "" : ""
                    };

                    SetTokens(now, now, tokenResponse);
                    _logger.LogInformation("Successfully obtained tokens from authorization code");
                    Console.WriteLine($"🎉 Token exchange successful!");
                    return true;
                }
            }
            else
            {
                _logger.LogError("Failed to get tokens from authorization code. Status: {StatusCode}, Content: {Content}",
                    response.StatusCode, responseContent);
                Console.WriteLine($"❌ Token exchange failed: {response.StatusCode}");
                Console.WriteLine($"❌ Error details: {responseContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tokens from authorization code");
        }

        return false;
    }

    public async Task<bool> RefreshAccessTokenAsync()
    {
        if (string.IsNullOrEmpty(RefreshToken))
        {
            _logger.LogWarning("Cannot refresh access token: no refresh token available");
            return false;
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.schwabapi.com/v1/oauth/token");
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", RefreshToken)
            });

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(jsonResponse);

                if (tokenResponse != null)
                {
                    var now = DateTime.UtcNow;
                    SetTokens(now, _refreshTokenIssued, tokenResponse);
                    _logger.LogInformation("Successfully refreshed access token");
                    return true;
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to refresh access token. Status: {StatusCode}, Content: {Content}",
                    response.StatusCode, errorContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing access token");
        }

        return false;
    }

    private void SetTokens(DateTime accessIssued, DateTime refreshIssued, TokenResponse tokenResponse)
    {
        AccessToken = tokenResponse.AccessToken;
        RefreshToken = tokenResponse.RefreshToken ?? RefreshToken; // Keep existing if not provided
        IdToken = tokenResponse.IdToken;
        _accessTokenIssued = accessIssued;
        _refreshTokenIssued = refreshIssued;

        SaveTokensToFile();
    }

    public async Task<bool> EnsureValidAccessTokenAsync()
    {
        if (IsAccessTokenValid)
            return true;

        if (IsRefreshTokenValid)
        {
            return await RefreshAccessTokenAsync();
        }

        _logger.LogWarning("Both access and refresh tokens are invalid. Re-authentication required.");
        return false;
    }

    private class TokenData
    {
        public string AccessTokenIssued { get; set; } = string.Empty;
        public string RefreshTokenIssued { get; set; } = string.Empty;
        public TokenDictionary? TokenDictionary { get; set; }
    }

    private class TokenDictionary
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? IdToken { get; set; }
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string Scope { get; set; } = string.Empty;
    }
}