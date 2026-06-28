using System;

namespace SecRandom.Models;

public sealed class MultiSelectSettingOption
{
    public MultiSelectSettingOption(string label, Func<bool> getValue, Action<bool> setValue)
    {
        Label = label;
        _getValue = getValue;
        _setValue = setValue;
    }

    private readonly Func<bool> _getValue;
    private readonly Action<bool> _setValue;

    public string Label { get; }

    public bool IsSelected => _getValue();

    public void SetSelected(bool value)
    {
        if (_getValue() != value)
        {
            _setValue(value);
        }
    }

    public override string ToString()
    {
        return Label;
    }
}
