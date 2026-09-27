// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Renderers;

/// <summary>
/// A text based <see cref="IMarkdownRenderer"/>.
/// </summary>
/// <seealso cref="RendererBase" />
public abstract class TextRendererBase : RendererBase
{
    private TextWriter _writer;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextRendererBase"/> class.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <exception cref="ArgumentNullException"></exception>
    protected TextRendererBase(TextWriter writer)
    {
        Writer = writer;
    }

    /// <summary>
    /// Gets or sets the writer.
    /// </summary>
    /// <exception cref="ArgumentNullException">if the value is null</exception>
    public TextWriter Writer
    {
        get => _writer;
        [MemberNotNull(nameof(_writer))]
        set
        {
            if (value is null)
            {
                ThrowHelper.ArgumentNullException(nameof(value));
            }

            // By default we output a newline with '\n' only even on Windows platforms
            value.NewLine = "\n";
            _writer = value;
        }
    }

    /// <summary>
    /// Renders the specified markdown object (returns the <see cref="Writer"/> as a render object).
    /// </summary>
    /// <param name="markdownObject">The markdown object.</param>
    /// <returns></returns>
    public override object Render(MarkdownObject markdownObject)
    {
        Write(markdownObject);
        return Writer;
    }
}

/// <summary>
/// Typed <see cref="TextRendererBase"/>.
/// </summary>
/// <typeparam name="T">Type of the renderer</typeparam>
/// <seealso cref="RendererBase" />
public abstract class TextRendererBase<T> : TextRendererBase where T : TextRendererBase<T>
{
    private sealed class Indent
    {
        private readonly string? _constant;
        private readonly string[]? _lineSpecific;
        private readonly string? _marker;
        private int _position;

        internal Indent(string constant)
        {
            _constant = constant;
        }

        internal Indent(string[] lineSpecific)
        {
            _lineSpecific = lineSpecific;
        }

        internal Indent(string marker, string rest)
        {
            _marker = marker;
            _constant = rest;
        }

        internal string Next()
        {
            if (_marker != null && _position == 0)
            {
                _position++;
                return _marker;
            }

            if (_constant != null)
            {
                return _constant;
            }

            //if (_lineSpecific.Count == 0) throw new Exception("Indents empty");
            if (_position == _lineSpecific!.Length) return string.Empty;

            return _lineSpecific![_position++];
        }

        internal int RemainingLines => _lineSpecific is null ? 0 : _lineSpecific.Length - _position;
    }

    /// <summary>
    /// Gets or sets the previous was line.
    /// </summary>
    protected bool PreviousWasLine;
    private readonly List<Indent> _indents;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextRendererBase{T}"/> class.
    /// </summary>
    /// <param name="writer">The writer.</param>
    protected TextRendererBase(TextWriter writer) : base(writer)
    {
        // We assume that we are starting as if we had previously a newline
        PreviousWasLine = true;
        _indents = new List<Indent>();
    }

    /// <summary>
    /// Performs the reset operation.
    /// </summary>
    protected internal void Reset()
    {
        if (Writer is StringWriter stringWriter)
        {
            stringWriter.GetStringBuilder().Length = 0;
        }
        else
        {
            ThrowHelper.InvalidOperationException("Cannot reset this TextWriter instance");
        }

        ResetInternal();
    }

    internal void ResetInternal()
    {
        ResetRenderingState();
        PreviousWasLine = true;
        _indents.Clear();
    }

    /// <summary>
    /// Ensures a newline.
    /// </summary>
    /// <returns>This instance</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T EnsureLine()
    {
        if (!PreviousWasLine)
        {
            PreviousWasLine = true;
            Writer.WriteLine();
        }
        return (T)this;
    }

    /// <summary>
    /// Performs the push indent operation.
    /// </summary>
    public void PushIndent(string indent)
    {
        if (indent is null) ThrowHelper.ArgumentNullException(nameof(indent));
        _indents.Add(new Indent(indent));
    }

    /// <summary>
    /// Performs the push indent operation.
    /// </summary>
    /// <param name="lineSpecific">The indent to use for each successive line.</param>
    /// <exception cref="ArgumentNullException"><paramref name="lineSpecific"/> is null.</exception>
    public void PushIndent(string[] lineSpecific)
    {
        if (lineSpecific is null) ThrowHelper.ArgumentNullException(nameof(lineSpecific));
        _indents.Add(new Indent(lineSpecific));

        // ensure that indents are written to the output stream
        // this assumes that calls after PushIndent wil write children content
        PreviousWasLine = true;
    }

    /// <summary>
    /// Pushes an indent that uses the specified marker on its first line and
    /// the same number of spaces on subsequent lines.
    /// </summary>
    /// <param name="marker">The first line of the hanging indent.</param>
    /// <remarks>
    /// Call at the beginning of a line, before writing child content. This method
    /// marks indents as pending but does not insert a newline; use <see cref="EnsureLine"/>
    /// first if necessary. The next write emits all active indents, including this marker.
    /// Call <see cref="PopIndent"/> after writing the indented content.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="marker"/> is null.</exception>
    public void PushHangingIndent(string marker)
    {
        if (marker is null) ThrowHelper.ArgumentNullException(nameof(marker));
        _indents.Add(new Indent(marker, new string(' ', marker.Length)));

        // ensure that indents are written to the output stream
        // this assumes that calls after PushHangingIndent will write children content
        PreviousWasLine = true;
    }

