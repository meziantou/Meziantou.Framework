using Meziantou.Framework.Email.Internals;
using Meziantou.Framework.Email.Internals.Tnef;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>Reads and writes TNEF streams ([MS-OXTNEF]): the <c>winmail.dat</c> attachments (<c>application/ms-tnef</c>) Outlook and Exchange send with a message. The item of a stream is read with the types of the items of Outlook data files: a <see cref="MapiMessage"/>, or a <see cref="MapiContact"/>, <see cref="MapiAppointment"/>, <see cref="MapiTask"/> or <see cref="MapiDistributionList"/>.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Email.Tests/TnefFileTests.cs" ref:region="TnefFile_Load" ref:dedent="true">
/// var message = TnefFile.Load(path);
///
/// // The class of the old versions of Microsoft Mail (IPM.Microsoft Mail.Note) is read as its MAPI class
/// Assert.Equal("IPM.Note", message.MessageClass);
/// foreach (var attachment in message.Attachments)
/// {
///     File.WriteAllBytes(Path.Combine(directory.FullName, Path.GetFileName(attachment.Name ?? "attachment")), attachment.GetContent());
/// }
/// </code>
/// </example>
public static class TnefFile
{
    /// <summary>Gets whether data starts with the signature of a TNEF stream.</summary>
    /// <param name="data">The data.</param>
    /// <returns><see langword="true"/> when the data starts with the signature.</returns>
    public static bool HasSignature(ReadOnlySpan<byte> data) => TnefParser.HasSignature(data);

    /// <summary>Reads the item of a TNEF stream.</summary>
    /// <param name="data">The content of the stream.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default options. <see cref="PstReadOptions.VerifyChecksums"/> checks the checksums of the attributes, <see cref="PstReadOptions.MaxPropertySize"/> limits the size of a property, and <see cref="PstReadOptions.MaxEmbeddedMessageDepth"/> the depth of the embedded messages.</param>
    /// <returns>The item.</returns>
    /// <exception cref="InvalidEmailDataException">The data is not a TNEF stream, or its structures are malformed.</exception>
    /// <exception cref="EmailResourceLimitException">A property is larger than <see cref="PstReadOptions.MaxPropertySize"/>.</exception>
    public static MapiMessage Load(ReadOnlySpan<byte> data, PstReadOptions? options = null)
    {
        return TnefMessageSource.Load(new TnefContext(options ?? PstReadOptions.Default), data, embeddingDepth: 0);
    }

    /// <summary>Reads the item of a TNEF stream, until its end.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default options.</param>
    /// <returns>The item.</returns>
    /// <exception cref="InvalidEmailDataException">The data is not a TNEF stream, or its structures are malformed.</exception>
    /// <exception cref="EmailResourceLimitException">A property is larger than <see cref="PstReadOptions.MaxPropertySize"/>.</exception>
    public static MapiMessage Load(Stream stream, PstReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Load(StreamHelper.ReadAll(stream, int.MaxValue), options);
    }

    /// <summary>Reads the item of a TNEF file, such as a <c>winmail.dat</c> file.</summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="options">The options, or <see langword="null"/> for the default options.</param>
    /// <returns>The item.</returns>
    /// <exception cref="InvalidEmailDataException">The data is not a TNEF stream, or its structures are malformed.</exception>
    /// <exception cref="EmailResourceLimitException">A property is larger than <see cref="PstReadOptions.MaxPropertySize"/>.</exception>
    public static MapiMessage Load(string path, PstReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Load(File.ReadAllBytes(path), options);
    }

    /// <summary>Writes an item as a TNEF stream, with Unicode strings.</summary>
    /// <param name="item">The item: a message, a contact, an appointment, a task or a distribution list.</param>
    /// <param name="stream">The stream.</param>
    public static void Save(MapiMessageBuilder item, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(stream);
        TnefWriter.Write(item, stream);
    }

    /// <summary>Writes an item as a TNEF file, such as a <c>winmail.dat</c> file.</summary>
    /// <param name="item">The item.</param>
    /// <param name="path">The path of the file.</param>
    public static void Save(MapiMessageBuilder item, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.Create(path);
        Save(item, stream);
    }

    /// <summary>Returns the TNEF stream of an item.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The content of the stream.</returns>
    public static byte[] ToByteArray(MapiMessageBuilder item)
    {
        using var stream = new MemoryStream();
        Save(item, stream);
        return stream.ToArray();
    }
}
