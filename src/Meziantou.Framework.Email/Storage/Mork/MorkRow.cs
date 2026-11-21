namespace Meziantou.Framework.Email.Storage;

/// <summary>A row of a Mork database: its cells, by column name.</summary>
public sealed class MorkRow
{
    internal MorkRow(string scope, string id)
    {
        Scope = scope;
        Id = id;
    }

    /// <summary>Gets the scope of the row, such as <c>ns:msg:db:row:scope:msgs:all</c>.</summary>
    public string Scope { get; }

    /// <summary>Gets the identifier of the row in its scope, a hexadecimal number.</summary>
    public string Id { get; }

    /// <summary>Gets the cells of the row: the values by column name.</summary>
    public IDictionary<string, string> Cells { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets or sets the value of a cell, or <see langword="null"/> when the row has no cell in the column.</summary>
    /// <param name="column">The name of the column.</param>
    /// <returns>The value.</returns>
    public string? this[string column]
    {
        get => Cells.TryGetValue(column, out var value) ? value : null;
        set
        {
            if (value is null)
            {
                Cells.Remove(column);
            }
            else
            {
                Cells[column] = value;
            }
        }
    }

    /// <summary>Returns the scope and the identifier of the row.</summary>
    /// <returns>The scope and the identifier.</returns>
    public override string ToString() => Id + ":" + Scope;
}
