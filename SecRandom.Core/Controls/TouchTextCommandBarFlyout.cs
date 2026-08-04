using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Input;

namespace SecRandom.Core.Controls;

/// <summary>
/// A compact touch editing toolbar that keeps common text actions immediately available.
/// </summary>
public sealed class TouchTextCommandBarFlyout : FACommandBarFlyout
{
    private WeakReference<Control>? _target;

    public TouchTextCommandBarFlyout()
    {
        _commandBar.IsDynamicOverflowEnabled = false;
        Opening += (_, _) => UpdateCommands();
        Opened += (_, _) =>
        {
            if (Target is not null)
                _target = new WeakReference<Control>(Target);
        };
    }

    private void UpdateCommands()
    {
        PrimaryCommands.Clear();
        SecondaryCommands.Clear();
        if (Target is not TextBox textBox)
            return;

        var selectionLength = Math.Abs(textBox.SelectionEnd - textBox.SelectionStart);
        if (!textBox.IsReadOnly && selectionLength > 0)
            PrimaryCommands.Add(CreateButton(FAStandardUICommandKind.Cut, Cut));
        if (selectionLength > 0 && textBox.PasswordChar == default)
            PrimaryCommands.Add(CreateButton(FAStandardUICommandKind.Copy, Copy));
        if (!textBox.IsReadOnly && textBox.CanPaste)
            PrimaryCommands.Add(CreateButton(FAStandardUICommandKind.Paste, Paste));
        if (!textBox.IsReadOnly && textBox.CanUndo)
            SecondaryCommands.Add(CreateButton(FAStandardUICommandKind.Undo, Undo));
        if (!textBox.IsReadOnly && textBox.CanRedo)
            SecondaryCommands.Add(CreateButton(FAStandardUICommandKind.Redo, Redo));
        if (!string.IsNullOrEmpty(textBox.Text))
            SecondaryCommands.Add(CreateButton(FAStandardUICommandKind.SelectAll, SelectAll));
    }

    private IFACommandBarElement CreateButton(FAStandardUICommandKind kind, Action action)
    {
        var command = new FAStandardUICommand(kind);
        command.ExecuteRequested += (_, _) => action();
        return new FACommandBarButton { Command = command };
    }

    private void Cut() => Execute(textBox => textBox.Cut());
    private void Copy() => Execute(textBox => textBox.Copy());
    private void Paste() => Execute(textBox => textBox.Paste());
    private void Undo() => Execute(textBox => textBox.Undo());
    private void Redo() => Execute(textBox => textBox.Redo());
    private void SelectAll() => Execute(textBox => textBox.SelectAll());

    private void Execute(Action<TextBox> action)
    {
        if (_target?.TryGetTarget(out var target) == true && target is TextBox textBox)
        {
            try
            {
                action(textBox);
            }
            catch
            {
                // Clipboard providers can reject an operation; leave the editor usable.
            }
        }

        Hide();
    }
}
