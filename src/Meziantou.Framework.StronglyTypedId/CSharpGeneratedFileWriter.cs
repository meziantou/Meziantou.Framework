using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework;
internal sealed class CSharpGeneratedFileWriter
{
    private readonly StringBuilder _stringBuilder = new(capacity: 2000);
    private bool _mustIndent = true;
    private bool _isEndOfBlock;

    public string IndentationString { get; set; } = "\t";
    public int Indentation { get; set; }

    public void EnsureFreeCapacity(int length)
    {
        _stringBuilder.EnsureCapacity(_stringBuilder.Capacity + length);
    }

    public void WriteLine()
    {
        _stringBuilder.Append('\n');
        _mustIndent = true;
        _isEndOfBlock = false;
    }

    public void WriteLine(string text)
    {
        if (_isEndOfBlock)
        {
            _isEndOfBlock = false;
            if (text is not "}" and not "else")
            {
                WriteLine();
            }
        }

        WriteIndentation();
        EnsureFreeCapacity(text.Length + 1);
        _stringBuilder.Append(text);
        _stringBuilder.Append('\n');
        _mustIndent = true;
        _isEndOfBlock = text == "}";
    }

    public void WriteLine(char text)
    {
        if (_isEndOfBlock)
        {
            _isEndOfBlock = false;
            if (text != '}')
            {
                WriteLine();
            }
        }

        WriteIndentation();
        EnsureFreeCapacity(2);
        _stringBuilder.Append(text);
        _stringBuilder.Append('\n');
        _mustIndent = true;
        _isEndOfBlock = text == '}';
    }

    public void Write(char text)
    {
        WriteIndentation();
        _stringBuilder.Append(text);
    }

    public void Write(string text)
    {
        WriteIndentation();
        _stringBuilder.Append(text);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "")]
    public IDisposable BeginPartialContext(ITypeSymbol type, Action<CSharpGeneratedFileWriter>? writeAttributes = null, string? baseTypes = null)
    {
        var initialIndentation = Indentation;
        var ns = GetNamespace(type.ContainingNamespace);
        if (ns is not null)
        {
            WriteLine("namespace " + ns);
            BeginBlock();
        }

        WriteContainingTypes(type.ContainingType);
        writeAttributes?.Invoke(this);
        WriteBeginType(type, baseTypes);
        return new CloseBlock(this, Indentation - initialIndentation);

        void WriteContainingTypes(ITypeSymbol? containingType)
        {
            if (containingType is null)
                return;

            WriteContainingTypes(containingType.ContainingType);
            WriteBeginType(containingType, baseTypes: null);
        }

        void WriteBeginType(ITypeSymbol typeSymbol, string? baseTypes)
        {
            var text = typeSymbol switch
            {
                { IsValueType: false, IsRecord: false } => "partial class " + typeSymbol.Name,
                { IsValueType: false, IsRecord: true } => "partial record " + typeSymbol.Name,
                { IsValueType: true, IsRecord: false } => "partial struct " + typeSymbol.Name,
                { IsValueType: true, IsRecord: true } => "partial record struct " + typeSymbol.Name,
            };

            Write(text);
            if (baseTypes is not null)
            {
                Write(" : ");
                Write(baseTypes);
            }

            WriteLine();
            _ = BeginBlock();
        }

        static string? GetNamespace(INamespaceSymbol ns)
        {
            string? str = null;
            while (ns is not null && !ns.IsGlobalNamespace)
            {
                if (str is not null)
                {
                    str = '.' + str;
                }

                str = ns.Name + str;
                ns = ns.ContainingNamespace;
            }

            return str;
        }
    }

    private void WriteIndentation()
    {
        if (!_mustIndent)
            return;

        for (var i = 0; i < Indentation; i++)
        {
            _stringBuilder.Append(IndentationString);
        }

        _mustIndent = false;
    }

    public IDisposable BeginBlock(string value)
    {
        WriteLine(value);
        WriteLine('{');
        Indentation++;
        return new CloseBlock(this, 1);
    }

    public IDisposable BeginBlock()
    {
        WriteLine('{');
        Indentation++;
        return new CloseBlock(this, 1);
    }

    public void EndBlock()
    {
        Indentation--;
        WriteLine('}');
    }

    public void WriteXmlComment(XNode[] nodes)
    {
        foreach (var node in nodes)
        {
            WriteXmlComment(node);
        }
    }

    public void WriteXmlComment(XNode node)
    {
        var content = node.ToString();
        using var reader = new StringReader(content);
        while (reader.ReadLine() is string line)
        {
            Write("/// ");
            WriteLine(line);
        }
    }

    public SourceText ToSourceText() => SourceText.From(_stringBuilder.ToString(), Encoding.UTF8);

    public void WriteAccessibility(Accessibility accessibility)
    {
        switch (accessibility)
        {
            case Accessibility.Private:
                Write("private");
                break;
            case Accessibility.ProtectedAndInternal:
                Write("private protected");
                break;
            case Accessibility.Protected:
                Write("protected");
                break;
            case Accessibility.Internal:
                Write("internal");
                break;
            case Accessibility.ProtectedOrInternal:
                Write("protected internal");
                break;
            case Accessibility.Public:
                Write("public");
                break;
        }
    }

    private sealed class CloseBlock : IDisposable
    {
        private readonly CSharpGeneratedFileWriter _writer;
        private readonly int _count;

        public CloseBlock(CSharpGeneratedFileWriter writer, int count)
        {
            _writer = writer;
            _count = count;
        }

        public void Dispose()
        {
            for (var i = 0; i < _count; i++)
            {
                _writer.EndBlock();
            }
        }
    }
}
