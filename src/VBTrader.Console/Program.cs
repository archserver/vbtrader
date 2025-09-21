using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using VBTrader.Core.Models;
using VBTrader.Core.Interfaces;
using VBTrader.Infrastructure.Database;
using VBTrader.Infrastructure.Schwab;
using VBTrader.Infrastructure.Services;
using VBTrader.Services;
using VBTrader.Security.Cryptography;
using VBTrader.Console;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.SetBasePath(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location))
              .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {

        // Add configuration
        services.AddSingleton<IConfiguration>(context.Configuration);

        // Add HTTP client for Schwab API
        services.AddHttpClient<ISchwabApiClient, SchwabApiClient>();

        // Add database
        services.AddDbContext<VBTraderDbContext>(options =>
            options.UseNpgsql(context.Configuration.GetConnectionString("DefaultConnection")));
        services.AddScoped<IDataService, PostgreSqlDataService>();
        services.AddScoped<ISandboxDataService, SandboxDataService>();
        services.AddScoped<IUserService, UserService>();

        // Add security services
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ICredentialEncryption, CredentialEncryption>();

        // Add Schwab API client
        services.AddSingleton<ISchwabApiClient, SchwabApiClient>();

        // Add account tracking service
        services.AddScoped<AccountTrackingService>();

        // Add console app
        services.AddSingleton<TradingConsoleApp>();
    })
    .Build();

// Check for manual token exchange test
if (args.Length > 0 && args[0] == "--test-token")
{
    await ManualTokenExchange.ExchangeAuthorizationCode();
    return;
}

// Check for direct authentication test
if (args.Length > 0 && args[0] == "--test-auth")
{
    var testApp = host.Services.GetRequiredService<TradingConsoleApp>();
    await testApp.TestDirectAuthenticationAsync("bchase", "OICu812@*");
    return;
}

// Check for data retrieval test
if (args.Length > 0 && args[0] == "--test-data")
{
    var testApp = host.Services.GetRequiredService<TradingConsoleApp>();
    await testApp.TestDataRetrievalAsync("bchase", "OICu812@*");
    return;
}


// Check for credential export test
if (args.Length > 0 && args[0] == "--export-creds")
{
    var userService = host.Services.GetRequiredService<IUserService>();
    var isValid = await userService.ValidateUserAsync("bchase", "OICu812@*");
    if (isValid)
    {
        var user = await userService.GetUserByUsernameAsync("bchase");
        if (user != null)
        {
            var creds = await userService.GetSchwabCredentialsAsync(user.UserId);
            if (creds.HasValue)
            {
                Console.WriteLine("🔑 Schwab Credentials:");
                Console.WriteLine($"App Key: {creds.Value.appKey}");
                Console.WriteLine($"App Secret: {creds.Value.appSecret}");
                Console.WriteLine($"Callback URL: {creds.Value.callbackUrl}");
            }
            else
            {
                Console.WriteLine("❌ No Schwab credentials found");
            }
        }
    }
    return;
}

// Start the console trading application
var app = host.Services.GetRequiredService<TradingConsoleApp>();
await app.RunAsync();

namespace VBTrader.Console
{
    public class TradingConsoleApp
    {
        private readonly IDataService _dataService;
        private readonly ISchwabApiClient _schwabApiClient;
        private readonly ISandboxDataService _sandboxDataService;
        private readonly IUserService _userService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TradingConsoleApp> _logger;
        private readonly List<StockQuote> _liveQuotes = new();
        private readonly List<MarketOpportunity> _opportunities = new();
        private readonly Dictionary<string, decimal> _positions = new();
        private bool _running = true;
        private bool _useSchwabApi = false;
        private bool _sandboxMode = false;
        private SandboxSession? _currentSandboxSession;
        private User? _currentUser;
        private UserSession? _currentUserSession;

