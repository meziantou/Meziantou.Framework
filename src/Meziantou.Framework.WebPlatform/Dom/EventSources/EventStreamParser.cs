using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// Interprets an event stream (https://html.spec.whatwg.org/multipage/server-sent-events.html#event-stream-interpretation)
/// as its bytes arrive: the bytes are decoded as UTF-8 (one leading byte order mark is removed), the lines end with CRLF,
/// LF or CR, and the fields of the lines (<c>data</c>, <c>event</c>, <c>id</c>, <c>retry</c>) are processed. An event is
/// dispatched at each blank line; the data of an event that is not complete at the end of the stream is discarded.
/// </summary>
/// <remarks>
/// A line is processed as soon as its end is received: a line that ends with a CR at the end of a chunk is not delayed until
/// the next chunk tells whether a LF follows (the LF is then ignored).
/// </remarks>
internal sealed class EventStreamParser
{
    private static readonly byte[] ByteOrderMark = [0xEF, 0xBB, 0xBF];

    private readonly Decoder _decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetDecoder();
    private readonly StringBuilder _line = new();
    private readonly StringBuilder _data = new();
    private readonly Action<EventStreamMessage> _dispatch;
    private readonly Action<TimeSpan> _setReconnectionTime;
    private string _eventType = "";
    private string _lastEventId;
    private char[] _chars = [];

    // The first bytes of the stream, until they tell whether the stream starts with a byte order mark
    private int _byteOrderMarkLength;
    private bool _byteOrderMarkChecked;
    private bool _lastCharacterWasCarriageReturn;

    /// <summary>Creates a parser.</summary>
    /// <param name="lastEventId">
    /// The initial value of the last event ID buffer. The specification initializes it to the empty string for each stream;
    /// the browsers keep the last event ID of the event source, so that the events of a reconnection that have no
    /// <c>id</c> field do not reset it.
    /// </param>
    /// <param name="dispatch">Called for each event that has data (https://html.spec.whatwg.org/multipage/server-sent-events.html#dispatchMessage).</param>
    /// <param name="setReconnectionTime">Called for each valid <c>retry</c> field.</param>
    public EventStreamParser(string lastEventId, Action<EventStreamMessage> dispatch, Action<TimeSpan> setReconnectionTime)
    {
        _lastEventId = lastEventId;
        LastEventId = lastEventId;
        _dispatch = dispatch;
        _setReconnectionTime = setReconnectionTime;
    }

    /// <summary>
    /// The last event ID string of the event source: the value of the last event ID buffer when the last event was
    /// dispatched (including the events without data, which are not reported).
    /// </summary>
    public string LastEventId { get; private set; }

    /// <summary>Processes the next bytes of the stream.</summary>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        // https://encoding.spec.whatwg.org/#utf-8-decode: one leading byte order mark is removed
        if (!_byteOrderMarkChecked)
        {
            while (bytes.Length > 0 && _byteOrderMarkLength < ByteOrderMark.Length && bytes[0] == ByteOrderMark[_byteOrderMarkLength])
            {
                _byteOrderMarkLength++;
                bytes = bytes[1..];
            }

            if (_byteOrderMarkLength == ByteOrderMark.Length)
            {
                _byteOrderMarkChecked = true;
            }
            else if (bytes.Length > 0)
            {
                // The stream does not start with a byte order mark: the bytes that looked like its beginning are decoded
                _byteOrderMarkChecked = true;
                Decode(ByteOrderMark.AsSpan(0, _byteOrderMarkLength));
            }
            else
            {
                return;
            }
        }

        Decode(bytes);
    }

    private void Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
            return;

        var maxCharCount = _decoder.GetCharCount(bytes, flush: false);
        if (_chars.Length < maxCharCount)
        {
            _chars = new char[Math.Max(maxCharCount, 256)];
        }

        var count = _decoder.GetChars(bytes, _chars, flush: false);
        ProcessCharacters(_chars.AsSpan(0, count));
    }

    private void ProcessCharacters(ReadOnlySpan<char> characters)
    {
        while (characters.Length > 0)
        {
            // The LF of a CRLF pair whose CR ended the previous line
            if (_lastCharacterWasCarriageReturn)
            {
                _lastCharacterWasCarriageReturn = false;
                if (characters[0] == '\n')
                {
                    characters = characters[1..];
                    continue;
                }
            }

            var index = characters.IndexOfAny('\r', '\n');
            if (index < 0)
            {
                _line.Append(characters);
                return;
            }

            _line.Append(characters[..index]);
            _lastCharacterWasCarriageReturn = characters[index] == '\r';
            characters = characters[(index + 1)..];
            var line = _line.ToString();
            _line.Clear();
            ProcessLine(line);
        }
    }

    private void ProcessLine(string line)
    {
        if (line.Length == 0)
        {
            DispatchEvent();
            return;
        }

        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon == 0)
            return;

        if (colon < 0)
        {
            ProcessField(line, "");
            return;
        }

        var valueStart = colon + 1 < line.Length && line[colon + 1] == ' ' ? colon + 2 : colon + 1;
        ProcessField(line[..colon], line[valueStart..]);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/server-sent-events.html#processField</summary>
    private void ProcessField(string name, string value)
    {
        switch (name)
        {
            case "event":
                _eventType = value;
                break;

            case "data":
                _data.Append(value).Append('\n');
                break;

            case "id":
                if (!value.Contains('\0', StringComparison.Ordinal))
                {
                    _lastEventId = value;
                }

                break;

            case "retry":
                if (value.Length > 0 && value.AsSpan().IndexOfAnyExceptInRange('0', '9') < 0)
                {
                    // A value that does not fit in the delay of a timer is clamped
                    var milliseconds = ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? Math.Min(parsed, int.MaxValue) : int.MaxValue;
                    _setReconnectionTime(TimeSpan.FromMilliseconds(milliseconds));
                }

                break;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/server-sent-events.html#dispatchMessage</summary>
    private void DispatchEvent()
    {
        LastEventId = _lastEventId;
        if (_data.Length == 0)
        {
            _eventType = "";
            return;
        }

        // The last LF of the data buffer is removed
        _data.Length--;
        var message = new EventStreamMessage(_eventType.Length == 0 ? "message" : _eventType, _data.ToString(), _lastEventId);
        _data.Clear();
        _eventType = "";
        _dispatch(message);
    }
}

/// <summary>An event of an event stream: the type of the event (<c>message</c> by default), its data and the last event ID string.</summary>
internal readonly record struct EventStreamMessage(string Type, string Data, string LastEventId);
