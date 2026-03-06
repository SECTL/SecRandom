using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Models;
using SecRandom.Models.Config;

namespace SecRandom.Services.Config;

public class MainConfigHandler : ConfigHandlerBase<MainConfigModel>
{
    private DrawSettingsConfig? _drawSettings;
    private RollCallSettingsConfig? _rollCallSettings;
    private QuickDrawSettingsConfig? _quickDrawSettings;
    private LotterySettingsConfig? _lotterySettings;
    private FaceDetectorSettingsConfig? _faceDetectorSettings;
    private LinkageSettingsConfig? _linkageSettings;
    private SecuritySettingsConfig? _securitySettings;
    private ThemeManagementSettingsConfig? _themeManagement;

    public MainConfigHandler(ILogger<MainConfigHandler> logger, ConfigServiceBase configService)
        : base(logger, configService, () => new MainConfigModel())
    {
        AttachBasicSettingsHandlers();
        AttachDrawSettingsHandlers();
        AttachFloatingWindowSettingsHandlers();
        AttachLinkageSettingsHandlers();
        AttachSecuritySettingsHandlers();
        AttachThemeManagementHandlers();
    }

    public override void Reload()
    {
        DetachBasicSettingsHandlers();
        DetachDrawSettingsHandlers();
        DetachFloatingWindowSettingsHandlers();
        DetachLinkageSettingsHandlers();
        DetachSecuritySettingsHandlers();
        DetachThemeManagementHandlers();
        base.Reload();
        AttachBasicSettingsHandlers();
        AttachDrawSettingsHandlers();
        AttachFloatingWindowSettingsHandlers();
        AttachLinkageSettingsHandlers();
        AttachSecuritySettingsHandlers();
        AttachThemeManagementHandlers();
    }