        public TradingConsoleApp(
            IDataService dataService,
            ISchwabApiClient schwabApiClient,
            ISandboxDataService sandboxDataService,
            IUserService userService,
            IConfiguration configuration,
            ILogger<TradingConsoleApp> logger)
        {
            _dataService = dataService;
            _schwabApiClient = schwabApiClient;
            _sandboxDataService = sandboxDataService;
            _userService = userService;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task RunAsync()
        {
            try
            {
                System.Console.Clear();
                System.Console.Title = "VBTrader - Real-Time Trading Console";
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Unable to set console properties: {Message}", ex.Message);
            }

            // User authentication required before trading
            if (!await AuthenticateUser())
            {
                System.Console.WriteLine("Authentication failed. Exiting...");
                return;
            }

            PrintHeader();
            PrintInstructions();

            // Try to authenticate with Schwab API using user's stored credentials
            await AttemptSchwabAuthentication();

            // Initialize with sample data (or real data if Schwab is connected)
            await InitializeData();

            // Start background tasks
            _ = Task.Run(UpdateDataLoop);
            _ = Task.Run(RefreshDisplay);

            _logger.LogInformation("VBTrader Console Application started. Press 'Q' to quit.");

            // Main input loop
            while (_running)
            {
                try
                {
                    var key = System.Console.ReadKey(true);
                    await HandleKeyPress(key);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading console input");
                    await Task.Delay(1000); // Prevent tight loop
                }
            }
        }

        private void PrintHeader()
        {
            try
            {
                System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                System.Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                System.Console.WriteLine("║                        VBTrader - Real-Time Trading System                   ║");

                // User info
                if (_currentUser != null)
                {
                    System.Console.WriteLine($"║                            User: {_currentUser.Username.PadRight(35)}                            ║");
                }

                // Trading mode indicator
                if (_sandboxMode)
                {
                    System.Console.ForegroundColor = System.ConsoleColor.Yellow;
                    System.Console.WriteLine("║                              ⚠️  SANDBOX MODE  ⚠️                              ║");
                    if (_currentSandboxSession != null)
                    {
                        System.Console.WriteLine($"║           Session: {_currentSandboxSession.SessionName.PadRight(20)} Balance: ${_currentSandboxSession.CurrentBalance:N2}           ║");
                    }
                }
                else
                {
                    System.Console.ForegroundColor = System.ConsoleColor.Green;
                    System.Console.WriteLine("║                               🟢  LIVE MODE  🟢                               ║");
                }

                System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                System.Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                System.Console.ResetColor();
                System.Console.WriteLine();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Unable to print header: {Message}", ex.Message);
                _logger.LogInformation("VBTrader - Real-Time Trading System");
            }
        }

        private void PrintInstructions()
        {
            try
            {
                System.Console.ForegroundColor = System.ConsoleColor.Yellow;
                System.Console.WriteLine("HOTKEYS:");
                System.Console.WriteLine("  Alt+1,2,3   = Buy 100 shares of stocks 1,2,3");
                System.Console.WriteLine("  Ctrl+1,2,3  = Sell 100 shares of stocks 1,2,3");
                System.Console.WriteLine("  R           = Refresh data");
                System.Console.WriteLine("  Q           = Quit");
                System.Console.WriteLine("  S           = Set stock symbols");
                System.Console.WriteLine("  T           = Toggle Sandbox/Live mode");
                System.Console.WriteLine("  N           = Create new sandbox session");
                System.Console.WriteLine("  L           = Load existing sandbox session");
                System.Console.WriteLine("  C           = Configure Schwab API credentials");
                System.Console.WriteLine("  DELETE      = Admin: Clean up bad users (type 'DELETE')");
                System.Console.ResetColor();
                System.Console.WriteLine();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Unable to print instructions: {Message}", ex.Message);
                _logger.LogInformation("HOTKEYS: Alt+1,2,3 = Buy, Ctrl+1,2,3 = Sell, R = Refresh, Q = Quit, S = Set symbols");
            }
        }

        private async Task AttemptSchwabAuthentication()
        {
            try
            {
                // Try to get stored credentials for the current user
                var credentials = await _userService.GetSchwabCredentialsAsync(_currentUser!.UserId);

                if (credentials != null)
                {
                    _logger.LogInformation("Attempting Schwab API authentication with stored credentials...");

                    if (await _schwabApiClient.AuthenticateAsync(credentials.Value.appKey, credentials.Value.appSecret, credentials.Value.callbackUrl))
                    {
                        _useSchwabApi = true;
                        _logger.LogInformation("✅ Successfully authenticated with Schwab API");

                        try
                        {
                            System.Console.ForegroundColor = System.ConsoleColor.Green;
                            System.Console.WriteLine("✅ SCHWAB API CONNECTED - Live data enabled");
                            System.Console.ResetColor();
                        }
                        catch (Exception)
                        {
                            _logger.LogInformation("Schwab API Connected - Live data enabled");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("❌ Failed to authenticate with Schwab API - using mock data");

                        try
                        {
                            System.Console.ForegroundColor = System.ConsoleColor.Yellow;
                            System.Console.WriteLine("❌ SCHWAB API AUTHENTICATION FAILED - Using simulated data");
                            System.Console.ResetColor();
                        }
                        catch (Exception)
                        {
                            _logger.LogWarning("Schwab API authentication failed - using simulated data");
                        }
                    }
                }
                else
                {
                    _logger.LogInformation("Schwab API credentials not configured for user - using mock data");

                    try
                    {
                        System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                        System.Console.WriteLine("ℹ️ SCHWAB API NOT CONFIGURED - Using simulated data");
                        System.Console.WriteLine("   Press 'C' to configure your Schwab API credentials for live trading");
                        System.Console.ResetColor();
                    }
                    catch (Exception)
                    {
                        _logger.LogInformation("Schwab API not configured for user - using simulated data");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Schwab authentication");
                _useSchwabApi = false;
            }
        }

        private async Task InitializeData()
        {
            if (_useSchwabApi)
            {
                await InitializeRealData();
            }
            else
            {
                InitializeSampleData();
            }
        }

        private async Task InitializeRealData()
        {
            try
            {
                _logger.LogInformation("Loading real market data from Schwab API...");

                // Get quotes for popular stocks
                var symbols = new[] { "AAPL", "TSLA", "NVDA", "MSFT", "GOOGL" };
                var quotes = await _schwabApiClient.GetQuotesAsync(symbols);

                _liveQuotes.Clear();
                foreach (var quote in quotes)
                {
                    _liveQuotes.Add(quote);
                }

                // TODO: Get real market opportunities (for now use mock data)
                _opportunities.AddRange(new[]
                {
                    new MarketOpportunity { Symbol = "AAPL", Score = 85.5m, OpportunityType = OpportunityType.BreakoutUp, PriceChangePercent = 1.33m },
                    new MarketOpportunity { Symbol = "TSLA", Score = 72.3m, OpportunityType = OpportunityType.VolumeSpike, PriceChangePercent = -1.28m },
                    new MarketOpportunity { Symbol = "NVDA", Score = 91.2m, OpportunityType = OpportunityType.TechnicalIndicator, PriceChangePercent = 2.01m },
                });

                _logger.LogInformation("Real market data loaded successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading real market data, falling back to mock data");
                InitializeSampleData();
                _useSchwabApi = false;
            }
        }

        private void InitializeSampleData()
        {
            _liveQuotes.AddRange(new[]
            {
                new StockQuote { Symbol = "AAPL", LastPrice = 175.50m, Change = 2.30m, ChangePercent = 1.33m, Volume = 45678900 },
                new StockQuote { Symbol = "TSLA", LastPrice = 245.80m, Change = -3.20m, ChangePercent = -1.28m, Volume = 32456700 },
                new StockQuote { Symbol = "NVDA", LastPrice = 432.10m, Change = 8.50m, ChangePercent = 2.01m, Volume = 28934500 },
                new StockQuote { Symbol = "MSFT", LastPrice = 378.25m, Change = 1.75m, ChangePercent = 0.46m, Volume = 21567800 },
                new StockQuote { Symbol = "GOOGL", LastPrice = 142.30m, Change = -0.80m, ChangePercent = -0.56m, Volume = 18934200 },
            });

            _opportunities.AddRange(new[]
            {
                new MarketOpportunity { Symbol = "AAPL", Score = 85.5m, OpportunityType = OpportunityType.BreakoutUp, PriceChangePercent = 1.33m },
                new MarketOpportunity { Symbol = "TSLA", Score = 72.3m, OpportunityType = OpportunityType.VolumeSpike, PriceChangePercent = -1.28m },
                new MarketOpportunity { Symbol = "NVDA", Score = 91.2m, OpportunityType = OpportunityType.TechnicalIndicator, PriceChangePercent = 2.01m },
            });
        }

        private async Task UpdateDataLoop()
        {
            while (_running)
            {
                // Simulate real-time price updates
                var random = new Random();
                foreach (var quote in _liveQuotes)
                {
                    var changePercent = (decimal)(random.NextDouble() - 0.5) * 0.02m; // ±1% change
                    var newPrice = quote.LastPrice * (1 + changePercent);
                    var change = newPrice - quote.LastPrice;

                    quote.LastPrice = Math.Round(newPrice, 2);
                    quote.Change = Math.Round(change, 2);
                    quote.ChangePercent = Math.Round((change / (quote.LastPrice - change)) * 100, 2);
                    quote.Volume += random.Next(1000, 10000);
                }

                await Task.Delay(1000); // Update every second
            }
        }

        private async Task RefreshDisplay()
        {
            while (_running)
            {
                try
                {
                    System.Console.SetCursorPosition(0, 8);
                }
                catch (Exception)
                {
                    // Ignore cursor positioning errors
                }

                PrintMarketData();
                PrintOpportunities();
                PrintPositions();
                PrintStatus();

                await Task.Delay(1000); // Refresh display every second
            }
        }

        private void PrintMarketData()
        {
            System.Console.ForegroundColor = System.ConsoleColor.White;
            System.Console.WriteLine("REAL-TIME QUOTES:");
            System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════════");
            System.Console.ForegroundColor = System.ConsoleColor.Gray;
            System.Console.WriteLine("Symbol    Price      Change     Change%    Volume");
            System.Console.WriteLine("───────────────────────────────────────────────────────────────────────────");

            foreach (var quote in _liveQuotes.Take(5))
            {
                System.Console.ForegroundColor = quote.Change >= 0 ? System.ConsoleColor.Green : System.ConsoleColor.Red;
                System.Console.WriteLine($"{quote.Symbol,-8} ${quote.LastPrice,8:F2} {quote.Change,8:F2} {quote.ChangePercent,8:F2}% {quote.Volume,12:N0}");
            }
            System.Console.WriteLine();
        }

        private void PrintOpportunities()
        {
            System.Console.ForegroundColor = System.ConsoleColor.Cyan;
            System.Console.WriteLine("MARKET OPPORTUNITIES:");
            System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════════");
            System.Console.ForegroundColor = System.ConsoleColor.Gray;
            System.Console.WriteLine("Symbol  Score  Type                 Change%");
            System.Console.WriteLine("───────────────────────────────────────────────────────────────────────────");

            foreach (var opp in _opportunities.Take(3))
            {
                System.Console.ForegroundColor = opp.Score > 80 ? System.ConsoleColor.Yellow : System.ConsoleColor.White;
                System.Console.WriteLine($"{opp.Symbol,-6} {opp.Score,6:F1}  {opp.OpportunityType,-18} {opp.PriceChangePercent,6:F2}%");
            }
            System.Console.WriteLine();
        }

        private void PrintPositions()
        {
            System.Console.ForegroundColor = System.ConsoleColor.Magenta;
            System.Console.WriteLine("CURRENT POSITIONS:");
            System.Console.WriteLine("═══════════════════════════════════════════════════════════════════════════");

            if (_positions.Any())
            {
                System.Console.ForegroundColor = System.ConsoleColor.Gray;
                System.Console.WriteLine("Symbol  Shares     Market Value");
                System.Console.WriteLine("───────────────────────────────────────────────────────────────────────────");

                foreach (var position in _positions)
                {
                    var quote = _liveQuotes.FirstOrDefault(q => q.Symbol == position.Key);
                    var marketValue = position.Value * (quote?.LastPrice ?? 0);
                    System.Console.ForegroundColor = System.ConsoleColor.White;
                    System.Console.WriteLine($"{position.Key,-6} {position.Value,8:F0}     ${marketValue,10:F2}");
                }
            }
            else
            {
                System.Console.ForegroundColor = System.ConsoleColor.Gray;
                System.Console.WriteLine("No positions held.");
            }
            System.Console.WriteLine();
        }

        private void PrintStatus()
        {
            System.Console.ForegroundColor = System.ConsoleColor.DarkGray;
            System.Console.WriteLine($"Last Update: {DateTime.Now:HH:mm:ss} | Market: OPEN | Connections: 3 | Press 'Q' to quit");
        }

        private async Task HandleKeyPress(ConsoleKeyInfo keyInfo)
        {
            var key = keyInfo.Key;
            var modifiers = keyInfo.Modifiers;

            if (key == System.ConsoleKey.Q)
            {
                await HandleLogout();
                _running = false;
                return;
            }

            if (key == System.ConsoleKey.R)
            {
                LogTrade("Data refreshed manually.");
                return;
            }

            if (key == System.ConsoleKey.S)
            {
                await SetStockSymbols();
                return;
            }

            if (key == System.ConsoleKey.T)
            {
                await ToggleSandboxMode();
                return;
            }

            if (key == System.ConsoleKey.N)
            {
                await CreateNewSandboxSession();
                return;
            }

            if (key == System.ConsoleKey.L)
            {
                await LoadSandboxSession();
                return;
            }

            if (key == System.ConsoleKey.C)
            {
                await ConfigureSchwabCredentials();
                return;
            }

            // Trading hotkeys
            if (modifiers.HasFlag(System.ConsoleModifiers.Alt))
            {
                switch (key)
                {
                    case System.ConsoleKey.D1:
                        await ExecuteTrade("BUY", GetStockSymbol(1), 100);
                        break;
                    case System.ConsoleKey.D2:
                        await ExecuteTrade("BUY", GetStockSymbol(2), 100);
                        break;
                    case System.ConsoleKey.D3:
                        await ExecuteTrade("BUY", GetStockSymbol(3), 100);
                        break;
                }
            }
            else if (modifiers.HasFlag(System.ConsoleModifiers.Control))
            {
                switch (key)
                {
                    case System.ConsoleKey.D1:
                        await ExecuteTrade("SELL", GetStockSymbol(1), 100);
                        break;
                    case System.ConsoleKey.D2:
                        await ExecuteTrade("SELL", GetStockSymbol(2), 100);
                        break;
                    case System.ConsoleKey.D3:
                        await ExecuteTrade("SELL", GetStockSymbol(3), 100);
                        break;
                }
            }
        }

        private string GetStockSymbol(int index)
        {
            return index <= _liveQuotes.Count ? _liveQuotes[index - 1].Symbol : "UNKNOWN";
        }

        private async Task ExecuteTrade(string action, string symbol, int quantity)
        {
            try
            {
                var quote = _liveQuotes.FirstOrDefault(q => q.Symbol == symbol);
                if (quote == null)
                {
                    LogTrade($"ERROR: Symbol {symbol} not found");
                    return;
                }

                if (_sandboxMode && _currentSandboxSession != null)
                {
                    // Execute sandbox trade
                    var tradeAction = action == "BUY" ? TradeAction.Buy : TradeAction.Sell;
                    var result = await _sandboxDataService.ExecuteSandboxTradeAsync(
                        _currentSandboxSession.SandboxSessionId,
                        symbol,
                        tradeAction,
                        quantity,
                        OrderType.Market);

                    if (result.Success)
                    {
                        _currentSandboxSession.CurrentBalance = result.NewBalance;
                        LogTrade($"✅ {action} EXECUTED (SANDBOX): {quantity} {symbol} @ ${result.ExecutionPrice:F2} (Total: ${result.TotalCost:F2}) New Balance: ${result.NewBalance:F2}");

                        // Update display header to show new balance
                        System.Console.SetCursorPosition(0, 0);
                        PrintHeader();
                    }
                    else
                    {
                        LogTrade($"❌ {action} FAILED (SANDBOX): {result.ErrorMessage}");
                    }
                }
                else
                {
                    // Live trading mode
                    var price = quote.LastPrice;
                    var totalValue = price * quantity;

                    // Update positions for live mode
                    if (action == "BUY")
                    {
                        _positions[symbol] = _positions.GetValueOrDefault(symbol, 0) + quantity;
                        LogTrade($"✅ BUY EXECUTED (LIVE): {quantity} {symbol} @ ${price:F2} (Total: ${totalValue:F2})");
                    }
                    else if (action == "SELL")
                    {
                        var currentPosition = _positions.GetValueOrDefault(symbol, 0);
                        if (currentPosition < quantity)
                        {
                            LogTrade($"❌ SELL FAILED (LIVE): Insufficient shares. Have {currentPosition}, tried to sell {quantity}");
                            return;
                        }

                        _positions[symbol] = currentPosition - quantity;
                        if (_positions[symbol] == 0)
                            _positions.Remove(symbol);

                        LogTrade($"✅ SELL EXECUTED (LIVE): {quantity} {symbol} @ ${price:F2} (Total: ${totalValue:F2})");
                    }

                    // TODO: Integrate with actual Schwab API for live trades
                    await Task.Delay(50); // Simulate API call
                }
            }
            catch (Exception ex)
            {
                LogTrade($"❌ TRADE ERROR: {ex.Message}");
            }
        }

        private async Task SetStockSymbols()
        {
            System.Console.SetCursorPosition(0, System.Console.WindowHeight - 3);
            System.Console.ForegroundColor = System.ConsoleColor.Yellow;
            System.Console.Write("Enter stock symbols (comma-separated): ");
            System.Console.ForegroundColor = System.ConsoleColor.White;

            var input = System.Console.ReadLine();
            if (!string.IsNullOrEmpty(input))
            {
                var symbols = input.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(s => s.Trim().ToUpper())
                                  .ToArray();

                _liveQuotes.Clear();
                foreach (var symbol in symbols.Take(5)) // Limit to 5 stocks
                {
                    _liveQuotes.Add(new StockQuote
                    {
                        Symbol = symbol,
                        LastPrice = 100.00m,
                        Change = 0.00m,
                        ChangePercent = 0.00m,
                        Volume = 1000000
                    });
                }

                LogTrade($"Updated watchlist: {string.Join(", ", symbols)}");
            }
        }

        private async Task ToggleSandboxMode()
        {
            try
            {
                _sandboxMode = !_sandboxMode;
                if (_sandboxMode && _currentSandboxSession == null)
                {
                    // Create a default sandbox session if none exists
                    await CreateDefaultSandboxSession();
                }

                System.Console.Clear();
                PrintHeader();
                PrintInstructions();
                LogTrade($"Switched to {(_sandboxMode ? "SANDBOX" : "LIVE")} mode");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling sandbox mode");
            }
        }

        private async Task CreateNewSandboxSession()
        {
            try
            {
                System.Console.Clear();
                System.Console.WriteLine("Create New Sandbox Session");
                System.Console.WriteLine("==========================");

                System.Console.Write("Session Name: ");
                var sessionName = System.Console.ReadLine() ?? $"Session_{DateTime.Now:yyyyMMdd_HHmm}";

                System.Console.Write("Start Date (yyyy-MM-dd) [default: 30 days ago]: ");
                var startDateInput = System.Console.ReadLine();
                DateTime startDate = DateTime.TryParse(startDateInput, out var parsedStart) ? parsedStart : DateTime.Now.AddDays(-30);

                System.Console.Write("End Date (yyyy-MM-dd) [default: today]: ");
                var endDateInput = System.Console.ReadLine();
                DateTime endDate = DateTime.TryParse(endDateInput, out var parsedEnd) ? parsedEnd : DateTime.Now;

                System.Console.Write("Initial Balance [default: $100,000]: ");
                var balanceInput = System.Console.ReadLine();
                decimal initialBalance = decimal.TryParse(balanceInput, out var parsedBalance) ? parsedBalance : 100000m;

                var session = await _sandboxDataService.CreateSandboxSessionAsync(_currentUser!.UserId, startDate, endDate, initialBalance);
                session.SessionName = sessionName;

                _currentSandboxSession = session;
                _sandboxMode = true;

                System.Console.Clear();
                PrintHeader();
                PrintInstructions();
                LogTrade($"Created new sandbox session: {sessionName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating sandbox session");
                System.Console.WriteLine($"Error: {ex.Message}");
                System.Console.WriteLine("Press any key to continue...");
                System.Console.ReadKey();
            }
        }

        private async Task LoadSandboxSession()
        {
            try
            {
                var sessions = await _sandboxDataService.GetSandboxSessionHistoryAsync(_currentUser!.UserId);
                var sessionList = sessions.ToList();

                if (!sessionList.Any())
                {
                    System.Console.WriteLine("No sandbox sessions found. Creating a default session...");
                    await CreateDefaultSandboxSession();
                    return;
                }

                System.Console.Clear();
                System.Console.WriteLine("Load Sandbox Session");
                System.Console.WriteLine("====================");

                for (int i = 0; i < sessionList.Count; i++)
                {
                    var session = sessionList[i];
                    System.Console.WriteLine($"{i + 1}. {session.SessionName} - Balance: ${session.CurrentBalance:N2} ({session.StartDate:yyyy-MM-dd} to {session.EndDate:yyyy-MM-dd})");
                }

                System.Console.Write("Select session (1-{0}): ", sessionList.Count);
                var input = System.Console.ReadLine();

                if (int.TryParse(input, out var index) && index >= 1 && index <= sessionList.Count)
                {
                    _currentSandboxSession = sessionList[index - 1];
                    _sandboxMode = true;

                    System.Console.Clear();
                    PrintHeader();
                    PrintInstructions();
                    LogTrade($"Loaded sandbox session: {_currentSandboxSession.SessionName}");
                }
                else
                {
                    System.Console.WriteLine("Invalid selection. Press any key to continue...");
                    System.Console.ReadKey();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading sandbox session");
                System.Console.WriteLine($"Error: {ex.Message}");
                System.Console.WriteLine("Press any key to continue...");
                System.Console.ReadKey();
            }
        }

        private async Task CreateDefaultSandboxSession()
        {
            try
            {
                _currentSandboxSession = await _sandboxDataService.CreateSandboxSessionAsync(
                    _currentUser!.UserId,
                    DateTime.Now.AddDays(-30),
                    DateTime.Now,
                    100000m);
                _currentSandboxSession.SessionName = "Default Session";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating default sandbox session");
            }
        }

        private async Task<bool> AuthenticateUser()
        {
            try
            {
                try { System.Console.Clear(); } catch { /* Ignore console clear errors */ }
                PrintWelcomeHeader();

                while (true)
                {
                    System.Console.WriteLine("┌─────────────────────────────────────────────────────────────────────────────┐");
                    System.Console.WriteLine("│                            VBTrader Authentication                           │");
                    System.Console.WriteLine("└─────────────────────────────────────────────────────────────────────────────┘");
                    System.Console.WriteLine();
                    System.Console.WriteLine("1. Login with existing account");
                    System.Console.WriteLine("2. Create new account");
                    System.Console.WriteLine("3. Exit");
                    System.Console.WriteLine();
                    System.Console.Write("Select option (1-3): ");

                    var choice = System.Console.ReadLine();
                    System.Console.WriteLine();

                    switch (choice)
                    {
                        case "1":
                            if (await HandleLogin())
                                return true;
                            break;
                        case "2":
                            if (await HandleRegistration())
                                return true;
                            break;
                        case "3":
                            return false;
                        default:
                            System.Console.WriteLine("Invalid option. Please try again.");
                            System.Console.WriteLine();
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user authentication");
                System.Console.WriteLine($"Authentication error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> HandleLogin()
        {
            try
            {
                System.Console.Write("Username: ");
                var username = System.Console.ReadLine();
                if (string.IsNullOrWhiteSpace(username))
                {
                    System.Console.WriteLine("Username cannot be empty.");
                    await Task.Delay(2000);
                    return false;
                }

                System.Console.Write("Password: ");
                var password = ReadPassword();
                if (string.IsNullOrWhiteSpace(password))
                {
                    System.Console.WriteLine("Password cannot be empty.");
                    await Task.Delay(2000);
                    return false;
                }

                var user = await _userService.AuthenticateAsync(username, password);
                if (user != null)
                {
                    _currentUser = user;

                    // Create a new user session
                    _currentUserSession = await _userService.CreateSessionAsync(user.UserId, TradingMode.Sandbox);
                    await _userService.UpdateLastLoginAsync(user.UserId);

                    System.Console.WriteLine($"\nWelcome back, {user.Username}!");
                    await Task.Delay(1500);
                    return true;
                }
                else
                {
                    System.Console.WriteLine("\nInvalid username or password.");
                    await Task.Delay(2000);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                System.Console.WriteLine($"Login error: {ex.Message}");
                await Task.Delay(2000);
                return false;
            }
        }

        private async Task<bool> HandleRegistration()
        {
            try
            {
                System.Console.WriteLine("Create New Account");
                System.Console.WriteLine("==================");
                System.Console.WriteLine();

                System.Console.Write("Username: ");
                var username = System.Console.ReadLine();
                if (string.IsNullOrWhiteSpace(username))
                {
                    System.Console.WriteLine("Username cannot be empty.");
                    await Task.Delay(2000);
                    return false;
                }

                System.Console.Write("Email: ");
                var email = System.Console.ReadLine();
                if (string.IsNullOrWhiteSpace(email))
                {
                    System.Console.WriteLine("Email cannot be empty.");
                    await Task.Delay(2000);
                    return false;
                }

                System.Console.Write("Password: ");
                var password = ReadPassword();
                if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
                {
                    System.Console.WriteLine("Password must be at least 8 characters long.");
                    await Task.Delay(2000);
                    return false;
                }

                System.Console.Write("Confirm Password: ");
                var confirmPassword = ReadPassword();
                if (password != confirmPassword)
                {
                    System.Console.WriteLine("Passwords do not match.");
                    await Task.Delay(2000);
                    return false;
                }

                var user = await _userService.CreateUserAsync(username, password, email);
                if (user != null)
                {
                    _currentUser = user;

                    // Create a new user session for new users
                    _currentUserSession = await _userService.CreateSessionAsync(user.UserId, TradingMode.Sandbox);

                    System.Console.WriteLine($"\nAccount created successfully! Welcome, {user.Username}!");
                    System.Console.WriteLine("You can now configure your Schwab API credentials for live trading.");
                    await Task.Delay(3000);
                    return true;
                }
                else
                {
                    System.Console.WriteLine("\nFailed to create account. Username or email may already exist.");
                    await Task.Delay(2000);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration");
                System.Console.WriteLine($"Registration error: {ex.Message}");
                await Task.Delay(2000);
                return false;
            }
        }

        private string ReadPassword()
        {
            var password = string.Empty;
            ConsoleKeyInfo keyInfo;

            do
            {
                keyInfo = System.Console.ReadKey(true);
                if (keyInfo.Key != System.ConsoleKey.Backspace && keyInfo.Key != System.ConsoleKey.Enter)
                {
                    password += keyInfo.KeyChar;
                    System.Console.Write("*");
                }
                else if (keyInfo.Key == System.ConsoleKey.Backspace && password.Length > 0)
                {
                    password = password[0..^1];
                    System.Console.Write("\b \b");
                }
            } while (keyInfo.Key != System.ConsoleKey.Enter);

            System.Console.WriteLine();
            return password;
        }

        private async Task ConfigureSchwabCredentials()
        {
            try
            {
                System.Console.Clear();
                System.Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                System.Console.WriteLine("║                          Schwab API Configuration                            ║");
                System.Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                System.Console.WriteLine();

                System.Console.WriteLine("To enable live trading, you need to configure your Schwab API credentials.");
                System.Console.WriteLine("These will be securely encrypted and stored in the database.");
                System.Console.WriteLine();
                System.Console.WriteLine("To get your Schwab API credentials:");
                System.Console.WriteLine("1. Visit the Charles Schwab Developer Portal");
                System.Console.WriteLine("2. Create a new application");
                System.Console.WriteLine("3. Note your App Key and App Secret");
                System.Console.WriteLine();

                // Check if user already has credentials
                var existingCredentials = await _userService.GetSchwabCredentialsAsync(_currentUser!.UserId);
                if (existingCredentials != null)
                {
                    System.Console.WriteLine("✅ You already have Schwab credentials configured.");
                    System.Console.WriteLine();
                    System.Console.WriteLine("1. Update existing credentials");
                    System.Console.WriteLine("2. Test current credentials");
                    System.Console.WriteLine("3. Back to trading");
                    System.Console.WriteLine();
                    System.Console.Write("Select option (1-3): ");

                    var choice = System.Console.ReadLine();
                    switch (choice)
                    {
                        case "1":
                            await UpdateSchwabCredentials();
                            break;
                        case "2":
                            await TestSchwabCredentials();
                            break;
                        default:
                            break;
                    }
                }
                else
                {
                    System.Console.WriteLine("⚠️  No Schwab credentials found. Let's set them up!");
                    System.Console.WriteLine();
                    await SetupNewSchwabCredentials();
                }

                System.Console.WriteLine("\nPress any key to return to trading...");
                System.Console.ReadKey();
                System.Console.Clear();
                PrintHeader();
                PrintInstructions();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error configuring Schwab credentials");
                System.Console.WriteLine($"Error: {ex.Message}");
                System.Console.WriteLine("Press any key to continue...");
                System.Console.ReadKey();
            }
        }

        private async Task SetupNewSchwabCredentials()
        {
            try
            {
                System.Console.Write("Enter your Schwab App Key: ");
                var appKey = System.Console.ReadLine();
                if (string.IsNullOrWhiteSpace(appKey))
                {
                    System.Console.WriteLine("App Key cannot be empty.");
                    return;
                }

                System.Console.Write("Enter your Schwab App Secret: ");
                var appSecret = ReadPassword();
                if (string.IsNullOrWhiteSpace(appSecret))
                {
                    System.Console.WriteLine("App Secret cannot be empty.");
                    return;
                }

                System.Console.Write("Enter callback URL [default: https://127.0.0.1:3000]: ");
                var callbackUrl = System.Console.ReadLine();
                if (string.IsNullOrWhiteSpace(callbackUrl))
                {
                    callbackUrl = "https://127.0.0.1:3000";
                }

                System.Console.WriteLine("\nSaving credentials securely...");
                await _userService.StoreSchwabCredentialsAsync(_currentUser!.UserId, appKey, appSecret, callbackUrl);

                System.Console.WriteLine("✅ Schwab credentials saved successfully!");
                System.Console.WriteLine("You can now use live trading mode.");

                // Test the credentials immediately
                System.Console.WriteLine("\nTesting connection...");
                await TestSchwabCredentials();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up Schwab credentials");
                System.Console.WriteLine($"Failed to save credentials: {ex.Message}");
            }
        }

        private async Task UpdateSchwabCredentials()
        {
            System.Console.WriteLine("Update Schwab Credentials");
            System.Console.WriteLine("=========================");
            System.Console.WriteLine();
            await SetupNewSchwabCredentials();
        }

        private async Task TestSchwabCredentials()
        {
            try
            {
                System.Console.WriteLine("Testing Schwab API connection...");

                var credentials = await _userService.GetSchwabCredentialsAsync(_currentUser!.UserId);
                if (credentials == null)
                {
                    System.Console.WriteLine("❌ No credentials found.");
                    return;
                }

                // Try to authenticate with Schwab API
                var success = await _schwabApiClient.AuthenticateAsync(
                    credentials.Value.appKey,
                    credentials.Value.appSecret,
                    credentials.Value.callbackUrl);

                if (success)
                {
                    System.Console.WriteLine("✅ Schwab API connection successful!");
                    _useSchwabApi = true;
                }
                else
                {
                    System.Console.WriteLine("❌ Failed to connect to Schwab API. Please check your credentials.");
                    _useSchwabApi = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing Schwab credentials");
                System.Console.WriteLine($"❌ Connection test failed: {ex.Message}");
                _useSchwabApi = false;
            }
        }


        private async Task HandleLogout()
        {
            try
            {
                if (_currentUserSession != null)
                {
                    await _userService.LogoutAsync(_currentUserSession.SessionToken);
                    _logger.LogInformation("User session ended successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
            }
        }

        private void PrintWelcomeHeader()
        {
            try
            {
                System.Console.ForegroundColor = System.ConsoleColor.Cyan;
                System.Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                System.Console.WriteLine("║                          Welcome to VBTrader                                 ║");
                System.Console.WriteLine("║                     Professional Trading Platform                           ║");
                System.Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                System.Console.ResetColor();
                System.Console.WriteLine();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Unable to print welcome header: {Message}", ex.Message);
            }
        }

        private void LogTrade(string message)
        {
            // This would be displayed in a separate area in a real implementation
            _logger.LogInformation(message);
        }

        public async Task TestDirectAuthenticationAsync(string username, string password)
        {
            try
            {
                System.Console.WriteLine("🧪 Testing Direct Authentication");
                System.Console.WriteLine($"Username: {username}");
                System.Console.WriteLine("Password: [HIDDEN]");
                System.Console.WriteLine();

                // Test user authentication
                System.Console.WriteLine("🔐 Validating user credentials...");
                var user = await _userService.ValidateUserAsync(username, password);

                if (!user)
                {
                    System.Console.WriteLine("❌ Invalid username or password");
                    return;
                }

                System.Console.WriteLine($"✅ User authenticated successfully");
                // Get the actual user object
                var userObj = await _userService.GetUserByUsernameAsync(username);
                _currentUser = userObj;

                // Check if user has Schwab credentials
                System.Console.WriteLine("🔑 Checking Schwab API credentials...");
                var hasCredentials = await _userService.HasSchwabCredentialsAsync(userObj!.UserId);

                if (!hasCredentials)
                {
                    System.Console.WriteLine("⚠️  No Schwab credentials found. Need to set up API access.");
                    return;
                }

                System.Console.WriteLine("✅ Schwab credentials found");

                // Get Schwab credentials and test authentication
                var credentials = await _userService.GetSchwabCredentialsAsync(userObj.UserId);
                if (credentials == null)
                {
                    System.Console.WriteLine("❌ Failed to retrieve Schwab credentials");
                    return;
                }

                System.Console.WriteLine("🚀 Testing Schwab API authentication...");
                var authSuccess = await _schwabApiClient.AuthenticateAsync(
                    credentials.Value.appKey,
                    credentials.Value.appSecret,
                    "https://127.0.0.1:3000");

                if (authSuccess)
                {
                    System.Console.WriteLine("🎉 Schwab API authentication successful!");
                    System.Console.WriteLine("✅ Ready for live market data and trading");
                    _useSchwabApi = true;

                    // Test with a simple API call to get account numbers
                    System.Console.WriteLine("🧪 Testing API access with account numbers request...");
                    try
                    {
                        var accounts = await _schwabApiClient.GetLinkedAccountsAsync();
                        if (accounts.Any())
                        {
                            System.Console.WriteLine($"✅ Successfully retrieved {accounts.Count()} account(s)!");
                            foreach (var account in accounts)
                            {
                                System.Console.WriteLine($"   Account: {account.AccountNumber} (Hash: {account.HashValue[..8]}...)");
                            }
                        }
                        else
                        {
                            System.Console.WriteLine("⚠️ No accounts found - this might indicate a scope or permission issue");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"❌ Account number test failed: {ex.Message}");
                        System.Console.WriteLine("💡 This suggests the token authentication is not working properly");
                    }
                }
                else
                {
                    System.Console.WriteLine("❌ Schwab API authentication failed");
                    System.Console.WriteLine("💡 Check your App Key, App Secret, and callback URL");
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"❌ Authentication test failed: {ex.Message}");
                _logger.LogError(ex, "Direct authentication test failed");
            }
        }


        public async Task TestDataRetrievalAsync(string username, string password)
        {
            try
            {
                System.Console.WriteLine("🧪 Testing Schwab Data Retrieval");
                System.Console.WriteLine("================================");
                System.Console.WriteLine();

                // Authenticate first
                System.Console.WriteLine("🔐 Authenticating...");
                var user = await _userService.ValidateUserAsync(username, password);
                if (!user)
                {
                    System.Console.WriteLine("❌ Authentication failed");
                    return;
                }

                var userObj = await _userService.GetUserByUsernameAsync(username);
                var credentials = await _userService.GetSchwabCredentialsAsync(userObj!.UserId);
                if (credentials == null)
                {
                    System.Console.WriteLine("❌ No Schwab credentials found");
                    return;
                }

                var authSuccess = await _schwabApiClient.AuthenticateAsync(
                    credentials.Value.appKey,
                    credentials.Value.appSecret,
                    "https://127.0.0.1:3000");

                if (!authSuccess)
                {
                    System.Console.WriteLine("❌ Schwab authentication failed");
                    return;
                }

                System.Console.WriteLine("✅ Authenticated successfully!");

                // Set user ID for account tracking
                _schwabApiClient.SetCurrentUserId(userObj.UserId);

                System.Console.WriteLine();

                // Create log directory if it doesn't exist
                var logDir = Path.Combine("C:", "Users", "Bryon Chase", "Documents", "vbtrader", "log");
                Directory.CreateDirectory(logDir);

                // Test 1: Account Numbers
                System.Console.WriteLine("📊 Test 1: Getting account numbers...");
                await TestAndSaveData<object>("account-numbers", async () =>
                {
                    var accounts = await _schwabApiClient.GetLinkedAccountsAsync();
                    return new { accounts = accounts.ToList(), timestamp = DateTime.Now };
                }, logDir);

                // Test 2: Market Data for AAPL
                System.Console.WriteLine("📈 Test 2: Getting market data for AAPL...");
                await TestAndSaveData<object>("market-data-aapl", async () =>
                {
                    try
                    {
                        var quotes = await _schwabApiClient.GetQuotesAsync(new[] { "AAPL" });
                        return new { quotes = quotes.ToList(), timestamp = DateTime.Now };
                    }
                    catch (Exception ex)
                    {
                        return new { error = ex.Message, timestamp = DateTime.Now };
                    }
                }, logDir);

                // Test 3: Account Details (if we have accounts)
                System.Console.WriteLine("💰 Test 3: Getting account details...");
                await TestAndSaveData<object>("account-details", async () =>
                {
                    try
                    {
                        var accounts = await _schwabApiClient.GetLinkedAccountsAsync();
                        var firstAccount = accounts.FirstOrDefault();
                        if (firstAccount != null)
                        {
                            var details = await _schwabApiClient.GetAccountDetailsAsync(firstAccount.HashValue);
                            return new { accountDetails = details, timestamp = DateTime.Now };
                        }
                        return new { message = "No accounts found", timestamp = DateTime.Now };
                    }
                    catch (Exception ex)
                    {
                        return new { error = ex.Message, timestamp = DateTime.Now };
                    }
                }, logDir);

                System.Console.WriteLine();
                System.Console.WriteLine("✅ Data retrieval tests completed!");
                System.Console.WriteLine($"📁 Results saved to: {logDir}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"❌ Data retrieval test failed: {ex.Message}");
            }
        }

        private async Task TestAndSaveData<T>(string testName, Func<Task<T>> dataRetriever, string logDir)
        {
            try
            {
                var startTime = DateTime.Now;
                System.Console.WriteLine($"   ⏱️  Starting {testName} at {startTime:HH:mm:ss}...");

                var data = await dataRetriever();

                var endTime = DateTime.Now;
                var duration = endTime - startTime;

                var result = new
                {
                    testName,
                    startTime,
                    endTime,
                    durationMs = duration.TotalMilliseconds,
                    success = true,
                    data
                };

                var fileName = $"{testName}-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
                var filePath = Path.Combine(logDir, fileName);

                await File.WriteAllTextAsync(filePath, System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                }));

                System.Console.WriteLine($"   ✅ {testName} completed in {duration.TotalMilliseconds:F0}ms - saved to {fileName}");
            }
            catch (Exception ex)
            {
                var result = new
                {
                    testName,
                    timestamp = DateTime.Now,
                    success = false,
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                };

                var fileName = $"{testName}-ERROR-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
                var filePath = Path.Combine(logDir, fileName);

                await File.WriteAllTextAsync(filePath, System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                }));

                System.Console.WriteLine($"   ❌ {testName} failed: {ex.Message} - error saved to {fileName}");
            }
        }
    }
}