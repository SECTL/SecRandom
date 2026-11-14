using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SecRandom.Core.Services.DataRoot;
using R = SecRandom.Langs.DataRootMigration.Resources;
using SecRandom.Services.CrashRecovery;
using SecRandom.Shared;
using SecRandom.Shared.DataRoot;

namespace SecRandom.Views;

/// <summary>
///     The one visible window of a migration launch. It runs before the Host exists, reads the journal written by
///     the settings page, and moves the data root with progress and an explicit retry. The old directory is never
///     deleted here; a successful move only renames it and then relaunches the app on the new root.
/// </summary>
internal sealed class DataRootMigrationWindow : Window
{
    private readonly TextBlock _detail;
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly Button _exit;
    private readonly Guid _migrationId;
    private readonly ProgressBar _progress;
    private readonly Button _restart;
    private readonly Button _retry;
    private readonly TextBlock _status;

    public DataRootMigrationWindow(Guid migrationId, IClassicDesktopStyleApplicationLifetime desktop)
    {
        _migrationId = migrationId;
        _desktop = desktop;

        Title = R.W_Title;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        _status = new TextBlock
        {
            Text = R.W_Preparing,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        _progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 6,
            IsIndeterminate = true,
            Margin = new Thickness(0, 16, 0, 0)
        };

        _detail = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
            Opacity = 0.75,
            MaxHeight = 220
        };

        _retry = new Button
        {
            Content = R.W_Retry,
            IsVisible = false,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _retry.Click += (_, _) => StartMigration();

        _restart = new Button
        {
            Content = R.W_Restart,
            IsVisible = false,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _restart.Click += (_, _) => RestartApplication();

        _exit = new Button
        {
            Content = R.W_Exit,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _exit.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 24, 0, 0)
        };
        buttons.Children.Add(_exit);
        buttons.Children.Add(_retry);
        buttons.Children.Add(_restart);

        Content = new StackPanel
        {
            Margin = new Thickness(28),
            Children =
            {
                _status,
                _progress,
                _detail,
                buttons
            }
        };

        Opened += (_, _) => StartMigration();
        Closing += (_, _) =>
        {
            // The migration window is the only window of this launch, so closing it must also end the process.
            Dispatcher.UIThread.Post(() => _desktop.Shutdown());
        };
    }

    private static string AnchorDirectory()
    {
        var appDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var packageRoot = Utils.PackageRoot;
        var isPortable = DataRootPaths.IsPortablePackageLayout(packageRoot, appDirectory);
        var anchorPath = DataRootPaths.GetAnchorPath(packageRoot, isPortable);
        return Path.GetDirectoryName(anchorPath) ?? packageRoot;
    }

    private void StartMigration()
    {
        _retry.IsVisible = false;
        _restart.IsVisible = false;
        _progress.IsIndeterminate = true;
        _progress.Value = 0;
        _status.Text = R.W_Preparing;
        _detail.Text = string.Empty;

        var journalPath = DataRootMigrationJournalStore.GetPath(AnchorDirectory());
        var journal = DataRootMigrationJournalStore.Read(journalPath);
        if (journal is null || !string.Equals(journal.TransactionId, _migrationId.ToString("N"),
                StringComparison.OrdinalIgnoreCase))
        {
            _progress.IsVisible = false;
            _status.Text = R.W_Failed;
            _detail.Text = R.W_JournalMissing;
            return;
        }

        if (journal.State == DataRootMigrationState.Completed)
        {
            ShowSuccess(journal.TargetRoot, journal.SourceArchiveRoot, null);
            return;
        }

        var progress = new Progress<DataRootMigrationProgress>(ReportProgress);
        _ = Task.Run(() =>
        {
            var engine = new DataRootMigrationEngine(journal, journalPath, AppContext.BaseDirectory);
            return engine.Run(progress, CancellationToken.None);
        }).ContinueWith(task => Dispatcher.UIThread.Post(() => Finish(task)), TaskScheduler.Default);
    }

    private void ReportProgress(DataRootMigrationProgress progress)
    {
        switch (progress.Phase)
        {
            case DataRootMigrationPhase.Preparing:
                _status.Text = R.W_Preparing;
                break;
            case DataRootMigrationPhase.Copying:
                _status.Text = string.Format(R.W_Copying, progress.CopiedFileCount, progress.TotalFileCount);
                _progress.IsIndeterminate = progress.TotalByteCount <= 0;
                if (progress.TotalByteCount > 0)
                    _progress.Value = Math.Clamp(progress.CopiedByteCount * 100.0 / progress.TotalByteCount, 0, 100);
                break;
            case DataRootMigrationPhase.Verifying:
                _status.Text = R.W_Verifying;
                break;
            case DataRootMigrationPhase.Committing:
                _status.Text = R.W_Committing;
                break;
            case DataRootMigrationPhase.Completed:
                _progress.IsIndeterminate = false;
                _progress.Value = 100;
                break;
            case DataRootMigrationPhase.Failed:
                _progress.IsVisible = false;
                break;
        }
    }

    private void Finish(Task<DataRootMigrationResult> task)
    {
        if (task.IsFaulted)
        {
            _progress.IsVisible = false;
            _status.Text = R.W_Failed;
            _detail.Text = string.Format(R.W_FailedDetail,
                task.Exception?.GetBaseException().Message ?? string.Empty);
            _retry.IsVisible = true;
            _restart.IsVisible = false;
            return;
        }

        var result = task.Result;
        if (!result.Success)
        {
            _progress.IsVisible = false;
            _status.Text = R.W_Failed;
            _detail.Text = string.Format(R.W_FailedDetail, result.Detail ?? string.Empty);
            _retry.IsVisible = true;
            _restart.IsVisible = false;
            return;
        }

        ShowSuccess(result.TargetRoot, result.SourceArchiveRoot, result.Warning);
    }

    private void ShowSuccess(string targetRoot, string? archiveRoot, string? warning)
    {
        _status.Text = R.W_Completed;
        _progress.IsVisible = true;
        _progress.IsIndeterminate = false;
        _progress.Value = 100;

        var lines = new List<string>
        {
            string.Format(R.W_CompletedDetail, targetRoot)
        };
        if (!string.IsNullOrWhiteSpace(archiveRoot))
            lines.Add(string.Format(R.W_OldDirectoryKept, archiveRoot));
        if (!string.IsNullOrWhiteSpace(warning))
            lines.Add(warning);

        _detail.Text = string.Join(Environment.NewLine, lines);
        _retry.IsVisible = false;
        _restart.IsVisible = true;
    }

    private void RestartApplication()
    {
        var startInfo = CrashRecoveryRuntime.CreateRestartStartInfo([]);
        try
        {
            if (startInfo is null)
                throw new InvalidOperationException("无法确定应用启动路径。");
            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            _status.Text = R.W_Failed;
            _detail.Text = string.Format(R.W_FailedDetail, exception.Message);
            _retry.IsVisible = true;
            _restart.IsVisible = false;
            return;
        }

        _desktop.Shutdown();
    }
}