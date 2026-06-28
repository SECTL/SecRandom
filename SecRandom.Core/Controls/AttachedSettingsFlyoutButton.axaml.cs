using System.Collections;
using Avalonia;
using Avalonia.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Shared.Interfaces;

namespace SecRandom.Core.Controls;

public partial class AttachedSettingsFlyoutButton : Button
{
    public event EventHandler? SettingsChanged;

    public static readonly StyledProperty<IAttachableSettingsObject?> TargetObjectProperty =
        AvaloniaProperty.Register<AttachedSettingsFlyoutButton, IAttachableSettingsObject?>(
            nameof(TargetObject));

    public static readonly StyledProperty<IEnumerable?> ControlInfosProperty =
        AvaloniaProperty.Register<AttachedSettingsFlyoutButton, IEnumerable?>(
            nameof(ControlInfos));

    public AttachedSettingsFlyoutButton()
    {
        InitializeComponent();
    }

    public IAttachableSettingsObject? TargetObject
    {
        get => GetValue(TargetObjectProperty);
        set => SetValue(TargetObjectProperty, value);
    }

    public IEnumerable? ControlInfos
    {
        get => GetValue(ControlInfosProperty);
        set => SetValue(ControlInfosProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == TargetObjectProperty || e.Property == ControlInfosProperty)
            UpdateAttachedSettingsContent();
    }

    private void UpdateAttachedSettingsContent()
    {
        if (TargetObject == null || ControlInfos == null)
        {
            AttachedSettingsItemsControl.ItemsSource = null;
            return;
        }

        AttachedSettingsItemsControl.ItemsSource = ControlInfos
            .OfType<AttachedSettingsControlInfo>()
            .Select(CreatePresenter)
            .ToList();
    }

    private AttachedSettingsControlPresenter CreatePresenter(AttachedSettingsControlInfo info)
    {
        var presenter = new AttachedSettingsControlPresenter
        {
            ControlInfo = info,
            TargetObject = TargetObject!
        };
        presenter.SettingsChanged += (_, _) => SettingsChanged?.Invoke(this, EventArgs.Empty);
        return presenter;
    }
}
