using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🧪 Manual Token Exchange Test");
        Console.WriteLine("=============================");

        // Your captured authorization code
        var authCode = "C0.b2F1dGgyLmJkYy5zY2h3YWIuY29t.q6vRyGVy7hcgi0tWDafMyAcLlPyj9a4WfHaUI0_KUu4"; // Removed @ suffix
        var appKey = "YOUR_APP_KEY"; // Replace with your actual app key
        var appSecret = "YOUR_APP_SECRET"; // Replace with your actual app secret
        var callbackUrl = "https://127.0.0.1:3000";

        Console.WriteLine($"Authorization Code: {authCode}");
        Console.WriteLine($"App Key: {appKey}");
        Console.WriteLine($"Callback URL: {callbackUrl}");
        Console.WriteLine();

        using var httpClient = new HttpClient();

        try
        {
            // Create the Basic Auth header
            var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{appKey}:{appSecret}"));

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.schwabapi.com/v1/oauth/token");
            request.Headers.Add("Authorization", $"Basic {authString}");
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", authCode),
                new KeyValuePair<string, string>("redirect_uri", callbackUrl)
            });

            Console.WriteLine("🚀 Making token exchange request...");
            Console.WriteLine($"URL: https://api.schwabapi.com/v1/oauth/token");
            Console.WriteLine($"Headers: Authorization: Basic [HIDDEN]");
            Console.WriteLine($"Form Data:");
            Console.WriteLine($"  grant_type: authorization_code");
            Console.WriteLine($"  code: {authCode}");
            Console.WriteLine($"  redirect_uri: {callbackUrl}");
            Console.WriteLine();

            var response = await httpClient.SendAsync(request);

            Console.WriteLine($"📡 Response Status: {response.StatusCode}");

            var responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"📄 Response Content: {responseContent}");

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("✅ Token exchange successful!");
            }
            else
            {
                Console.WriteLine("❌ Token exchange failed!");
                Console.WriteLine();
                Console.WriteLine("🔍 Debugging Info:");
                Console.WriteLine($"- Status Code: {response.StatusCode}");
                Console.WriteLine($"- Reason Phrase: {response.ReasonPhrase}");
                Console.WriteLine($"- Response Headers:");
                foreach (var header in response.Headers)
                {
                    Console.WriteLine($"  {header.Key}: {string.Join(", ", header.Value)}");
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