using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        Console.WriteLine("🔍 Direct Schwab Token Exchange Test");
        Console.WriteLine("====================================");

        // From your token.txt file
        var authCode = "C0.b2F1dGgyLmJkYy5zY2h3YWIuY29t.q6vRyGVy7hcgi0tWDafMyAcLlPyj9a4WfHaUI0_KUu4";
        var callbackUrl = "https://127.0.0.1:3000";

        // You'll need to provide these - they're your actual Schwab Developer App credentials
        Console.Write("Enter your Schwab App Key: ");
        var appKey = Console.ReadLine();
        Console.Write("Enter your Schwab App Secret: ");
        var appSecret = Console.ReadLine();

        if (string.IsNullOrEmpty(appKey) || string.IsNullOrEmpty(appSecret))
        {
            Console.WriteLine("❌ App Key and App Secret are required");
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"🔑 Testing token exchange...");
        Console.WriteLine($"📋 Authorization Code: {authCode}");
        Console.WriteLine();

        using var httpClient = new HttpClient();

        try
        {
            var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{appKey}:{appSecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.schwabapi.com/v1/oauth/token");
            request.Headers.Add("Authorization", $"Basic {authString}");
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", authCode),
                new KeyValuePair<string, string>("redirect_uri", callbackUrl)
            });

            Console.WriteLine("🚀 Sending request to Schwab...");
            var response = await httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"📡 Response Status: {response.StatusCode}");
            Console.WriteLine($"📄 Response Body: {content}");

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("✅ SUCCESS! Token exchange worked!");
            }
            else
            {
                Console.WriteLine("❌ FAILED! Here's what Schwab returned:");
                Console.WriteLine($"   Status: {response.StatusCode}");
                Console.WriteLine($"   Content: {content}");

                // Common error interpretations
                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    Console.WriteLine();
                    Console.WriteLine("💡 This is likely due to:");
                    Console.WriteLine("   - Authorization code already used (they expire after one use)");
                    Console.WriteLine("   - Authorization code expired (they expire after ~10 minutes)");
                    Console.WriteLine("   - Wrong App Key/Secret combination");
                    Console.WriteLine("   - Callback URL mismatch");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Exception: {ex.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}