using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Meziantou.Framework.Templating
{
    public class Template
    {
        private const string DefaultClassName = "Template";
        private const string DefaultRunMethodName = "Run";
        private const string DefaultWriterParameterName = "__output__";

        private static readonly object s_lock = new object();
        private static readonly Type? s_defaultWriterType = null; // dynamic by default

        private MethodInfo? _runMethodInfo = null;
        private string? _className;
        private string? _runMethodName;
        private string? _writerParameterName;
        private Type? _writerType;
        private readonly List<TemplateArgument> _arguments = new List<TemplateArgument>();
        private readonly List<string> _usings = new List<string>();
        private readonly List<string> _referencePaths = new List<string>();

        [NotNull]
        private string? ClassName
        {
            get => string.IsNullOrEmpty(_className) ? DefaultClassName : _className;
            set => _className = value;
        }

        [NotNull]
        private string? RunMethodName
        {
            get => string.IsNullOrEmpty(_runMethodName) ? DefaultRunMethodName : _runMethodName;
            set => _runMethodName = value;
        }

        [NotNull]
        public string? OutputParameterName
        {
            get => string.IsNullOrEmpty(_writerParameterName) ? DefaultWriterParameterName : _writerParameterName;
            set => _writerParameterName = value;
        }

        public Type? OutputType
        {
            get => _writerType ?? s_defaultWriterType;
            set => _writerType = value;
        }

        public string? BaseClassFullTypeName { get; set; }

        public string StartCodeBlockDelimiter { get; set; } = "<%";
        public string EndCodeBlockDelimiter { get; set; } = "%>";
        public IList<ParsedBlock>? Blocks { get; private set; }
        public bool IsBuilt => _runMethodInfo != null;
        public string? SourceCode { get; private set; }

        public IReadOnlyList<TemplateArgument> Arguments => _arguments;
        public IReadOnlyList<string> Usings => _usings;
        public IReadOnlyList<string> ReferencePaths => _referencePaths;

        public bool Debug { get; set; } = false;

        public void AddReference(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (type.Assembly.Location == null)
                throw new ArgumentException("Assembly has no location.", nameof(type));

            _referencePaths.Add(type.Assembly.Location);
        }

        public void AddUsing(string @namespace)
        {
            AddUsing(@namespace, alias: null);
        }

        public void AddUsing(string @namespace, string? alias)
        {
            if (@namespace == null)
                throw new ArgumentNullException(nameof(@namespace));

            if (!string.IsNullOrEmpty(alias))
            {
                _usings.Add(alias + " = " + @namespace);
            }
            else
            {
                _usings.Add(@namespace);
            }
        }

        public void AddUsing(Type type)
        {
            AddUsing(type, alias: null);
        }

        public void AddUsing(Type type, string? alias)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            if (!string.IsNullOrEmpty(alias))
            {
                _usings.Add(alias + " = " + GetFriendlyTypeName(type));
            }
            else
            {
                if (type.Namespace != null)
                {
                    _usings.Add(type.Namespace);
                }
            }

            AddReference(type);
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            var friendlyName = type.Name;
            if (type.IsGenericType)
            {
                var iBacktick = friendlyName.IndexOf('`');
                if (iBacktick > 0)
                {
                    friendlyName = friendlyName.Remove(iBacktick);
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
                if (type.FullName == null)
                    throw new ArgumentException("type has no FullName", nameof(type));

                friendlyName = type.FullName;
            }

            return friendlyName.Replace('+', '.');
        }

        public void AddArgument(string name)
        {
            AddArgument(name, type: null);
        }

        public void AddArgument<T>(string name)
        {
            AddArgument(name, typeof(T));
        }

        public void AddArgument(string name, Type? type)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            _arguments.Add(new TemplateArgument(name, type));
            if (type != null)
            {
                AddReference(type);
            }
        }

        public void AddArguments(IReadOnlyDictionary<string, object?> arguments)
        {
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));

            foreach (var argument in arguments)
            {
                AddArgument(argument.Key, argument.Value?.GetType());
            }
        }

        public void AddArguments(params string[] arguments)
        {
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));

            foreach (var argument in arguments)
            {
                AddArgument(argument);
            }
        }

        public void Load(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            using var reader = new StringReader(text);
            Load(reader);
        }

        public void Load(TextReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            using var r = new TextReaderWithPosition(reader);
            Load(r);
        }

        private void Load(TextReaderWithPosition reader)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

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

            blocks.Sort();
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

        public void Build()
        {
            if (Blocks == null)
                throw new InvalidOperationException("Template is not loaded.");

            if (IsBuilt)
                return;

            lock (s_lock)
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
                        if (argument == null)
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
                Compile(source);
            }
        }

        protected virtual ParsedBlock CreateParsedBlock(string text, int index)
        {
            return new ParsedBlock(this, text, index);
        }

        protected virtual CodeBlock CreateCodeBlock(string text, int index)
        {
            return new CodeBlock(this, text, index);
        }

        protected virtual SyntaxTree CreateSyntaxTree(string source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var options = CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Latest)
                .WithPreprocessorSymbols(Debug ? "DEBUG" : "RELEASE");

            return CSharpSyntaxTree.ParseText(source, options);
        }

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

            var result = references.Where(_ => _ != null).Distinct(StringComparer.Ordinal);
            //var str = string.Join("\r\n", result);            
            return result.Select(path => MetadataReference.CreateFromFile(path)).ToArray();
        }

        protected virtual CSharpCompilation CreateCompilation(SyntaxTree syntaxTree)
        {
            if (syntaxTree == null)
                throw new ArgumentNullException(nameof(syntaxTree));

            var assemblyName = "Template_" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + Guid.NewGuid().ToString("N");
            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithDeterministic(deterministic: true)
                .WithOptimizationLevel(Debug ? OptimizationLevel.Debug : OptimizationLevel.Release)
                .WithPlatform(Platform.AnyCpu);

            var compilation = CSharpCompilation.Create(assemblyName,
                new[] { syntaxTree },
                CreateReferences(),
                options);

            return compilation;
        }

        protected virtual EmitOptions CreateEmitOptions()
        {
            return new EmitOptions()
                .WithDebugInformationFormat(DebugInformationFormat.PortablePdb);
        }

        protected virtual void Compile(string source)
        {
            var syntaxTree = CreateSyntaxTree(source);
            var compilation = CreateCompilation(syntaxTree);

            using var dllStream = new MemoryStream();
            using var pdbStream = new MemoryStream();
            var emitResult = compilation.Emit(dllStream, pdbStream, options: CreateEmitOptions());
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

        protected virtual Assembly LoadAssembly(MemoryStream peStream, MemoryStream pdbStream)
        {
            return Assembly.Load(peStream.ToArray(), pdbStream.ToArray());
        }

        protected virtual MethodInfo FindMethod(Assembly assembly)
        {
            var type = assembly.GetType(ClassName);
            System.Diagnostics.Debug.Assert(type != null);

            var methodInfo = type.GetMethod(RunMethodName);
            System.Diagnostics.Debug.Assert(methodInfo != null);

            return methodInfo;
        }

        protected virtual StringWriter CreateStringWriter()
        {
            return new StringWriter();
        }

        protected virtual object CreateOutput(TextWriter writer)
        {
            return new Output(this, writer);
        }

        public string Run(params object?[] parameters)
        {
            using var writer = CreateStringWriter();
            Run(writer, parameters);
            return writer.ToString();
        }

        public virtual void Run(TextWriter writer, params object?[] parameters)
        {
            if (!IsBuilt)
            {
                Build();
            }

            var p = CreateMethodParameters(writer, parameters);

            InvokeRunMethod(p);
        }

        protected virtual object[] CreateMethodParameters(TextWriter writer, object?[]? parameters)
        {
            var p = new object[parameters?.Length + 1 ?? 1];
            p[0] = CreateOutput(writer);
            parameters?.CopyTo(p, 1);
            return p;
        }

        public string Run(IReadOnlyDictionary<string, object?> parameters)
        {
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            using var writer = new StringWriter();
            Run(writer, parameters);
            return writer.ToString();
        }

        public virtual void Run(TextWriter writer, IReadOnlyDictionary<string, object?> parameters)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (parameters == null)
                throw new ArgumentNullException(nameof(parameters));

            var p = CreateMethodParameters(writer, parameters);
            InvokeRunMethod(p);
        }

        protected virtual object?[] CreateMethodParameters(TextWriter writer, IReadOnlyDictionary<string, object?> parameters)
        {
            if (!IsBuilt)
            {
                Build();
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

        protected virtual void InvokeRunMethod(object?[] p)
        {
            if (!IsBuilt)
            {
                Build();
            }

            _runMethodInfo!.Invoke(null, p);
        }
    }
}
