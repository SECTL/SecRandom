using System.ComponentModel;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.ViewModels.SettingsPages;
using SecRandom.Views;

namespace SecRandom.Views.SettingsPages.General;

/// <summary>
///     集控设置页（<c>settings.general.control</c>）：本机开关、节点身份、连接状态。
/// </summary>
/// <remarks>
///     页面本身不持有状态：开关、组 ID、节点通道地址与期望状态都通过
///     <see cref="ControlSettingsPageViewModel" /> 直接读写 <c>data/config/control/node-state.json</c>，
///     刻意不放进 <c>settings.json</c>——设置导入不该能悄悄打开远控。
/// </remarks>
[PageInfo("settings.general.control", FluentIcons.PlugConnectedFilled, "settings.general")]
public partial class ControlSettingsPage : UserControl
{
    public ControlSettingsPage()
    {
        ViewModel = IAppHost.GetService<ControlSettingsPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        // 接入/保存的结果用 Toast 通知：设置页是长列表，内联那行文字用户很容易漏看。
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public ControlSettingsPageViewModel ViewModel { get; }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(ControlSettingsPageViewModel.EnrollmentMessage):
                NotifyMessage(ViewModel.EnrollmentMessage, ViewModel.EnrollmentMessageIsSuccess);
                break;
            case nameof(ControlSettingsPageViewModel.ClassSaveMessage):
                NotifyMessage(ViewModel.ClassSaveMessage, ViewModel.ClassSaveMessageIsSuccess);
                break;
        }
    }

    /// <summary>空串是「没有消息」（命令开始前会先清空），不要弹空 Toast。</summary>
    private static void NotifyMessage(string? message, bool success)
    {
        if (string.IsNullOrEmpty(message))
            return;

        if (success)
            MainView.ShowSuccessToast(message);
        else
            MainView.ShowToast(message, FAInfoBarSeverity.Error);
    }
}