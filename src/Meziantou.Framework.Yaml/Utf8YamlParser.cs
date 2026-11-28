using Meziantou.Framework.Yaml.Events;

namespace Meziantou.Framework.Yaml;

/// <summary>Produces the events of <see cref="IParser"/> with a <see cref="Utf8YamlReader"/>.</summary>
internal sealed class Utf8YamlParser : IParser
{
    private readonly Utf8YamlTokenSource _source;
    private bool _streamStarted;
    private bool _streamEnded;

    public Utf8YamlParser(string yaml, int maxDepth, string? sourceName)
    {
        _source = new Utf8YamlTokenSource(yaml, maxDepth, sourceName);
    }

    public Utf8YamlParser(TextReader reader, int maxDepth, string? sourceName)
    {
        _source = new Utf8YamlTokenSource(reader, maxDepth, sourceName);
    }

    public ParsingEvent? Current { get; private set; }

    public bool MoveNext()
    {
        if (!_streamStarted)
        {
            _streamStarted = true;
            Current = new StreamStart(default, default);
            return true;
        }

        if (_streamEnded)
        {
            Current = null;
            return false;
        }

        if (_source.Read())
        {
            Current = _source.CreateEvent();
            return true;
        }

        var end = _source.Start;
        Current = new StreamEnd(end, end);
        _streamEnded = true;
        return true;
    }
}
