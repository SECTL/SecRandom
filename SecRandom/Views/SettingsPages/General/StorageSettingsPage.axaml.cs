using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Services.DataRoot;
using SecRandom.Services.Security;
using SecRandom.Shared;
using SecRandom.Shared.DataRoot;
using LR = SecRandom.Langs.SettingsPages.General.Storage.Resources;

namespace SecRandom.Views.SettingsPages.General;

/// <summary>
///     Storage settings page: shows the effective data root, how it was chosen, any leftover data directories,
///     and starts a verified migration to a new root. The move itself happens in a dedicated window after the
///     app restarts with <c>--data-root-migration</c>, because every service captures the root when it is created.
/// </summary>
[PageInfo("settings.general.storage", FluentIcons.HardDriveFilled, "settings.general")]
public partial class StorageSettingsPage : UserControl, INotifyPropertyChanged
{
    private readonly ISecurityService _security = IAppHost.GetService<ISecurityService>();
    private string _customPath = string.Empty;
    private bool _isBusy;
    private int _modeIndex;
    private string _statusText = string.Empty;
    private string _targetPath = string.Empty;

    public StorageSettingsPage()
    {
        DataContext = this;
        InitializeComponent();
        LoadState();
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> OtherRoots { get; } = [];

    /// <summary>The page only exists on desktop; mobile keeps its fixed system data directory.</summary>
    public bool IsDesktop => true;

    public string CurrentRoot => Utils.DataRoot;

    public string SourceText => DescribeSource(Utils.DataRootSource);

    public string AnchorPathText => Utils.AnchorPath ?? string.Empty;

    public bool HasAnchor => !string.IsNullOrEmpty(AnchorPathText);

    /// <summary>
    ///     Set when the resolved root is still inside a program directory an update would replace; the page
    ///     turns this into a visible warning instead of silently relying on the user noticing the path.
    /// </summary>
    public bool NeedsMigrationNotice => Utils.NeedsMigrationNotice;

    public bool HasOtherRoots => OtherRoots.Count > 0;

    public bool IsPackageModeAvailable => Utils.IsPortablePackage || GlobalConstants.IsDevelopment;

    public int ModeIndex
    {
        get => _modeIndex;
        set
        {
            if (_modeIndex == value)
                return;

            _modeIndex = value;
            // The target follows the selected mode, so switching away from a custom folder and back
            // restores the folder the user picked instead of reusing another mode's default path.
            _targetPath = DefaultRootForMode(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomMode));
            OnPropertyChanged(nameof(IsTargetReadOnly));
            OnPropertyChanged(nameof(CanBrowse));
            OnPropertyChanged(nameof(TargetPath));
            OnPropertyChanged(nameof(IsReadyToMigrate));
        }
    }

    public bool IsCustomMode => _modeIndex == 2;

