using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;

namespace SecRandom.ViewModels;

public class ViewModelBase(MainConfigHandler configHandler) : ObservableRecipient
{
    public MainConfigModel Config { get; } = configHandler.Data;
}