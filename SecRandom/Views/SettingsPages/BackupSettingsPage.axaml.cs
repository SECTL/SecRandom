using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Models;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.backup", "\uE07D")]
public partial class BackupSettingsPage : UserControl
{
    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public BackupConfig Settings { get; }

    public ObservableCollection<BackupMetadata> Backups { get; } = [];
    
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
}