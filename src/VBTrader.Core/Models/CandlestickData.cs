namespace VBTrader.Core.Models;

public class CandlestickData
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }

    // MACD Data
    public decimal? MACD { get; set; }
    public decimal? MACDSignal { get; set; }
    public decimal? MACDHistogram { get; set; }

    // Additional Technical Indicators
    public decimal? EMA12 { get; set; }
    public decimal? EMA26 { get; set; }
    public decimal? RSI { get; set; }
    public decimal? BollingerUpper { get; set; }
    public decimal? BollingerLower { get; set; }
    public decimal? BollingerMiddle { get; set; }
}

public class MACDSettings
{
    public int FastPeriod { get; set; } = 12;
    public int SlowPeriod { get; set; } = 26;
    public int SignalPeriod { get; set; } = 9;
}

public enum TimeFrame
{
    OneMinute = 1,
    FiveMinutes = 5,
    TenMinutes = 10,
    FifteenMinutes = 15,
    ThirtyMinutes = 30,
    OneHour = 60,
    FourHours = 240,
    OneDay = 1440,
    Weekly = 10080,
    Monthly = 43200
}

public enum PeriodType
{
    Day,
    Month,
    Year,
    YTD
}

public enum FrequencyType
{
    Minute,
    Daily,
    Weekly,
    Monthly
}

public class PriceHistoryRequest
{
    public string Symbol { get; set; } = string.Empty;
    public PeriodType? PeriodType { get; set; }
    public int? Period { get; set; }
    public FrequencyType? FrequencyType { get; set; }
    public int? Frequency { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool NeedExtendedHoursData { get; set; } = false;
    public bool NeedPreviousClose { get; set; } = false;
}

public class BulkPriceHistoryRequest
{
    public List<string> Symbols { get; set; } = new();
    public PeriodType PeriodType { get; set; } = PeriodType.Day;
    public int Period { get; set; } = 10;
    public FrequencyType FrequencyType { get; set; } = FrequencyType.Minute;
    public int Frequency { get; set; } = 1;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool NeedExtendedHoursData { get; set; } = false;
    public int MaxSymbols { get; set; } = 5;
}