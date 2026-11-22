using System.Globalization;
using Meziantou.Framework.WasmEngine.Text;

namespace Meziantou.Framework.WasmEngine.SpecTests.Runner;

/// <summary>
/// Runs a script of the official test suite (https://github.com/WebAssembly/spec/tree/main/interpreter#scripts): the
/// modules, the actions and the assertions. Each failed command is reported with its line, so that the expectations
/// list the known failures.
/// </summary>
internal sealed class WastRunner : IDisposable
{
    private readonly string _text;
    private readonly WasmRuntime _runtime;
    private readonly WasmStore _store;
    private readonly Dictionary<string, WasmInstance> _instances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WasmModule> _definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WasmInstance> _registered = new(StringComparer.Ordinal);
    private readonly Dictionary<long, WasmHostReference> _hostReferences = [];
    private WasmInstance? _current;
    private WasmModule? _currentDefinition;
    private long _deadline = long.MaxValue;

    /// <summary>Creates a runner.</summary>
    /// <param name="text">The text of the script.</param>
    /// <param name="mode">How the functions run.</param>
    public WastRunner(string text, WastMode mode)
    {
        _text = text;
        _runtime = new WasmRuntime(new WasmRuntimeOptions
        {
            InterruptionCheckInterval = TimeSpan.FromMilliseconds(50),
            CompilationThreshold = mode switch
            {
                WastMode.Compiled => 0,
                WastMode.Osr => int.MaxValue,
                _ => -1,
            },
            OsrThreshold = mode == WastMode.Osr ? 1 : null,
        });
        _store = _runtime.CreateStore(new WasmStoreOptions
        {
            CheckInterrupt = () =>
            {
                if (System.Diagnostics.Stopwatch.GetTimestamp() > Volatile.Read(ref _deadline))
                    throw new TimeoutException("The command runs for too long");
            },
        });
        _registered["spectest"] = CreateSpecTest();
    }

    public List<SpecFailure> Failures { get; } = [];

    public int CommandCount { get; private set; }

    public void Dispose()
    {
        _store.Dispose();
        _runtime.Dispose();
    }

