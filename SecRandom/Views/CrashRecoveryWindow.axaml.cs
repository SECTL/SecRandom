using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SecRandom.Services.CrashRecovery;

namespace SecRandom.Views;

public partial class CrashRecoveryWindow : Window
{
    private readonly CrashRecoveryView _content;

    public CrashRecoveryWindow()
        : this(new CrashRecoveryPromptOptions(string.Empty, false), static () => false)
    {
    }

    public CrashRecoveryWindow(
        CrashRecoveryPromptOptions options,
        Func<bool> restartApp,
        bool canIgnore = true)
    {
        InitializeComponent();
        _content = new CrashRecoveryView(options, restartApp, canIgnore);
        _content.Dismissed += (_, _) => Close();
        this.FindControl<ContentControl>("ContentHost")!.Content = _content;
    }

    public bool WasIgnored => _content.WasIgnored;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

}
