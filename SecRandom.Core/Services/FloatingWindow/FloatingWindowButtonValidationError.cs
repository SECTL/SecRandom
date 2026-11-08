namespace SecRandom.Core.Services.FloatingWindow;

/// <summary>自定义按钮配置的校验结果。由设置页翻译成本地化提示。</summary>
public enum FloatingWindowButtonValidationError
{
    None = 0,
    LabelRequired,
    LabelTooLong,
    IconMissing,
    IconImageInvalid,
    TargetRequired,
    TargetTooLong,
    TargetInvalidUri,
    TargetInvalidPath,
    UnsupportedAction
}
