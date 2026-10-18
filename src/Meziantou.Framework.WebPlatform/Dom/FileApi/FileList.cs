using System.Collections;

namespace Meziantou.Framework.WebPlatform.Dom.FileApi;

/// <summary>https://w3c.github.io/FileAPI/#filelist-section</summary>
[IdlInterface("FileList", "https://w3c.github.io/FileAPI/#filelist-section")]
public sealed class FileList : IReadOnlyList<File>, Scripting.IStructuredSerializable
{
    /// <summary>The serialization steps of FileList (https://w3c.github.io/FileAPI/#filelist-section): the serialized files.</summary>
    object Scripting.IStructuredSerializable.CloneForStructuredSerialization() => new FileList([.. _files.Select(f => (File)((Scripting.IStructuredSerializable)f).CloneForStructuredSerialization())]);

    private readonly List<File> _files;

    internal FileList(List<File> files)
    {
        _files = files;
    }

    /// <summary>Gets the number of files in the list.</summary>
    [IdlMember("length", "https://w3c.github.io/FileAPI/#dfn-length")]
    public uint Length => (uint)_files.Count;

    /// <summary>Returns the file at the given index, or null when the index is out of range.</summary>
    [IdlMember("item", "https://w3c.github.io/FileAPI/#dfn-item")]
    public File? Item(uint index) => index < (uint)_files.Count ? _files[(int)index] : null;

    /// <summary>Gets the number of files in the list.</summary>
    [NotInIdl]
    public int Count => _files.Count;

    /// <summary>Gets the file at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public File this[int index] => _files[index];

    /// <inheritdoc/>
    [NotInIdl("IEnumerable implementation")]
    public IEnumerator<File> GetEnumerator() => _files.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
