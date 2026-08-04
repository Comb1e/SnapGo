using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using SnapGo.App.ViewModels;

namespace SnapGo.App.Views;

public partial class HistoryPanel : UserControl
{
    private HistoryViewModel _viewModel = null!;

    public HistoryPanel()
    {
        InitializeComponent();
    }

    public void SetViewModel(HistoryViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.LoadCommand.ExecuteAsync(null);
        _viewModel.PropertyChanged += (_, _) =>
        {
            ItemsControl.ItemsSource = _viewModel.Items;
        };
        ItemsControl.ItemsSource = _viewModel.Items;
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _viewModel.SearchText = SearchBox.Text;
        if (string.IsNullOrWhiteSpace(SearchBox.Text))
            await _viewModel.LoadCommand.ExecuteAsync(null);
        else
            await _viewModel.SearchCommand.ExecuteAsync(null);
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshCommand.ExecuteAsync(null);
    }

    private void Item_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is HistoryItemViewModel item)
        {
            // Show full image
            using var ms = new MemoryStream(item.ImageData);
            var decoder = new PngBitmapDecoder(ms,
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
            var image = decoder.Frames[0];

            var window = new Window
            {
                Title = item.Title,
                Width = Math.Min(image.Width + 40, 1200),
                Height = Math.Min(image.Height + 40, 900),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Content = new Image { Source = image, Stretch = System.Windows.Media.Stretch.Uniform }
            };
            window.ShowDialog();
        }
    }

    private async void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is HistoryItemViewModel item)
        {
            await _viewModel.DeleteCommand.ExecuteAsync(item);
        }
    }
}
