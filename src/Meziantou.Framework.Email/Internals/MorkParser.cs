using System.Text;

namespace Meziantou.Framework.Email.Internals;

/// <summary>Reads a Mork database: dictionaries (<c>&lt;(80=value)&gt;</c>), rows (<c>[1:scope(^80=value)(^81^90)]</c>), tables (<c>{1:scope {(k^80:c)} [row] 2}</c>) and groups (<c>@$${1{@ ... @$$}1}@</c>).</summary>
internal sealed class MorkParser
{
    private const int MaxDepth = 32;

    private readonly MorkDocument _document;
    private readonly byte[] _data;
    private readonly Dictionary<string, string> _columns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _atoms = new(StringComparer.Ordinal);
    private int _position;
    private int _end;

    public MorkParser(MorkDocument document, byte[] data)
    {
        _document = document;
        _data = data;
        _end = data.Length;
    }

    public void Parse()
    {
        if (!_data.AsSpan().TrimStart(" \t\r\n"u8).StartsWith("// <!-- <mdb:mork"u8))
        {
            throw new InvalidEmailDataException("The content is not a Mork database: the header is missing.");
        }

        ParseContent(depth: 0);
    }

    private void ParseContent(int depth)
    {
        if (depth > MaxDepth)
        {
            throw new InvalidEmailDataException("The groups of the Mork database are nested too deeply.");
        }

        while (SkipSpace())
        {
            switch (_data[_position])
            {
                case (byte)'<':
                    ParseDictionary();
                    break;
                case (byte)'[':
                    ParseRow(defaultScope: "", table: null);
                    break;
                case (byte)'{':
                    ParseTable();
                    break;
                case (byte)'@' when StartsWith("@$${"):
                    ParseGroup(depth);
                    break;
                default:
                    _position++;
                    break;
            }
        }
    }

    /// <summary>Reads a group: its content is applied when it ends with <c>@$$}id}@</c>, and ignored when it is aborted (<c>@$$}~~}@</c>) or not ended.</summary>
    private void ParseGroup(int depth)
    {
        _position += 4;
        var start = Array.IndexOf(_data, (byte)'{', _position, _end - _position);
        if (start < 0 || start + 1 >= _end || _data[start + 1] != '@')
        {
            _position = _end;
            return;
        }

        var contentStart = start + 2;
        var marker = _data.AsSpan(contentStart, _end - contentStart).IndexOf("@$$}"u8);
        if (marker < 0)
        {
            _position = _end;
            return;
        }

        var contentEnd = contentStart + marker;
        var after = contentEnd + 4;
        var aborted = _data.AsSpan(after, _end - after).StartsWith("~~}@"u8);
        if (!aborted)
        {
            var savedEnd = _end;
            _position = contentStart;
            _end = contentEnd;
            ParseContent(depth + 1);
            _end = savedEnd;
        }

        var close = _data.AsSpan(after, _end - after).IndexOf("}@"u8);
        _position = close < 0 ? _end : after + close + 2;
    }

    private void ParseDictionary()
    {
        _position++;
        var columns = false;
        while (SkipSpace())
        {
            var c = _data[_position];
            if (c == '>')
            {
                _position++;
                return;
            }

            if (c == '<')
            {
                // The metadata of the dictionary: (a=c) for the dictionary of the columns
                _position++;
                while (SkipSpace() && _data[_position] != '>')
                {
                    if (_data[_position] == '(')
                    {
                        _position++;
                        var name = ReadUntil((byte)'=', (byte)')');
                        if (_position < _end && _data[_position] == '=')
                        {
                            _position++;
                            var value = ReadLiteral();
                            if (name == "a")
                            {
                                columns = Encoding.ASCII.GetString(value) == "c";
                            }
                        }
                        else
                        {
                            _position++;
                        }
                    }
                    else
                    {
                        _position++;
                    }
                }

                _position++;
                continue;
            }

            if (c == '(')
            {
                _position++;
                var id = ReadUntil((byte)'=', (byte)')').ToUpperInvariant();
                if (_position < _end && _data[_position] == '=')
                {
                    _position++;
                    var value = ReadLiteral();
                    if (columns)
                    {
                        _columns[id] = Decode(value);
                    }
                    else
                    {
                        _atoms[id] = value;
                    }
                }
                else
                {
                    _position++;
                }

                continue;
            }

            _position++;
        }
    }

