using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VBTrader.UI.ViewModels;
using VBTrader.Core.Models;

namespace VBTrader.UI.Controls;

public sealed partial class TradingChart : UserControl
{
    public ChartViewModel? ViewModel { get; private set; }

    public TradingChart()
    {
        this.InitializeComponent();
        // ViewModel will be set externally via dependency injection
    }

    public void SetViewModel(ChartViewModel viewModel)
    {
        ViewModel = viewModel;
        this.DataContext = ViewModel;
    }

    private void TimeFrameComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel != null && sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
        {
            if (int.TryParse(item.Tag?.ToString(), out int minutes))
            {
                ViewModel.SelectedTimeFrame = (TimeFrame)minutes;
                _ = ViewModel.RefreshDataAsync();
            }
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.RefreshDataAsync();
        }
    }

    public async Task LoadSymbolAsync(string symbol)
    {
        if (ViewModel != null)
        {
            await ViewModel.LoadChartDataAsync(symbol);
        }
    }
}