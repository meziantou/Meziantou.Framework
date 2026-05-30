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

    private static readonly Lock BuildLock = new();

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
    public bool IsBuilt => _runMethodInfo != null;

    /// <summary>Gets the generated C# source code after building the template.</summary>
    public string? SourceCode { get; private set; }

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

        using var reader = new StringReader(text);
        Load(reader);
    }

    /// <summary>Loads the template from a <see cref="TextReader"/>.</summary>
    /// <param name="reader">The text reader containing the template.</param>
    public void Load(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        using var r = new TextReaderWithPosition(reader);
        Load(r);
    }

    private void Load(TextReaderWithPosition reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (IsBuilt)
            throw new InvalidOperationException("Template is already built.");

        var blocks = new List<TemplateBlock>();
        var isInBlock = false;
        var currentBlock = new StringBuilder();
        var nextDelimiter = StartCodeBlockDelimiter;
        var blockDelimiterIndex = 0;
        var blockIndex = 0;
        var startLine = reader.Line;
        var startColumn = reader.Column;
        var startIndex = reader.Index;
        var delimiterStartLine = 0;
        var delimiterStartColumn = 0;
        var delimiterStartIndex = 0;

        int n;
        while ((n = reader.Read()) >= 0)
        {
            var c = (char)n;
            var line = reader.PreviousLine;
            var column = reader.PreviousColumn;
            var index = reader.PreviousIndex;

            if (blockDelimiterIndex < nextDelimiter.Length && c == nextDelimiter[blockDelimiterIndex])
            {
                if (blockDelimiterIndex == 0)
                {
                    delimiterStartLine = line;
                    delimiterStartColumn = column;
                    delimiterStartIndex = index;
                }

                blockDelimiterIndex++;
                if (blockDelimiterIndex >= nextDelimiter.Length) // end of delimiter
                {
                    var text = currentBlock.ToString(0, currentBlock.Length - (blockDelimiterIndex - 1));
                    var start = new TextPosition(startLine, startColumn, startIndex);
                    var end = new TextPosition(delimiterStartLine, delimiterStartColumn, delimiterStartIndex);
                    var block = CreateBlock(isInBlock, text, blockIndex++, start, end);
                    if (block is not null)
                    {
                        blocks.Add(block);
                    }

                    currentBlock.Clear();
                    blockDelimiterIndex = 0;
                    if (isInBlock)
                    {
                        nextDelimiter = StartCodeBlockDelimiter;
                        isInBlock = false;
                    }
                    else
                    {
                        nextDelimiter = EndCodeBlockDelimiter;
                        isInBlock = true;
                    }

                    continue;
                }
            }
            else
            {
                blockDelimiterIndex = 0;
            }

            if (currentBlock.Length == 0)
            {
                startLine = line;
                startColumn = column;
                startIndex = index;
            }

            currentBlock.Append(c);
        }

        // Create final parsed block if needed
        if (currentBlock.Length > 0)
        {
            var start = new TextPosition(startLine, startColumn, startIndex);
            var end = new TextPosition(reader.Line, reader.Column, reader.Index);
            var block = CreateBlock(codeBlock: false, currentBlock.ToString(), blockIndex, start, end);
            if (block is not null)
            {
                blocks.Add(block);
            }
        }

        blocks.Sort(TemplateBlockComparer.IndexComparer);
        Blocks.Clear();
        Blocks.AddRange(blocks);
    }

    private TemplateBlock? CreateBlock(bool codeBlock, string text, int index, TextPosition start, TextPosition end)
    {
        TemplateBlock block;
        if (codeBlock && TryParseDirective(text, out var name, out var value))
        {
            block = CreateDirectiveBlock(text, name, value, index);
        }
        else
        {
            block = codeBlock ? CreateCodeBlock(text, index) : CreateTextBlock(text, index);
        }

        block.Span = new TextSpan(start, end);
        return block;
    }

    private static bool TryParseDirective(string text, [NotNullWhen(true)] out string? name, [NotNullWhen(true)] out string? value)
    {
        var trimmedText = text.Trim();
        if (!trimmedText.StartsWith('@', StringComparison.Ordinal))
        {
            name = null;
            value = null;
            return false;
        }

        var directiveText = trimmedText[1..].TrimStart();
        if (directiveText.Length == 0)
        {
            name = null;
            value = null;
            return false;
        }

        if (!char.IsLetter(directiveText[0]))
        {
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
            name = null;
            value = null;
            return false;
        }

        return true;
    }

    /// <summary>Compiles the template into executable code.</summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    public void Build(CancellationToken cancellationToken)
    {
        if (IsBuilt)
            return;

        lock (BuildLock)
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
                    tw.WriteLine(block.BuildCode());
                }

                tw.Indent--;
                tw.WriteLine("}");
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

    /// <summary>Creates a directive block.</summary>
    /// <param name="text">The original directive text.</param>
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
        _runMethodInfo = FindMethod(assembly);
        if (_runMethodInfo == null)
        {
            throw new TemplateException("Run method not found in the generated assembly.");
        }
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
        var type = assembly.GetType(ClassName);
        System.Diagnostics.Debug.Assert(type != null);

        var methodInfo = type.GetMethod(RunMethodName);
        System.Diagnostics.Debug.Assert(methodInfo != null);

        return methodInfo;
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

        var parameterInfos = _runMethodInfo!.GetParameters();
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

        _runMethodInfo!.Invoke(null, p);
    }
}
