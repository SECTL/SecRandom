using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Langs.SettingsPages.ListManagement.LotteryList.Resources;

namespace SecRandom.Views.SettingsPages.ListManagement;

[PageInfo("settings.listManagement.lotteryList", FluentIcons.LotteryRegular, "settings.listManagement")]
public partial class LotteryListSettingsPage : UserControl, INotifyPropertyChanged
{
    private string _selectedPrizeListName = string.Empty;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;
    private readonly ILogger<LotteryListSettingsPage> _logger =
        IAppHost.GetService<ILogger<LotteryListSettingsPage>>();

    public LotteryListSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        RefreshPrizeLists();
    }

    public ObservableCollection<string> PrizeListNames { get; } = [];

    public string SelectedPrizeListName
    {
        get => _selectedPrizeListName;
        set
        {
            if (_selectedPrizeListName == value)
                return;

            _selectedPrizeListName = value;
            OnPropertyChanged();
        }
    }

    public PrizeList? SelectedPrizeList => SelectedPrizeListConfig?.Data;

    private PrizeListConfig? SelectedPrizeListConfig { get; set; }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private void RefreshPrizeLists()
    {
        RefreshPrizeLists(SelectedPrizeListName);
    }

    private void RefreshPrizeLists(string selectedName)
    {
        PrizeListNames.Clear();

        foreach (var file in Directory.GetFiles(Utils.GetDirectoryPath("data", "list", "lottery_list"), "*.json")
                     .OrderBy(Path.GetFileName))
            PrizeListNames.Add(Path.GetFileNameWithoutExtension(file));

        if (PrizeListNames.Count == 0)
        {
            var config = new PrizeListConfig("default");
            config.Save();
            PrizeListNames.Add(config.Name);
        }

        SelectedPrizeListName = PrizeListNames.Contains(selectedName) ? selectedName : PrizeListNames[0];
        LoadSelectedPrizeList();
    }

    private void LoadSelectedPrizeList()
    {
        if (string.IsNullOrWhiteSpace(SelectedPrizeListName))
            return;

        SelectedPrizeListConfig?.Save();
        SelectedPrizeListConfig = new PrizeListConfig(SelectedPrizeListName);
        OnPropertyChanged(nameof(SelectedPrizeList));
    }

    private void SaveSelectedPrizeList()
    {
        SelectedPrizeListConfig?.Save();

        var service = IAppHost.TryGetService<IProfileService>();
        if (service?.PrizeListConfig?.Name == SelectedPrizeListName)
            service.LoadPrizeProfile(SelectedPrizeListName, saveCurrent: false);

        var config = IAppHost.TryGetService<MainConfigHandler>();
        if (config != null && config.Data.QuickDrawSettings.DefaultClass != SelectedPrizeListName)
        {
            config.Data.QuickDrawSettings.DefaultClass = SelectedPrizeListName;
            config.Save();
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        SaveSelectedPrizeList();
    }

    private void AttachedSettings_OnSettingsChanged(object? sender, EventArgs e)
    {
        SaveSelectedPrizeList();
    }

    private void PrizeListComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        LoadSelectedPrizeList();
    }

    private void RefreshButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RefreshPrizeLists();
    }

    private async void AddListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var listName = await ShowListNameDialogAsync(LR.M_ListNameDialogTitle_Add, LR.M_ListNameDialogPrimary_Add,
            CreateDefaultListName());
        if (listName == null || !ValidateNewListName(listName))
            return;

        SaveSelectedPrizeList();
        var config = new PrizeListConfig(listName);
        config.Save();
        RefreshPrizeLists(listName);
        _logger.LogInformation("已创建奖品池：奖品池={ListName}。", listName);
        this.ShowSuccessToast(string.Format(LR.M_AddListSuccess, listName));
    }

    private async void RenameListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedPrizeListName) || SelectedPrizeListConfig == null)
        {
            this.ShowWarningToast(LR.M_SelectListFirst);
            return;
        }

        var oldName = SelectedPrizeListName;
        var newName = await ShowListNameDialogAsync(LR.M_ListNameDialogTitle_Rename, LR.M_ListNameDialogPrimary_Rename,
            oldName);
        if (newName == null || newName == oldName || !ValidateNewListName(newName))
            return;

        SaveSelectedPrizeList();

        RenameProfileFile(new PrizeList(oldName), new PrizeList(newName));
        RenameProfileFile(new PrizeHistory(oldName), new PrizeHistory(newName));

        var service = IAppHost.GetService<IProfileService>();
        if (service.PrizeListConfig?.Name == oldName)
            service.LoadPrizeProfile(newName, saveCurrent: false);

        RefreshPrizeLists(newName);
        _logger.LogInformation("已重命名奖品池：原奖品池={OldListName}，新奖品池={NewListName}。", oldName, newName);
        this.ShowSuccessToast(string.Format(LR.M_RenameListSuccess, newName));
    }

    private async void DeleteListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedPrizeListName) || SelectedPrizeListConfig == null)
        {
            this.ShowWarningToast(LR.M_SelectListFirst);
            return;
        }

        if (PrizeListNames.Count <= 1)
        {
            this.ShowWarningToast(LR.M_KeepOneList);
            return;
        }

        var deleteName = SelectedPrizeListName;
        if (!await ConfirmDeleteAsync(deleteName))
            return;

        SelectedPrizeListConfig = null;
        DeleteProfileFile(new PrizeList(deleteName));
        DeleteProfileFile(new PrizeHistory(deleteName));

        var nextName = PrizeListNames.FirstOrDefault(name => name != deleteName) ?? string.Empty;
        RefreshPrizeLists(nextName);

        var service = IAppHost.GetService<IProfileService>();
        if (service.PrizeListConfig?.Name == deleteName)
            service.LoadPrizeProfile(nextName, saveCurrent: false);

        _logger.LogInformation("已删除奖品池：奖品池={ListName}。", deleteName);
        this.ShowSuccessToast(string.Format(LR.M_DeleteListSuccess, deleteName));
    }

    private void ImportButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedPrizeListConfig == null)
        {
            this.ShowWarningToast(LR.M_SelectListFirst);
            return;
        }

        var view = new LotteryListImportView(SelectedPrizeListName, OnPrizesImported);
        SettingsView.Current?.OpenDrawer(view);
        _logger.LogInformation("打开奖品池导入面板：目标奖品池={ListName}。", SelectedPrizeListName);
    }

    private async Task<bool> ConfirmOverwriteAsync(int currentCount, int importCount)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_OverwriteTitle,
            Content = string.Format(LR.M_OverwriteContent, currentCount, importCount),
            PrimaryButtonText = LR.M_OverwritePrimary,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));

        return result == FAContentDialogResult.Primary;
    }

    private async Task<bool> ConfirmDeleteAsync(string listName)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_DeleteListTitle,
            Content = string.Format(LR.M_DeleteListContent, listName),
            PrimaryButtonText = LR.M_DeleteListPrimary,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));

        return result == FAContentDialogResult.Primary;
    }

    private async Task<string?> ShowListNameDialogAsync(string title, string primaryButtonText, string listName)
    {
        var textBox = new TextBox
        {
            Text = listName,
            PlaceholderText = LR.M_ListNamePlaceholder,
            MinWidth = 320
        };

        var result = await new FAContentDialog
        {
            Title = title,
            Content = textBox,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Primary
        }.ShowAsync(TopLevel.GetTopLevel(this));

        return result == FAContentDialogResult.Primary ? textBox.Text?.Trim() : null;
    }

    private bool ValidateNewListName(string listName)
    {
        if (string.IsNullOrWhiteSpace(listName))
        {
            this.ShowWarningToast(LR.M_ListNameEmpty);
            return false;
        }

        if (listName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            this.ShowWarningToast(LR.M_ListNameInvalid);
            return false;
        }

        if (File.Exists(GetPrizeListPath(listName)))
        {
            this.ShowWarningToast(string.Format(LR.M_ListNameExists, listName));
            return false;
        }

        return true;
    }

    private string CreateDefaultListName()
    {
        var baseName = LR.C_DefaultListName;
        if (!File.Exists(GetPrizeListPath(baseName)))
            return baseName;

        for (var i = 2;; i++)
        {
            var name = $"{baseName} {i}";
            if (!File.Exists(GetPrizeListPath(name)))
                return name;
        }
    }

    private static string GetPrizeListPath(string listName)
    {
        return Utils.GetFilePath("data", "list", "lottery_list", $"{listName}.json");
    }

    private static void RenameProfileFile(ProfileConfigBase oldConfig, ProfileConfigBase newConfig)
    {
        var oldPath = oldConfig.ConfigFilePath;
        var newPath = newConfig.ConfigFilePath;

        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(oldPath))
            return;

        var directory = Path.GetDirectoryName(newPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (File.Exists(newPath))
            File.Delete(newPath);

        File.Move(oldPath, newPath);
    }

    private static void DeleteProfileFile(ProfileConfigBase config)
    {
        var path = config.ConfigFilePath;
        if (File.Exists(path))
            File.Delete(path);
    }

    private async void OnPrizesImported(IReadOnlyList<Prize> prizes)
    {
        if (SelectedPrizeListConfig?.Data == null)
            return;

        var currentCount = SelectedPrizeListConfig.Data.Prizes.Count;
        if (currentCount > 0 && !await ConfirmOverwriteAsync(currentCount, prizes.Count))
            return;

        SelectedPrizeListConfig.Data.Prizes.Clear();
        foreach (var prize in prizes)
            SelectedPrizeListConfig.Data.Prizes.Add(prize);

        SaveSelectedPrizeList();
        IAppHost.TryGetService<IProfileService>()?.LoadPrizeProfile(SelectedPrizeListName, saveCurrent: false);
        OnPropertyChanged(nameof(SelectedPrizeList));
        SettingsView.Current?.CloseDrawer();
        _logger.LogInformation("已导入奖品池：目标奖品池={ListName}，导入数量={Count}。", SelectedPrizeListName, prizes.Count);
        this.ShowSuccessToast(string.Format(LR.M_ImportSuccess, prizes.Count, SelectedPrizeListName));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
