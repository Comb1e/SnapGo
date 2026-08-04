using System.Windows;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using SnapGo.App.Helpers;
using SnapGo.App.Models;

namespace SnapGo.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly IOptionsMonitor<AppSettings> _options;

    public SettingsWindow(IOptions<AppSettings> options, IOptionsMonitor<AppSettings> optionsMonitor)
    {
        InitializeComponent();
        _settings = options.Value;
        _options = optionsMonitor;
        LoadSettings();
    }

    private void LoadSettings()
    {
        // General
        ChkAutoStart.IsChecked = AutoStartHelper.IsAutoStartEnabled();
        ChkStartMinimized.IsChecked = _settings.General.StartMinimized;

        // Docker mode
        var mode = _settings.Docker.Mode;
        if (mode == "OnDemand") RbOnDemand.IsChecked = true;
        else if (mode == "Smart") RbSmart.IsChecked = true;
        else RbAlways.IsChecked = true;

        TxtIdleTimeout.Text = _settings.Docker.IdleTimeoutMinutes.ToString();
        TxtHotkey.Text = $"{_settings.Hotkey.Modifiers}+{_settings.Hotkey.Key}";
        TxtSavePath.Text = _settings.Storage.DefaultSavePath;
    }

    private void BrowseSavePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select default save folder"
        };
        if (dialog.ShowDialog() == true)
        {
            TxtSavePath.Text = dialog.FolderName;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Apply auto-start immediately
        var autoStart = ChkAutoStart.IsChecked == true;
        AutoStartHelper.SetAutoStart(autoStart);

        // Save settings to appsettings.json
        var configPath = System.IO.Path.Combine(
            AppContext.BaseDirectory, "appsettings.json");

        if (System.IO.File.Exists(configPath))
        {
            try
            {
                var root = System.Text.Json.Nodes.JsonObject.Parse(
                    System.IO.File.ReadAllText(configPath));

                if (root != null)
                {
                    var appNode = root["App"];
                    if (appNode != null)
                    {
                        appNode["General"]!["AutoStart"] = autoStart;
                        appNode["General"]!["StartMinimized"] = ChkStartMinimized.IsChecked == true;
                        appNode["Docker"]!["Mode"] = GetSelectedMode();
                        appNode["Docker"]!["IdleTimeoutMinutes"] = int.TryParse(TxtIdleTimeout.Text, out var t) ? t : 30;
                        appNode["Storage"]!["DefaultSavePath"] = TxtSavePath.Text;
                    }

                    System.IO.File.WriteAllText(configPath, root.ToString());
                }
            }
            catch { /* Best effort */ }
        }

        DialogResult = true;
        Close();
    }

    private string GetSelectedMode()
    {
        if (RbOnDemand.IsChecked == true) return "OnDemand";
        if (RbSmart.IsChecked == true) return "Smart";
        return "AlwaysOn";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
