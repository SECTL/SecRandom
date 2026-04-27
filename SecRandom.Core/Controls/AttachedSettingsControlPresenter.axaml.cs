using Avalonia;
using Avalonia.Controls;
using SecRandom.Core.Abstraction.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Interfaces;
using SecRandom.Shared.Models;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Controls;

public partial class AttachedSettingsControlPresenter : UserControl
{
    public static readonly StyledProperty<AttachedSettingsControlInfo> ControlInfoProperty = AvaloniaProperty.Register<AttachedSettingsControlPresenter, AttachedSettingsControlInfo>(
        nameof(ControlInfo));

    public AttachedSettingsControlInfo ControlInfo
    {
        get => GetValue(ControlInfoProperty);
        set => SetValue(ControlInfoProperty, value);
    }

    public static readonly StyledProperty<AttachableSettingsObject> TargetObjectProperty = AvaloniaProperty.Register<AttachedSettingsControlPresenter, AttachableSettingsObject>(
        nameof(TargetObject));

    public AttachableSettingsObject TargetObject
    {
        get => GetValue(TargetObjectProperty);
        set => SetValue(TargetObjectProperty, value);
    }
    
    public static readonly StyledProperty<object?> ContentObjectProperty = AvaloniaProperty.Register<AttachedSettingsControlPresenter, object?>(
        nameof(ContentObject));

    public object? ContentObject
    {
        get => GetValue(ContentObjectProperty);
        set => SetValue(ContentObjectProperty, value);
    }

    public static readonly StyledProperty<IAttachedSettings?> AssociatedAttachedSettingsProperty = AvaloniaProperty.Register<AttachedSettingsControlPresenter, IAttachedSettings?>(
        nameof(AssociatedAttachedSettings));

    public IAttachedSettings? AssociatedAttachedSettings
    {
        get => GetValue(AssociatedAttachedSettingsProperty);
        set => SetValue(AssociatedAttachedSettingsProperty, value);
    }
    
    public AttachedSettingsControlPresenter() 
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TargetObjectProperty || e.Property == ControlInfoProperty)
        {
            UpdateContent();
        }
        base.OnPropertyChanged(e);
    }

    private void UpdateContent()
    {
        if (TargetObject == null || ControlInfo == null)
        {
            return;
        }
        
        
        TargetObject.AttachedObjects.TryGetValue(ControlInfo.Guid, out var settings);
        var control = AttachedSettingsControlBase.GetInstance(ControlInfo, ref settings);
        control?.Target = TargetObject switch
        {
            Student => AttachedSettingsTargets.Student,
            _ => AttachedSettingsTargets.None
        };
        
        ContentObject = control;
        MainContentPresenter.Content = ContentObject;
        AssociatedAttachedSettings = settings as IAttachedSettings;
        UpdateSourceSettings(AssociatedAttachedSettings);
    }
    
    private void UpdateSourceSettings(IAttachedSettings? settings)
    {
        if (settings?.IsAttachSettingsEnabled != true && ControlInfo.HasEnabledState)
        {
            // 在附加设置没有启用，且控件有附加设置启用状态的情况下不回写设置信息，以降低档案文件大小。
            return;
        }
        TargetObject.AttachedObjects[ControlInfo.Guid] = settings;
    }
}