using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.Templating;

/// <summary>Represents a text template with embedded code blocks that can be dynamically compiled and executed.</summary>
/// <example>
/// <code><![CDATA[
/// var template = new Template();
/// template.Load("Hello <%=Name%>!");
/// template.Arguments.Add(new TemplateArgument("Name", typeof(string)));
/// var result = template.Run("Meziantou");
/// // result: "Hello Meziantou!"
/// ]]></code>
/// </example>
public class Template
{
    private const string DefaultClassName = "Template";
    private const string DefaultRunMethodName = "Run";
    private const string DefaultWriterParameterName = "__output__";

    private readonly Lock _buildLock = new();

    private MethodInfo? _runMethodInfo;

    [NotNull]
    private string? ClassName
    {
        get => string.IsNullOrEmpty(field) ? DefaultClassName : field;
        set;
    }

    [NotNull]
    private string? RunMethodName
    {
        get => string.IsNullOrEmpty(field) ? DefaultRunMethodName : field;
        set;
    }

    /// <summary>Gets or sets the name of the output parameter used in the generated code.</summary>
    [NotNull]
    public string? OutputParameterName
    {
        get => string.IsNullOrEmpty(field) ? DefaultWriterParameterName : field;
        set;
    }

    /// <summary>Gets or sets the type of the output parameter.</summary>
    public Type? OutputType { get; set; }

    /// <summary>Gets or sets the full type name of the base class for the generated template class.</summary>
    public string? BaseClassFullTypeName { get; set; }

    /// <summary>Gets or sets the delimiter that marks the start of a code block.</summary>
    public string StartCodeBlockDelimiter { get; set; } = "<%";

    /// <summary>Gets or sets the delimiter that marks the end of a code block.</summary>
    public string EndCodeBlockDelimiter { get; set; } = "%>";

    /// <summary>Gets the list of parsed blocks after loading a template.</summary>
    public BlockCollection Blocks { get; } = [];

    /// <summary>Gets a value indicating whether the template has been built.</summary>
    public bool IsBuilt => Volatile.Read(ref _runMethodInfo) is not null;

    /// <summary>Gets the generated C# source code after building the template.</summary>
    public string? SourceCode { get; private set; }

    /// <summary>Gets or sets the optional source file name used in compiler diagnostics.</summary>
    public string? SourceFileName { get; set; }

    /// <summary>Gets the list of template arguments.</summary>
    public ArgumentCollection Arguments { get; } = [];

    /// <summary>Gets the list of using directives.</summary>
    public UsingCollection Usings { get; } = ["System", "System.Collections.Generic", "System.Linq", "System.Text", "System.Threading.Tasks"];

    /// <summary>Gets the list of interfaces implemented by the generated template class.</summary>
    public InterfaceCollection ImplementedInterfaces { get; } = [];

    /// <summary>Gets the list of assembly references used for template compilation.</summary>
    public AssemblyReferenceCollection AssemblyReferences { get; } = [];

    /// <summary>Gets the list of C# source files included in the template compilation.</summary>
    public FileReferenceCollection IncludedSourceFiles { get; } = [];

    /// <summary>Gets or sets a value indicating whether to compile the template in debug mode.</summary>
    public bool Debug { get; set; }

    /// <summary>Loads the template from a string.</summary>
    /// <param name="text">The template text containing code blocks.</param>
    public void Load(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        LoadCore(text);
    }

    /// <summary>Loads the template from a <see cref="TextReader"/>.</summary>
    /// <param name="reader">The text reader containing the template.</param>
    public void Load(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        ThrowIfBuilt();
        LoadCore(reader.ReadToEnd());
    }

    private void ThrowIfBuilt()
    {
        if (IsBuilt)
            throw new InvalidOperationException("Template is already built.");
    }