    protected override void Data_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainConfigModel.BasicSettings))
        {
            DetachBasicSettingsHandlers();
            AttachBasicSettingsHandlers();
        }
        else if (e.PropertyName == nameof(MainConfigModel.FloatingWindowSettings))
        {
            DetachFloatingWindowSettingsHandlers();
            AttachFloatingWindowSettingsHandlers();
        }
        else if (e.PropertyName == nameof(MainConfigModel.DrawSettings))
        {
            DetachDrawSettingsHandlers();
            AttachDrawSettingsHandlers();
        }
        else if (e.PropertyName == nameof(MainConfigModel.LinkageSettings))
        {
            DetachLinkageSettingsHandlers();
            AttachLinkageSettingsHandlers();
        }
        else if (e.PropertyName == nameof(MainConfigModel.SecuritySettings))
        {
            DetachSecuritySettingsHandlers();
            AttachSecuritySettingsHandlers();
        }
        else if (e.PropertyName == nameof(MainConfigModel.ThemeManagement))
        {
            DetachThemeManagementHandlers();
            AttachThemeManagementHandlers();
        }

        base.Data_OnPropertyChanged(sender, e);
    }

    private void AttachBasicSettingsHandlers()
    {
        Data.BasicSettings.PropertyChanged += BasicSettings_OnPropertyChanged;
    }

    private void DetachBasicSettingsHandlers()
    {
        Data.BasicSettings.PropertyChanged -= BasicSettings_OnPropertyChanged;
    }

    private void BasicSettings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BasicSettingsConfig.UiLanguageMode))
        {
            App.ApplyUiLanguageMode(Data.BasicSettings.UiLanguageMode);
        }
        else if (e.PropertyName == nameof(BasicSettingsConfig.UiThemeModeIndex))
        {
            App.ApplyUiThemeModeIndex(Data.BasicSettings.UiThemeModeIndex);
        }
        else if (e.PropertyName == nameof(BasicSettingsConfig.UiFontFamilyName) ||
                 e.PropertyName == nameof(BasicSettingsConfig.UiFontFamilyIndex) ||
                 e.PropertyName == nameof(BasicSettingsConfig.UiFontWeightIndex))
        {
            App.ApplyUiFont(
                Data.BasicSettings.UiFontFamilyName,
                Data.BasicSettings.UiFontFamilyIndex,
                Data.BasicSettings.UiFontWeightIndex);
        }

        Save();
    }

    private void AttachDrawSettingsHandlers()
    {
        _drawSettings = Data.DrawSettings;
        _drawSettings.PropertyChanged += DrawSettings_OnPropertyChanged;

        _rollCallSettings = _drawSettings.RollCallSettings;
        _rollCallSettings.PropertyChanged += DrawSettingsChild_OnPropertyChanged;

        _quickDrawSettings = _drawSettings.QuickDrawSettings;
        _quickDrawSettings.PropertyChanged += DrawSettingsChild_OnPropertyChanged;

        _lotterySettings = _drawSettings.LotterySettings;
        _lotterySettings.PropertyChanged += DrawSettingsChild_OnPropertyChanged;

        _faceDetectorSettings = _drawSettings.FaceDetectorSettings;
        _faceDetectorSettings.PropertyChanged += DrawSettingsChild_OnPropertyChanged;
    }

    private void DetachDrawSettingsHandlers()
    {
        if (_drawSettings is not null)
        {
            _drawSettings.PropertyChanged -= DrawSettings_OnPropertyChanged;
            _drawSettings = null;
        }

        if (_rollCallSettings is not null)
        {
            _rollCallSettings.PropertyChanged -= DrawSettingsChild_OnPropertyChanged;
            _rollCallSettings = null;
        }

        if (_quickDrawSettings is not null)
        {
            _quickDrawSettings.PropertyChanged -= DrawSettingsChild_OnPropertyChanged;
            _quickDrawSettings = null;
        }

        if (_lotterySettings is not null)
        {
            _lotterySettings.PropertyChanged -= DrawSettingsChild_OnPropertyChanged;
            _lotterySettings = null;
        }

        if (_faceDetectorSettings is not null)
        {
            _faceDetectorSettings.PropertyChanged -= DrawSettingsChild_OnPropertyChanged;
            _faceDetectorSettings = null;
        }
    }

    private void DrawSettings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DrawSettingsConfig.RollCallSettings) or
            nameof(DrawSettingsConfig.QuickDrawSettings) or
            nameof(DrawSettingsConfig.LotterySettings) or
            nameof(DrawSettingsConfig.FaceDetectorSettings))
        {
            DetachDrawSettingsHandlers();
            AttachDrawSettingsHandlers();
        }

        Save();
    }

    private void DrawSettingsChild_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Save();
    }

    private void AttachFloatingWindowSettingsHandlers()
    {
        Data.FloatingWindowSettings.PropertyChanged += FloatingWindowSettings_OnPropertyChanged;
        Data.FloatingWindowSettings.FloatingWindowButtonControl.CollectionChanged +=
            FloatingWindowButtonControl_OnCollectionChanged;
    }

    private void DetachFloatingWindowSettingsHandlers()
    {
        Data.FloatingWindowSettings.PropertyChanged -= FloatingWindowSettings_OnPropertyChanged;
        Data.FloatingWindowSettings.FloatingWindowButtonControl.CollectionChanged -=
            FloatingWindowButtonControl_OnCollectionChanged;
    }

    private void FloatingWindowSettings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Save();
    }

    private void FloatingWindowButtonControl_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Save();
    }
    
    private void AttachLinkageSettingsHandlers()
    {
        _linkageSettings = Data.LinkageSettings;
        _linkageSettings.PropertyChanged += LinkageSettings_OnPropertyChanged;
    }
    
    private void DetachLinkageSettingsHandlers()
    {
        if (_linkageSettings is null)
        {
            return;
        }
        
        _linkageSettings.PropertyChanged -= LinkageSettings_OnPropertyChanged;
        _linkageSettings = null;
    }
    
    private void LinkageSettings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Save();
    }

    private void AttachSecuritySettingsHandlers()
    {
        _securitySettings = Data.SecuritySettings;
        _securitySettings.PropertyChanged += SecuritySettings_OnPropertyChanged;
    }

    private void DetachSecuritySettingsHandlers()
    {
        if (_securitySettings is null)
        {
            return;
        }

        _securitySettings.PropertyChanged -= SecuritySettings_OnPropertyChanged;
        _securitySettings = null;
    }

    private void SecuritySettings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Save();
    }

    private void AttachThemeManagementHandlers()
    {
        _themeManagement = Data.ThemeManagement;
        _themeManagement.PropertyChanged += ThemeManagement_OnPropertyChanged;
    }

    private void DetachThemeManagementHandlers()
    {
        if (_themeManagement is null)
        {
            return;
        }

        _themeManagement.PropertyChanged -= ThemeManagement_OnPropertyChanged;
        _themeManagement = null;
    }

    private void ThemeManagement_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Save();
    }
}
