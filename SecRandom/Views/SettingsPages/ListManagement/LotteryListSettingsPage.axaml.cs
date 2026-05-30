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
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Attributes;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Langs.SettingsPages.ListManagement.LotteryList.Resources;

namespace SecRandom.Views.SettingsPages.ListManagement;

[PageInfo("settings.listManagement.lotteryList", FluentIcons.LotteryRegular, "settings.listManagement")]
public partial class LotteryListSettingsPage : UserControl, INotifyPropertyChanged
{
    private string _selectedPrizeListName = string.Empty;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

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

        var service = IAppHost.GetService<IProfileService>();
        if (service.PrizeListConfig?.Name == SelectedPrizeListName)
            service.PrizeListConfig.Reload();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
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

        File.Move(GetPrizeListPath(oldName), GetPrizeListPath(newName));

        var service = IAppHost.GetService<IProfileService>();
        if (service.PrizeListConfig?.Name == oldName)
            service.PrizeListConfig.Reload();

        RefreshPrizeLists(newName);
        this.ShowSuccessToast(string.Format(LR.M_RenameListSuccess, newName));
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
        OnPropertyChanged(nameof(SelectedPrizeList));
        SettingsView.Current?.CloseDrawer();
        this.ShowSuccessToast(string.Format(LR.M_ImportSuccess, prizes.Count, SelectedPrizeListName));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}