    private void LoadCore(string text)
    {
        ThrowIfBuilt();

        var startCodeBlockDelimiter = StartCodeBlockDelimiter;
        var endCodeBlockDelimiter = EndCodeBlockDelimiter;
        var span = text.AsSpan();
        var blocks = new List<TemplateBlock>();
        var position = new TextPositionTracker();
        var blockIndex = 0;

        while (true)
        {
            // Text up to the next start delimiter
            var remaining = span[position.Index..];
            var delimiterOffset = remaining.IndexOf(startCodeBlockDelimiter, StringComparison.Ordinal);
            if (delimiterOffset < 0)
                break;

            AddBlock(blocks, codeBlock: false, span, ref position, delimiterOffset, ref blockIndex);
            position.Advance(startCodeBlockDelimiter);

            // Code up to the next end delimiter. An unterminated block falls back to a text block.
            remaining = span[position.Index..];
            delimiterOffset = remaining.IndexOf(endCodeBlockDelimiter, StringComparison.Ordinal);
            if (delimiterOffset < 0)
                break;

            var canTrimLeadingWhitespaceForLineOnlyBlock = TryGetTrailingLineWhitespaceLengthFromLastTextBlock(blocks, out var trailingWhitespaceLength);
            var block = AddBlock(blocks, codeBlock: true, span, ref position, delimiterOffset, ref blockIndex);
            position.Advance(endCodeBlockDelimiter);

            // A directive, class member or statement block sitting alone on its line swallows that line
            if (canTrimLeadingWhitespaceForLineOnlyBlock && block is not null && IsLineOnlyTrimmableBlock(block))
            {
                var lineEndLength = GetLineOnlyBlockTrailingLength(span, position.Index);
                if (lineEndLength >= 0)
                {
                    position.Advance(span.Slice(position.Index, lineEndLength));
                    TrimTrailingWhitespaceFromLastTextBlock(blocks, trailingWhitespaceLength);
                }
            }
        }

        // Create final parsed block if needed
        if (position.Index < span.Length)
        {
            AddBlock(blocks, codeBlock: false, span, ref position, span.Length - position.Index, ref blockIndex);
        }

        blocks.Sort(TemplateBlockComparer.IndexComparer);
        Blocks.Clear();
        Blocks.AddRange(blocks);

        TemplateBlock? AddBlock(List<TemplateBlock> blocks, bool codeBlock, ReadOnlySpan<char> span, ref TextPositionTracker position, int length, ref int blockIndex)
        {
            var start = position.Current;
            var content = span.Slice(position.Index, length);
            position.Advance(content);
            var block = CreateBlock(codeBlock, content.ToString(), blockIndex++, start, position.Current);
            if (block is not null)
            {
                blocks.Add(block);
            }

            return block;
        }
    }

    /// <summary>
    /// Gets the number of characters between <paramref name="index"/> and the end of the line (inclusive of the line
    /// terminator), or -1 when anything other than spaces and tabs remains on the line.
    /// </summary>
    private static int GetLineOnlyBlockTrailingLength(ReadOnlySpan<char> span, int index)
    {
        var current = index;
        while (current < span.Length && span[current] is ' ' or '\t')
        {
            current++;
        }

        if (current >= span.Length)
            return current - index;

        if (span[current] == '\r')
        {
            current++;
            if (current < span.Length && span[current] == '\n')
            {
                current++;
            }

            return current - index;
        }

        if (span[current] == '\n')
            return current - index + 1;

        return -1;
    }

    /// <summary>Tracks line, column and index while walking through the template text.</summary>
    private struct TextPositionTracker
    {
        private bool _previousIsCarriageReturn;

        public TextPositionTracker()
        {
        }

        public int Line { get; private set; } = 1;
        public int Column { get; private set; } = 1;
        public int Index { get; private set; }

        public readonly TextPosition Current => new(Line, Column, Index);

        public void Advance(ReadOnlySpan<char> text)
        {
            while (!text.IsEmpty)
            {
                var newLineIndex = text.IndexOfAny('\r', '\n');
                if (newLineIndex < 0)
                {
                    Column += text.Length;
                    Index += text.Length;
                    _previousIsCarriageReturn = false;
                    return;
                }

                if (newLineIndex > 0)
                {
                    Column += newLineIndex;
                    Index += newLineIndex;
                    _previousIsCarriageReturn = false;
                }

                Index++;
                if (text[newLineIndex] == '\r')
                {
                    Line++;
                    Column = 1;
                    _previousIsCarriageReturn = true;
                }
                else
                {
                    if (!_previousIsCarriageReturn)
                    {
                        Line++;
                        Column = 1;
                    }

                    _previousIsCarriageReturn = false;
                }

                text = text[(newLineIndex + 1)..];
            }
        }
    }

