namespace Meziantou.Framework.Mail;

/// <summary>A table of a Mork database: a kind, and rows.</summary>
public sealed class MorkTable
{
    internal MorkTable(string scope, string id)
    {
        Scope = scope;
        Id = id;
    }

    /// <summary>Gets the scope of the table.</summary>
    public string Scope { get; }

    /// <summary>Gets the identifier of the table in its scope, a hexadecimal number.</summary>
    public string Id { get; }

    /// <summary>Gets or sets the kind of the table, such as <c>ns:msg:db:table:kind:msgs</c>.</summary>
    public string? Kind { get; set; }

    /// <summary>Gets the rows of the table, in their order.</summary>
    public IList<MorkRow> Rows { get; } = [];

    /// <summary>Returns the kind of the table.</summary>
    /// <returns>The kind of the table.</returns>
    public override string ToString() => Kind ?? Id;
}
