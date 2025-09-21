using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using VBTrader.Core.Models;
using VBTrader.Infrastructure.Database;
using System.Collections.ObjectModel;

namespace VBTrader.UI.ViewModels;

public partial class ChartViewModel : ObservableObject
{
    private readonly IDataService _dataService;

    [ObservableProperty]
    private string _symbol = string.Empty;

    [ObservableProperty]
    private TimeFrame _selectedTimeFrame = TimeFrame.OneMinute;

    [ObservableProperty]
    private DateTime _fromDate = DateTime.Today.AddDays(-7);

    [ObservableProperty]
    private DateTime _toDate = DateTime.Today;

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // Chart data
    public ObservableCollection<ISeries> CandlestickSeries { get; } = new();
    public ObservableCollection<ISeries> VolumeSeries { get; } = new();
    public ObservableCollection<ISeries> MACDSeries { get; } = new();

    // Chart axes
    public Axis[] XAxes { get; set; }
    public Axis[] YAxes { get; set; }
    public Axis[] VolumeYAxes { get; set; }
    public Axis[] MACDYAxes { get; set; }

    // MACD Settings
    [ObservableProperty]
    private MACDSettings _macdSettings = new();

    public ChartViewModel(IDataService dataService)
    {
        _dataService = dataService;
        InitializeAxes();
        InitializeChart();
    }

    private void InitializeAxes()
    {
        XAxes = new Axis[]
        {
            new Axis
            {
                Name = "Time",
                NamePaint = new SolidColorPaint(SKColors.White),
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                TextSize = 10,
                SeparatorsPaint = new SolidColorPaint(SKColors.Gray) { StrokeThickness = 1 },
                Labeler = value => new DateTime((long)value).ToString("HH:mm")
            }
        };

        YAxes = new Axis[]
        {
            new Axis
            {
                Name = "Price ($)",
                NamePaint = new SolidColorPaint(SKColors.White),
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                TextSize = 10,
                SeparatorsPaint = new SolidColorPaint(SKColors.Gray) { StrokeThickness = 1 },
                Position = LiveChartsCore.Measure.AxisPosition.End,
                Labeler = value => $"${value:F2}"
            }
        };

        VolumeYAxes = new Axis[]
        {
            new Axis
            {
                Name = "Volume",
                NamePaint = new SolidColorPaint(SKColors.White),
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                TextSize = 10,
                SeparatorsPaint = new SolidColorPaint(SKColors.Gray) { StrokeThickness = 1 },
                Position = LiveChartsCore.Measure.AxisPosition.End,
                Labeler = value => FormatVolume((long)value)
            }
        };

        MACDYAxes = new Axis[]
        {
            new Axis
            {
                Name = "MACD",
                NamePaint = new SolidColorPaint(SKColors.White),
                LabelsPaint = new SolidColorPaint(SKColors.LightGray),
                TextSize = 10,
                SeparatorsPaint = new SolidColorPaint(SKColors.Gray) { StrokeThickness = 1 },
                Position = LiveChartsCore.Measure.AxisPosition.End
            }
        };
    }

    private void InitializeChart()
    {
        // Initialize with empty series
        CandlestickSeries.Clear();
        VolumeSeries.Clear();
        MACDSeries.Clear();

        StatusMessage = "Ready to load chart data";
    }