    private void ParseTable()
    {
        _position++;
        SkipSpace();
        var cut = TrySkip((byte)'-');
        var (id, scope) = ReadObjectId(defaultScope: "");
        if (id.Length == 0)
        {
            SkipTo((byte)'}');
            return;
        }

        var table = _document.GetOrAddTable(scope, id);
        if (cut)
        {
            table.Rows.Clear();
        }

        while (SkipSpace())
        {
            var c = _data[_position];
            switch (c)
            {
                case (byte)'}':
                    _position++;
                    return;
                case (byte)'{':
                    ParseTableMetadata(table);
                    break;
                case (byte)'[':
                    ParseRow(scope, table);
                    break;
                case (byte)'-':
                    {
                        // A row removed from the table
                        _position++;
                        SkipSpace();
                        if (_position < _end && _data[_position] == '[')
                        {
                            var row = ParseRow(scope, table: null);
                            if (row is not null)
                            {
                                table.Rows.Remove(row);
                            }
                        }
                        else
                        {
                            var (rowId, rowScope) = ReadObjectId(scope);
                            if (rowId.Length > 0)
                            {
                                table.Rows.Remove(_document.GetOrAddRow(rowScope, rowId));
                            }
                        }

                        break;
                    }

                default:
                    {
                        // A reference to a row
                        var (rowId, rowScope) = ReadObjectId(scope);
                        if (rowId.Length == 0)
                        {
                            _position++;
                            break;
                        }

                        var row = _document.GetOrAddRow(rowScope, rowId);
                        if (!table.Rows.Contains(row))
                        {
                            table.Rows.Add(row);
                        }

                        break;
                    }
            }
        }
    }

    private void ParseTableMetadata(MorkTable table)
    {
        _position++;
        while (SkipSpace() && _data[_position] != '}')
        {
            if (_data[_position] == '(')
            {
                var (column, value) = ReadCell();
                if (column == "k" && value is not null)
                {
                    table.Kind = value;
                }
            }
            else
            {
                _position++;
            }
        }

        _position++;
    }

    private MorkRow? ParseRow(string defaultScope, MorkTable? table)
    {
        _position++;
        SkipSpace();
        var cut = TrySkip((byte)'-');
        var (id, scope) = ReadObjectId(defaultScope);
        if (id.Length == 0)
        {
            SkipTo((byte)']');
            return null;
        }

        var row = _document.GetOrAddRow(scope, id);
        if (cut)
        {
            row.Cells.Clear();
        }

        if (table is not null && !table.Rows.Contains(row))
        {
            table.Rows.Add(row);
        }

        while (SkipSpace())
        {
            var c = _data[_position];
            if (c == ']')
            {
                _position++;
                break;
            }

            if (c == '[')
            {
                // The metadata of the row
                SkipTo((byte)']');
                continue;
            }

            if (c == '-')
            {
                // A cell removed from the row
                _position++;
                SkipSpace();
                if (_position < _end && _data[_position] == '(')
                {
                    var (column, _) = ReadCell();
                    if (column is not null)
                    {
                        row.Cells.Remove(column);
                    }
                }

                continue;
            }

            if (c == '(')
            {
                var (column, value) = ReadCell();
                if (column is not null)
                {
                    row[column] = value ?? "";
                }

                continue;
            }

            _position++;
        }

        return row;
    }

    /// <summary>Reads a cell: <c>(column=literal)</c> or <c>(column^atom)</c>, where the column is a name or <c>^id</c>.</summary>
    private (string? Column, string? Value) ReadCell()
    {
        _position++;
        string column;
        if (TrySkip((byte)'^'))
        {
            var id = ReadUntil((byte)'=', (byte)'^', (byte)')').ToUpperInvariant();
            column = _columns.TryGetValue(id, out var name) ? name : "^" + id;
        }
        else
        {
            column = ReadUntil((byte)'=', (byte)'^', (byte)')');
        }

        if (_position >= _end)
        {
            return (null, null);
        }

        string? value = null;
        var c = _data[_position];
        if (c == '=')
        {
            _position++;
            value = Decode(ReadLiteral());
        }
        else if (c == '^')
        {
            _position++;
            var reference = ReadUntil((byte)')').ToUpperInvariant();
            _position++;

            // A reference to a column (":c") or to a value
            var colon = reference.IndexOf(':', StringComparison.Ordinal);
            if (colon >= 0)
            {
                var id = reference[..colon];
                value = reference[(colon + 1)..] == "C" && _columns.TryGetValue(id, out var name) ? name : _atoms.TryGetValue(id, out var atom) ? Decode(atom) : "";
            }
            else
            {
                value = _atoms.TryGetValue(reference, out var atom) ? Decode(atom) : _columns.GetValueOrDefault(reference, "");
            }
        }
        else
        {
            _position++;
        }

        return (column, value);
    }

