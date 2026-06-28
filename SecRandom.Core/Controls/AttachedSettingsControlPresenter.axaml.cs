using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using SecRandom.Core.Abstraction.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Shared.Interfaces;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Controls;

public partial class AttachedSettingsControlPresenter : UserControl, INotifyPropertyChanged
{
    private const double ExpandedMaxHeight = 420;
    private INotifyPropertyChanged? _settingsPropertyChanged;
    private double _expandedContentMaxHeight;
    private double _expandedContentOpacity;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public event EventHandler? SettingsChanged;

    public static readonly StyledProperty<AttachedSettingsControlInfo> ControlInfoProperty =
        AvaloniaProperty.Register<AttachedSettingsControlPresenter, AttachedSettingsControlInfo>(
            nameof(ControlInfo));

    public static readonly StyledProperty<IAttachableSettingsObject> TargetObjectProperty =
        AvaloniaProperty.Register<AttachedSettingsControlPresenter, IAttachableSettingsObject>(
            nameof(TargetObject));

    public static readonly StyledProperty<object?> ContentObjectProperty =
        AvaloniaProperty.Register<AttachedSettingsControlPresenter, object?>(
            nameof(ContentObject));

    public static readonly StyledProperty<IAttachedSettings?> AssociatedAttachedSettingsProperty =
        AvaloniaProperty.Register<AttachedSettingsControlPresenter, IAttachedSettings?>(
            nameof(AssociatedAttachedSettings));

    public AttachedSettingsControlPresenter()
    {
        InitializeComponent();
    }

    public double ExpandedContentMaxHeight
    {
        get => _expandedContentMaxHeight;
        private set
        {
            if (Math.Abs(_expandedContentMaxHeight - value) < double.Epsilon)
                return;

            _expandedContentMaxHeight = value;
            OnPropertyChanged(nameof(ExpandedContentMaxHeight));
        }
    }

    public double ExpandedContentOpacity
    {
        get => _expandedContentOpacity;
        private set
        {
            if (Math.Abs(_expandedContentOpacity - value) < double.Epsilon)
                return;

            _expandedContentOpacity = value;
            OnPropertyChanged(nameof(ExpandedContentOpacity));
        }
    }

    public AttachedSettingsControlInfo ControlInfo
    {
        get => GetValue(ControlInfoProperty);
        set => SetValue(ControlInfoProperty, value);
    }

    public IAttachableSettingsObject TargetObject
    {
        get => GetValue(TargetObjectProperty);
        set => SetValue(TargetObjectProperty, value);
    }

    public object? ContentObject
    {
        get => GetValue(ContentObjectProperty);
        set => SetValue(ContentObjectProperty, value);
    }

    public IAttachedSettings? AssociatedAttachedSettings
    {
        get => GetValue(AssociatedAttachedSettingsProperty);
        set => SetValue(AssociatedAttachedSettingsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TargetObjectProperty || e.Property == ControlInfoProperty)
            UpdateContent();

        base.OnPropertyChanged(e);
    }

    private void UpdateContent()
    {
        if (TargetObject == null || ControlInfo == null)
            return;

        if (_settingsPropertyChanged != null)
        {
            _settingsPropertyChanged.PropertyChanged -= AssociatedAttachedSettings_OnPropertyChanged;
            _settingsPropertyChanged = null;
        }

        TargetObject.AttachedObjects.TryGetValue(ControlInfo.Guid, out var settings);
        var control = AttachedSettingsControlBase.GetInstance(ControlInfo, ref settings);
        control?.Target = TargetObject switch
        {
            Student => AttachedSettingsTargets.Student,
            Prize => AttachedSettingsTargets.Prize,
            StudentList => AttachedSettingsTargets.StudentList,
            PrizeList => AttachedSettingsTargets.PrizeList,
            _ => AttachedSettingsTargets.None
        };

        ContentObject = control;
        MainContentPresenter.Content = ContentObject;
        AssociatedAttachedSettings = settings as IAttachedSettings;
        if (AssociatedAttachedSettings is INotifyPropertyChanged propertyChanged)
        {
            _settingsPropertyChanged = propertyChanged;
            _settingsPropertyChanged.PropertyChanged += AssociatedAttachedSettings_OnPropertyChanged;
        }

        UpdateExpandedContentState();
        UpdateSourceSettings(AssociatedAttachedSettings, false);
    }

    private void AssociatedAttachedSettings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is null or nameof(IAttachedSettings.IsAttachSettingsEnabled))
            UpdateExpandedContentState();

        UpdateSourceSettings(AssociatedAttachedSettings, true);
    }

    private void UpdateExpandedContentState()
    {
        ExpandedContentMaxHeight = ExpandedMaxHeight;
        ExpandedContentOpacity = 1;
    }

    private void UpdateSourceSettings(IAttachedSettings? settings, bool notifyChanged)
    {
        if (settings == null)
            TargetObject.AttachedObjects.Remove(ControlInfo.Guid);
        else
            TargetObject.AttachedObjects[ControlInfo.Guid] = settings;

        if (notifyChanged)
            SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    private void OnPropertyChanged(string propertyName)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