    public string TargetPath
    {
        get => _targetPath;
        set
        {
            if (string.Equals(_targetPath, value, StringComparison.Ordinal))
                return;

            _targetPath = value;
            if (_modeIndex == 2)
                _customPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsReadyToMigrate));
        }
    }

    public bool IsTargetReadOnly => _modeIndex != 2;

    public bool CanBrowse => _modeIndex == 2 && !_isBusy;

    public bool IsReadyToMigrate => !_isBusy && !string.IsNullOrWhiteSpace(_targetPath);

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (string.Equals(_statusText, value, StringComparison.Ordinal))
                return;

            _statusText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(_statusText);

    private void LoadState()
    {
        var modeIndex = Utils.DataRootMode switch
        {
            DataRootMode.Package => 0,
            DataRootMode.Standard => 1,
            _ => 2
        };

        // An installed build whose legacy data sits in the program directory reports Package; that mode is
        // not offered there, so the page opens on the standard location the user should move to.
        if (modeIndex == 0 && !IsPackageModeAvailable)
            modeIndex = 1;

        _modeIndex = modeIndex;
        _customPath = Utils.DataRoot;
        _targetPath = DefaultRootForMode(modeIndex);

        OtherRoots.Clear();
        foreach (var root in Utils.OtherDataRoots)
            OtherRoots.Add(root);

        OnPropertyChanged(nameof(CurrentRoot));
        OnPropertyChanged(nameof(SourceText));
        OnPropertyChanged(nameof(AnchorPathText));
        OnPropertyChanged(nameof(HasAnchor));
        OnPropertyChanged(nameof(NeedsMigrationNotice));
        OnPropertyChanged(nameof(HasOtherRoots));
        OnPropertyChanged(nameof(ModeIndex));
        OnPropertyChanged(nameof(TargetPath));
        OnPropertyChanged(nameof(IsTargetReadOnly));
        OnPropertyChanged(nameof(CanBrowse));
        OnPropertyChanged(nameof(IsReadyToMigrate));
    }

    private string DefaultRootForMode(int modeIndex) => modeIndex switch
    {
        0 => Path.Combine(Utils.PackageRoot, "data"),
        1 => DataRootPaths.GetStandardDataRoot(),
        _ => _customPath
    };

    private async void Browse_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        try
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = LR.S_Target,
                AllowMultiple = false
            });

            if (folders.Count == 0)
                return;

            var path = folders[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
                return;

            _customPath = Path.GetFullPath(path);
            TargetPath = _customPath;
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(LR.M_Err_TargetInvalid, exception.Message);
        }
    }

    private async void Migrate_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        var source = Utils.DataRoot;
        var target = _targetPath?.Trim();
        var mode = (DataRootMode)_modeIndex;

        var validation = DataRootMigrationEngine.Validate(source, target, AppContext.BaseDirectory, out var detail);
        if (validation != DataRootMigrationErrorCode.None)
        {
            await ShowErrorAsync(DescribeError(validation), detail);
            return;
        }

        if (!await ConfirmMigrationAsync(target!))
            return;

        _isBusy = true;
        NotifyInteractive();
        try
        {
            var transactionId = Guid.NewGuid().ToString("N");
            var portable = DataRootPaths.IsPortablePackageLayout(Utils.PackageRoot, AppContext.BaseDirectory);
            var anchorPath = DataRootPaths.GetAnchorPath(Utils.PackageRoot, portable);
            var anchorDirectory = Path.GetDirectoryName(anchorPath) ?? DataRootPaths.GetStandardAnchorDirectory();
            var journalPath = DataRootMigrationJournalStore.GetPath(anchorDirectory);
            var fullTarget = Path.GetFullPath(target!);

            var journal = new DataRootMigrationJournal
            {
                TransactionId = transactionId,
                State = DataRootMigrationState.Pending,
                SourceRoot = source,
                TargetRoot = fullTarget,
                StagingRoot = DataRootMigrationEngine.GetStagingPath(fullTarget, transactionId),
                Mode = mode,
                PackageKind = DataRootPaths.ReadPackageKind(AppContext.BaseDirectory),
                AnchorPath = anchorPath
            };

            // Restarting is the protected operation here: the migration itself always ends in a restart, and the
            // authorization gate is the same one the restart dialog uses.
            var authorized = await _security.AuthorizeAsync(SecurityOperation.RestartApplication, () =>
            {
                DataRootMigrationJournalStore.Write(journalPath, journal);
                App.Current.Restart(["--data-root-migration", transactionId]);
                return Task.CompletedTask;
            });

            if (!authorized)
            {
                _isBusy = false;
                NotifyInteractive();
            }
        }
        catch (Exception exception)
        {
            _isBusy = false;
            NotifyInteractive();
            await ShowErrorAsync(LR.M_Err_Journal, exception.Message);
        }
    }

    private void NotifyInteractive()
    {
        OnPropertyChanged(nameof(CanBrowse));
        OnPropertyChanged(nameof(IsReadyToMigrate));
    }

    private async Task<bool> ConfirmMigrationAsync(string target)
    {
        var result = await new FAContentDialog
        {
            Title = LR.M_RestartTitle,
            Content = string.Format(LR.M_RestartContent, target),
            PrimaryButtonText = LR.M_Start,
            CloseButtonText = LR.C_Cancel,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));

        return result == FAContentDialogResult.Primary;
    }

    private async Task ShowErrorAsync(string message, string? detail = null)
    {
        await new FAContentDialog
        {
            Title = LR.S_Change,
            Content = string.IsNullOrWhiteSpace(detail) ? message : $"{message}\n\n{detail}",
            CloseButtonText = SecRandom.Langs.Common.Resources.C_Close,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
    }

    private static string DescribeError(DataRootMigrationErrorCode code) => code switch
    {
        DataRootMigrationErrorCode.SourceMissing => LR.M_Err_SourceMissing,
        DataRootMigrationErrorCode.SourceEmpty => LR.M_Err_SourceMissing,
        DataRootMigrationErrorCode.TargetEqualsSource => LR.M_Err_SameRoot,
        DataRootMigrationErrorCode.TargetInsideSource => LR.M_Err_Nested,
        DataRootMigrationErrorCode.SourceInsideTarget => LR.M_Err_Nested,
        DataRootMigrationErrorCode.TargetInsideApp => LR.M_Err_InsideApp,
        DataRootMigrationErrorCode.TargetNotEmpty => LR.M_Err_NotEmpty,
        DataRootMigrationErrorCode.TargetNotWritable => LR.M_Err_NotWritable,
        DataRootMigrationErrorCode.TargetInvalid => LR.M_Err_TargetInvalid,
        _ => LR.M_Err_TargetInvalid
    };

    private static string DescribeSource(DataRootSource source) => source switch
    {
        DataRootSource.CommandLine => LR.Src_CommandLine,
        DataRootSource.Environment => LR.Src_Environment,
        DataRootSource.Anchor => LR.Src_Anchor,
        DataRootSource.Legacy => LR.Src_Legacy,
        DataRootSource.RepairedPayload => LR.Src_Repaired,
        DataRootSource.ModeDefault => LR.Src_Default,
        _ => LR.Src_Unknown
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}