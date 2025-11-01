namespace Meziantou.Framework.CodeDom;

/// <summary>Defines an interface for code objects that can have comments before and after them.</summary>
public interface ICommentable
{
    /// <summary>Gets the collection of comments that appear after this code object.</summary>
    CommentCollection CommentsAfter { get; }

    /// <summary>Gets the collection of comments that appear before this code object.</summary>
    CommentCollection CommentsBefore { get; }
}
