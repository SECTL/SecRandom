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
using SecRandom.Shared.Models.Profile;
using LR = SecRandom.Langs.SettingsPages.ListManagement.LotteryList.Resources;

namespace SecRandom.Views.SettingsPages.ListManagement;

[PageInfo("settings.listManagement.lotteryList", FluentIcons.LotteryFilled, "settings.listManagement")]
public partial class LotteryListSettingsPage : UserControl, INotifyPropertyChanged
{
    private string _selectedPrizeListName = string.Empty;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;
    private readonly ILogger<LotteryListSettingsPage> _logger =
        IAppHost.GetService<ILogger<LotteryListSettingsPage>>();
    private readonly IProfileCatalogManager _catalogManager =
        IAppHost.GetService<IProfileCatalogManager>();

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

    public PrizeList? SelectedPrizeList { get; private set; }

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

        foreach (var name in _catalogManager.GetPrizeListNames())
            PrizeListNames.Add(name);

        if (PrizeListNames.Count == 0)
        {
            _catalogManager.CreatePrizeList("default");
            PrizeListNames.Add("default");
        }

        SelectedPrizeListName = PrizeListNames.Contains(selectedName) ? selectedName : PrizeListNames[0];
        LoadSelectedPrizeList();
    }

    private void LoadSelectedPrizeList()
    {
        if (string.IsNullOrWhiteSpace(SelectedPrizeListName))
            return;

        if (SelectedPrizeList != null)
            _catalogManager.SavePrizeList(SelectedPrizeList);
        SelectedPrizeList = _catalogManager.LoadPrizeList(SelectedPrizeListName);

        OnPropertyChanged(nameof(SelectedPrizeList));
    }

    private void SaveSelectedPrizeList()
    {
        if (SelectedPrizeList != null)
            _catalogManager.SavePrizeList(SelectedPrizeList);
        _catalogManager.SetDefaultPrizePool(SelectedPrizeListName);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        SaveSelectedPrizeList();
    }

    private void AttachedSettings_OnSettingsChanged(object? sender, EventArgs e)
    {
        SaveSelectedPrizeList();
    }

    private void PrizeListListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        LoadSelectedPrizeList();
    }

    private void RefreshButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RefreshPrizeLists();
    }

    private async void DeletePrizeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: Prize prize } || SelectedPrizeList == null)
            return;

        var displayName = string.IsNullOrWhiteSpace(prize.Name) ? prize.Id : prize.Name;
        if (!await ConfirmDeletePrizeAsync(displayName))
            return;

        if (!SelectedPrizeList.Prizes.Remove(prize))
        {
            this.ShowWarningToast(LR.M_DeletePrizeNotFound);
            OnPropertyChanged(nameof(SelectedPrizeList));
            return;
        }

        SaveSelectedPrizeList();
        OnPropertyChanged(nameof(SelectedPrizeList));
        _logger.LogInformation("已删除奖品池条目：奖品池={ListName}，记录={RecordId}。", SelectedPrizeListName, prize.RecordId);
        this.ShowSuccessToast(string.Format(LR.M_DeletePrizeSuccess, displayName));
    }

    private void ChangeCurrentListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var control = (Control)Resources["ChangeCurrentListDrawer"]!;
        control.DataContext = this;
        SettingsView.Current?.OpenDrawer(control);
    }

    private async void AddListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var listName = await ShowListNameDialogAsync(LR.M_ListNameDialogTitle_Add, LR.M_ListNameDialogPrimary_Add,
            GetNewListName());
        if (listName == null || !ValidateNewListName(listName))
            return;

        SaveSelectedPrizeList();
        _catalogManager.CreatePrizeList(listName);
        RefreshPrizeLists(listName);
        _logger.LogInformation("已创建奖品池：奖品池={ListName}。", listName);
        this.ShowSuccessToast(string.Format(LR.M_AddListSuccess, listName));
    }

    private async void DeleteListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedPrizeListName) || SelectedPrizeList == null)
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

        SelectedPrizeList = null;
        _catalogManager.DeletePrizeList(deleteName, deleteHistory: true);

        var nextName = PrizeListNames.FirstOrDefault(name => name != deleteName) ?? string.Empty;
        RefreshPrizeLists(nextName);

        _logger.LogInformation("已删除奖品池：奖品池={ListName}。", deleteName);
        this.ShowSuccessToast(string.Format(LR.M_DeleteListSuccess, deleteName));
    }

    private async void RenameListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedPrizeListName) || SelectedPrizeList == null)
        {
            this.ShowWarningToast(LR.M_SelectListFirst);
            return;
        }

        var oldName = SelectedPrizeListName;
        var newName = await ShowListNameDialogAsync(LR.M_ListNameDialogTitle_Rename,
            LR.M_ListNameDialogPrimary_Rename, oldName);
        if (newName == null || string.Equals(oldName, newName, StringComparison.Ordinal))
            return;

        if (!ValidateNewListName(newName) || !_catalogManager.RenamePrizeList(oldName, newName))
            return;

        SelectedPrizeList = null;
        RefreshPrizeLists(newName);
        _logger.LogInformation("已重命名奖品池：旧奖品池={OldListName}，新奖品池={NewListName}。", oldName, newName);
        this.ShowSuccessToast(string.Format(LR.M_RenameListSuccess, newName));
    }

    private void ImportButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedPrizeList == null)
        {
            this.ShowWarningToast(LR.M_SelectListFirst);
            return;
        }

        var view = new LotteryListImportView(SelectedPrizeListName, OnPrizesImported);
        SettingsView.Current?.OpenDrawer(view);
        _logger.LogInformation("打开奖品池导入面板：目标奖品池={ListName}。", SelectedPrizeListName);
    }

    private async void AddPrizeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedPrizeList == null)
        {
            this.ShowWarningToast(LR.M_SelectListFirst);
            return;
        }

        var form = new StackPanel { Spacing = 8 };
        var id = AddInputField(form, LR.C_PrizeId);
        var name = AddInputField(form, LR.C_Name);
        var weight = AddInputField(form, LR.C_Weight, "1");
        var count = AddInputField(form, LR.C_Count, "1");
        var tags = AddInputField(form, LR.C_Tags);

        var dialog = new FAContentDialog
        {
            Title = LR.M_AddPrizeTitle,
            Content = form,
            PrimaryButtonText = LR.C_AddPrize,
            CloseButtonText = LR.C_Cancel,
            IsPrimaryButtonEnabled = false,
            DefaultButton = FAContentDialogButton.Primary
        };

        void UpdatePrimaryButtonState()
        {
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(id.Text) ||
                                            !string.IsNullOrWhiteSpace(name.Text);
        }

        id.TextChanged += (_, _) => UpdatePrimaryButtonState();
        name.TextChanged += (_, _) => UpdatePrimaryButtonState();
        var result = await dialog.ShowAsync(TopLevel.GetTopLevel(this));

        if (result != FAContentDialogResult.Primary)
            return;

        var prizeId = id.Text?.Trim() ?? string.Empty;
        var prizeName = name.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(prizeId) && string.IsNullOrWhiteSpace(prizeName))
        {
            this.ShowWarningToast(LR.M_AddPrizeRequired);
            return;
        }

        if (!double.TryParse(weight.Text, out var prizeWeight) || prizeWeight <= 0 ||
            !int.TryParse(count.Text, out var prizeCount) || prizeCount <= 0)
        {
            this.ShowWarningToast(LR.M_AddPrizeInvalidValues);
            return;
        }

        SelectedPrizeList.Prizes.Add(new Prize
        {
            Id = prizeId,
            Name = prizeName,
            Weight = prizeWeight,
            Count = prizeCount,
            Tags = tags.Text?.Trim() ?? string.Empty
        });
        SaveSelectedPrizeList();
        OnPropertyChanged(nameof(SelectedPrizeList));
        _logger.LogInformation("已向奖品池新增奖品：奖品池={ListName}，当前奖品数={Count}。", SelectedPrizeListName,
            SelectedPrizeList.Prizes.Count);
        this.ShowSuccessToast(LR.M_AddPrizeSuccess);
    }

    private static TextBox AddInputField(StackPanel form, string label, string initialText = "")
    {
        var field = new StackPanel { Spacing = 4 };
        field.Children.Add(new TextBlock { Text = label });

        var input = new TextBox { Text = initialText, MinWidth = 320 };
        field.Children.Add(input);
        form.Children.Add(field);
        return input;
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

    private async Task<bool> ConfirmDeletePrizeAsync(string prizeName)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_DeletePrizeTitle,
            Content = string.Format(LR.M_DeletePrizeContent, prizeName, SelectedPrizeListName),
            PrimaryButtonText = LR.M_DeletePrizePrimary,
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

        if (_catalogManager.PrizeListExists(listName))
        {
            this.ShowWarningToast(string.Format(LR.M_ListNameExists, listName));
            return false;
        }

        return true;
    }

    private string GetNewListName()
    {
        var defaultName = LR.C_DefaultListName;
        var candidateName = defaultName;
        var suffix = 2;

        while (_catalogManager.PrizeListExists(candidateName))
        {
            candidateName = $"{defaultName} {suffix}";
            suffix++;
        }

        return candidateName;
    }

    private async void OnPrizesImported(IReadOnlyList<Prize> prizes)
    {
        if (SelectedPrizeList == null)
            return;

        var currentCount = SelectedPrizeList.Prizes.Count;
        if (currentCount > 0 && !await ConfirmOverwriteAsync(currentCount, prizes.Count))
            return;

        _catalogManager.ReplacePrizes(SelectedPrizeListName, prizes);
        SelectedPrizeList = _catalogManager.LoadPrizeList(SelectedPrizeListName);
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