    private static bool IsLineOnlyTrimmableBlock(TemplateBlock block)
    {
        return block is DirectiveBlock or ClassMemberBlock or CodeBlock { IsExpression: false };
    }

    private static bool TryGetTrailingLineWhitespaceLengthFromLastTextBlock(List<TemplateBlock> blocks, out int trailingWhitespaceLength)
    {
        trailingWhitespaceLength = 0;

        if (blocks.Count == 0)
        {
            return true;
        }

        if (blocks[^1] is not TextBlock textBlock)
        {
            return false;
        }

        var text = textBlock.Text;
        var lastCarriageReturnIndex = text.LastIndexOf('\r', StringComparison.Ordinal);
        var lastLineFeedIndex = text.LastIndexOf('\n', StringComparison.Ordinal);
        var lastNewLineIndex = Math.Max(lastCarriageReturnIndex, lastLineFeedIndex);
        var trailingText = text.AsSpan(lastNewLineIndex + 1);
        foreach (var character in trailingText)
        {
            if (!char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        trailingWhitespaceLength = trailingText.Length;
        return true;
    }

    private void TrimTrailingWhitespaceFromLastTextBlock(List<TemplateBlock> blocks, int trailingWhitespaceLength)
    {
        if (trailingWhitespaceLength <= 0)
        {
            return;
        }

        if (blocks.Count == 0 || blocks[^1] is not TextBlock textBlock)
        {
            return;
        }

        var newLength = textBlock.Text.Length - trailingWhitespaceLength;
        if (newLength <= 0)
        {
            blocks.RemoveAt(blocks.Count - 1);
            return;
        }

        var newText = textBlock.Text[..newLength];
        var end = new TextPosition(textBlock.End.Line, textBlock.End.Column - trailingWhitespaceLength, textBlock.End.Index - trailingWhitespaceLength);
        var newBlock = CreateTextBlock(newText, textBlock.Index);
        newBlock.Span = new TextSpan(textBlock.Start, end);
        blocks[^1] = newBlock;
    }

    private TemplateBlock? CreateBlock(bool codeBlock, string text, int index, TextPosition start, TextPosition end)
    {
        TemplateBlock block;
        if (codeBlock && TryParseDirective(text, out var blockText, out var name, out var value))
        {
            block = CreateDirectiveBlock(blockText, name, value, index);
            start = MoveForward(start);
        }
        else if (codeBlock && TryRemovePrefix(text, '+', out blockText))
        {
            block = CreateClassMemberBlock(blockText, index);
            start = MoveForward(start);
        }
        else if (codeBlock && TryRemovePrefix(text, '=', out blockText))
        {
            block = CreateCodeExpressionBlock(blockText, index);
            start = MoveForward(start);
        }
        else
        {
            block = codeBlock ? CreateCodeBlock(text, index) : CreateTextBlock(text, index);
        }

        block.Span = new TextSpan(start, end);
        return block;
    }

    private static bool TryParseDirective(string text, [NotNullWhen(true)] out string? blockText, [NotNullWhen(true)] out string? name, [NotNullWhen(true)] out string? value)
    {
        if (!TryRemovePrefix(text, '@', out var directiveBlockText))
        {
            blockText = null;
            name = null;
            value = null;
            return false;
        }

        var directiveText = directiveBlockText.TrimStart();
        if (directiveText.Length == 0)
        {
            blockText = null;
            name = null;
            value = null;
            return false;
        }

        if (!char.IsLetter(directiveText[0]))
        {
            blockText = null;
            name = null;
            value = null;
            return false;
        }

        var nameLength = directiveText.AsSpan().IndexOfAny([' ', '\t', '\r', '\n']);
        if (nameLength < 0)
        {
            name = directiveText;
            value = string.Empty;
        }
        else
        {
            name = directiveText[..nameLength];
            value = directiveText[nameLength..].Trim();
        }

        if (name.Length == 0)
        {
            blockText = null;
            name = null;
            value = null;
            return false;
        }

        blockText = directiveBlockText;
        return true;
    }

    private static bool TryRemovePrefix(string text, char prefix, [NotNullWhen(true)] out string? value)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length > 0 && text[0] == prefix)
        {
            value = text[1..];
            return true;
        }

        value = null;
        return false;
    }

