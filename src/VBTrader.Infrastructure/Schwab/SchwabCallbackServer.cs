using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Web;

namespace VBTrader.Infrastructure.Schwab;

public class SchwabCallbackServer : IDisposable
{
    private readonly ILogger<SchwabCallbackServer> _logger;
    private HttpListener? _listener;
    private bool _isListening;
    private TaskCompletionSource<string>? _authorizationCodeTcs;

    public SchwabCallbackServer(ILogger<SchwabCallbackServer> logger)
    {
        _logger = logger;
    }

    public async Task<string?> StartAndWaitForAuthorizationCodeAsync(string callbackUrl, string appKey, CancellationToken cancellationToken = default)
    {
        if (_isListening)
            throw new InvalidOperationException("Server is already listening");

        try
        {
            // Parse the callback URL to get the listening address
            var uri = new Uri(callbackUrl);
            var prefix = $"{uri.Scheme}://{uri.Host}:{uri.Port}/";

            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);

            // If HTTPS, validate SSL certificate setup
            if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Attempting to start HTTPS callback server on port {Port}", uri.Port);
                await ValidateHttpsSetup(uri.Host, uri.Port);
            }

            _listener.Start();
            _isListening = true;

            _logger.LogInformation("Callback server started listening on: {Prefix}", prefix);

