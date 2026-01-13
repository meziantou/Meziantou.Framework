using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Meziantou.Framework.Templating;

/// <summary>Represents a text template with embedded code blocks that can be dynamically compiled and executed.</summary>
/// <example>
/// <code><![CDATA[
/// var template = new Template();
/// template.Load("Hello <%=Name%>!");
/// template.AddArgument("Name", typeof(string));
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
    private readonly List<TemplateArgument> _arguments = [];
    private readonly List<string> _usings = [];
    private readonly List<string> _referencePaths = [];

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
    public IList<ParsedBlock>? Blocks { get; private set; }

    /// <summary>Gets a value indicating whether the template has been built.</summary>
    public bool IsBuilt => _runMethodInfo != null;

    /// <summary>Gets the generated C# source code after building the template.</summary>
    public string? SourceCode { get; private set; }

    /// <summary>Gets the list of template arguments.</summary>
    public IReadOnlyList<TemplateArgument> Arguments => _arguments;

    /// <summary>Gets the list of using directives.</summary>
    public IReadOnlyList<string> Usings => _usings;

    /// <summary>Gets the list of assembly reference paths.</summary>
    public IReadOnlyList<string> ReferencePaths => _referencePaths;

    /// <summary>Gets or sets a value indicating whether to compile the template in debug mode.</summary>
    public bool Debug { get; set; }

    /// <summary>Adds a reference to the assembly containing the specified type.</summary>
    /// <param name="type">The type whose assembly should be referenced.</param>
    public void AddReference(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (type.Assembly.Location is null)
            throw new ArgumentException("Assembly has no location.", nameof(type));

        _referencePaths.Add(type.Assembly.Location);
    }

    /// <summary>Adds a using directive for the specified namespace.</summary>
    /// <param name="namespace">The namespace to import.</param>
    public void AddUsing(string @namespace)
    {
        AddUsing(@namespace, alias: null);
    }

    /// <summary>Adds a using directive for the specified namespace with an optional alias.</summary>
    /// <param name="namespace">The namespace to import.</param>
    /// <param name="alias">The alias for the namespace, or <see langword="null"/> for no alias.</param>
    public void AddUsing(string @namespace, string? alias)
    {
        ArgumentNullException.ThrowIfNull(@namespace);

        if (!string.IsNullOrEmpty(alias))
        {
            _usings.Add(alias + " = " + @namespace);
        }
        else
        {
            _usings.Add(@namespace);
        }
    }

    /// <summary>Adds a using directive for the namespace of the specified type and a reference to its assembly.</summary>
    /// <param name="type">The type whose namespace should be imported.</param>
    public void AddUsing(Type type)
    {
        AddUsing(type, alias: null);
    }

    /// <summary>Adds a using directive for the specified type with an optional alias, and a reference to its assembly.</summary>
    /// <param name="type">The type to import.</param>
    /// <param name="alias">The alias for the type, or <see langword="null"/> for no alias.</param>
    public void AddUsing(Type type, string? alias)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (!string.IsNullOrEmpty(alias))
        {
            _usings.Add(alias + " = " + GetFriendlyTypeName(type));
        }
        else
        {
            if (type.Namespace is not null)
            {
                _usings.Add(type.Namespace);
            }
        }

        AddReference(type);
    }

    private static string GetFriendlyTypeName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var friendlyName = type.Name;
        if (type.IsGenericType)
        {
            var iBacktick = friendlyName.IndexOf('`', StringComparison.Ordinal);
            if (iBacktick > 0)
            {
                friendlyName = friendlyName[..iBacktick];
            }

            friendlyName += "<";
            var typeParameters = type.GetGenericArguments();
            for (var i = 0; i < typeParameters.Length; ++i)
            {
                var typeParamName = GetFriendlyTypeName(typeParameters[i]);
                friendlyName += i == 0 ? typeParamName : "," + typeParamName;
            }

            friendlyName += ">";
            friendlyName = type.Namespace + "." + friendlyName;
        }
        else
        {
            if (type.FullName is null)
                throw new ArgumentException("type has no FullName", nameof(type));

            friendlyName = type.FullName;
        }

        return friendlyName.Replace('+', '.');
    }

    /// <summary>Adds a template argument with dynamic type.</summary>
    /// <param name="name">The name of the argument.</param>
    public void AddArgument(string name)
    {
        AddArgument(name, type: null);
    }

    /// <summary>Adds a template argument with the specified type.</summary>
    /// <typeparam name="T">The type of the argument.</typeparam>
    /// <param name="name">The name of the argument.</param>
    public void AddArgument<T>(string name)
    {
        AddArgument(name, typeof(T));
    }

    /// <summary>Adds a template argument with the specified type.</summary>
    /// <param name="name">The name of the argument.</param>
    /// <param name="type">The type of the argument, or <see langword="null"/> for dynamic type.</param>
    public void AddArgument(string name, Type? type)
    {
        ArgumentNullException.ThrowIfNull(name);

        _arguments.Add(new TemplateArgument(name, type));
        if (type != null)
        {
            AddReference(type);
        }
    }

    /// <summary>Adds multiple template arguments from a dictionary, inferring types from the values.</summary>
    /// <param name="arguments">A dictionary of argument names and values.</param>
    public void AddArguments(IReadOnlyDictionary<string, object?> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        foreach (var argument in arguments)
        {
            AddArgument(argument.Key, argument.Value?.GetType());
        }
    }

    /// <summary>Adds multiple template arguments with dynamic types.</summary>
    /// <param name="arguments">The names of the arguments.</param>
    public void AddArguments(params string[] arguments)
    {
        foreach (var argument in arguments)
        {
            AddArgument(argument);
        }
    }

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

        var blocks = new List<ParsedBlock>();
        var isInBlock = false;
        var currentBlock = new StringBuilder();
        var nextDelimiter = StartCodeBlockDelimiter;
        var blockDelimiterIndex = 0;
        var blockIndex = 0;
        var startLine = reader.Line;
        var startColumn = reader.Column;

        int n;
        while ((n = reader.Read()) >= 0)
        {
            var c = (char)n;

            if (blockDelimiterIndex < nextDelimiter.Length && c == nextDelimiter[blockDelimiterIndex])
            {
                blockDelimiterIndex++;
                if (blockDelimiterIndex >= nextDelimiter.Length) // end of delimiter
                {
                    var text = currentBlock.ToString(0, currentBlock.Length - (blockDelimiterIndex - 1));
                    var block = CreateBlock(isInBlock, text, blockIndex++, startLine, startColumn, reader.Line, reader.Column);
                    blocks.Add(block);

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
                startLine = reader.Line;
                startColumn = reader.Column;
            }

            currentBlock.Append(c);
        }

        // Create final parsed block if needed
        if (currentBlock.Length > 0)
        {
            var block = CreateBlock(codeBlock: false, currentBlock.ToString(), blockIndex, startLine, startColumn, reader.Line, reader.Column);
            blocks.Add(block);
        }

        blocks.Sort(ParsedBlockComparer.IndexComparer);
        Blocks = blocks;
    }

    private ParsedBlock CreateBlock(bool codeBlock, string text, int index, int startLine, int startColumn, int endLine, int endColumn)
    {
        var block = codeBlock ? CreateCodeBlock(text, index) : CreateParsedBlock(text, index);
        block.StartLine = startLine;
        block.StartColumn = startColumn;
        block.EndLine = endLine;
        block.EndColumn = endColumn;
        return block;
    }

    /// <summary>Compiles the template into executable code.</summary>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    public void Build(CancellationToken cancellationToken)
    {
        if (Blocks is null)
            throw new InvalidOperationException("Template is not loaded.");

        if (IsBuilt)
            return;

        lock (BuildLock)
        {
            if (IsBuilt)
                return;

            using var sw = new StringWriter();
            using (var tw = new IndentedTextWriter(sw))
            {
                foreach (var @using in Usings)
                {
                    if (string.IsNullOrEmpty(@using))
                        continue;
                    tw.WriteLine("using " + @using + ";");
                }

                tw.Write("public class " + ClassName);
                if (!string.IsNullOrEmpty(BaseClassFullTypeName))
                {
                    tw.Write(" : " + BaseClassFullTypeName);
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
        }
    }

    /// <summary>Creates a parsed block for text content.</summary>
    /// <param name="text">The text content.</param>
    /// <param name="index">The block index.</param>
    /// <returns>A new <see cref="ParsedBlock"/> instance.</returns>
    protected virtual ParsedBlock CreateParsedBlock(string text, int index)
    {
        return new ParsedBlock(this, text, index);
    }

    /// <summary>Creates a code block for executable code.</summary>
    /// <param name="text">The code content.</param>
    /// <param name="index">The block index.</param>
    /// <returns>A new <see cref="CodeBlock"/> instance.</returns>
    protected virtual CodeBlock CreateCodeBlock(string text, int index)
    {
        return new CodeBlock(this, text, index);
    }

    /// <summary>Creates a syntax tree from the generated source code.</summary>
    /// <param name="source">The C# source code.</param>
    /// <param name="cancellationToken">A cancellation token to observe.</param>
    /// <returns>A syntax tree for compilation.</returns>
    protected virtual SyntaxTree CreateSyntaxTree(string source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var options = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithPreprocessorSymbols(Debug ? "DEBUG" : "RELEASE");

        return CSharpSyntaxTree.ParseText(source, options, cancellationToken: cancellationToken);
    }

    /// <summary>Creates the list of assembly references for compilation.</summary>
    /// <returns>An array of metadata references.</returns>
    protected virtual MetadataReference[] CreateReferences()
    {
        var references = new List<string>
        {
            typeof(object).Assembly.Location,
            typeof(Template).Assembly.Location,
            // Require to use dynamic keyword                
            typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly.Location,
            typeof(Microsoft.CSharp.RuntimeBinder.CSharpArgumentInfo).Assembly.Location,
            typeof(System.Dynamic.DynamicObject).Assembly.Location,
            typeof(System.Linq.Expressions.ExpressionType).Assembly.Location,
            Assembly.Load(new AssemblyName("mscorlib")).Location,
            Assembly.Load(new AssemblyName("System.Runtime")).Location,
            Assembly.Load(new AssemblyName("System.Dynamic.Runtime")).Location,
            Assembly.Load(new AssemblyName("netstandard")).Location,
        };

        if (OutputType != null)
        {
            references.Add(OutputType.Assembly.Location);
        }

        foreach (var reference in ReferencePaths)
        {
            if (string.IsNullOrEmpty(reference))
                continue;

            references.Add(reference);
        }

        var result = references.Where(_ => _ is not null).Distinct(StringComparer.Ordinal);
        //var str = string.Join("\r\n", result);            
        return result.Select(path => MetadataReference.CreateFromFile(path)).ToArray();
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
