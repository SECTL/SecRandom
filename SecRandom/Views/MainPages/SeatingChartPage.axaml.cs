using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.ViewModels.MainPages;

namespace SecRandom.Views.MainPages;

[PageInfo("main.seatingChart", FluentIcons.PeopleListFilled, location: PageLocation.Bottom, useFullWidth: true, hidePageTitle: true)]
public partial class SeatingChartPage : UserControl
{
    public SeatingChartPage()
    {
        ViewModel = IAppHost.GetService<SeatingChartPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public SeatingChartPageViewModel ViewModel { get; }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ViewModel.RefreshCommand.Execute(null);
    }

    private async void ImportCspsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var text = await ReadJsonAsync("导入 CSPS 座位表");
        if (text is null)
            return;
        try
        {
            ViewModel.ImportCsps(text);
        }
        catch (Exception exception)
        {
            ViewModel.StatusText = exception.Message;
        }
    }

    private async void ExportCspsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await WriteJsonAsync("导出 CSPS 座位表", "seating-chart.csps", ViewModel.ExportCsps());
        }
        catch (Exception exception)
        {
            ViewModel.StatusText = exception.Message;
        }
    }

    private async void ImportCslsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var text = await ReadJsonAsync("导入 CSLS 学生名单");
        if (text is null)
            return;
        try
        {
            ViewModel.ImportCsls(text);
        }
        catch (Exception exception)
        {
            ViewModel.StatusText = exception.Message;
        }
    }

    private async void ExportCslsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await WriteJsonAsync("导出 CSLS 学生名单", "student-list.csls", ViewModel.ExportCsls());
        }
        catch (Exception exception)
        {
            ViewModel.StatusText = exception.Message;
        }
    }

    private async Task<string?> ReadJsonAsync(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return null;
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("CSIS JSON") { Patterns = ["*.csps", "*.csls", "*.json"] }]
        });
        var file = files.FirstOrDefault();
        if (file is null)
            return null;
        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private async Task WriteJsonAsync(string title, string suggestedFileName, string content)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = [new FilePickerFileType("CSIS JSON") { Patterns = ["*.json"] }]
        });
        if (file is null)
            return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
    }

    private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
}
