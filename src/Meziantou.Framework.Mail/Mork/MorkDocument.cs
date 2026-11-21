using System.Globalization;
using System.Text;
using Meziantou.Framework.Mail.Internals;

namespace Meziantou.Framework.Mail;

/// <summary>A Mork database: the text format of the summaries of the folders of Thunderbird (<c>.msf</c>) and of its old address books (<c>.mab</c>). It has tables of rows, and cells by column name; the dictionaries, the transactions (groups) and the updates of the file are applied when it is read.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Mail.Tests/MboxTests.cs" ref:region="MorkDocument_Usage" ref:dedent="true">
/// var document = MorkDocument.Load(path);
/// var table = document.FindTable("ns:msg:db:table:kind:msgs")!;
/// Assert.Equal("First", table.Rows[0]["subject"]);
/// Assert.Equal("Café", table.Rows[1]["subject"]);
/// </code>
/// </example>
public sealed class MorkDocument
{
    private const int MaxSize = 512 * 1024 * 1024;

    private readonly List<MorkTable> _tables = [];
    private readonly List<MorkRow> _rows = [];
    private readonly Dictionary<(string Scope, string Id), MorkTable> _tablesById = [];
    private readonly Dictionary<(string Scope, string Id), MorkRow> _rowsById = [];

    /// <summary>Gets the tables, in the order of the file.</summary>
    public IReadOnlyList<MorkTable> Tables => _tables;

    /// <summary>Gets the rows, in the tables or not, in the order of the file.</summary>
    public IReadOnlyList<MorkRow> Rows => _rows;

    /// <summary>Reads a Mork database.</summary>
    /// <param name="data">The content of the file.</param>
    /// <returns>The database.</returns>
    /// <exception cref="InvalidMailDataException">The content is not a Mork database.</exception>
    public static MorkDocument Load(ReadOnlySpan<byte> data)
    {
        var document = new MorkDocument();
        new MorkParser(document, data.ToArray()).Parse();
        return document;
    }

    /// <summary>Reads a Mork database from a stream, until its end.</summary>
    /// <param name="stream">The stream.</param>
    /// <returns>The database.</returns>
    /// <exception cref="InvalidMailDataException">The content is not a Mork database.</exception>
    /// <exception cref="MailResourceLimitException">The content is larger than 512 MB.</exception>
    public static MorkDocument Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Load(StreamHelper.ReadAll(stream, MaxSize));
    }

    /// <summary>Reads a Mork database, such as an <c>.msf</c> file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <returns>The database.</returns>
    /// <exception cref="InvalidMailDataException">The content is not a Mork database.</exception>
    /// <exception cref="MailResourceLimitException">The content is larger than 512 MB.</exception>
    public static MorkDocument Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    /// <summary>Returns the first table of a kind.</summary>
    /// <param name="kind">The kind, such as <c>ns:msg:db:table:kind:msgs</c>.</param>
    /// <returns>The table, or <see langword="null"/> when the database has none of this kind.</returns>
    public MorkTable? FindTable(string kind) => _tables.Find(table => string.Equals(table.Kind, kind, StringComparison.Ordinal));

    /// <summary>Returns a table, and creates it when the database does not have it.</summary>
    /// <param name="scope">The scope of the table.</param>
    /// <param name="id">The identifier of the table in its scope, a hexadecimal number.</param>
    /// <param name="kind">The kind of the table.</param>
    /// <returns>The table.</returns>
    public MorkTable GetOrAddTable(string scope, string id, string? kind = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var key = (scope, NormalizeId(id));
        if (!_tablesById.TryGetValue(key, out var table))
        {
            table = new MorkTable(scope, key.Item2);
            _tablesById.Add(key, table);
            _tables.Add(table);
        }

        if (kind is not null)
        {
            table.Kind = kind;
        }

        return table;
    }

    /// <summary>Returns a row, and creates it when the database does not have it.</summary>
    /// <param name="scope">The scope of the row.</param>
    /// <param name="id">The identifier of the row in its scope, a hexadecimal number.</param>
    /// <returns>The row.</returns>
    public MorkRow GetOrAddRow(string scope, string id)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var key = (scope, NormalizeId(id));
        if (!_rowsById.TryGetValue(key, out var row))
        {
            row = new MorkRow(scope, key.Item2);
            _rowsById.Add(key, row);
            _rows.Add(row);
        }

        return row;
    }

    /// <summary>Writes the database: a dictionary of the names of the columns and of the scopes, then the tables and the rows that are in no table.</summary>
    /// <param name="stream">The stream.</param>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        int Name(string name)
        {
            if (!names.TryGetValue(name, out var id))
            {
                id = 0x80 + names.Count;
                names.Add(name, id);
            }

            return id;
        }

        var body = new StringBuilder();
        var inTables = new HashSet<MorkRow>();
        foreach (var table in _tables)
        {
            body.Append('{').Append(table.Id).Append(":^").Append(Hex(Name(table.Scope))).Append(' ');
            if (table.Kind is not null)
            {
                body.Append("{(k^").Append(Hex(Name(table.Kind))).Append(":c)(s=9)}");
            }

            body.Append('\n');
            foreach (var row in table.Rows)
            {
                inTables.Add(row);
                AppendRow(body, row, Name);
            }

            body.Append("}\n");
        }

        foreach (var row in _rows)
        {
            if (!inTables.Contains(row))
            {
                AppendRow(body, row, Name);
            }
        }

        var header = new StringBuilder("// <!-- <mdb:mork:z v=\"1.4\"/> -->\n< <(a=c)> // (f=iso-8859-1)\n");
        foreach (var (name, id) in names)
        {
            header.Append("  (").Append(Hex(id)).Append('=').Append(Escape(name)).Append(")\n");
        }

        header.Append(">\n");
        stream.Write(Encoding.ASCII.GetBytes(header.ToString()));
        stream.Write(Encoding.ASCII.GetBytes(body.ToString()));
    }

    /// <summary>Writes the database in a file.</summary>
    /// <param name="path">The path of the file.</param>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.Create(path);
        Save(stream);
    }

    internal static string NormalizeId(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        foreach (var c in id)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                throw new ArgumentException($"The identifier '{id}' is not a hexadecimal number.", nameof(id));
            }
        }

        return id.ToUpperInvariant();
    }

    private static void AppendRow(StringBuilder body, MorkRow row, Func<string, int> name)
    {
        body.Append("  [").Append(row.Id).Append(":^").Append(Hex(name(row.Scope)));
        foreach (var (column, value) in row.Cells)
        {
            body.Append("(^").Append(Hex(name(column))).Append('=').Append(Escape(value)).Append(')');
        }

        body.Append("]\n");
    }

    private static string Hex(int value) => value.ToString("X", CultureInfo.InvariantCulture);

    /// <summary>Escapes a value: <c>\</c> before <c>)</c>, <c>\</c> and <c>$</c>, and <c>$XX</c> for the bytes of UTF-8 that are not printable ASCII.</summary>
    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            if (b is (byte)')' or (byte)'\\' or (byte)'$')
            {
                builder.Append('\\').Append((char)b);
            }
            else if (b < 0x20 || b >= 0x7F)
            {
                builder.Append('$').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append((char)b);
            }
        }

        return builder.ToString();
    }
}
