using System.Net.Http;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using SnapGo.App.Helpers;
using SnapGo.App.Models;
using SnapGo.App.Services;
using SnapGo.App.ViewModels;
using SnapGo.App.Views;

namespace SnapGo.App;

public partial class App : Application
{
    private readonly IHost _host;
    private Mutex? _singleInstanceMutex;

    public IServiceProvider Services => _host.Services;

    public App()
    {
        _host = CreateHost();
    }

    private static IHost CreateHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                // Configuration
                services.Configure<AppSettings>(context.Configuration.GetSection("App"));

                // HttpClient
                services.AddSingleton<HttpClient>(_ => new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(
                        context.Configuration.GetValue<int>("App:Ocr:TimeoutSeconds", 30))
                });

                // Core services (singleton — application lifetime)
                services.AddSingleton<IScreenshotService, ScreenshotService>();
                services.AddSingleton<IOcrService, OcrService>();
                services.AddSingleton<IDockerService, DockerService>();
                services.AddSingleton<IClipboardService, ClipboardService>();
                services.AddSingleton<IHistoryService, HistoryService>();
                services.AddSingleton<IAnnotationService, AnnotationService>();

                // ViewModels (transient — new instance per navigation)
                services.AddTransient<MainViewModel>();
                services.AddTransient<ScreenshotViewModel>();
                services.AddTransient<ResultViewModel>();
                services.AddTransient<HistoryViewModel>();
                services.AddTransient<SettingsViewModel>();

                // Views (transient)
                services.AddTransient<MainWindow>();
                services.AddTransient<ScreenshotOverlay>();
                services.AddTransient<FloatingToolbar>();
                services.AddTransient<ResultPanel>();
                services.AddTransient<SettingsWindow>();
            })
            .UseSerilog((context, cfg) =>
            {
                var logSettings = context.Configuration.GetSection("App:Logging").Get<LoggingSettings>();
                cfg.MinimumLevel.Information();
                cfg.WriteTo.File(
                    path: System.IO.Path.Combine(AppContext.BaseDirectory, logSettings?.LogFilePath ?? "logs/snapgo-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7);
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Single-instance check: if another SnapGo is already running,
        // bring its window to front and exit this instance.
        _singleInstanceMutex = new Mutex(true, @"Global\SnapGo_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;

            // Find and activate the existing instance's window
            var existingHwnd = NativeMethods.FindWindow(null, "SnapGo");
            if (existingHwnd != IntPtr.Zero)
            {
                if (NativeMethods.IsIconic(existingHwnd))
                    NativeMethods.ShowWindow(existingHwnd, NativeMethods.SW_RESTORE);
                NativeMethods.SetForegroundWindow(existingHwnd);
            }

            Current.Shutdown();
            return;
        }

        base.OnStartup(e);
        await _host.StartAsync();

        // Apply auto-start setting on launch
        ApplyAutoStart();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        var settings = _host.Services.GetRequiredService<IOptions<AppSettings>>().Value;

        // Show window first (creates HWND, fires Loaded → registers hotkey + sets tray icon)
        if (!settings.General.StartMinimized)
        {
            mainWindow.ShowInTaskbar = true;
        }
        mainWindow.Show();

        // Then minimize to tray if configured
        if (settings.General.StartMinimized)
        {
            mainWindow.WindowState = WindowState.Minimized;
        }
    }

    private static void ApplyAutoStart()
    {
        var settings = ((App)Current).Services.GetRequiredService<IOptions<AppSettings>>().Value;

        // Sync registry with config on startup
        var registryEnabled = AutoStartHelper.IsAutoStartEnabled();
        if (settings.General.AutoStart != registryEnabled)
        {
            AutoStartHelper.SetAutoStart(settings.General.AutoStart);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await _host.StopAsync(cts.Token);
        _host.Dispose();
        _singleInstanceMutex?.Dispose();
    }
}
