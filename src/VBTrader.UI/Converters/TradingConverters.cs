using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace VBTrader.UI.Converters;

public class CurrencyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is decimal decimalValue)
            return $"${decimalValue:F2}";
        if (value is double doubleValue)
            return $"${doubleValue:F2}";
        if (value is float floatValue)
            return $"${floatValue:F2}";
        return "$0.00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class CurrencySignedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is decimal decimalValue)
            return decimalValue >= 0 ? $"+${decimalValue:F2}" : $"-${Math.Abs(decimalValue):F2}";
        if (value is double doubleValue)
            return doubleValue >= 0 ? $"+${doubleValue:F2}" : $"-${Math.Abs(doubleValue):F2}";
        if (value is float floatValue)
            return floatValue >= 0 ? $"+${floatValue:F2}" : $"-${Math.Abs(floatValue):F2}";
        return "+$0.00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class PercentageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is decimal decimalValue)
            return $"{decimalValue:F2}%";
        if (value is double doubleValue)
            return $"{doubleValue:F2}%";
        if (value is float floatValue)
            return $"{floatValue:F2}%";
        return "0.00%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class PercentageSignedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is decimal decimalValue)
            return decimalValue >= 0 ? $"+{decimalValue:F2}%" : $"{decimalValue:F2}%";
        if (value is double doubleValue)
            return doubleValue >= 0 ? $"+{doubleValue:F2}%" : $"{doubleValue:F2}%";
        if (value is float floatValue)
            return floatValue >= 0 ? $"+{floatValue:F2}%" : $"{floatValue:F2}%";
        return "+0.00%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class ScoreConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is decimal decimalValue)
            return $"{decimalValue:F0}";
        if (value is double doubleValue)
            return $"{doubleValue:F0}";
        if (value is int intValue)
            return intValue.ToString();
        return "0";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class DateTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTime dateTime)
            return dateTime.ToString("HH:mm:ss");
        return DateTime.Now.ToString("HH:mm:ss");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}

public class BoolToStatusColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isConnected)
        {
            return isConnected
                ? App.Current.Resources["TradingGreen"] as SolidColorBrush
                : App.Current.Resources["TradingRed"] as SolidColorBrush;
        }
        return App.Current.Resources["TradingRed"] as SolidColorBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}