            // Create the authorization URL and open it in the browser
            var authUrl = $"https://api.schwabapi.com/v1/oauth/authorize?client_id={appKey}&redirect_uri={Uri.EscapeDataString(callbackUrl)}";
            _logger.LogInformation("Opening authorization URL: {AuthUrl}", authUrl);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = authUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not automatically open browser. Please manually navigate to: {AuthUrl}", authUrl);
            }

            // Wait for the callback
            _authorizationCodeTcs = new TaskCompletionSource<string>();

            // Start listening for requests
            _ = Task.Run(async () =>
            {
                try
                {
                    while (_isListening && !cancellationToken.IsCancellationRequested)
                    {
                        var context = await _listener.GetContextAsync();
                        await HandleCallbackRequest(context);
                    }
                }
                catch (Exception ex) when (!_isListening)
                {
                    // Expected when stopping the listener
                    _logger.LogDebug("Listener stopped: {Message}", ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in callback server");
                    _authorizationCodeTcs?.TrySetException(ex);
                }
            }, cancellationToken);

            // Wait for the authorization code with timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                combinedCts.Token.Register(() => _authorizationCodeTcs?.TrySetCanceled());
                return await _authorizationCodeTcs.Task;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Authorization callback timed out or was cancelled");
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting callback server");
            return null;
        }
        finally
        {
            Stop();
        }
    }

    private async Task HandleCallbackRequest(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            _logger.LogInformation("OAuth callback received");

            // Extract the authorization code from the query string
            var query = request.Url?.Query;
            var fullUrl = request.Url?.ToString() ?? "";

            if (!string.IsNullOrEmpty(query))
            {
                var queryParams = HttpUtility.ParseQueryString(query);
                var error = queryParams["error"];

                // Extract code using Python-style parsing: code=...%40 + @ or code=...@
                string? code = null;
                if (fullUrl.Contains("code="))
                {
                    var codeStart = fullUrl.IndexOf("code=") + 5;

                    // Check for %40 (URL encoded @)
                    var codeEnd = fullUrl.IndexOf("%40", codeStart);
                    if (codeEnd > codeStart)
                    {
                        var rawCode = fullUrl[codeStart..codeEnd] + "@";
                        // URL decode the authorization code as required by Schwab documentation
                        code = Uri.UnescapeDataString(rawCode);
                        Console.WriteLine($"🔍 Raw code (from %40): {rawCode}");
                        Console.WriteLine($"🔍 Decoded code: {code}");
                    }
                    else
                    {
                        // Check for @ (already unencoded)
                        codeEnd = fullUrl.IndexOf("@", codeStart);
                        if (codeEnd > codeStart)
                        {
                            var rawCode = fullUrl[codeStart..(codeEnd + 1)]; // Include the @
                            code = rawCode; // No decoding needed
                            Console.WriteLine($"🔍 Raw code (with @): {rawCode}");
                            Console.WriteLine($"🔍 Final code: {code}");
                        }
                        else
                        {
                            // Fallback: look for session parameter or end of query
                            codeEnd = fullUrl.IndexOf("&", codeStart);
                            if (codeEnd == -1) codeEnd = fullUrl.Length;

                            var rawCode = fullUrl[codeStart..codeEnd];
                            code = rawCode.EndsWith("@") ? rawCode : rawCode + "@";
                            Console.WriteLine($"🔍 Raw code (fallback): {rawCode}");
                            Console.WriteLine($"🔍 Final code: {code}");
                        }
                    }
                }


                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogError("Authorization error received: {Error}", error);
                    Console.WriteLine($"❌ Authorization failed: {error}");

                    // Send error response to browser
                    var errorResponse = $"<html><body><h2>Authorization Error</h2><p>{error}</p><p>You may close this window.</p></body></html>";
                    await SendResponse(response, errorResponse, HttpStatusCode.BadRequest);

                    _authorizationCodeTcs?.TrySetException(new Exception($"Authorization error: {error}"));
                    return;
                }

                if (!string.IsNullOrEmpty(code))
                {
                    _logger.LogInformation("Authorization code received successfully");
                    Console.WriteLine($"✅ Authorization successful! Exchanging code for tokens...");

                    // Send success response to browser
                    var successResponse = "<html><body><h2>Authorization Successful</h2><p>You may now close this window and return to the VBTrader application.</p></body></html>";
                    await SendResponse(response, successResponse, HttpStatusCode.OK);

                    // Keep the full authorization code including @ symbol as required by Schwab
                    _authorizationCodeTcs?.TrySetResult(code);
                    return;
                }
            }

            // No code found - send error response
            var noCodeResponse = "<html><body><h2>No Authorization Code</h2><p>No authorization code was found in the callback. Please try again.</p></body></html>";
            await SendResponse(response, noCodeResponse, HttpStatusCode.BadRequest);

            _authorizationCodeTcs?.TrySetException(new Exception("No authorization code received in callback"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling callback request");
            _authorizationCodeTcs?.TrySetException(ex);
        }
    }

    private static async Task SendResponse(HttpListenerResponse response, string content, HttpStatusCode statusCode)
    {
        try
        {
            response.StatusCode = (int)statusCode;
            response.ContentType = "text/html";

            var buffer = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer);
            response.OutputStream.Close();
        }
        catch (Exception)
        {
            // Ignore errors when sending response - the important part is getting the code
        }
    }

    public void Stop()
    {
        if (_isListening)
        {
            _isListening = false;
            _listener?.Stop();
            _listener?.Close();
            _logger.LogInformation("Callback server stopped");
        }
    }

    public void Dispose()
    {
        Stop();
        _listener?.Close();
        _authorizationCodeTcs?.TrySetCanceled();
    }

    private async Task ValidateHttpsSetup(string host, int port)
    {
        try
        {
            _logger.LogInformation("Validating HTTPS setup for {Host}:{Port}", host, port);

            // Check if we can find a suitable certificate
            var certificate = FindOrCreateDevelopmentCertificate(host);
            if (certificate == null)
            {
                _logger.LogWarning("No suitable SSL certificate found for {Host}. Creating development certificate...", host);
                await CreateDevelopmentCertificate();
                certificate = FindOrCreateDevelopmentCertificate(host);
            }

            if (certificate != null)
            {
                _logger.LogInformation("Found SSL certificate: {Subject} (Thumbprint: {Thumbprint})",
                    certificate.Subject, certificate.Thumbprint);

                // Attempt to bind certificate to port (this may require admin privileges)
                await EnsureCertificateBinding(host, port, certificate);
            }
            else
            {
                throw new InvalidOperationException($"Could not create or find SSL certificate for {host}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate HTTPS setup");
            throw new InvalidOperationException(
                $"HTTPS setup failed. Please ensure you have a valid SSL certificate for {host}:{port}. " +
                "You may need to run as administrator or manually set up the certificate binding.");
        }
    }

    private X509Certificate2? FindOrCreateDevelopmentCertificate(string host)
    {
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);

            // Look for existing development certificates
            // Try to find by exact host first, then fallback to localhost for 127.0.0.1
            var searchHosts = new[] { host, "localhost" };
            X509Certificate2? certificate = null;

            foreach (var searchHost in searchHosts)
            {
                certificate = store.Certificates
                    .Find(X509FindType.FindBySubjectName, searchHost, false)
                    .OfType<X509Certificate2>()
                    .Where(cert => cert.HasPrivateKey &&
                                  cert.NotAfter > DateTime.Now &&
                                  cert.NotBefore < DateTime.Now)
                    .OrderByDescending(cert => cert.NotAfter)
                    .FirstOrDefault();

                if (certificate != null)
                {
                    _logger.LogInformation("Found certificate for {SearchHost} (requested: {Host})", searchHost, host);
                    break;
                }
            }

            return certificate;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error searching for existing certificates");
            return null;
        }
    }

    private async Task CreateDevelopmentCertificate()
    {
        try
        {
            _logger.LogInformation("Creating development SSL certificate...");

            // Use dotnet dev-certs to create the certificate
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "dev-certs https --trust",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("Development certificate created successfully");
            }
            else
            {
                var error = await process.StandardError.ReadToEndAsync();
                _logger.LogWarning("dotnet dev-certs command failed: {Error}", error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create development certificate");
        }
    }

    private async Task EnsureCertificateBinding(string host, int port, X509Certificate2 certificate)
    {
        try
        {
            _logger.LogInformation("Checking certificate binding for {Host}:{Port}", host, port);

            // Check if binding already exists
            var bindingExists = await CheckExistingBinding(host, port);
            if (bindingExists)
            {
                _logger.LogInformation("Certificate binding already exists for {Host}:{Port}", host, port);
                return;
            }

            // Try to create the binding (may require admin privileges)
            await CreateCertificateBinding(host, port, certificate);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not verify/create certificate binding. You may need to run as administrator.");
            // Don't throw here - the binding might not be strictly necessary in all cases
        }
    }

    private async Task<bool> CheckExistingBinding(string host, int port)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"http show sslcert ipport={host}:{port}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            return process.ExitCode == 0 && !string.IsNullOrEmpty(output);
        }
        catch
        {
            return false;
        }
    }

    private async Task CreateCertificateBinding(string host, int port, X509Certificate2 certificate)
    {
        try
        {
            var appId = "{12345678-1234-5678-9abc-123456789012}"; // Random GUID for this application
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"http add sslcert ipport={host}:{port} certhash={certificate.Thumbprint} appid={appId}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("Successfully created certificate binding for {Host}:{Port}", host, port);
            }
            else
            {
                var error = await process.StandardError.ReadToEndAsync();
                _logger.LogWarning("Failed to create certificate binding: {Error}", error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception while creating certificate binding");
        }
    }
}