    private static TextPosition MoveForward(TextPosition position)
    {
        return new TextPosition(position.Line, position.Column + 1, position.Index + 1);
    }

    /// <summary>Compiles the template into executable code.</summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    public void Build(CancellationToken cancellationToken)
    {
        if (IsBuilt)
            return;

        lock (_buildLock)
        {
            if (IsBuilt)
                return;

            ApplyDirectives();

            using var sw = new StringWriter();
            using (var tw = new IndentedTextWriter(sw))
            {
                foreach (var @using in Usings)
                {
                    tw.WriteLine("using " + @using + ";");
                }

                var inheritanceTypes = new List<string>();
                if (!string.IsNullOrEmpty(BaseClassFullTypeName))
                {
                    inheritanceTypes.Add(BaseClassFullTypeName);
                }

                foreach (var @interface in ImplementedInterfaces)
                {
                    inheritanceTypes.Add(@interface);
                }

                tw.Write("public class " + ClassName);
                if (inheritanceTypes.Count > 0)
                {
                    tw.Write(" : " + string.Join(", ", inheritanceTypes));
                }

                tw.WriteLine();
                tw.WriteLine("{");
                tw.Indent++;

                tw.Write("public static void " + RunMethodName);
                tw.Write("(");
                tw.Write(OutputType?.FullName ?? "dynamic");
                tw.Write(" " + OutputParameterName);

                foreach (var argument in Arguments)
                {
                    if (argument is null)
                        continue;

                    tw.Write(", ");
                    tw.Write(argument.Type?.FullName ?? "dynamic");
                    tw.Write(" ");
                    tw.Write(argument.Name);
                }

                tw.Write(")");
                tw.WriteLine();
                tw.WriteLine("{");
                tw.Indent++;

                foreach (var block in Blocks)
                {
                    if (block is ClassMemberBlock)
                        continue;

                    WriteBlock(tw, block);
                }

                tw.Indent--;
                tw.WriteLine("}");

                foreach (var block in Blocks)
                {
                    if (block is not ClassMemberBlock)
                        continue;

                    WriteBlock(tw, block);
                }

                tw.Indent--;
                tw.WriteLine("}");
            }

            var source = sw.ToString();
            SourceCode = source;
            Compile(source, cancellationToken);
            if (IsBuilt)
            {
                FreezeCollections();
            }
        }
    }

    private void WriteBlock(IndentedTextWriter writer, TemplateBlock block)
    {
        var code = block.BuildCode();
        if (block is CodeBlock or ClassMemberBlock && code.Length > 0 && block.Text.Length > 0)
        {
            var textOffset = code.IndexOf(block.Text, StringComparison.Ordinal);
            if (textOffset >= 0)
            {
                var generatedCodeColumnOffset = (writer.Indent * IndentedTextWriter.DefaultTabString.Length) + textOffset;
                writer.WriteLineNoTabs(CreateLineDirective(block.Span, generatedCodeColumnOffset));
                writer.WriteLine(code);
                writer.WriteLineNoTabs("#line default");
                return;
            }
        }

        writer.WriteLine(code);
    }

    private string CreateLineDirective(TextSpan span, int generatedCodeColumnOffset)
    {
        var fileName = SyntaxFactory.Literal(SourceFileName ?? string.Empty).Text;
        return string.Create(CultureInfo.InvariantCulture, $"#line ({span.Start.Line}, {span.Start.Column}) - ({span.End.Line}, {span.End.Column}) {generatedCodeColumnOffset} {fileName}");
    }

    private void ApplyDirectives()
    {
        foreach (var block in Blocks)
        {
            if (block is DirectiveBlock directive)
            {
                directive.ApplyDirective();
            }
        }
    }

    /// <summary>Creates a text block for text content.</summary>
    /// <param name="text">The text content.</param>
    /// <param name="index">The block index.</param>
    /// <returns>A new <see cref="TextBlock"/> instance.</returns>
    protected virtual TextBlock CreateTextBlock(string text, int index)
    {
        return new TextBlock(this, text, index);
    }

