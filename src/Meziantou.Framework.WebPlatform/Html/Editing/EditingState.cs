using Meziantou.Framework.WebPlatform.Dom;
using DomRange = Meziantou.Framework.WebPlatform.Dom.Range;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>
/// The state of the editing commands of a document (https://w3c.github.io/editing/docs/execCommand/): the CSS styling flag,
/// the default single-line container name, and the state and value overrides of the commands.
/// </summary>
internal sealed class EditingState
{
    private Dictionary<string, bool>? _stateOverrides;
    private Dictionary<string, string>? _valueOverrides;
    private (Node StartNode, uint StartOffset, Node EndNode, uint EndOffset)? _overridesRange;

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#css-styling-flag: set by the styleWithCSS and useCSS commands.</summary>
    public bool CssStyling { get; set; }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#default-single-line-container-name: <c>div</c> or <c>p</c>, set by the defaultParagraphSeparator command.</summary>
    public string DefaultSingleLineContainerName { get; set; } = "div";

    /// <summary>
    /// Whether a command is running, until its input event is dispatched: as in Chromium and Gecko, a command that a script
    /// runs meanwhile (e.g. from a listener of the input event) is not run (https://github.com/w3c/editing/issues/200).
    /// </summary>
    public bool IsExecutingCommand { get; set; }

    /// <summary>The undo history of the editable content of the document (the undo and redo commands).</summary>
    public EditingHistory History { get; } = new();

    /// <summary>
    /// https://w3c.github.io/editing/docs/execCommand/#state-override: the overrides are unset when a boundary point of the
    /// active range changes. They are compared with the boundary points of the range the overrides were set for.
    /// </summary>
    private void ResetOverrides(DomRange range)
    {
        var current = (range.StartContainer, range.StartOffset, range.EndContainer, range.EndOffset);
        if (_overridesRange is { } stored && ReferenceEquals(stored.StartNode, current.StartContainer) && stored.StartOffset == current.StartOffset
            && ReferenceEquals(stored.EndNode, current.EndContainer) && stored.EndOffset == current.EndOffset)
        {
            return;
        }

        _stateOverrides?.Clear();
        _valueOverrides?.Clear();
        _overridesRange = current;
    }

    public bool? GetStateOverride(DomRange range, string command)
    {
        ResetOverrides(range);
        return _stateOverrides is not null && _stateOverrides.TryGetValue(command, out var value) ? value : null;
    }

    public void SetStateOverride(DomRange range, string command, bool state)
    {
        ResetOverrides(range);
        (_stateOverrides ??= new(StringComparer.Ordinal))[command] = state;
    }

    public void UnsetStateOverride(DomRange range, string command)
    {
        ResetOverrides(range);
        _stateOverrides?.Remove(command);
    }

    public string? GetValueOverride(DomRange range, string command)
    {
        ResetOverrides(range);
        return _valueOverrides is not null && _valueOverrides.TryGetValue(command, out var value) ? value : null;
    }

    /// <summary>Sets the value override of a command. backColor and hiliteColor share their value override.</summary>
    public void SetValueOverride(DomRange range, string command, string value)
    {
        ResetOverrides(range);
        _valueOverrides ??= new(StringComparer.Ordinal);
        _valueOverrides[command] = value;
        if (command == "backcolor")
        {
            _valueOverrides["hilitecolor"] = value;
        }
        else if (command == "hilitecolor")
        {
            _valueOverrides["backcolor"] = value;
        }
    }

    public void UnsetValueOverride(DomRange range, string command)
    {
        ResetOverrides(range);
        if (_valueOverrides is null)
            return;

        _valueOverrides.Remove(command);
        if (command == "backcolor")
        {
            _valueOverrides.Remove("hilitecolor");
        }
        else if (command == "hilitecolor")
        {
            _valueOverrides.Remove("backcolor");
        }
    }
}
