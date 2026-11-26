using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Meziantou.Framework.Globbing.Internals;

[StructLayout(LayoutKind.Auto)]
internal ref struct PathReader
{
    private ReadOnlySpan<char> _filename;
    private int _currentSegmentLength;
    private readonly bool _pathSeparatorAware;

    public PathReader(ReadOnlySpan<char> path, ReadOnlySpan<char> filename, PathItemType? itemType, bool pathSeparatorAware = true)
    {
        _pathSeparatorAware = pathSeparatorAware;
        if (path.IsEmpty)
        {
            CurrentText = filename;
            _filename = [];
        }
        else
        {
            CurrentText = path;
            _filename = filename;
        }

        _currentSegmentLength = int.MinValue;

        if (itemType is null)
        {
            // A pattern that is not path-separator aware matches a plain string: a trailing '/' belongs to it and
            // says nothing about the kind of item, so it must not be trimmed.
            if (_pathSeparatorAware)
            {
                if (!_filename.IsEmpty)
                {
                    if (IsPathSeparator(_filename[^1]))
                    {
                        _filename = _filename[..^1];
                        IsDirectory = true;
                    }
                }
                else
                {
                    if (!CurrentText.IsEmpty && IsPathSeparator(CurrentText[^1]))
                    {
                        CurrentText = CurrentText[..^1];
                        IsDirectory = true;
                    }
                }
            }
        }
        else
        {
            IsDirectory = itemType is PathItemType.Directory;
        }

        if (!_filename.IsEmpty && IndexOfSeparator(_filename) >= 0)
            throw new ArgumentException("Filename contains a directory separator", nameof(filename));
    }

    public bool IsDirectory { get; }

    public ReadOnlySpan<char> CurrentText { get; private set; }
    public ReadOnlySpan<char> CurrentSegment => CurrentText[..CurrentSegmentLength];

    /// <summary>The text that ends with the last segment of the path.</summary>
    public readonly ReadOnlySpan<char> EndText => _filename.IsEmpty ? TextWithoutTrailingSeparator : _filename;

    /// <summary>
    ///     <see cref="CurrentText"/> without its trailing separator, which only ends the last segment: the matcher
    ///     consumes it with <see cref="ConsumeEndOfSegment"/>, so "a/b/" ends with the segment "b", not with an empty one.
    /// </summary>
    private readonly ReadOnlySpan<char> TextWithoutTrailingSeparator => !CurrentText.IsEmpty && IsPathSeparator(CurrentText[^1]) ? CurrentText[..^1] : CurrentText;

    public int CurrentSegmentLength
    {
        get
        {
            if (_currentSegmentLength == int.MinValue)
            {
                _currentSegmentLength = _pathSeparatorAware ? IndexOfSeparator(CurrentText) : -1;
                if (_currentSegmentLength == -1)
                {
                    _currentSegmentLength = CurrentText.Length;
                }
            }

            return _currentSegmentLength;
        }
    }

    public readonly bool IsEndOfCurrentSegment => CurrentText.IsEmpty || (_pathSeparatorAware && IsPathSeparator(CurrentText[0]));

    public readonly ReadOnlySpan<char> LastSegment
    {
        get
        {
            if (!_filename.IsEmpty)
                return _filename;

            var endSegmentIndex = LastIndexOfSeparator(CurrentText);
            if (endSegmentIndex == -1)
                return CurrentText;

            return CurrentText[(endSegmentIndex + 1)..];
        }
    }

    public readonly bool IsEndOfPath => CurrentText.IsEmpty;

    /// <summary>
    ///     A number that identifies the position of the reader in the path, which memoizes the result of matching
    ///     the rest of the pattern from there. The text left to read is a suffix of the path, so its length identifies
    ///     it, except that the end of the directory and the start of the file name leave the same text.
    /// </summary>
    public readonly int MemoPosition => ((CurrentText.Length + _filename.Length) * 2) + (CurrentText.IsEmpty && !_filename.IsEmpty ? 1 : 0);

    /// <summary>The number of values <see cref="MemoPosition"/> can take from the current position to the end of the path.</summary>
    public readonly int MemoPositionCount => MemoPosition + 2;

    public void ConsumeInSegment(int count)
    {
        Debug.Assert(count > 0);
        Debug.Assert(count <= CurrentSegmentLength);

        CurrentText = CurrentText[count..];
        if (_currentSegmentLength != int.MinValue)
        {
            _currentSegmentLength -= count;
        }
    }

    public void ConsumeEndOfSegment()
    {
        if (!_pathSeparatorAware)
        {
            CurrentText = _filename;
            _filename = [];
            _currentSegmentLength = CurrentText.Length;
            return;
        }

        if (CurrentText.IsEmpty)
        {
            CurrentText = _filename;
            _filename = [];
            _currentSegmentLength = CurrentText.Length;
        }
        else
        {
            Debug.Assert(IsPathSeparator());
            CurrentText = CurrentText[1..];
            _currentSegmentLength = int.MinValue;
            MoveToFileNameAfterTrailingSeparator();
        }
    }

    /// <summary>
    ///     A directory given with a trailing separator, such as "src/" or the root "/", is exhausted once that
    ///     separator is consumed, and the file name is the next segment.
    /// </summary>
    private void MoveToFileNameAfterTrailingSeparator()
    {
        if (CurrentText.IsEmpty && !_filename.IsEmpty)
        {
            CurrentText = _filename;
            _filename = [];
            _currentSegmentLength = CurrentText.Length;
        }
    }

    public void ConsumeToLastSegment()
    {
        if (!_filename.IsEmpty)
        {
            CurrentText = _filename;
            _filename = [];
        }
        else
        {
            // When the text holds no separator, index is -1 and the slice starts at 0, which keeps the whole text.
            var text = TextWithoutTrailingSeparator;
            var index = LastIndexOfSeparator(text);
            CurrentText = text[(index + 1)..];
        }

        _currentSegmentLength = CurrentText.Length;
    }

    public void ConsumeSegment()
    {
        if (!_pathSeparatorAware)
        {
            CurrentText = _filename;
            _filename = [];
            _currentSegmentLength = CurrentText.Length;
            return;
        }

        var endSegmentIndex = IndexOfSeparator(CurrentText);
        if (endSegmentIndex == -1)
        {
            CurrentText = _filename;
            _filename = [];
            _currentSegmentLength = CurrentText.Length;
        }
        else
        {
            CurrentText = CurrentText[(endSegmentIndex + 1)..];
        }

        _currentSegmentLength = int.MinValue;
        MoveToFileNameAfterTrailingSeparator();
    }

    public void ConsumeToEnd()
    {
        CurrentText = [];
        _filename = [];
        _currentSegmentLength = 0;
    }

    public bool TryConsumePathSuffix(string[] segments, StringComparison comparison)
    {
        // The trailing separator of the directory only separates it from the file name, or ends the last segment
        ReadOnlySpan<char> currentText = TextWithoutTrailingSeparator;
        ReadOnlySpan<char> filename = _filename;

        for (var i = segments.Length - 1; i >= 0; i--)
        {
            ReadOnlySpan<char> currentSegment;
            if (!filename.IsEmpty)
            {
                currentSegment = filename;
                filename = [];
            }
            else
            {
                if (currentText.IsEmpty)
                    return false;

                var endSegmentIndex = LastIndexOfSeparator(currentText);
                if (endSegmentIndex == -1)
                {
                    currentSegment = currentText;
                    currentText = [];
                }
                else
                {
                    currentSegment = currentText[(endSegmentIndex + 1)..];
                    currentText = currentText[..endSegmentIndex];
                }
            }

            if (!currentSegment.Equals(segments[i].AsSpan(), comparison))
                return false;
        }

        ConsumeToEnd();
        return true;
    }

    /// <summary>Whether one of the segments of the text left to read starts with a dot.</summary>
    public readonly bool HasSegmentStartingWithDot()
    {
        if (!_filename.IsEmpty && _filename[0] == '.')
            return true;

        var text = CurrentText;
        if (!text.IsEmpty && text[0] == '.')
            return true;

        // A single search for the separator followed by a dot, rather than one search per dot: a path such as
        // 'src/Meziantou.Framework.Globbing/Glob.cs' holds a dot in most of its segments.
        if (text.IndexOf(['/', '.']) >= 0)
            return true;

        return Path.DirectorySeparatorChar != '/' && text.IndexOf([Path.DirectorySeparatorChar, '.']) >= 0;
    }

    // Both separators are '/' on Unix, where a search for a single character is faster. The condition is a
    // constant for the JIT.
    public static int IndexOfSeparator(ReadOnlySpan<char> text)
    {
        return Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar ? text.IndexOf(Path.DirectorySeparatorChar) : text.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static int LastIndexOfSeparator(ReadOnlySpan<char> text)
    {
        return Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar ? text.LastIndexOf(Path.DirectorySeparatorChar) : text.LastIndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool IsPathSeparator(char c)
    {
        return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
    }

    public readonly bool IsPathSeparator()
    {
        if (!_pathSeparatorAware || CurrentText.IsEmpty)
            return false;

        var c = CurrentText[0];
        return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
    }
}
