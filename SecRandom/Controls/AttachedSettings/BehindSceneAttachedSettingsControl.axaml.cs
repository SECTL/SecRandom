using SecRandom.Core;
using SecRandom.Core.Abstraction.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Models.AttachedSettings;

namespace SecRandom.Controls.AttachedSettings;

[AttachedSettingsUsage(AttachedSettingsTargets.Student | AttachedSettingsTargets.Prize)]
[AttachedSettingsControlInfo(GlobalConstants.BehindSceneAttachedSettings, "\uE230")]
public partial class BehindSceneAttachedSettingsControl : AttachedSettingsControlBase<BehindSceneAttachedSettings>
{
    public BehindSceneAttachedSettingsControl()
    {
        InitializeComponent();
    }
}