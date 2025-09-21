using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using VBTrader.Core.Interfaces;
using VBTrader.Core.Models;
using VBTrader.Infrastructure.Database;
using VBTrader.Infrastructure.SchwabApi;
using VBTrader.Security.Authentication;
using VBTrader.Security.Cryptography;
using VBTrader.Services;
using VBTrader.UI.Services;
using VBTrader.UI.ViewModels;
using VBTrader.UI.Views;
using Microsoft.EntityFrameworkCore;

namespace VBTrader.UI;

public partial class App : Application
{
    private readonly IHost _host;
    private Window? _mainWindow;

    public App()
    {
        this.InitializeComponent();

        _host = CreateHostBuilder().Build();
    }

    private static IHostBuilder CreateHostBuilder()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true);
                config.AddUserSecrets<App>();
            })
            .ConfigureServices((context, services) =>
            {
                // Configuration
                var configuration = context.Configuration;

                // Database
                services.AddDbContext<VBTraderDbContext>(options =>
                    options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

                // Core services
                services.AddSingleton<IPasswordHasher, PasswordHasher>();
                services.AddSingleton<ICredentialEncryption, CredentialEncryption>();
                services.AddSingleton<IUserAuthenticationService, UserAuthenticationService>();

                // API Client
                services.AddHttpClient<ISchwabApiClient, SchwabApiClient>();

                // Data service
                services.AddScoped<IDataService, PostgreSqlDataService>();

                // Market settings
                services.AddSingleton(provider =>
                {
                    var config = new MarketSettings();
                    configuration.GetSection("MarketSettings").Bind(config);
                    return config;
                });

                // Background services
                services.AddHostedService<MarketDataCollectionService>();

                // UI Services
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<IHotkeyService, HotkeyService>();
                services.AddSingleton<IThemeService, ThemeService>();

                // ViewModels
                services.AddTransient<LoginViewModel>();
                services.AddTransient<MainViewModel>();
                services.AddTransient<MarketDataViewModel>();
                services.AddTransient<ChartViewModel>();
                services.AddTransient<OpportunityViewModel>();
                services.AddTransient<SettingsViewModel>();

                // Views
                services.AddTransient<LoginWindow>();
                services.AddTransient<MainWindow>();
                services.AddTransient<SettingsWindow>();

                // Logging
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.AddDebug();
                });
            });
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await _host.StartAsync();

        var authService = _host.Services.GetRequiredService<IUserAuthenticationService>();

        // Check if user exists, show login or register accordingly
        if (authService.HasExistingUser())
        {
            _mainWindow = _host.Services.GetRequiredService<LoginWindow>();
        }
        else
        {
            _mainWindow = _host.Services.GetRequiredService<LoginWindow>();
            // Set register mode in login window
        }

        _mainWindow.Activate();
    }

    protected override async void OnSuspended(SuspendingEventArgs args)
    {
        var deferral = args.SuspendingOperation.GetDeferral();
        try
        {
            await _host.StopAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }

    public static T GetService<T>() where T : class
    {
        if ((App.Current as App)?._host?.Services?.GetService(typeof(T)) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
        }

        return service;
    }
}