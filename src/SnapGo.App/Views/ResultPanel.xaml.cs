using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SnapGo.App.Models;
using SnapGo.App.Services;
using SnapGo.App.ViewModels;

namespace SnapGo.App.Views;

public partial class ResultPanel : Window
{
    private readonly ResultViewModel _viewModel;

    public ResultPanel(ResultViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        _viewModel.PropertyChanged += (s, e) => RefreshUI();
    }

    public void SetResult(OcrResult result)
    {
        _viewModel.SetResult(result);
        Title = "OCR Result";
    }

    public void SetError(string message)
    {
        _viewModel.SetError(message);
        Title = "OCR Error";
    }

    public void SetLoading(string message = "Recognizing text...")
    {
        _viewModel.SetLoading(message);
    }

    private void RefreshUI()
    {
        TxtElapsed.Text = _viewModel.ElapsedInfo;
        TxtModel.Text = _viewModel.ModelInfo;

        if (_viewModel.IsLoading)
        {
            LoadingPanel.Visibility = Visibility.Visible;
            TxtLoading.Text = _viewModel.LoadingMessage;
            TxtError.Visibility = Visibility.Collapsed;
            ResultTextBox.Visibility = Visibility.Collapsed;
        }
        else if (_viewModel.HasError)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            TxtError.Text = _viewModel.ErrorMessage ?? "Unknown error";
            TxtError.Visibility = Visibility.Visible;
            ResultTextBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            TxtError.Visibility = Visibility.Collapsed;
            ResultTextBox.Text = _viewModel.RecognizedText;
            ResultTextBox.Visibility = Visibility.Visible;
        }
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CopyTextCommand.Execute(null);
    }

    private void SaveText_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Text File|*.txt",
            DefaultExt = "txt",
            FileName = $"ocr_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };

        if (dialog.ShowDialog() == true)
        {
            File.WriteAllText(dialog.FileName, _viewModel.RecognizedText);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