    /// <summary>
    /// Performs the pop indent operation.
    /// </summary>
    public void PopIndent()
    {
        if (this._indents.Count > 0)
            _indents.RemoveAt(_indents.Count - 1);
        else
            throw new InvalidOperationException("No indent to pop");
    }

    /// <summary>
    /// Performs the clear indent operation.
    /// </summary>
    public void ClearIndent() => _indents.Clear();

    // The number of lines of the last indent specific to each line that are not written yet
    internal int RemainingIndentLines => _indents.Count == 0 ? 0 : _indents[^1].RemainingLines;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void WriteIndent()
    {
        if (PreviousWasLine)
        {
            WriteIndentCore();
        }
    }

    private void WriteIndentCore()
    {
        PreviousWasLine = false;
        for (int i = 0; i < _indents.Count; i++)
        {
            var indent = _indents[i];
            var indentText = indent.Next();
            Writer.Write(indentText);
        }
    }

    /// <summary>
    /// Writes the specified content.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <returns>This instance</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Write(string? content)
    {
        WriteIndent();
        Writer.Write(content);
        return (T)this;
    }

    /// <summary>
    /// Writes the specified char repeated a specified number of times.
    /// </summary>
    /// <param name="c">The char to write.</param>
    /// <param name="count">The number of times to write the char.</param>
    /// <returns>This instance</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal T Write(char c, int count)
    {
        WriteIndent();

        for (int i = 0; i < count; i++)
        {
            Writer.Write(c);
        }

        return (T)this;
    }

    /// <summary>
    /// Writes the specified slice.
    /// </summary>
    /// <param name="slice">The slice.</param>
    /// <returns>This instance</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Write(ref StringSlice slice)
    {
        Write(slice.AsSpan());
        return (T)this;
    }

    /// <summary>
    /// Writes the specified slice.
    /// </summary>
    /// <param name="slice">The slice.</param>
    /// <returns>This instance</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Write(StringSlice slice)
    {
        Write(slice.AsSpan());
        return (T)this;
    }

    /// <summary>
    /// Writes the specified character.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <returns>This instance</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Write(char content)
    {
        WriteIndent();
        if (content == '\n')
        {
            PreviousWasLine = true;
        }
        Writer.Write(content);
        return (T)this;
    }

    /// <summary>
    /// Writes the specified content.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <param name="offset">The offset.</param>
    /// <param name="length">The length.</param>
    /// <returns>This instance</returns>
    public T Write(string content, int offset, int length)
    {
        if (content is not null)
        {
            Write(content.AsSpan(offset, length));
        }
        return (T)this;
    }

    /// <summary>
    /// Writes the specified content.
    /// </summary>
    /// <param name="content">The content.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySpan<char> content)
    {
        if (!content.IsEmpty)
        {
            WriteIndent();
            WriteRaw(content);
            if (content[content.Length - 1] == '\n')
            {
                PreviousWasLine = true;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void WriteRaw(char content) => Writer.Write(content);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void WriteRaw(string? content) => Writer.Write(content);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void WriteRaw(ReadOnlySpan<char> content)
    {
        Writer.Write(content);
    }

    /// <summary>
    /// Writes a newline.
    /// </summary>
    /// <returns>This instance</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T WriteLine()
    {
        WriteIndent();
        Writer.WriteLine();
        PreviousWasLine = true;
        return (T)this;
    }

    /// <summary>
    /// Writes a newline.
    /// </summary>
    /// <returns>This instance</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T WriteLine(NewLine newLine)
    {
        WriteIndent();
        Writer.Write(newLine.AsString());
        PreviousWasLine = true;
        return (T)this;
    }

    /// <summary>
    /// Writes a content followed by a newline.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <returns>This instance</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T WriteLine(string content)
    {
        WriteIndent();
        PreviousWasLine = true;
        Writer.WriteLine(content);
        return (T)this;
    }

    /// <summary>
    /// Writes a content followed by a newline.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <returns>This instance</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T WriteLine(char content)
    {
        WriteIndent();
        PreviousWasLine = true;
        Writer.WriteLine(content);
        return (T)this;
    }

    /// <summary>
    /// Writes the inlines of a leaf inline.
    /// </summary>
    /// <param name="leafBlock">The leaf block.</param>
    /// <returns>This instance</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T WriteLeafInline(LeafBlock leafBlock)
    {
        if (leafBlock is null) ThrowHelper.ArgumentNullException_leafBlock();
        Inline? inline = leafBlock.Inline;

        while (inline != null)
        {
            Write(inline);
            inline = inline.NextSibling;
        }

        return (T)this;
    }
}
