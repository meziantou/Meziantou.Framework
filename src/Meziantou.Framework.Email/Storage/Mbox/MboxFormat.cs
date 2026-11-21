namespace Meziantou.Framework.Email.Storage;

/// <summary>The variants of the mbox format: how a line of a message that starts with <c>From </c> is escaped, and how the end of a message is found.</summary>
public enum MboxFormat
{
    /// <summary>A message ends before the next line that starts with <c>From </c>; a line of a message that starts with <c>From </c> is escaped as <c>&gt;From </c>. Reading removes the <c>&gt;</c> of the lines that start with <c>&gt;From </c>, which is ambiguous for a line that started with <c>&gt;From </c>.</summary>
    Mboxo,

    /// <summary>As <see cref="Mboxo"/>, but every line that starts with <c>From </c> after any number of <c>&gt;</c> gets one more <c>&gt;</c>, so that the escaping is reversible. The format of most mail tools.</summary>
    Mboxrd,

    /// <summary>A <c>Content-Length</c> header gives the size of the body, and the lines are escaped as in <see cref="Mboxo"/>.</summary>
    Mboxcl,

    /// <summary>A <c>Content-Length</c> header gives the size of the body, and the lines are not escaped.</summary>
    Mboxcl2,
}