    /// <summary>Creates a code block for executable code.</summary>
    /// <param name="text">The code content.</param>
    /// <param name="index">The block index.</param>
    /// <returns>A new <see cref="CodeBlock"/> instance.</returns>
    protected virtual CodeBlock CreateCodeBlock(string text, int index)
    {
        return new CodeBlock(this, text, index);
    }

    /// <summary>Creates a code block for an evaluation expression.</summary>
    /// <param name="text">The expression content.</param>
    /// <param name="index">The block index.</param>
    /// <returns>A new <see cref="CodeBlock"/> instance.</returns>
    protected virtual CodeBlock CreateCodeExpressionBlock(string text, int index)
    {
        return new CodeBlock(this, text, index, isExpression: true);
    }

    /// <summary>Creates a class member block for class-level members.</summary>
    /// <param name="text">The member content.</param>
    /// <param name="index">The block index.</param>
    /// <returns>A new <see cref="ClassMemberBlock"/> instance.</returns>
    protected virtual ClassMemberBlock CreateClassMemberBlock(string text, int index)
    {
        return new ClassMemberBlock(this, text, index);
    }

    /// <summary>Creates a directive block.</summary>
    /// <param name="text">The directive content without the directive marker.</param>
    /// <param name="name">The directive name.</param>
    /// <param name="value">The directive value.</param>
    /// <param name="index">The block index.</param>
    /// <returns>A new <see cref="DirectiveBlock"/> instance.</returns>
    protected virtual DirectiveBlock CreateDirectiveBlock(string text, string name, string value, int index)
    {
        return new DirectiveBlock(this, text, index, name, value);
    }

    private void FreezeCollections()
    {
        Arguments.Freeze();
        Usings.Freeze();
        ImplementedInterfaces.Freeze();
        AssemblyReferences.Freeze();
        IncludedSourceFiles.Freeze();
        Blocks.Freeze();
    }

    protected virtual CSharpParseOptions CreateParseOptions()
    {
        return CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithPreprocessorSymbols(Debug ? "DEBUG" : "RELEASE");
    }

    /// <summary>Creates a syntax tree from the generated source code.</summary>
    /// <param name="source">The C# source code.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A syntax tree for compilation.</returns>
    protected virtual SyntaxTree CreateSyntaxTree(string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        return CSharpSyntaxTree.ParseText(source, CreateParseOptions(), cancellationToken: cancellationToken);
    }

    /// <summary>Creates a syntax tree from an included source file.</summary>
    /// <param name="sourcePath">The path to the C# source file.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A syntax tree for compilation.</returns>
    protected virtual SyntaxTree CreateIncludedSyntaxTree(string sourcePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var source = SourceText.From(File.ReadAllText(sourcePath), Encoding.UTF8);
        return CSharpSyntaxTree.ParseText(source, CreateParseOptions(), sourcePath, cancellationToken: cancellationToken);
    }

    /// <summary>Creates the list of assembly references for compilation.</summary>
    /// <returns>An array of metadata references.</returns>
    protected virtual MetadataReference[] CreateReferences()
    {
        var references = new List<AssemblyReference>
        {
            new AssemblyReference(typeof(object).Assembly.Location),
            new AssemblyReference(typeof(Template).Assembly.Location),
            // Require to use dynamic keyword
            new AssemblyReference(typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly.Location),
            new AssemblyReference(typeof(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo).Assembly.Location),
            new AssemblyReference(typeof(System.Dynamic.DynamicObject).Assembly.Location),
            new AssemblyReference(typeof(System.Linq.Expressions.ExpressionType).Assembly.Location),
            new AssemblyReference(Assembly.Load(new AssemblyName("mscorlib")).Location),
            new AssemblyReference(Assembly.Load(new AssemblyName("System.Runtime")).Location),
            new AssemblyReference(Assembly.Load(new AssemblyName("System.Dynamic.Runtime")).Location),
            new AssemblyReference(Assembly.Load(new AssemblyName("netstandard")).Location),
        };

        if (OutputType != null)
        {
            references.Add(new AssemblyReference(OutputType.Assembly.Location));
        }

        references.AddRange(AssemblyReferences);

        return references
            .DistinctBy(reference => (reference.Path, reference.Alias))
            .Select(CreateMetadataReference)
            .ToArray();
    }