    public async Task LoadChartDataAsync(string symbol)
    {
        if (string.IsNullOrEmpty(symbol))
            return;

        IsLoading = true;
        StatusMessage = $"Loading chart data for {symbol}...";
        Symbol = symbol;

        try
        {
            var candlestickData = await _dataService.GetCandlestickDataAsync(
                symbol, SelectedTimeFrame, FromDate, ToDate);

            if (!candlestickData.Any())
            {
                StatusMessage = "No chart data available";
                return;
            }

            UpdateCandlestickChart(candlestickData);
            UpdateVolumeChart(candlestickData);
            UpdateMACDChart(candlestickData);

            StatusMessage = $"Loaded {candlestickData.Count()} data points for {symbol}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading chart data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateCandlestickChart(IEnumerable<CandlestickData> data)
    {
        CandlestickSeries.Clear();

        var candlestickPoints = data.Select(candle => new FinancialPoint
        {
            X = candle.Timestamp.Ticks,
            Open = (double)candle.Open,
            High = (double)candle.High,
            Low = (double)candle.Low,
            Close = (double)candle.Close
        }).ToArray();

        var candlestickSeries = new CandlesticksSeries<FinancialPoint>
        {
            Values = candlestickPoints,
            Name = Symbol,
            UpFill = new SolidColorPaint(SKColor.Parse("#00C851")),   // Green for bullish
            UpStroke = new SolidColorPaint(SKColor.Parse("#00C851")) { StrokeThickness = 1 },
            DownFill = new SolidColorPaint(SKColor.Parse("#FF4444")), // Red for bearish
            DownStroke = new SolidColorPaint(SKColor.Parse("#FF4444")) { StrokeThickness = 1 },
            MaxBarWidth = 10
        };

        CandlestickSeries.Add(candlestickSeries);
    }

    private void UpdateVolumeChart(IEnumerable<CandlestickData> data)
    {
        VolumeSeries.Clear();

        var volumePoints = data.Select(candle => new ObservablePoint
        {
            X = candle.Timestamp.Ticks,
            Y = candle.Volume
        }).ToArray();

        var volumeSeries = new ColumnSeries<ObservablePoint>
        {
            Values = volumePoints,
            Name = "Volume",
            Fill = new SolidColorPaint(SKColor.Parse("#4400C851")),  // Semi-transparent green
            Stroke = new SolidColorPaint(SKColor.Parse("#00C851")) { StrokeThickness = 1 },
            MaxBarWidth = 8
        };

        VolumeSeries.Add(volumeSeries);
    }

    private void UpdateMACDChart(IEnumerable<CandlestickData> data)
    {
        MACDSeries.Clear();

        var dataWithMACD = data.Where(d => d.MACD.HasValue && d.MACDSignal.HasValue).ToArray();

        if (!dataWithMACD.Any())
        {
            // Calculate MACD if not present
            dataWithMACD = CalculateMACD(data.ToArray());
        }

        // MACD Line
        var macdPoints = dataWithMACD.Select(candle => new ObservablePoint
        {
            X = candle.Timestamp.Ticks,
            Y = (double)(candle.MACD ?? 0)
        }).ToArray();

        var macdLineSeries = new LineSeries<ObservablePoint>
        {
            Values = macdPoints,
            Name = "MACD",
            Stroke = new SolidColorPaint(SKColor.Parse("#2196F3")) { StrokeThickness = 2 },
            Fill = null,
            GeometrySize = 0
        };

        // Signal Line
        var signalPoints = dataWithMACD.Select(candle => new ObservablePoint
        {
            X = candle.Timestamp.Ticks,
            Y = (double)(candle.MACDSignal ?? 0)
        }).ToArray();

        var signalLineSeries = new LineSeries<ObservablePoint>
        {
            Values = signalPoints,
            Name = "Signal",
            Stroke = new SolidColorPaint(SKColor.Parse("#FF9800")) { StrokeThickness = 2 },
            Fill = null,
            GeometrySize = 0
        };

        // Histogram
        var histogramPoints = dataWithMACD.Select(candle => new ObservablePoint
        {
            X = candle.Timestamp.Ticks,
            Y = (double)(candle.MACDHistogram ?? 0)
        }).ToArray();

        var histogramSeries = new ColumnSeries<ObservablePoint>
        {
            Values = histogramPoints,
            Name = "Histogram",
            Fill = new SolidColorPaint(SKColor.Parse("#449C27B0")),  // Semi-transparent purple
            Stroke = new SolidColorPaint(SKColor.Parse("#9C27B0")) { StrokeThickness = 1 },
            MaxBarWidth = 6
        };

        MACDSeries.Add(macdLineSeries);
        MACDSeries.Add(signalLineSeries);
        MACDSeries.Add(histogramSeries);
    }

    private CandlestickData[] CalculateMACD(CandlestickData[] data)
    {
        if (data.Length < MacdSettings.SlowPeriod + MacdSettings.SignalPeriod)
            return data;

        var closes = data.Select(d => (double)d.Close).ToArray();

        // Calculate EMA12 and EMA26
        var ema12 = CalculateEMA(closes, MacdSettings.FastPeriod);
        var ema26 = CalculateEMA(closes, MacdSettings.SlowPeriod);

        // Calculate MACD line
        var macdLine = new double[data.Length];
        for (int i = MacdSettings.SlowPeriod - 1; i < data.Length; i++)
        {
            macdLine[i] = ema12[i] - ema26[i];
        }

        // Calculate Signal line (EMA of MACD)
        var validMacdValues = macdLine.Skip(MacdSettings.SlowPeriod - 1).ToArray();
        var signalLine = CalculateEMA(validMacdValues, MacdSettings.SignalPeriod);

        // Update data with calculated values
        for (int i = 0; i < data.Length; i++)
        {
            if (i >= MacdSettings.SlowPeriod - 1)
            {
                data[i].EMA12 = (decimal)ema12[i];
                data[i].EMA26 = (decimal)ema26[i];
                data[i].MACD = (decimal)macdLine[i];

                var signalIndex = i - (MacdSettings.SlowPeriod - 1);
                if (signalIndex >= MacdSettings.SignalPeriod - 1)
                {
                    data[i].MACDSignal = (decimal)signalLine[signalIndex];
                    data[i].MACDHistogram = data[i].MACD - data[i].MACDSignal;
                }
            }
        }

        return data;
    }

    private double[] CalculateEMA(double[] values, int period)
    {
        var ema = new double[values.Length];
        var multiplier = 2.0 / (period + 1);

        // Initialize with SMA for the first period
        var sma = values.Take(period).Average();
        ema[period - 1] = sma;

        // Calculate EMA
        for (int i = period; i < values.Length; i++)
        {
            ema[i] = (values[i] * multiplier) + (ema[i - 1] * (1 - multiplier));
        }

        return ema;
    }

    private string FormatVolume(long volume)
    {
        if (volume >= 1_000_000_000)
            return $"{volume / 1_000_000_000.0:F1}B";
        if (volume >= 1_000_000)
            return $"{volume / 1_000_000.0:F1}M";
        if (volume >= 1_000)
            return $"{volume / 1_000.0:F1}K";
        return volume.ToString("N0");
    }

    public async Task RefreshDataAsync()
    {
        if (!string.IsNullOrEmpty(Symbol))
        {
            await LoadChartDataAsync(Symbol);
        }
    }

    public void UpdateTimeFrame(TimeFrame timeFrame)
    {
        SelectedTimeFrame = timeFrame;
        _ = RefreshDataAsync(); // Fire and forget
    }

    public void UpdateMACDSettings(MACDSettings settings)
    {
        MacdSettings = settings;
        _ = RefreshDataAsync(); // Fire and forget
    }
}