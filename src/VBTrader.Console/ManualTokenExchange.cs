using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using VBTrader.Infrastructure.Database;
using VBTrader.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace VBTrader.Console;

public static class ManualTokenExchange
{
    public static async Task ExchangeAuthorizationCode()
    {
        System.Console.WriteLine("=== Manual Schwab Token Exchange ===");
        System.Console.WriteLine();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json")
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var options = new DbContextOptionsBuilder<VBTraderDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        using var context = new VBTraderDbContext(options);
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<UserService>();
        var userService = new UserService(context, null!, logger);

        try
        {
            // Get current user (assume user ID 9 from the logs)
            var user = await context.Users.FirstOrDefaultAsync(u => u.UserId == 9);
            if (user == null)
            {
                System.Console.WriteLine("❌ User not found");
                return;
            }

            System.Console.WriteLine($"Found user: {user.Username}");

            // Get stored credentials
            var credentials = await userService.GetSchwabCredentialsAsync(user.UserId);
            if (credentials == null)
            {
                System.Console.WriteLine("❌ No Schwab credentials found");
                return;
            }

            System.Console.WriteLine($"App Key: {credentials.Value.appKey[..8]}...");
            System.Console.WriteLine($"Callback URL: {credentials.Value.callbackUrl}");
            System.Console.WriteLine();

            // Get authorization code from user
            System.Console.WriteLine("Enter the authorization code from the browser URL:");
            System.Console.WriteLine("(Look for ?code=... in the URL)");
            System.Console.Write("Authorization Code: ");
            var authCode = System.Console.ReadLine();

            if (string.IsNullOrWhiteSpace(authCode))
            {
                System.Console.WriteLine("❌ Authorization code cannot be empty");
                return;
            }

            // Clean up the code (remove URL encoding, @ suffix, etc.)
            authCode = System.Web.HttpUtility.UrlDecode(authCode);
            if (authCode.EndsWith("@"))
                authCode = authCode[..^1];

            System.Console.WriteLine($"Cleaned authorization code: {authCode[..20]}...");
            System.Console.WriteLine();

            // Exchange for tokens
            System.Console.WriteLine("Exchanging authorization code for tokens...");
            var success = await ExchangeCodeForTokens(
                credentials.Value.appKey,
                credentials.Value.appSecret,
                credentials.Value.callbackUrl,
                authCode);

            if (success)
            {
                System.Console.WriteLine("✅ Token exchange successful!");
                System.Console.WriteLine("Schwab API authentication is working correctly!");
            }
            else
            {
                System.Console.WriteLine("❌ Token exchange failed");
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"❌ Error: {ex.Message}");
        }

        System.Console.WriteLine();
        System.Console.WriteLine("Press any key to continue...");
        System.Console.ReadKey();
    }

    private static async Task<bool> ExchangeCodeForTokens(string appKey, string appSecret, string callbackUrl, string authorizationCode)
    {
        try
        {
            using var httpClient = new HttpClient();

            // Prepare the token request
            var tokenRequest = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", authorizationCode),
                new KeyValuePair<string, string>("redirect_uri", callbackUrl),
                new KeyValuePair<string, string>("client_id", appKey)
            });

            // Set up basic authentication
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{appKey}:{appSecret}"));
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            System.Console.WriteLine("Making token exchange request to Schwab...");

            // Make the request
            var response = await httpClient.PostAsync("https://api.schwabapi.com/v1/oauth/token", tokenRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            System.Console.WriteLine($"Response Status: {response.StatusCode}");
            System.Console.WriteLine($"Response Content: {responseContent}");

            if (response.IsSuccessStatusCode)
            {
                var tokenResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

                if (tokenResponse.TryGetProperty("access_token", out var accessToken))
                {
                    System.Console.WriteLine($"✅ Access Token received: {accessToken.GetString()![..20]}...");
                }

                if (tokenResponse.TryGetProperty("refresh_token", out var refreshToken))
                {
                    System.Console.WriteLine($"✅ Refresh Token received: {refreshToken.GetString()![..20]}...");
                }

                return true;
            }
            else
            {
                System.Console.WriteLine($"❌ Token exchange failed: {response.StatusCode}");
                System.Console.WriteLine($"Error details: {responseContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"❌ Exception during token exchange: {ex.Message}");
            return false;
        }
    }
}