    private static MetadataReference CreateMetadataReference(AssemblyReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var properties = MetadataReferenceProperties.Assembly;
        if (!string.IsNullOrEmpty(reference.Alias))
        {
            properties = properties.WithAliases([reference.Alias]);
        }

        return MetadataReference.CreateFromFile(reference.Path, properties);
    }

    /// <summary>Creates a C# compilation from the syntax tree.</summary>
    /// <param name="syntaxTree">The syntax tree to compile.</param>
    /// <returns>A C# compilation instance.</returns>
    protected virtual CSharpCompilation CreateCompilation(SyntaxTree syntaxTree)
    {
        ArgumentNullException.ThrowIfNull(syntaxTree);

        var assemblyName = "Template_" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + Guid.NewGuid().ToString("N");
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithDeterministic(deterministic: true)
            .WithOptimizationLevel(Debug ? OptimizationLevel.Debug : OptimizationLevel.Release)
            .WithPlatform(Platform.AnyCpu);

        var compilation = CSharpCompilation.Create(assemblyName,
            [syntaxTree],
            CreateReferences(),
            options);

        return compilation;
    }

    /// <summary>Creates emit options for the compilation.</summary>
    /// <returns>Emit options for the compiler.</returns>
    protected virtual EmitOptions CreateEmitOptions()
    {
        return new EmitOptions()
            .WithDebugInformationFormat(DebugInformationFormat.PortablePdb);
    }

    /// <summary>Compiles the source code into an assembly.</summary>
    /// <param name="source">The C# source code to compile.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    protected virtual void Compile(string source, CancellationToken cancellationToken)
    {
        var syntaxTree = CreateSyntaxTree(source, cancellationToken);
        var compilation = CreateCompilation(syntaxTree);
        if (IncludedSourceFiles.Count > 0)
        {
            var includedSyntaxTrees = IncludedSourceFiles
                .Select(file => CreateIncludedSyntaxTree(file.Path, cancellationToken));
            compilation = compilation.AddSyntaxTrees(includedSyntaxTrees);
        }

        using var dllStream = new MemoryStream();
        using var pdbStream = new MemoryStream();
        var emitResult = compilation.Emit(dllStream, pdbStream, options: CreateEmitOptions(), cancellationToken: cancellationToken);
        if (!emitResult.Success)
        {
            throw new TemplateException("Template file is not valid." + Environment.NewLine + string.Join(Environment.NewLine, emitResult.Diagnostics));
        }

        dllStream.Seek(0, SeekOrigin.Begin);
        pdbStream.Seek(0, SeekOrigin.Begin);

        var assembly = LoadAssembly(dllStream, pdbStream);
        var runMethodInfo = FindMethod(assembly);
        if (runMethodInfo is null)
        {
            throw new TemplateException("Run method not found in the generated assembly.");
        }

        // Publish last: a thread observing IsBuilt must also observe everything written above.
        Volatile.Write(ref _runMethodInfo, runMethodInfo);
    }

    /// <summary>Loads an assembly from memory streams.</summary>
    /// <param name="peStream">The stream containing the assembly.</param>
    /// <param name="pdbStream">The stream containing debug symbols.</param>
    /// <returns>The loaded assembly.</returns>
    protected virtual Assembly LoadAssembly(MemoryStream peStream, MemoryStream pdbStream)
    {
        return Assembly.Load(peStream.ToArray(), pdbStream.ToArray());
    }

    /// <summary>Finds the Run method in the compiled assembly.</summary>
    /// <param name="assembly">The compiled assembly.</param>
    /// <returns>The Run method information.</returns>
    protected virtual MethodInfo FindMethod(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var type = assembly.GetType(ClassName)
            ?? throw new TemplateException($"Type '{ClassName}' was not found in the generated assembly.");

        // A class member block can declare its own overload of the run method, which makes
        // Type.GetMethod(name) throw AmbiguousMatchException. Identify the generated one by its
        // first parameter instead: it is always the output parameter.
        var methodInfo = Array.Find(
            type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly),
            IsRunMethod)
            ?? throw new TemplateException($"Method '{RunMethodName}' was not found in the generated assembly.");

        return methodInfo;

