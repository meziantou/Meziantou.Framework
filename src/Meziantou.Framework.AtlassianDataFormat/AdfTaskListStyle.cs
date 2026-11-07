namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>How an <see cref="AdfTaskList"/> is converted.</summary>
public enum AdfTaskListStyle
{
    /// <summary>A GitHub task list, using <c>- [ ]</c> and <c>- [x]</c>.</summary>
    Checkbox = 0,

    /// <summary>A bullet list with no checkbox.</summary>
    PlainText,
}
