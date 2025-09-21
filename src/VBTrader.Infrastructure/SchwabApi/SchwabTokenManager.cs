using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using VBTrader.Security.Cryptography;

namespace VBTrader.Infrastructure.SchwabApi;

public class SchwabTokenManager
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private const string TokenEndpoint = "https://api.schwabapi.com/v1/oauth/token";
    private const string AuthorizeEndpoint = "https://api.schwabapi.com/v1/oauth/authorize";

    public SchwabTokenManager(HttpClient httpClient, ILogger logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> InitializeTokensAsync(SchwabCredentials credentials)
    {
        // Check if we have valid tokens
        if (!string.IsNullOrEmpty(credentials.AccessToken) &&
            credentials.TokenExpiry.HasValue &&
            credentials.TokenExpiry.Value > DateTime.UtcNow.AddMinutes(5))
        {
            _logger.LogInformation("Using existing valid access token");
            return true;
        }

        // Try to refresh with existing refresh token
        if (!string.IsNullOrEmpty(credentials.RefreshToken))
        {
            _logger.LogInformation("Attempting to refresh access token");
            var refreshSuccess = await RefreshAccessTokenAsync(credentials);
            if (refreshSuccess)
                return true;
        }

        // Need to get new tokens through authorization flow
        _logger.LogInformation("Starting authorization flow for new tokens");
        return await AuthorizeNewTokensAsync(credentials);
    }

    public async Task<bool> RefreshAccessTokenAsync(SchwabCredentials credentials)
    {
        if (string.IsNullOrEmpty(credentials.RefreshToken))
        {
            _logger.LogWarning("No refresh token available");
            return false;
        }

        try
        {
            var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.AppKey}:{credentials.AppSecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", credentials.RefreshToken)
            });

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(content);

                if (tokenResponse != null)
                {
                    credentials.AccessToken = tokenResponse.access_token;
                    credentials.RefreshToken = tokenResponse.refresh_token ?? credentials.RefreshToken;
                    credentials.TokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.expires_in - 60); // 1 minute buffer
                    credentials.LastUpdated = DateTime.UtcNow;

                    _logger.LogInformation("Successfully refreshed access token");
                    return true;
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to refresh token: {StatusCode} - {Content}", response.StatusCode, errorContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing access token");
        }

        return false;
    }

    private async Task<bool> AuthorizeNewTokensAsync(SchwabCredentials credentials)
    {
        try
        {
            // Generate authorization URL
            var authUrl = $"{AuthorizeEndpoint}?client_id={credentials.AppKey}&redirect_uri={credentials.CallbackUrl}";

            _logger.LogInformation("Authorization required. Please visit: {AuthUrl}", authUrl);

            // For now, we'll need manual intervention
            // In a full implementation, you might:
            // 1. Launch a browser window
            // 2. Set up a local web server to capture the callback
            // 3. Extract the authorization code

            Console.WriteLine($"Please visit the following URL to authorize the application:");
            Console.WriteLine(authUrl);
            Console.WriteLine("After authorization, paste the full callback URL here:");

            var callbackUrl = Console.ReadLine();
            if (string.IsNullOrEmpty(callbackUrl))
            {
                _logger.LogError("No callback URL provided");
                return false;
            }

            // Extract authorization code from callback URL
            var uri = new Uri(callbackUrl);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var code = query["code"];

            if (string.IsNullOrEmpty(code))
            {
                _logger.LogError("No authorization code found in callback URL");
                return false;
            }

            // Exchange authorization code for tokens
            return await ExchangeCodeForTokensAsync(credentials, code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in authorization flow");
            return false;
        }
    }

    private async Task<bool> ExchangeCodeForTokensAsync(SchwabCredentials credentials, string authorizationCode)
    {
        try
        {
            var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.AppKey}:{credentials.AppSecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", authorizationCode),
                new KeyValuePair<string, string>("redirect_uri", credentials.CallbackUrl)
            });

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(content);

                if (tokenResponse != null)
                {
                    credentials.AccessToken = tokenResponse.access_token;
                    credentials.RefreshToken = tokenResponse.refresh_token;
                    credentials.TokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.expires_in - 60); // 1 minute buffer
                    credentials.LastUpdated = DateTime.UtcNow;

                    _logger.LogInformation("Successfully obtained new tokens");
                    return true;
                }
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to exchange code for tokens: {StatusCode} - {Content}", response.StatusCode, errorContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exchanging authorization code for tokens");
        }

        return false;
    }

    private class TokenResponse
    {
        public string access_token { get; set; } = string.Empty;
        public string? refresh_token { get; set; }
        public int expires_in { get; set; }
        public string token_type { get; set; } = string.Empty;
        public string? scope { get; set; }
    }
}