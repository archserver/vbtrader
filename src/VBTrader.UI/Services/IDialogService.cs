using Microsoft.UI.Xaml.Controls;

namespace VBTrader.UI.Services;

public interface IDialogService
{
    Task<ContentDialogResult> ShowMessageAsync(string title, string message);
    Task<ContentDialogResult> ShowConfirmationAsync(string title, string message);
    Task<string?> ShowInputAsync(string title, string message, string placeholder = "");
    Task<bool> ShowErrorAsync(string title, string message);
    Task ShowProgressAsync(string title, string message, Func<IProgress<string>, CancellationToken, Task> operation);
}

public class DialogService : IDialogService
{
    public async Task<ContentDialogResult> ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = GetCurrentXamlRoot()
        };

        return await dialog.ShowAsync();
    }

    public async Task<ContentDialogResult> ShowConfirmationAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = "Yes",
            SecondaryButtonText = "No",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = GetCurrentXamlRoot()
        };

        return await dialog.ShowAsync();
    }

    public async Task<string?> ShowInputAsync(string title, string message, string placeholder = "")
    {
        var textBox = new TextBox
        {
            PlaceholderText = placeholder,
            Width = 300
        };

        var stackPanel = new StackPanel
        {
            Spacing = 10
        };
        stackPanel.Children.Add(new TextBlock { Text = message });
        stackPanel.Children.Add(textBox);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = stackPanel,
            PrimaryButtonText = "OK",
            SecondaryButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = GetCurrentXamlRoot()
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? textBox.Text : null;
    }

    public async Task<bool> ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = GetCurrentXamlRoot()
        };

        await dialog.ShowAsync();
        return true;
    }

    public async Task ShowProgressAsync(string title, string message, Func<IProgress<string>, CancellationToken, Task> operation)
    {
        var progressRing = new ProgressRing
        {
            IsActive = true,
            Width = 50,
            Height = 50
        };

        var progressText = new TextBlock
        {
            Text = message,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center
        };

        var stackPanel = new StackPanel
        {
            Spacing = 15,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center
        };
        stackPanel.Children.Add(progressRing);
        stackPanel.Children.Add(progressText);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = stackPanel,
            XamlRoot = GetCurrentXamlRoot()
        };

        var cts = new CancellationTokenSource();
        var progress = new Progress<string>(status =>
        {
            progressText.Text = status;
        });

        // Show dialog without waiting
        var dialogTask = dialog.ShowAsync();

        try
        {
            await operation(progress, cts.Token);
        }
        finally
        {
            dialog.Hide();
        }
    }

    private Microsoft.UI.Xaml.XamlRoot GetCurrentXamlRoot()
    {
        if (App.Current.MainWindow?.Content is Microsoft.UI.Xaml.FrameworkElement element)
        {
            return element.XamlRoot;
        }

        throw new InvalidOperationException("No active window found");
    }
}