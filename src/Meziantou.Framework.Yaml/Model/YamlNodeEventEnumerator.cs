using System.Collections;
using System.Diagnostics;
using Meziantou.Framework.Yaml.Events;
using Scalar = Meziantou.Framework.Yaml.Events.Scalar;

namespace Meziantou.Framework.Yaml.Model;

/// <summary>Represents the Yaml Node Event Enumerator.</summary>
public sealed class YamlNodeEventEnumerator : IEnumerable<ParsingEvent>, IEnumerator<ParsingEvent>
{
    private readonly YamlNode _root;
    private YamlNode? _currentNode;
    private int _currentIndex;
    private Stack<YamlNode>? _nodePath;
    private Stack<int>? _indexPath;

    /// <summary>Initializes a new instance of this type.</summary>
    public YamlNodeEventEnumerator(YamlNode root)
    {
        this._root = root;
        _currentNode = root;
        _currentIndex = -1;
    }

    /// <summary>Gets enumerator.</summary>
    public IEnumerator<ParsingEvent> GetEnumerator()
    {
        return this;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>Releases resources used by the current instance.</summary>
    public void Dispose() { }

    /// <summary>Advances the enumerator to the next element.</summary>
    public bool MoveNext()
    {
        if (_currentNode == null)
            return false;

        if (_currentNode is YamlStream stream)
        {
            if (_currentIndex == -1)
            {
                Current = stream.StreamStart;
                _currentIndex++;
                return true;
            }

            if (_currentIndex < stream.Count)
            {
                Push(stream[_currentIndex]);
                return true;
            }

            Current = stream.StreamEnd;
            Pop();
            return true;
        }

        if (_currentNode is YamlDocument document)
        {
            if (_currentIndex == -1)
            {
                Current = document.DocumentStart;
                _currentIndex++;
                return true;
            }

            if (_currentIndex < 1)
            {
                if (document.Contents is not null)
                {
                    Push(document.Contents);
                    return true;
                }

                _currentIndex++;
            }

            Current = document.DocumentEnd;
            Pop();
            return true;
        }

        if (_currentNode is YamlMapping mapping)
        {
            if (_currentIndex == -1)
            {
                Current = mapping.MappingStart;
                _currentIndex++;
                return true;
            }

            if (_currentIndex < mapping.Count * 2)
            {
                if (_currentIndex % 2 == 0)
                    Push(((List<YamlElement>)mapping.Keys)[_currentIndex / 2]);
                else
                {
                    var mappingValue = mapping[(_currentIndex - 1) / 2].Value;
                    if (mappingValue is null)
                    {
                        Current = new Scalar("null");
                        _currentIndex++;
                    }
                    else
                    {
                        Push(mappingValue);
                    }
                }
                return true;
            }

            Current = mapping.MappingEnd;
            Pop();
            return true;
        }

        if (_currentNode is YamlSequence sequence)
        {
            if (_currentIndex == -1)
            {
                Current = sequence.SequenceStart;
                _currentIndex++;
                return true;
            }

            if (_currentIndex < sequence.Count)
            {
                Push(sequence[_currentIndex]);
                return true;
            }

            Current = sequence.SequenceEnd;
            Pop();
            return true;
        }

        if (_currentNode is YamlValue value)
        {
            Current = value.Scalar;
            Pop();
            return true;
        }

        return false;
    }

    private void Push(YamlNode nextNode)
    {
        if (nextNode is YamlValue value)
        {
            Current = value.Scalar;
            _currentIndex++;
            return;
        }

        if (_nodePath == null)
        {
            _nodePath = new Stack<YamlNode>();
            _indexPath = new Stack<int>();
        }

        Debug.Assert(_currentNode is not null);
        Debug.Assert(_indexPath is not null);
        _nodePath.Push(_currentNode);
        _indexPath.Push(_currentIndex);
        _currentNode = nextNode;
        _currentIndex = -1;
        MoveNext();
    }

    private void Pop()
    {
        if (_currentNode == _root)
        {
            _currentNode = null;
            return;
        }

        Debug.Assert(_nodePath is not null);
        Debug.Assert(_indexPath is not null);
        _currentNode = _nodePath.Pop();
        _currentIndex = _indexPath.Pop() + 1;
    }

    /// <summary>Resets the enumerator to its initial position.</summary>
    public void Reset()
    {
        Current = null!;
        _nodePath?.Clear();
        _indexPath?.Clear();
        _currentNode = _root;
        _currentIndex = -1;
    }

    /// <summary>Gets or sets current.</summary>
    public ParsingEvent Current { get; private set; } = null!;

    object IEnumerator.Current => Current;
}