    /// <summary>Reads an object identifier: a hexadecimal number, then <c>:scope</c> or <c>:^id</c>.</summary>
    private (string Id, string Scope) ReadObjectId(string defaultScope)
    {
        var start = _position;
        while (_position < _end && char.IsAsciiHexDigit((char)_data[_position]))
        {
            _position++;
        }

        var id = Encoding.ASCII.GetString(_data, start, _position - start).ToUpperInvariant();
        var scope = defaultScope;
        if (_position < _end && _data[_position] == ':')
        {
            _position++;
            if (TrySkip((byte)'^'))
            {
                var reference = ReadToken().ToUpperInvariant();
                scope = _columns.TryGetValue(reference, out var name) ? name : "^" + reference;
            }
            else
            {
                scope = ReadToken();
            }
        }

        return (id.Length > 64 ? "" : id, scope);
    }

    private string ReadToken()
    {
        var start = _position;
        while (_position < _end && _data[_position] is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'(' or (byte)')' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}'))
        {
            _position++;
        }

        return Encoding.Latin1.GetString(_data, start, _position - start);
    }

    private string ReadUntil(params ReadOnlySpan<byte> stops)
    {
        var start = _position;
        while (_position < _end && stops.IndexOf(_data[_position]) < 0)
        {
            _position++;
        }

        return Encoding.Latin1.GetString(_data, start, _position - start).Trim();
    }

    /// <summary>Reads a literal until its unescaped <c>)</c>: <c>\</c> escapes the next character, <c>\</c> before a line break continues the line, and <c>$XX</c> is a byte.</summary>
    private byte[] ReadLiteral()
    {
        using var result = new MemoryStream();
        while (_position < _end)
        {
            var c = _data[_position++];
            if (c == ')')
            {
                break;
            }

            if (c == '\\' && _position < _end)
            {
                var next = _data[_position++];
                if (next is (byte)'\r' or (byte)'\n')
                {
                    if (next == '\r' && _position < _end && _data[_position] == '\n')
                    {
                        _position++;
                    }

                    continue;
                }

                result.WriteByte(next);
                continue;
            }

            if (c == '$' && _position + 1 < _end && char.IsAsciiHexDigit((char)_data[_position]) && char.IsAsciiHexDigit((char)_data[_position + 1]))
            {
                result.WriteByte(Convert.FromHexString(_data.AsSpan(_position, 2))[0]);
                _position += 2;
                continue;
            }

            result.WriteByte(c);
        }

        return result.ToArray();
    }

    /// <summary>Skips the spaces and the comments. Returns <see langword="false"/> at the end.</summary>
    private bool SkipSpace()
    {
        while (_position < _end)
        {
            var c = _data[_position];
            if (c is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f')
            {
                _position++;
            }
            else if (c == '/' && _position + 1 < _end && _data[_position + 1] == '/')
            {
                while (_position < _end && _data[_position] != '\n')
                {
                    _position++;
                }
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    private bool TrySkip(byte value)
    {
        if (_position < _end && _data[_position] == value)
        {
            _position++;
            return true;
        }

        return false;
    }

    private void SkipTo(byte value)
    {
        while (_position < _end && _data[_position] != value)
        {
            _position++;
        }

        _position = Math.Min(_end, _position + 1);
    }

    private bool StartsWith(string value) => _data.AsSpan(_position, _end - _position).StartsWith(Encoding.ASCII.GetBytes(value));

    private static string Decode(byte[] value) => Encoding.UTF8.GetString(value);
}