        bool IsRunMethod(MethodInfo method)
        {
            if (!string.Equals(method.Name, RunMethodName, StringComparison.Ordinal))
                return false;

            var parameters = method.GetParameters();
            return parameters.Length > 0 && string.Equals(parameters[0].Name, OutputParameterName, StringComparison.Ordinal);
        }
    }

    /// <summary>Creates a string writer for capturing template output.</summary>
    /// <returns>A new string writer instance.</returns>
    protected virtual StringWriter CreateStringWriter()
    {
        return new StringWriter();
    }

    /// <summary>Creates an output object for the template.</summary>
    /// <param name="writer">The text writer to write output to.</param>
    /// <returns>An output object.</returns>
    protected virtual object CreateOutput(TextWriter writer)
    {
        return new Output(this, writer);
    }

    /// <summary>Executes the template with the specified parameters and returns the result.</summary>
    /// <param name="parameters">The parameter values to pass to the template.</param>
    /// <returns>The generated text from the template.</returns>
    public string Run(params object?[] parameters)
    {
        using var writer = CreateStringWriter();
        Run(writer, parameters);
        return writer.ToString();
    }

    /// <summary>Executes the template with the specified parameters and writes the result to a text writer.</summary>
    /// <param name="writer">The text writer to write the output to.</param>
    /// <param name="parameters">The parameter values to pass to the template.</param>
    public virtual void Run(TextWriter writer, params object?[] parameters)
    {
        if (!IsBuilt)
        {
            Build(CancellationToken.None);
        }

        var p = CreateMethodParameters(writer, parameters);
        InvokeRunMethod(p);
    }

    /// <summary>Creates the method parameters for template execution.</summary>
    /// <param name="writer">The text writer for output.</param>
    /// <param name="parameters">The template parameter values.</param>
    /// <returns>An array of method parameters.</returns>
    protected virtual object[] CreateMethodParameters(TextWriter writer, object?[]? parameters)
    {
        var p = new object[parameters?.Length + 1 ?? 1];
        p[0] = CreateOutput(writer);
        parameters?.CopyTo(p, 1);
        return p;
    }

    /// <summary>Executes the template with named parameters and returns the result.</summary>
    /// <param name="parameters">A dictionary of parameter names and values.</param>
    /// <returns>The generated text from the template.</returns>
    public string Run(IReadOnlyDictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        using var writer = new StringWriter();
        Run(writer, parameters);
        return writer.ToString();
    }

    /// <summary>Executes the template with named parameters and writes the result to a text writer.</summary>
    /// <param name="writer">The text writer to write the output to.</param>
    /// <param name="parameters">A dictionary of parameter names and values.</param>
    public virtual void Run(TextWriter writer, IReadOnlyDictionary<string, object?> parameters)
    {
        ArgumentNullException.ThrowIfNull(writer);

        ArgumentNullException.ThrowIfNull(parameters);

        var p = CreateMethodParameters(writer, parameters);
        InvokeRunMethod(p);
    }

    /// <summary>Creates the method parameters for template execution with named parameters.</summary>
    /// <param name="writer">The text writer for output.</param>
    /// <param name="parameters">A dictionary of parameter names and values.</param>
    /// <returns>An array of method parameters.</returns>
    protected virtual object?[] CreateMethodParameters(TextWriter writer, IReadOnlyDictionary<string, object?> parameters)
    {
        if (!IsBuilt)
        {
            Build(CancellationToken.None);
        }

        var parameterInfos = Volatile.Read(ref _runMethodInfo)!.GetParameters();
        var p = new object?[parameterInfos.Length];
        foreach (var pi in parameterInfos)
        {
            if (string.Equals(pi.Name, OutputParameterName, StringComparison.Ordinal))
            {
                p[pi.Position] = CreateOutput(writer);
            }
            else
            {
                if (parameters.TryGetValue(pi.Name!, out var value))
                {
                    p[pi.Position] = value;
                }
            }
        }

        return p;
    }

    /// <summary>Invokes the compiled Run method with the specified parameters.</summary>
    /// <param name="p">The method parameters.</param>
    protected virtual void InvokeRunMethod(object?[] p)
    {
        if (!IsBuilt)
        {
            Build(CancellationToken.None);
        }

        Volatile.Read(ref _runMethodInfo)!.Invoke(null, p);
    }
}
