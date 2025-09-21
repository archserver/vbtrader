using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VBTrader.Core.Interfaces;
using VBTrader.Core.Models;
using VBTrader.Security.Cryptography;

namespace VBTrader.Infrastructure.SchwabApi;

public class SchwabApiClient : ISchwabApiClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SchwabApiClient> _logger;
    private readonly SchwabTokenManager _tokenManager;

    private const string BaseApiUrl = "https://api.schwabapi.com";
    private SchwabCredentials? _credentials;

    public SchwabApiClient(HttpClient httpClient, ILogger<SchwabApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _tokenManager = new SchwabTokenManager(_httpClient, _logger);

        _httpClient.BaseAddress = new Uri(BaseApiUrl);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public bool IsAuthenticated => _credentials != null && !string.IsNullOrEmpty(_credentials.AccessToken);

    public bool IsStreaming { get; private set; }

    public async Task<bool> AuthenticateAsync(string appKey, string appSecret, string callbackUrl)
    {
        try
        {
            _credentials = new SchwabCredentials
            {
                AppKey = appKey,
                AppSecret = appSecret,
                CallbackUrl = callbackUrl
            };

            var success = await _tokenManager.InitializeTokensAsync(_credentials);
            if (success)
            {
                SetAuthorizationHeader();
                _logger.LogInformation("Successfully authenticated with Schwab API");
                return true;
            }

            _logger.LogError("Failed to authenticate with Schwab API");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during authentication");
            return false;
        }
    }

    public async Task<bool> RefreshTokenAsync()
    {
        if (_credentials == null)
            return false;

        try
        {
            var success = await _tokenManager.RefreshAccessTokenAsync(_credentials);
            if (success)
            {
                SetAuthorizationHeader();
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing token");
            return false;
        }
    }

    public async Task<IEnumerable<AccountInfo>> GetLinkedAccountsAsync()
    {
        try
        {
            await EnsureValidTokenAsync();

            var response = await _httpClient.GetAsync("/trader/v1/accounts/accountNumbers");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var accounts = JsonSerializer.Deserialize<List<SchwabAccountInfo>>(content) ?? new List<SchwabAccountInfo>();

            return accounts.Select(a => new AccountInfo
            {
                AccountNumber = a.accountNumber,
                HashValue = a.hashValue
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting linked accounts");
            return Enumerable.Empty<AccountInfo>();
        }
    }

    public async Task<AccountDetails> GetAccountDetailsAsync(string accountHash, bool includePositions = false)
    {
        try
        {
            await EnsureValidTokenAsync();

            var fields = includePositions ? "?fields=positions" : "";
            var response = await _httpClient.GetAsync($"/trader/v1/accounts/{accountHash}{fields}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var accountData = JsonSerializer.Deserialize<SchwabAccountDetails>(content);

            return MapToAccountDetails(accountData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting account details for {AccountHash}", accountHash);
            throw;
        }
    }

    public async Task<StockQuote> GetQuoteAsync(string symbol)
    {
        var quotes = await GetQuotesAsync(new[] { symbol });
        return quotes.FirstOrDefault() ?? new StockQuote { Symbol = symbol };
    }

    public async Task<IEnumerable<StockQuote>> GetQuotesAsync(IEnumerable<string> symbols)
    {
        try
        {
            await EnsureValidTokenAsync();

            var symbolString = string.Join(",", symbols);
            var response = await _httpClient.GetAsync($"/marketdata/v1/quotes?symbols={symbolString}&fields=quote");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var quotesResponse = JsonSerializer.Deserialize<Dictionary<string, SchwabQuoteData>>(content);

            var quotes = new List<StockQuote>();
            foreach (var kvp in quotesResponse ?? new Dictionary<string, SchwabQuoteData>())
            {
                var quote = MapToStockQuote(kvp.Key, kvp.Value.quote);
                quotes.Add(quote);
            }

            return quotes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting quotes for symbols: {Symbols}", string.Join(",", symbols));
            return Enumerable.Empty<StockQuote>();
        }
    }

    public async Task<IEnumerable<CandlestickData>> GetPriceHistoryAsync(string symbol, TimeFrame timeFrame, DateTime? startDate = null, DateTime? endDate = null)
    {
        var request = new PriceHistoryRequest
        {
            Symbol = symbol,
            StartDate = startDate,
            EndDate = endDate
        };

        // Convert legacy TimeFrame to new system
        SetRequestFromTimeFrame(request, timeFrame);

        return await GetPriceHistoryAsync(request);
    }

    public async Task<IEnumerable<CandlestickData>> GetPriceHistoryAsync(PriceHistoryRequest request)
    {
        try
        {
            await EnsureValidTokenAsync();

            var queryParams = new List<string> { $"symbol={request.Symbol}" };

            // Add period type and period
            if (request.PeriodType.HasValue)
            {
                queryParams.Add($"periodType={request.PeriodType.ToString()!.ToLower()}");
                if (request.Period.HasValue)
                    queryParams.Add($"period={request.Period}");
            }

            // Add frequency type and frequency
            if (request.FrequencyType.HasValue)
            {
                queryParams.Add($"frequencyType={request.FrequencyType.ToString()!.ToLower()}");
                if (request.Frequency.HasValue)
                    queryParams.Add($"frequency={request.Frequency}");
            }

            // Add date range
            if (request.StartDate.HasValue)
                queryParams.Add($"startDate={((DateTimeOffset)request.StartDate.Value).ToUnixTimeMilliseconds()}");
            if (request.EndDate.HasValue)
                queryParams.Add($"endDate={((DateTimeOffset)request.EndDate.Value).ToUnixTimeMilliseconds()}");

            // Add optional parameters
            if (request.NeedExtendedHoursData)
                queryParams.Add("needExtendedHoursData=true");
            if (request.NeedPreviousClose)
                queryParams.Add("needPreviousClose=true");

            var query = string.Join("&", queryParams);
            var response = await _httpClient.GetAsync($"/marketdata/v1/pricehistory?{query}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var historyData = JsonSerializer.Deserialize<SchwabPriceHistory>(content);

            return MapToCandlestickData(request.Symbol, historyData?.candles ?? new List<SchwabCandle>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting price history for {Symbol}", request.Symbol);
            return Enumerable.Empty<CandlestickData>();
        }
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
        try
        {
            await EnsureValidTokenAsync();

            var sortParam = MapMoversSort(sort);
            var queryParams = new List<string> { $"sort={sortParam}" };

            if (frequency.HasValue)
                queryParams.Add($"frequency={frequency}");

            var query = string.Join("&", queryParams);
            var response = await _httpClient.GetAsync($"/marketdata/v1/movers/{index}?{query}");
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var movers = JsonSerializer.Deserialize<List<SchwabMover>>(content) ?? new List<SchwabMover>();

            return movers.Select(MapMoverToStockQuote);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting movers for {Index}", index);
            return Enumerable.Empty<StockQuote>();
        }
    }

    public Task<OptionChain> GetOptionChainAsync(string symbol, string? contractType = null)
    {
        throw new NotImplementedException("Option chains will be implemented in a future version");
    }

    public Task<IEnumerable<Order>> GetOrdersAsync(string accountHash, DateTime fromDate, DateTime toDate)
    {
        throw new NotImplementedException("Order management will be implemented in a future version");
    }

    public Task<OrderResult> PlaceOrderAsync(string accountHash, OrderRequest order)
    {
        throw new NotImplementedException("Order placement will be implemented in a future version");
    }

    public Task<bool> CancelOrderAsync(string accountHash, string orderId)
    {
        throw new NotImplementedException("Order cancellation will be implemented in a future version");
    }

    public Task<Order> GetOrderDetailsAsync(string accountHash, string orderId)
    {
        throw new NotImplementedException("Order details will be implemented in a future version");
    }

    public Task StartStreamingAsync(IEnumerable<string> symbols, Action<StreamingData> onDataReceived)
    {
        throw new NotImplementedException("Streaming will be implemented in a future version");
    }

    public Task StopStreamingAsync()
    {
        throw new NotImplementedException("Streaming will be implemented in a future version");
    }

    private async Task EnsureValidTokenAsync()
    {
        if (_credentials == null)
            throw new InvalidOperationException("Client is not authenticated");

        if (_credentials.TokenExpiry.HasValue && _credentials.TokenExpiry.Value <= DateTime.UtcNow.AddMinutes(5))
        {
            await RefreshTokenAsync();
        }
    }

    private void SetAuthorizationHeader()
    {
        if (!string.IsNullOrEmpty(_credentials?.AccessToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _credentials.AccessToken);
        }
    }

    // Mapping methods
    private StockQuote MapToStockQuote(string symbol, SchwabQuote quote)
    {
        return new StockQuote
        {
            Symbol = symbol,
            LastPrice = quote.lastPrice,
            Change = quote.netChange,
            ChangePercent = quote.netPercentChangeInDouble,
            Volume = quote.totalVolume,
            Ask = quote.askPrice,
            Bid = quote.bidPrice,
            High = quote.highPrice,
            Low = quote.lowPrice,
            Open = quote.openPrice,
            PreviousClose = quote.closePrice,
            LastUpdated = DateTime.UtcNow,
            MarketCap = quote.marketCap,
            SharesFloat = quote.sharesOutstanding
        };
    }

    private AccountDetails MapToAccountDetails(SchwabAccountDetails? accountData)
    {
        if (accountData?.securitiesAccount == null)
            return new AccountDetails();

        var account = accountData.securitiesAccount;
        return new AccountDetails
        {
            AccountNumber = account.accountNumber,
            TotalValue = account.currentBalances?.liquidationValue ?? 0,
            CashBalance = account.currentBalances?.cashBalance ?? 0,
            BuyingPower = account.currentBalances?.buyingPower ?? 0,
            Positions = account.positions?.Select(MapToPosition) ?? Enumerable.Empty<Position>()
        };
    }

    private Position MapToPosition(SchwabPosition position)
    {
        return new Position
        {
            Symbol = position.instrument?.symbol ?? "",
            Quantity = (int)position.longQuantity,
            AveragePrice = position.averagePrice,
            CurrentPrice = position.marketValue / Math.Max(position.longQuantity, 1),
            UnrealizedPnL = position.marketValue - (position.averagePrice * position.longQuantity)
        };
    }

    private IEnumerable<CandlestickData> MapToCandlestickData(string symbol, IEnumerable<SchwabCandle> candles)
    {
        return candles.Select(c => new CandlestickData
        {
            Symbol = symbol,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(c.datetime).DateTime,
            Open = c.open,
            High = c.high,
            Low = c.low,
            Close = c.close,
            Volume = c.volume
        });
    }

    private StockQuote MapMoverToStockQuote(SchwabMover mover)
    {
        return new StockQuote
        {
            Symbol = mover.symbol,
            LastPrice = mover.last,
            Change = mover.change,
            ChangePercent = mover.netPercentChangeInDouble,
            Volume = mover.totalVolume,
            LastUpdated = DateTime.UtcNow
        };
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

    private string MapMoversSort(MoversSort sort)
    {
        return sort switch
        {
            MoversSort.Volume => "VOLUME",
            MoversSort.Trades => "TRADES",
            MoversSort.PercentChangeUp => "PERCENT_CHANGE_UP",
            MoversSort.PercentChangeDown => "PERCENT_CHANGE_DOWN",
            _ => "VOLUME"
        };
    }

    public void SetCurrentUserId(int userId)
    {
        // TODO: Implement account tracking for this client
        _logger.LogDebug("SetCurrentUserId called with {UserId} (not implemented in this client)", userId);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

// Schwab API DTOs
internal class SchwabAccountInfo
{
    public string accountNumber { get; set; } = string.Empty;
    public string hashValue { get; set; } = string.Empty;
}

internal class SchwabQuoteData
{
    public SchwabQuote quote { get; set; } = new();
}

internal class SchwabQuote
{
    public decimal lastPrice { get; set; }
    public decimal netChange { get; set; }
    public decimal netPercentChangeInDouble { get; set; }
    public long totalVolume { get; set; }
    public decimal askPrice { get; set; }
    public decimal bidPrice { get; set; }
    public decimal highPrice { get; set; }
    public decimal lowPrice { get; set; }
    public decimal openPrice { get; set; }
    public decimal closePrice { get; set; }
    public decimal marketCap { get; set; }
    public float sharesOutstanding { get; set; }
}

internal class SchwabAccountDetails
{
    public SchwabSecuritiesAccount? securitiesAccount { get; set; }
}

internal class SchwabSecuritiesAccount
{
    public string accountNumber { get; set; } = string.Empty;
    public SchwabCurrentBalances? currentBalances { get; set; }
    public List<SchwabPosition>? positions { get; set; }
}

internal class SchwabCurrentBalances
{
    public decimal liquidationValue { get; set; }
    public decimal cashBalance { get; set; }
    public decimal buyingPower { get; set; }
}

internal class SchwabPosition
{
    public decimal longQuantity { get; set; }
    public decimal averagePrice { get; set; }
    public decimal marketValue { get; set; }
    public SchwabInstrument? instrument { get; set; }
}

internal class SchwabInstrument
{
    public string symbol { get; set; } = string.Empty;
}

internal class SchwabPriceHistory
{
    public List<SchwabCandle>? candles { get; set; }
}

internal class SchwabCandle
{
    public long datetime { get; set; }
    public decimal open { get; set; }
    public decimal high { get; set; }
    public decimal low { get; set; }
    public decimal close { get; set; }
    public long volume { get; set; }
}

internal class SchwabMover
{
    public string symbol { get; set; } = string.Empty;
    public decimal last { get; set; }
    public decimal change { get; set; }
    public decimal netPercentChangeInDouble { get; set; }
    public long totalVolume { get; set; }
}