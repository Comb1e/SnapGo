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

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
