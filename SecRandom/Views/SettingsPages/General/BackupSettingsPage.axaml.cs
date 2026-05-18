using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Models;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.backup", "\uE07D", "settings.general")]
public partial class BackupSettingsPage : UserControl
{
    public BackupSettingsPage()
    {
        Settings = ViewModel.Config.Backup;
        DataContext = this;
        InitializeComponent();

        Backups.Add(new BackupMetadata
        {
            FileName = "SecRandom_v2.3.2_auto_20260329_201650.zip",
            DateTime = DateTime.Now,
            Size = "11.1 KB"
        });

        Backups.Add(new BackupMetadata
        {
            FileName = "example.zip",
            DateTime = DateTime.Now,
            Size = "11.1 KB"
        });

        Backups.Add(new BackupMetadata
        {
            FileName = "example.zip",
            DateTime = DateTime.Now,
            Size = "18.1 KB"
        });
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public BackupConfig Settings { get; }

    public ObservableCollection<BackupMetadata> Backups { get; } = [];
}