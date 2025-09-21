using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VBTrader.Core.Interfaces;
using VBTrader.Core.Models;
using VBTrader.Infrastructure.Services;

namespace VBTrader.Infrastructure.Schwab;

public class SchwabApiClient : ISchwabApiClient, IDisposable
{
    private readonly ILogger<SchwabApiClient> _logger;
    private readonly HttpClient _httpClient;
    private readonly SchwabTokenManager _tokenManager;
    private readonly SchwabCallbackServer _callbackServer;
    private readonly AccountTrackingService? _accountTrackingService;

    // Current user context for account tracking
    private int _currentUserId = 0;

    private string? _appKey;
    private string? _appSecret;
    private string? _callbackUrl;

    // Base URLs as specified in Python code
    private const string TraderBaseUrl = "https://api.schwabapi.com/trader/v1/";
    private const string MarketDataBaseUrl = "https://api.schwabapi.com/marketdata/v1/";

    public bool IsAuthenticated => _tokenManager.IsAccessTokenValid;
    public bool IsStreaming { get; private set; }

    public SchwabApiClient(ILogger<SchwabApiClient> logger, HttpClient httpClient, AccountTrackingService? accountTrackingService = null)
    {
        _logger = logger;
        _httpClient = httpClient;
        _accountTrackingService = accountTrackingService;

        // Create separate loggers - will need to inject ILoggerFactory in real implementation
        var tokenLogger = new LoggerWrapper<SchwabTokenManager>(_logger);
        var callbackLogger = new LoggerWrapper<SchwabCallbackServer>(_logger);

        _tokenManager = new SchwabTokenManager(tokenLogger, httpClient);
        _callbackServer = new SchwabCallbackServer(callbackLogger);

        // Set up HTTP client defaults - no base address since we'll use full URLs
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "VBTrader/1.0");
    }

    public async Task<bool> AuthenticateAsync(string appKey, string appSecret, string callbackUrl)
    {
        _appKey = appKey;
        _appSecret = appSecret;
        _callbackUrl = callbackUrl;

        // Check if we already have valid tokens
        if (await _tokenManager.EnsureValidAccessTokenAsync())
        {
            UpdateHttpClientAuth();
            _logger.LogInformation("Already authenticated with valid tokens");
            return true;
        }

        // Need to get new tokens - start the callback server and authentication flow
        _logger.LogInformation("Starting OAuth authentication flow...");

        try
        {
            var authorizationCode = await _callbackServer.StartAndWaitForAuthorizationCodeAsync(
                callbackUrl, appKey, CancellationToken.None);

            if (string.IsNullOrEmpty(authorizationCode))
            {
                _logger.LogError("Failed to obtain authorization code");
                Console.WriteLine("❌ Failed to get authorization code from callback");
                return false;
            }

            Console.WriteLine($"⚙️ Exchanging authorization code for access tokens...");
            var success = await _tokenManager.GetTokensFromAuthorizationCodeAsync(
                appKey, appSecret, callbackUrl, authorizationCode);

            if (success)
            {
                UpdateHttpClientAuth();
                _logger.LogInformation("Authentication completed successfully");
                Console.WriteLine($"✅ Schwab API authentication successful!");
                Console.WriteLine($"✅ Ready to access market data and account information");
            }
            else
            {
                Console.WriteLine($"❌ Token exchange failed - check credentials and try again");
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed");
            return false;
        }
    }

    public async Task<bool> RefreshTokenAsync()
    {
        var success = await _tokenManager.RefreshAccessTokenAsync();
        if (success)
        {
            UpdateHttpClientAuth();
        }
        return success;
    }

    private void UpdateHttpClientAuth()
    {
        if (!string.IsNullOrEmpty(_tokenManager.AccessToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _tokenManager.AccessToken);
        }
    }

    private async Task<T?> SendGetRequestAsync<T>(string endpoint, Dictionary<string, string>? parameters = null)
    {
        await EnsureAuthenticatedAsync();

        var queryString = parameters != null
            ? "?" + string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"))
            : "";

        var response = await _httpClient.GetAsync($"{endpoint}{queryString}");

        if (response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync();

            // Log the raw JSON response for debugging
            _logger.LogInformation("Raw API Response for {Endpoint}: {JsonResponse}", endpoint, json);

            // Save account information to database when we get account data
            if (endpoint.Contains("/accounts") && !endpoint.Contains("/orders") && _accountTrackingService != null)
            {
                try
                {
                    await _accountTrackingService.SaveAccountInformationAsync(_currentUserId, json, "API_REFRESH");
                    _logger.LogInformation("Account data saved to database successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save account data to database");
                }
            }

            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        _logger.LogError("API request failed. Status: {StatusCode}, Content: {Content}",
            response.StatusCode, errorContent);

        return default;
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (!await _tokenManager.EnsureValidAccessTokenAsync())
        {
            throw new UnauthorizedAccessException("Authentication required. Please call AuthenticateAsync first.");
        }
        UpdateHttpClientAuth();
    }

    // Account Management
    public async Task<IEnumerable<AccountInfo>> GetLinkedAccountsAsync()
    {
        var accounts = await SendGetRequestAsync<List<SchwabAccount>>($"{TraderBaseUrl}accounts");
        return accounts?.Select(a => new AccountInfo
        {
            AccountNumber = a.AccountNumber ?? "",
            HashValue = a.HashValue ?? ""
        }) ?? Enumerable.Empty<AccountInfo>();
    }

    public async Task<AccountDetails> GetAccountDetailsAsync(string accountHash, bool includePositions = false)
    {
        var parameters = includePositions
            ? new Dictionary<string, string> { { "fields", "positions" } }
            : null;

        var accountWrapper = await SendGetRequestAsync<SchwabAccountWrapper>($"{TraderBaseUrl}accounts/{accountHash}", parameters);

        return new AccountDetails
        {
            AccountNumber = accountWrapper?.SecuritiesAccount?.AccountNumber ?? "",
            TotalValue = accountWrapper?.SecuritiesAccount?.CurrentBalances?.AvailableFunds ?? 0,
            CashBalance = accountWrapper?.SecuritiesAccount?.CurrentBalances?.TotalCash ?? 0,
            BuyingPower = accountWrapper?.SecuritiesAccount?.CurrentBalances?.BuyingPower ?? 0,
            Positions = accountWrapper?.SecuritiesAccount?.Positions?.Select(p => new Position
            {
                Symbol = p.Instrument?.Symbol ?? "",
                Quantity = (int)(p.LongQuantity ?? 0),
                AveragePrice = p.AveragePrice ?? 0,
                CurrentPrice = p.MarketValue ?? 0,
                UnrealizedPnL = (p.MarketValue ?? 0) - ((p.AveragePrice ?? 0) * (p.LongQuantity ?? 0))
            }) ?? Enumerable.Empty<Position>()
        };
    }

    public async Task<IEnumerable<Order>> GetOrdersAsync(string accountHash, DateTime fromDate, DateTime toDate)
    {
        var parameters = new Dictionary<string, string>
        {
            { "fromEnteredTime", fromDate.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
            { "toEnteredTime", toDate.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") }
        };

        var orders = await SendGetRequestAsync<List<SchwabOrder>>($"trader/v1/accounts/{accountHash}/orders", parameters);

        return orders?.Select(o => new Order
        {
            OrderId = o.OrderId?.ToString() ?? "",
            Symbol = o.OrderLegCollection?.FirstOrDefault()?.Instrument?.Symbol ?? "",
            Quantity = (int)(o.Quantity ?? 0),
            Price = o.Price ?? 0,
            OrderType = ParseOrderType(o.OrderType),
            Side = ParseOrderSide(o.OrderLegCollection?.FirstOrDefault()?.Instruction),
            Status = ParseOrderStatus(o.Status),
            CreatedTime = o.EnteredTime ?? DateTime.MinValue
        }) ?? Enumerable.Empty<Order>();
    }

    // Market Data
    public async Task<StockQuote> GetQuoteAsync(string symbol)
    {
        var quotes = await GetQuotesAsync(new[] { symbol });
        return quotes.FirstOrDefault() ?? new StockQuote { Symbol = symbol };
    }

    public async Task<IEnumerable<StockQuote>> GetQuotesAsync(IEnumerable<string> symbols)
    {
        var symbolList = string.Join(",", symbols);
        var parameters = new Dictionary<string, string>
        {
            { "symbols", symbolList }
        };

        var response = await SendGetRequestAsync<Dictionary<string, SchwabQuote>>($"{MarketDataBaseUrl}quotes", parameters);

        return response?.Select(kvp => new StockQuote
        {
            Symbol = kvp.Key,
            LastPrice = (decimal)(kvp.Value.Quote?.LastPrice ?? 0),
            Change = (decimal)(kvp.Value.Quote?.NetChange ?? 0),
            ChangePercent = (decimal)(kvp.Value.Quote?.NetPercentChangeInDouble ?? 0),
            Volume = kvp.Value.Quote?.TotalVolume ?? 0
        }) ?? Enumerable.Empty<StockQuote>();
    }

    public async Task<IEnumerable<CandlestickData>> GetPriceHistoryAsync(string symbol, TimeFrame timeFrame, DateTime? startDate = null, DateTime? toDate = null)
    {
        var request = new PriceHistoryRequest
        {
            Symbol = symbol,
            StartDate = startDate,
            EndDate = toDate
        };

        // Convert legacy TimeFrame to new system
        SetRequestFromTimeFrame(request, timeFrame);

        return await GetPriceHistoryAsync(request);
    }

    public async Task<IEnumerable<CandlestickData>> GetPriceHistoryAsync(PriceHistoryRequest request)
    {
        var parameters = new Dictionary<string, string>();

        // Add period type and period
        if (request.PeriodType.HasValue)
        {
            parameters["periodType"] = request.PeriodType.ToString()!.ToLower();
            if (request.Period.HasValue)
                parameters["period"] = request.Period.ToString()!;
        }

        // Add frequency type and frequency
        if (request.FrequencyType.HasValue)
        {
            parameters["frequencyType"] = request.FrequencyType.ToString()!.ToLower();
            if (request.Frequency.HasValue)
                parameters["frequency"] = request.Frequency.ToString()!;
        }

        // Add date range
        if (request.StartDate.HasValue)
            parameters["startDate"] = ((DateTimeOffset)request.StartDate.Value).ToUnixTimeMilliseconds().ToString();
        if (request.EndDate.HasValue)
            parameters["endDate"] = ((DateTimeOffset)request.EndDate.Value).ToUnixTimeMilliseconds().ToString();

        // Add optional parameters
        if (request.NeedExtendedHoursData)
            parameters["needExtendedHoursData"] = "true";
        if (request.NeedPreviousClose)
            parameters["needPreviousClose"] = "true";

        var response = await SendGetRequestAsync<SchwabPriceHistory>($"marketdata/v1/pricehistory", parameters);

        return response?.Candles?.Select(c => new CandlestickData
        {
            Symbol = request.Symbol,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(c.Datetime ?? 0).DateTime,
            Open = (decimal)(c.Open ?? 0),
            High = (decimal)(c.High ?? 0),
            Low = (decimal)(c.Low ?? 0),
            Close = (decimal)(c.Close ?? 0),
            Volume = c.Volume ?? 0
        }) ?? Enumerable.Empty<CandlestickData>();
    }

    public async Task<Dictionary<string, IEnumerable<CandlestickData>>> GetBulkPriceHistoryAsync(BulkPriceHistoryRequest request)
    {
        var results = new Dictionary<string, IEnumerable<CandlestickData>>();
        var symbols = request.Symbols.Take(request.MaxSymbols).ToList();

        _logger.LogInformation("Fetching bulk price history for {Count} symbols: {Symbols}",
            symbols.Count, string.Join(", ", symbols));

        var tasks = symbols.Select(async symbol =>
        {
            var symbolRequest = new PriceHistoryRequest
            {
                Symbol = symbol,
                PeriodType = request.PeriodType,
                Period = request.Period,
                FrequencyType = request.FrequencyType,
                Frequency = request.Frequency,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                NeedExtendedHoursData = request.NeedExtendedHoursData
            };

            try
            {
                var data = await GetPriceHistoryAsync(symbolRequest);
                return new { Symbol = symbol, Data = data };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get price history for {Symbol}", symbol);
                return new { Symbol = symbol, Data = Enumerable.Empty<CandlestickData>() };
            }
        });

        var taskResults = await Task.WhenAll(tasks);

        foreach (var result in taskResults)
        {
            results[result.Symbol] = result.Data;
        }

        _logger.LogInformation("Completed bulk price history fetch. Successfully retrieved data for {SuccessCount}/{TotalCount} symbols",
            results.Count(r => r.Value.Any()), symbols.Count);

        return results;
    }

    public async Task<IEnumerable<StockQuote>> GetMoversAsync(string index, MoversSort sort, int? frequency = null)
    {
        var parameters = new Dictionary<string, string>
        {
            { "sort", sort.ToString().ToUpper() },
            { "frequency", frequency?.ToString() ?? "0" }
        };

        var response = await SendGetRequestAsync<List<SchwabMover>>($"marketdata/v1/movers/{index}", parameters);

        return response?.Select(m => new StockQuote
        {
            Symbol = m.Symbol ?? "",
            LastPrice = (decimal)(m.LastPrice ?? 0),
            Change = (decimal)(m.Change ?? 0),
            ChangePercent = (decimal)(m.ChangePercent ?? 0),
            Volume = m.Volume ?? 0
        }) ?? Enumerable.Empty<StockQuote>();
    }

    // Options
    public async Task<OptionChain> GetOptionChainAsync(string symbol, string? contractType = null)
    {
        var parameters = new Dictionary<string, string>
        {
            { "symbol", symbol }
        };

        if (!string.IsNullOrEmpty(contractType))
            parameters["contractType"] = contractType;

        var response = await SendGetRequestAsync<SchwabOptionChain>($"marketdata/v1/chains", parameters);

        // TODO: Implement option chain parsing
        return new OptionChain { Symbol = symbol };
    }

    // Trading
    public async Task<OrderResult> PlaceOrderAsync(string accountHash, OrderRequest order)
    {
        await EnsureAuthenticatedAsync();

        var schwabOrder = new
        {
            orderType = order.OrderType.ToString().ToUpper(),
            session = "NORMAL",
            duration = order.Duration,
            orderStrategyType = "SINGLE",
            orderLegCollection = new[]
            {
                new
                {
                    instruction = order.Side.ToString().ToUpper(),
                    quantity = order.Quantity,
                    instrument = new
                    {
                        symbol = order.Symbol,
                        assetType = "EQUITY"
                    }
                }
            },
            price = order.Price
        };

        var json = JsonSerializer.Serialize(schwabOrder);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"trader/v1/accounts/{accountHash}/orders", content);

        if (response.IsSuccessStatusCode)
        {
            // Extract order ID from Location header if available
            var location = response.Headers.Location?.ToString();
            var orderId = location?.Split('/').LastOrDefault();

            return new OrderResult
            {
                Success = true,
                OrderId = orderId
            };
        }

        var errorContent = await response.Content.ReadAsStringAsync();
        return new OrderResult
        {
            Success = false,
            ErrorMessage = $"Order placement failed: {response.StatusCode} - {errorContent}"
        };
    }

    public async Task<bool> CancelOrderAsync(string accountHash, string orderId)
    {
        await EnsureAuthenticatedAsync();

        var response = await _httpClient.DeleteAsync($"trader/v1/accounts/{accountHash}/orders/{orderId}");
        return response.IsSuccessStatusCode;
    }

    public async Task<Order> GetOrderDetailsAsync(string accountHash, string orderId)
    {
        var order = await SendGetRequestAsync<SchwabOrder>($"trader/v1/accounts/{accountHash}/orders/{orderId}");

        return new Order
        {
            OrderId = order?.OrderId?.ToString() ?? orderId,
            Symbol = order?.OrderLegCollection?.FirstOrDefault()?.Instrument?.Symbol ?? "",
            Quantity = (int)(order?.Quantity ?? 0),
            Price = order?.Price ?? 0,
            OrderType = ParseOrderType(order?.OrderType),
            Side = ParseOrderSide(order?.OrderLegCollection?.FirstOrDefault()?.Instruction),
            Status = ParseOrderStatus(order?.Status),
            CreatedTime = order?.EnteredTime ?? DateTime.MinValue
        };
    }

    // Streaming (placeholder implementation)
    public async Task StartStreamingAsync(IEnumerable<string> symbols, Action<StreamingData> onDataReceived)
    {
        // TODO: Implement Schwab streaming API
        IsStreaming = true;
        await Task.CompletedTask;
    }

    public async Task StopStreamingAsync()
    {
        // TODO: Implement streaming stop
        IsStreaming = false;
        await Task.CompletedTask;
    }

    /// <summary>
    /// Set the current user ID for account tracking
    /// </summary>
    public void SetCurrentUserId(int userId)
    {
        _currentUserId = userId;
        _logger.LogDebug("Current user ID set to {UserId} for account tracking", userId);
    }

    private void SetRequestFromTimeFrame(PriceHistoryRequest request, TimeFrame timeFrame)
    {
        switch (timeFrame)
        {
            case TimeFrame.OneMinute:
            case TimeFrame.FiveMinutes:
            case TimeFrame.TenMinutes:
            case TimeFrame.FifteenMinutes:
            case TimeFrame.ThirtyMinutes:
            case TimeFrame.OneHour:
                request.PeriodType = PeriodType.Day;
                request.Period = 10;
                request.FrequencyType = FrequencyType.Minute;
                request.Frequency = GetFrequencyFromTimeFrame(timeFrame);
                break;

            case TimeFrame.FourHours:
                request.PeriodType = PeriodType.Month;
                request.Period = 1;
                request.FrequencyType = FrequencyType.Daily;
                request.Frequency = 1;
                break;

            case TimeFrame.OneDay:
                request.PeriodType = PeriodType.Year;
                request.Period = 1;
                request.FrequencyType = FrequencyType.Daily;
                request.Frequency = 1;
                break;

            case TimeFrame.Weekly:
                request.PeriodType = PeriodType.Year;
                request.Period = 2;
                request.FrequencyType = FrequencyType.Weekly;
                request.Frequency = 1;
                break;

            case TimeFrame.Monthly:
                request.PeriodType = PeriodType.Year;
                request.Period = 10;
                request.FrequencyType = FrequencyType.Monthly;
                request.Frequency = 1;
                break;
        }
    }

    private int GetFrequencyFromTimeFrame(TimeFrame timeFrame)
    {
        return timeFrame switch
        {
            TimeFrame.OneMinute => 1,
            TimeFrame.FiveMinutes => 5,
            TimeFrame.TenMinutes => 10,
            TimeFrame.FifteenMinutes => 15,
            TimeFrame.ThirtyMinutes => 30,
            TimeFrame.OneHour => 60,
            TimeFrame.FourHours => 240,
            TimeFrame.OneDay => 1,
            TimeFrame.Weekly => 1,
            TimeFrame.Monthly => 1,
            _ => 1
        };
    }

    // Helper methods
    private static OrderType ParseOrderType(string? orderType) => orderType?.ToUpper() switch
    {
        "MARKET" => OrderType.Market,
        "LIMIT" => OrderType.Limit,
        "STOP" => OrderType.Stop,
        "STOP_LIMIT" => OrderType.StopLimit,
        _ => OrderType.Market
    };

    private static OrderSide ParseOrderSide(string? instruction) => instruction?.ToUpper() switch
    {
        "BUY" => OrderSide.Buy,
        "SELL" => OrderSide.Sell,
        _ => OrderSide.Buy
    };

    private static OrderStatus ParseOrderStatus(string? status) => status?.ToUpper() switch
    {
        "PENDING_ACTIVATION" => OrderStatus.Pending,
        "FILLED" => OrderStatus.Filled,
        "PARTIALLY_FILLED" => OrderStatus.PartiallyFilled,
        "CANCELED" => OrderStatus.Cancelled,
        "REJECTED" => OrderStatus.Rejected,
        _ => OrderStatus.Pending
    };

    public void Dispose()
    {
        _callbackServer?.Dispose();
        _httpClient?.Dispose();
    }

    // Schwab API Response Models
    private class SchwabAccount
    {
        public string? AccountNumber { get; set; }
        public string? HashValue { get; set; }
    }

    private class SchwabAccountWrapper
    {
        public SchwabAccountInfo? SecuritiesAccount { get; set; }
    }

    private class SchwabAccountInfo
    {
        public string? AccountNumber { get; set; }
        public SchwabBalances? CurrentBalances { get; set; }
        public List<SchwabPosition>? Positions { get; set; }
    }

    private class SchwabBalances
    {
        public decimal? AvailableFunds { get; set; }
        public decimal? TotalCash { get; set; }
        public decimal? BuyingPower { get; set; }
        public decimal? Equity { get; set; }
        public decimal? LiquidationValue { get; set; }
    }

    private class SchwabPosition
    {
        public SchwabInstrument? Instrument { get; set; }
        public decimal? LongQuantity { get; set; }
        public decimal? AveragePrice { get; set; }
        public decimal? MarketValue { get; set; }
    }

    private class SchwabInstrument
    {
        public string? Symbol { get; set; }
    }

    private class SchwabOrder
    {
        public long? OrderId { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? Price { get; set; }
        public string? OrderType { get; set; }
        public string? Status { get; set; }
        public DateTime? EnteredTime { get; set; }
        public List<SchwabOrderLeg>? OrderLegCollection { get; set; }
    }

    private class SchwabOrderLeg
    {
        public string? Instruction { get; set; }
        public SchwabInstrument? Instrument { get; set; }
    }

    private class SchwabQuote
    {
        public SchwabQuoteData? Quote { get; set; }
    }

    private class SchwabQuoteData
    {
        public double? LastPrice { get; set; }
        public double? NetChange { get; set; }
        public double? NetPercentChangeInDouble { get; set; }
        public long? TotalVolume { get; set; }
    }

    private class SchwabPriceHistory
    {
        public List<SchwabCandle>? Candles { get; set; }
    }

    private class SchwabCandle
    {
        public long? Datetime { get; set; }
        public double? Open { get; set; }
        public double? High { get; set; }
        public double? Low { get; set; }
        public double? Close { get; set; }
        public long? Volume { get; set; }
    }

    private class SchwabMover
    {
        public string? Symbol { get; set; }
        public double? LastPrice { get; set; }
        public double? Change { get; set; }
        public double? ChangePercent { get; set; }
        public long? Volume { get; set; }
    }

    private class SchwabOptionChain
    {
        public string? Symbol { get; set; }
        // TODO: Add option chain properties
    }

    // Simple logger wrapper to bridge ILogger types
    private class LoggerWrapper<T> : ILogger<T>
    {
        private readonly ILogger _logger;

        public LoggerWrapper(ILogger logger)
        {
            _logger = logger;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return _logger.BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _logger.IsEnabled(logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _logger.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}