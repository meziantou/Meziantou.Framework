using System.Collections;

namespace Meziantou.Framework.Email;

/// <summary>The header fields of a message or of a MIME part, in their order. The names are compared ignoring case.</summary>
public sealed class MimeHeaderCollection : IReadOnlyList<MimeHeader>
{
    private readonly List<MimeHeader> _headers = [];

    /// <summary>Gets the number of headers.</summary>
    public int Count => _headers.Count;

    /// <summary>Gets a header from its position.</summary>
    /// <param name="index">The position of the header.</param>
    /// <returns>The header.</returns>
    public MimeHeader this[int index] => _headers[index];

    /// <summary>Gets the decoded value of the first header with a name, or <see langword="null"/>.</summary>
    /// <param name="name">The name of the header.</param>
    /// <returns>The value, or <see langword="null"/> when there is no such header.</returns>
    public string? this[string name] => Find(name)?.Value;

    /// <summary>Returns the first header with a name.</summary>
    /// <param name="name">The name of the header.</param>
    /// <returns>The header, or <see langword="null"/>.</returns>
    public MimeHeader? Find(string name) => _headers.Find(header => header.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns the headers with a name, in their order.</summary>
    /// <param name="name">The name of the headers.</param>
    /// <returns>The headers.</returns>
    public IEnumerable<MimeHeader> FindAll(string name) => _headers.Where(header => header.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds a header at the end.</summary>
    /// <param name="header">The header.</param>
    public void Add(MimeHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        _headers.Add(header);
    }

    /// <summary>Adds a header whose value is a text, encoded when it is not ASCII.</summary>
    /// <param name="name">The name of the header.</param>
    /// <param name="value">The text.</param>
    public void Add(string name, string value) => Add(MimeHeader.Create(name, value));

    /// <summary>Replaces the headers with a name by one header, at the position of the first one, or removes them when the value is <see langword="null"/>.</summary>
    /// <param name="header">The header, or <see langword="null"/> to remove the headers.</param>
    /// <param name="name">The name of the headers to replace.</param>
    public void Set(string name, MimeHeader? header)
    {
        var index = _headers.FindIndex(existing => existing.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        RemoveAll(name);
        if (header is not null)
        {
            _headers.Insert(index < 0 ? _headers.Count : Math.Min(index, _headers.Count), header);
        }
    }

    /// <summary>Replaces the headers with a name by a text value, or removes them when the value is <see langword="null"/>.</summary>
    /// <param name="name">The name of the header.</param>
    /// <param name="value">The text, or <see langword="null"/>.</param>
    public void Set(string name, string? value) => Set(name, value is null ? null : MimeHeader.Create(name, value));

    /// <summary>Removes the headers with a name.</summary>
    /// <param name="name">The name of the headers.</param>
    /// <returns>The number of headers removed.</returns>
    public int RemoveAll(string name) => _headers.RemoveAll(header => header.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Removes a header.</summary>
    /// <param name="header">The header.</param>
    /// <returns><see langword="true"/> when the collection contained the header.</returns>
    public bool Remove(MimeHeader header) => _headers.Remove(header);

    /// <inheritdoc />
    public IEnumerator<MimeHeader> GetEnumerator() => _headers.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
