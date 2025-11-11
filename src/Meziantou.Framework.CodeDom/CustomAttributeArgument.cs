namespace Meziantou.Framework.CodeDom;

/// <summary>Represents an argument passed to a custom attribute.</summary>
public class CustomAttributeArgument : CodeObject, ICommentable
{
    public CustomAttributeArgument()
        : this(propertyName: null, value: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CustomAttributeArgument"/> class with the specified value.</summary>
    /// <param name="value">The argument value.</param>
    public CustomAttributeArgument(Expression? value)
        : this(propertyName: null, value)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CustomAttributeArgument"/> class with the specified property name and value.</summary>
    /// <param name="propertyName">The property name for named arguments, or null for positional arguments.</param>
    /// <param name="value">The argument value.</param>
    public CustomAttributeArgument(string? propertyName, Expression? value)
    {
        CommentsBefore = new CommentCollection(this);
        CommentsAfter = new CommentCollection(this);

        PropertyName = propertyName;
        Value = value;
    }

    public CommentCollection CommentsBefore { get; }
    public CommentCollection CommentsAfter { get; }
    public string? PropertyName { get; set; }

    public Expression? Value
    {
        get;
        set => SetParent(ref field, value);
    }
}
