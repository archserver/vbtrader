using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace VBTrader.UI.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            var invert = parameter?.ToString() == "Inverse";
            var visible = invert ? !boolValue : boolValue;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility visibility)
        {
            var invert = parameter?.ToString() == "Inverse";
            var isVisible = visibility == Visibility.Visible;
            return invert ? !isVisible : isVisible;
        }
        return false;
    }
}

public class BoolNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool boolValue ? !boolValue : true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is bool boolValue ? !boolValue : false;
    }
}

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var str = value?.ToString();
        var invert = parameter?.ToString() == "Inverse";
        var hasValue = !string.IsNullOrEmpty(str);
        var visible = invert ? !hasValue : hasValue;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class BoolToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue && parameter is string paramString)
        {
            var parts = paramString.Split('|');
            if (parts.Length == 2)
            {
                return boolValue ? parts[0] : parts[1];
            }
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class NumberToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is decimal decimalValue)
        {
            return decimalValue >= 0 ? App.Current.Resources["TradingGreen"] : App.Current.Resources["TradingRed"];
        }
        if (value is double doubleValue)
        {
            return doubleValue >= 0 ? App.Current.Resources["TradingGreen"] : App.Current.Resources["TradingRed"];
        }
        if (value is float floatValue)
        {
            return floatValue >= 0 ? App.Current.Resources["TradingGreen"] : App.Current.Resources["TradingRed"];
        }
        return App.Current.Resources["TradingForeground"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class NumberToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is decimal decimalValue)
        {
            return decimalValue >= 0 ? "▲" : "▼";
        }
        if (value is double doubleValue)
        {
            return doubleValue >= 0 ? "▲" : "▼";
        }
        if (value is float floatValue)
        {
            return floatValue >= 0 ? "▲" : "▼";
        }
        return "■";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class NewsRatingToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is VBTrader.Core.Models.NewsRating rating)
        {
            return rating switch
            {
                VBTrader.Core.Models.NewsRating.Amazing => App.Current.Resources["NewsAmazing"],
                VBTrader.Core.Models.NewsRating.Great => App.Current.Resources["NewsGreat"],
                VBTrader.Core.Models.NewsRating.Good => App.Current.Resources["NewsGood"],
                VBTrader.Core.Models.NewsRating.OK => App.Current.Resources["NewsOK"],
                VBTrader.Core.Models.NewsRating.Bad => App.Current.Resources["NewsBad"],
                _ => App.Current.Resources["TradingMutedForeground"]
            };
        }
        return App.Current.Resources["TradingMutedForeground"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class NewsRatingToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is VBTrader.Core.Models.NewsRating rating)
        {
            return rating switch
            {
                VBTrader.Core.Models.NewsRating.Amazing => "AMAZING",
                VBTrader.Core.Models.NewsRating.Great => "GREAT",
                VBTrader.Core.Models.NewsRating.Good => "GOOD",
                VBTrader.Core.Models.NewsRating.OK => "OK",
                VBTrader.Core.Models.NewsRating.Bad => "BAD",
                _ => "N/A"
            };
        }
        return "N/A";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class VolumeToFormattedStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is long volume)
        {
            if (volume >= 1_000_000_000)
                return $"{volume / 1_000_000_000.0:F1}B";
            if (volume >= 1_000_000)
                return $"{volume / 1_000_000.0:F1}M";
            if (volume >= 1_000)
                return $"{volume / 1_000.0:F1}K";
            return volume.ToString("N0");
        }
        return "0";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}