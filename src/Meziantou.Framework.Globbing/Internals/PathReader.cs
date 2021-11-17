using System;
using System.Runtime.InteropServices;
using System.Diagnostics;

#if NET472
using Microsoft.IO;
#else
using System.IO;
#endif

namespace Meziantou.Framework.Globbing.Internals
{
    [StructLayout(LayoutKind.Auto)]
    internal ref struct PathReader
    {
        private ReadOnlySpan<char> _filename;
        private int _currentSegmentLength;

        public PathReader(ReadOnlySpan<char> path, ReadOnlySpan<char> filename)
        {
            if (path.IsEmpty)
            {
                CurrentText = filename;
                _filename = ReadOnlySpan<char>.Empty;
            }
            else
            {
                CurrentText = path;
                _filename = filename;
            }

            _currentSegmentLength = int.MinValue;
        }

        public ReadOnlySpan<char> CurrentText { get; private set; }
        public ReadOnlySpan<char> CurrentSegment => CurrentText[..CurrentSegmentLength];

        public readonly ReadOnlySpan<char> EndText => _filename.IsEmpty ? CurrentText : _filename;

        public int CurrentSegmentLength
        {
            get
            {
                if (_currentSegmentLength == int.MinValue)
                {
                    _currentSegmentLength = CurrentText.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (_currentSegmentLength == -1)
                    {
                        _currentSegmentLength = CurrentText.Length;
                    }
                }
                return _currentSegmentLength;
            }
        }

        public readonly bool IsEndOfCurrentSegment => CurrentText.IsEmpty || IsPathSeparator(CurrentText[0]);

        public readonly ReadOnlySpan<char> LastSegment
        {
            get
            {
                if (!_filename.IsEmpty)
                    return _filename;

                var endSegmentIndex = CurrentText.LastIndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (endSegmentIndex == -1)
                    return CurrentText;

                return CurrentText[(endSegmentIndex + 1)..];
            }
        }

        public readonly bool IsEndOfPath => CurrentText.IsEmpty;

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
            if (CurrentText.IsEmpty)
            {
                CurrentText = _filename;
                _filename = ReadOnlySpan<char>.Empty;
                _currentSegmentLength = CurrentText.Length;
            }
            else
            {
                Debug.Assert(IsPathSeparator());
                CurrentText = CurrentText[1..];
                _currentSegmentLength = int.MinValue;
            }
        }

        public void ConsumeToLastSegment()
        {
            if (!_filename.IsEmpty)
            {
                CurrentText = _filename;
                _filename = ReadOnlySpan<char>.Empty;
            }
            else
            {
                var index = CurrentText.LastIndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (index >= -1)
                {
                    CurrentText = CurrentText[(index + 1)..];
                }
            }

            _currentSegmentLength = CurrentText.Length;
        }

        public void ConsumeSegment()
        {
            var endSegmentIndex = CurrentText.IndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (endSegmentIndex == -1)
            {
                CurrentText = _filename;
                _filename = ReadOnlySpan<char>.Empty;
                _currentSegmentLength = CurrentText.Length;
            }
            else
            {
                CurrentText = CurrentText[(endSegmentIndex + 1)..];
            }

            _currentSegmentLength = int.MinValue;
        }

        public void ConsumeToEnd()
        {
            CurrentText = ReadOnlySpan<char>.Empty;
            _filename = ReadOnlySpan<char>.Empty;
            _currentSegmentLength = 0;
        }

        public static bool IsPathSeparator(char c)
        {
            return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
        }

        public readonly bool IsPathSeparator()
        {
            var c = CurrentText[0];
            return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
        }
    }
}