    private int GetLine(int position)
    {
        var line = 1;
        for (var i = 0; i < position && i < _text.Length; i++)
        {
            if (_text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    public void Run()
    {
        List<SNode> commands;
        try
        {
            commands = SExpressionReader.Parse(_text);

            // A script can be the fields of a module (an inline module)
            if (IsInlineModule(commands))
            {
                commands = SExpressionReader.Parse("(module " + _text + "\n)");
            }
        }
        catch (FormatException ex)
        {
            Failures.Add(new SpecFailure(0, "script: " + ex.Message));
            return;
        }

        foreach (var command in commands)
        {
            CommandCount++;
            Volatile.Write(ref _deadline, System.Diagnostics.Stopwatch.GetTimestamp() + (System.Diagnostics.Stopwatch.Frequency * 20));
            try
            {
                RunCommand(command);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Failures.Add(new SpecFailure(GetLine(command.Position), command.Head + ": " + FirstLine(ex.GetType().Name + ": " + ex.Message)));
            }
        }
    }

    /// <summary>Whether a script is the fields of a module (an inline module), which runs as one module command.</summary>
    public static bool IsInlineModule(List<SNode> commands) => commands.Count > 0 && commands[0].Head is "func" or "memory" or "table" or "global" or "type" or "rec" or "import" or "export" or "elem" or "data" or "start" or "tag";

    private static string FirstLine(string message)
    {
        var index = message.IndexOf('\n', StringComparison.Ordinal);
        return index < 0 ? message : message[..index];
    }

    private void RunCommand(SNode command)
    {
        var items = command.Items ?? throw new InvalidOperationException("unexpected token");
        switch (command.Head)
        {
            case "module":
            {
                var index = 1;
                if (items.Count > 1 && items[1].IsKeyword("definition"))
                {
                    var definition = _runtime.Compile(WatParser.EncodeModule(command));
                    _currentDefinition = definition;
                    if (items.Count > 2 && items[2] is { IsList: false, Kind: TokenKind.Id } id)
                    {
                        _definitions[id.Text] = definition;
                    }

                    return;
                }

                if (items.Count > 1 && items[1].IsKeyword("instance"))
                {
                    index = 2;
                    string? instanceName = null;
                    if (index < items.Count && items[index] is { IsList: false, Kind: TokenKind.Id } instanceId)
                    {
                        instanceName = instanceId.Text;
                        index++;
                    }

                    var definition = index < items.Count && items[index] is { IsList: false, Kind: TokenKind.Id } definitionId ? _definitions[definitionId.Text] : _currentDefinition!;
                    var instance = Instantiate(definition);
                    _current = instance;
                    if (instanceName is not null)
                    {
                        _instances[instanceName] = instance;
                    }

                    return;
                }

                _current = null;
                var module = _runtime.Compile(WatParser.EncodeModule(command));
                var created = Instantiate(module);
                _current = created;
                if (items.Count > index && items[index] is { IsList: false, Kind: TokenKind.Id } name)
                {
                    _instances[name.Text] = created;
                }

                return;
            }

            case "register":
            {
                var name = items[1].Text;
                var instance = items.Count > 2 ? _instances[items[2].Text] : _current ?? throw new InvalidOperationException("no module");
                _registered[name] = instance;
                return;
            }

            case "invoke" or "get":
                RunAction(command);
                return;

            case "assert_return":
            {
                var results = RunAction(items[1]);
                var expected = items.Skip(2).ToList();
                if (results.Length != expected.Count)
                    throw new InvalidOperationException("expected " + expected.Count + " results, got " + results.Length);

                for (var i = 0; i < expected.Count; i++)
                {
                    if (!MatchResult(results[i], expected[i]))
                        throw new InvalidOperationException("result " + i + ": expected " + expected[i] + ", got " + results[i]);
                }

                return;
            }

            case "assert_trap":
            {
                if (items[1].Head == "module")
                {
                    ExpectFailure<WasmTrapException>(() => Instantiate(_runtime.Compile(WatParser.EncodeModule(items[1]))), items[^1].Text);
                    return;
                }

                ExpectFailure<WasmTrapException>(() => RunAction(items[1]), items[^1].Text);
                return;
            }

            case "assert_exhaustion":
            {
                var exception = ExpectFailure<WasmTrapException>(() => RunAction(items[1]), items[^1].Text);
                if (exception.TrapKind != WasmTrapKind.StackOverflow)
                    throw new InvalidOperationException("expected a stack overflow, got " + exception.Message);

                return;
            }

            case "assert_exception":
                ExpectFailure<WasmThrowException>(() => RunAction(items[1]), "exception");
                return;

            case "assert_invalid" or "assert_malformed":
            {
                byte[] bytes;
                try
                {
                    bytes = WatParser.EncodeModule(items[1]);
                }
                catch (FormatException) when (command.Head == "assert_malformed")
                {
                    // A malformed text (the modules of assert_invalid are well-formed: only their validation fails)
                    return;
                }

                ExpectFailure<WasmCompileException>(() => _runtime.Compile(bytes), items[^1].Text);
                return;
            }

            case "assert_unlinkable":
            {
                var module = _runtime.Compile(WatParser.EncodeModule(items[1]));
                ExpectFailure<WasmLinkException>(() => Instantiate(module), items[^1].Text);
                return;
            }

            case "assert_uninstantiable":
            {
                var module = _runtime.Compile(WatParser.EncodeModule(items[1]));
                ExpectFailure<WasmTrapException>(() => Instantiate(module), items[^1].Text);
                return;
            }

            default:
                throw new NotSupportedException("unsupported command " + command.Head);
        }
    }

    private static TException ExpectFailure<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception ex) when (ex is WasmException or TimeoutException or NotSupportedException or FormatException)
        {
            throw new InvalidOperationException("expected " + typeof(TException).Name + " (" + message + "), got " + ex.GetType().Name + ": " + ex.Message, ex);
        }

        throw new InvalidOperationException("expected " + typeof(TException).Name + " (" + message + "), but it succeeded");
    }

    private WasmInstance Instantiate(WasmModule module)
    {
        var imports = new List<WasmExtern>();
        foreach (var import in module.Imports)
        {
            if (!_registered.TryGetValue(import.Module, out var instance))
                throw new WasmLinkException("unknown import module " + import.Module);

            imports.Add(instance.GetExport(import.Name) ?? throw new WasmLinkException("unknown import " + import.Module + "." + import.Name));
        }

        return _store.Instantiate(module, imports);
    }

    private WasmInstance GetInstance(List<SNode> items, ref int index)
    {
        if (items[index] is { IsList: false, Kind: TokenKind.Id } id)
        {
            index++;
            return _instances[id.Text];
        }

        return _current ?? throw new InvalidOperationException("no module");
    }

    private WasmValue[] RunAction(SNode action)
    {
        var items = action.Items!;
        var index = 1;
        var instance = GetInstance(items, ref index);
        var name = items[index++].Text;
        var export = instance.GetExport(name) ?? throw new InvalidOperationException("unknown export " + name);
        if (action.Head == "get")
            return [((WasmGlobal)export).Get()];

        var function = (WasmFunction)export;
        var arguments = items.Skip(index).Select(ParseValue).ToArray();
        return function.Call(arguments);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Values
    // ------------------------------------------------------------------------------------------------------------

    private WasmValue ParseValue(SNode node)
    {
        var items = node.Items!;
        var text = items.Count > 1 ? items[1].Text : "";
        switch (node.Head)
        {
            case "i32.const":
                return WasmValue.FromI32(WatNumbers.TryParseInteger(text, 32, out var i32) ? (int)i32 : throw new FormatException(text));
            case "i64.const":
                return WasmValue.FromI64(WatNumbers.TryParseInteger(text, 64, out var i64) ? i64 : throw new FormatException(text));
            case "f32.const":
                return WasmValue.FromF32Bits(WatNumbers.TryParseF32(text, out var f32) ? (int)f32 : throw new FormatException(text));
            case "f64.const":
                return WasmValue.FromF64Bits(WatNumbers.TryParseF64(text, out var f64) ? (long)f64 : throw new FormatException(text));
            case "v128.const":
            {
                var bytes = ParseV128(items);
                return WasmValue.FromV128(BitConverter.ToInt64(bytes, 0), BitConverter.ToInt64(bytes, 8));
            }

            case "ref.null":
                return WasmValue.Null;
            case "ref.extern" or "ref.host":
                return WasmValue.FromReference(GetHostReference(long.Parse(text, CultureInfo.InvariantCulture)));
            default:
                throw new NotSupportedException("unsupported value " + node);
        }
    }

    private WasmHostReference GetHostReference(long id)
    {
        if (!_hostReferences.TryGetValue(id, out var reference))
        {
            reference = new WasmHostReference(id);
            _hostReferences.Add(id, reference);
        }

        return reference;
    }

    private static byte[] ParseV128(List<SNode> items)
    {
        var shape = items[1].Text;
        var result = new byte[16];
        var (count, size) = shape switch
        {
            "i8x16" => (16, 1),
            "i16x8" => (8, 2),
            "i32x4" or "f32x4" => (4, 4),
            _ => (2, 8),
        };
        for (var i = 0; i < count; i++)
        {
            var text = items[2 + i].Text;
            ulong bits;
            if (shape == "f32x4")
            {
                bits = WatNumbers.TryParseF32(text, out var f32) ? f32 : throw new FormatException(text);
            }
            else if (shape == "f64x2")
            {
                bits = WatNumbers.TryParseF64(text, out var f64) ? f64 : throw new FormatException(text);
            }
            else
            {
                bits = WatNumbers.TryParseInteger(text, size * 8, out var value) ? (ulong)value : throw new FormatException(text);
            }

            for (var b = 0; b < size; b++)
            {
                result[(i * size) + b] = (byte)(bits >> (8 * b));
            }
        }

        return result;
    }

    private static bool MatchResult(WasmValue actual, SNode expected)
    {
        var items = expected.Items!;
        var text = items.Count > 1 ? items[1].Text : "";
        switch (expected.Head)
        {
            case "either":
                return items.Skip(1).Any(alternative => MatchResult(actual, alternative));
            case "i32.const":
                return actual.Kind == WasmValueKind.I32 && WatNumbers.TryParseInteger(text, 32, out var i32) && actual.I32 == (int)i32;
            case "i64.const":
                return actual.Kind == WasmValueKind.I64 && WatNumbers.TryParseInteger(text, 64, out var i64) && actual.I64 == i64;
            case "f32.const":
                return actual.Kind == WasmValueKind.F32 && MatchF32((uint)actual.F32Bits, text);
            case "f64.const":
                return actual.Kind == WasmValueKind.F64 && MatchF64((ulong)actual.F64Bits, text);
            case "v128.const":
            {
                if (actual.Kind != WasmValueKind.V128)
                    return false;

                var bytes = new byte[16];
                BitConverter.TryWriteBytes(bytes, actual.V128Low);
                BitConverter.TryWriteBytes(bytes.AsSpan(8), actual.V128High);
                var shape = items[1].Text;
                if (shape is "f32x4" or "f64x2")
                {
                    var size = shape == "f32x4" ? 4 : 8;
                    for (var i = 0; i < 16 / size; i++)
                    {
                        var lane = items[2 + i].Text;
                        var matches = size == 4 ? MatchF32(BitConverter.ToUInt32(bytes, i * 4), lane) : MatchF64(BitConverter.ToUInt64(bytes, i * 8), lane);
                        if (!matches)
                            return false;
                    }

                    return true;
                }

                return ParseV128(items).AsSpan().SequenceEqual(bytes);
            }

            case "ref.null":
                return actual.Kind == WasmValueKind.Ref && actual.Reference is null;
            case "ref.extern" or "ref.host":
                if (items.Count == 1)
                    return actual.Reference is not null;

                return actual.Reference is WasmHostReference host && host.Value is long value && value == long.Parse(text, CultureInfo.InvariantCulture);
            case "ref.func":
                return actual.Reference is WasmFunction;
            case "ref.struct":
                return actual.Reference is WasmGcObject { IsArray: false };
            case "ref.array":
                return actual.Reference is WasmGcObject { IsArray: true };
            case "ref.i31":
                return actual.Reference is WasmI31Reference;
            case "ref.eq":
                return actual.Reference is WasmGcObject or WasmI31Reference;
            case "ref.any":
                return actual.Reference is not null;
            case "ref.exn":
                return actual.Reference is WasmExceptionReference;
            default:
                throw new NotSupportedException("unsupported result " + expected);
        }
    }

    private static bool MatchF32(uint bits, string text)
    {
        var nan = (bits & 0x7F80_0000) == 0x7F80_0000 && (bits & 0x007F_FFFF) != 0;
        if (text == "nan:canonical")
            return nan && (bits & 0x7FFF_FFFF) == 0x7FC0_0000;

        if (text == "nan:arithmetic")
            return nan && (bits & 0x0040_0000) != 0;

        return WatNumbers.TryParseF32(text, out var expected) && expected == bits;
    }

    private static bool MatchF64(ulong bits, string text)
    {
        var nan = (bits & 0x7FF0_0000_0000_0000) == 0x7FF0_0000_0000_0000 && (bits & 0x000F_FFFF_FFFF_FFFF) != 0;
        if (text == "nan:canonical")
            return nan && (bits & 0x7FFF_FFFF_FFFF_FFFF) == 0x7FF8_0000_0000_0000;

        if (text == "nan:arithmetic")
            return nan && (bits & 0x0008_0000_0000_0000) != 0;

        return WatNumbers.TryParseF64(text, out var expected) && expected == bits;
    }

    // ------------------------------------------------------------------------------------------------------------
    // The spectest module (https://github.com/WebAssembly/spec/blob/main/interpreter/host/spectest.ml)
    // ------------------------------------------------------------------------------------------------------------

    private WasmInstance CreateSpecTest()
    {
        var module = _runtime.Compile(WasmRuntime.WatToWasm("""
            (module
              (import "host" "print" (func $print))
              (import "host" "print_i32" (func $print_i32 (param i32)))
              (import "host" "print_i64" (func $print_i64 (param i64)))
              (import "host" "print_f32" (func $print_f32 (param f32)))
              (import "host" "print_f64" (func $print_f64 (param f64)))
              (import "host" "print_i32_f32" (func $print_i32_f32 (param i32 f32)))
              (import "host" "print_f64_f64" (func $print_f64_f64 (param f64 f64)))
              (export "print" (func $print))
              (export "print_i32" (func $print_i32))
              (export "print_i64" (func $print_i64))
              (export "print_f32" (func $print_f32))
              (export "print_f64" (func $print_f64))
              (export "print_i32_f32" (func $print_i32_f32))
              (export "print_f64_f64" (func $print_f64_f64))
              (global (export "global_i32") i32 (i32.const 666))
              (global (export "global_i64") i64 (i64.const 666))
              (global (export "global_f32") f32 (f32.const 666.6))
              (global (export "global_f64") f64 (f64.const 666.6))
              (table (export "table") 10 20 funcref)
              (table (export "table64") i64 10 20 funcref)
              (memory (export "memory") 1 2)
              (memory (export "shared_memory") 1 2 shared))
            """));

        WasmFunction Print(params WasmValueType[] parameters) => _store.CreateFunction(new WasmFunctionType(parameters, []), (_, _) => { });
        var host = _store.Instantiate(module,
        [
            Print(),
            Print(WasmValueType.I32),
            Print(WasmValueType.I64),
            Print(WasmValueType.F32),
            Print(WasmValueType.F64),
            Print(WasmValueType.I32, WasmValueType.F32),
            Print(WasmValueType.F64, WasmValueType.F64),
        ]);
        return host;
    }
}

/// <summary>A command of a script that failed.</summary>
internal sealed record SpecFailure(int Line, string Message);

/// <summary>How the functions of a script run.</summary>
internal enum WastMode
{
    /// <summary>With the interpreter.</summary>
    Interpreted,

    /// <summary>Compiled to .NET code on their first call.</summary>
    Compiled,

    /// <summary>With the interpreter, and in .NET code from their first loop iteration (OSR).</summary>
    Osr,
}
