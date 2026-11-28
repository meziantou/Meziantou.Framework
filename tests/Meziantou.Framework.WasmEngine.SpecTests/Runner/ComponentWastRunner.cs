using System.Text;
using Meziantou.Framework.WasmEngine.Components;
using Meziantou.Framework.WasmEngine.Text;

namespace Meziantou.Framework.WasmEngine.SpecTests.Runner;

/// <summary>
/// Runs a script of the tests of the component model (https://github.com/WebAssembly/component-model/tree/main/test): the
/// components, the invocations and the assertions. Each failed command is reported with its line, so that the
/// expectations list the known failures.
/// </summary>
internal sealed class ComponentWastRunner : IDisposable
{
    private readonly string _text;
    private readonly WasmRuntime _runtime;
    private readonly List<WasmStore> _stores = [];
    private readonly Dictionary<string, WasmComponent> _definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WasmComponentInstance> _instances = new(StringComparer.Ordinal);
    private WasmComponentInstance? _current;
    private long _deadline = long.MaxValue;

    public ComponentWastRunner(string text, WastMode mode)
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
    }

    public List<SpecFailure> Failures { get; } = [];

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

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
        }
        catch (FormatException ex)
        {
            Failures.Add(new SpecFailure(0, "script: " + ex.Message));
            return;
        }

        foreach (var command in commands)
        {
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

    /// <summary>The number of commands of a script, for the summary.</summary>
    public static int CountCommands(string text) => SExpressionReader.Parse(text).Count;

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
            case "component":
            {
                if (items.Count > 1 && items[1].IsKeyword("instance"))
                {
                    var index = 2;
                    string? instanceName = null;
                    if (index < items.Count && items[index] is { IsList: false, Kind: TokenKind.Id } instanceId)
                    {
                        instanceName = instanceId.Text;
                        index++;
                    }

                    var definitionName = items[index].Text;
                    var instance = Instantiate(_definitions[definitionName]);
                    _current = instance;
                    if (instanceName is not null)
                    {
                        _instances[instanceName] = instance;
                    }

                    return;
                }

                var component = Compile(command);
                if (items.Count > 1 && items[1].IsKeyword("definition"))
                {
                    if (items.Count > 2 && items[2] is { IsList: false, Kind: TokenKind.Id } id)
                    {
                        _definitions[id.Text] = component;
                    }

                    return;
                }

                _current = null;
                var created = Instantiate(component);
                _current = created;
                if (items.Count > 1 && items[1] is { IsList: false, Kind: TokenKind.Id } name)
                {
                    _instances[name.Text] = created;
                }

                return;
            }

            case "invoke":
                RunAction(command);
                return;

            case "assert_return":
            {
                var result = RunAction(items[1]);
                if (items.Count == 2)
                {
                    if (result is not null)
                        throw new InvalidOperationException("expected no result, got " + result);

                    return;
                }

                var expected = ParseValue(items[2]);
                if (result is not { } actual || !actual.Equals(expected))
                    throw new InvalidOperationException("expected " + expected + ", got " + (result?.ToString() ?? "no result"));

                return;
            }

            case "assert_trap":
            {
                if (items[1].Head == "component")
                {
                    ExpectFailure<WasmTrapException>(() => Instantiate(Compile(items[1])), items[^1].Text);
                    return;
                }

                ExpectFailure<WasmTrapException>(() => RunAction(items[1]), items[^1].Text);
                return;
            }

            case "assert_invalid" or "assert_malformed":
            {
                byte[] bytes;
                try
                {
                    bytes = ComponentWatParser.ParseComponent(items[1]);
                }
                catch (FormatException) when (command.Head == "assert_malformed")
                {
                    // A malformed text (the components of assert_invalid are well-formed: only their validation fails)
                    return;
                }

                ExpectFailure<WasmCompileException>(() => _runtime.CompileComponent(bytes), items[^1].Text);
                return;
            }

            case "assert_unlinkable":
            {
                var component = Compile(items[1]);
                ExpectFailure<WasmLinkException>(() => Instantiate(component), items[^1].Text);
                return;
            }

            default:
                throw new NotSupportedException("unsupported command " + command.Head);
        }
    }

    private WasmComponent Compile(SNode node) => _runtime.CompileComponent(ComponentWatParser.ParseComponent(node));

    private WasmComponentInstance Instantiate(WasmComponent component)
    {
        // Each component has its own store: a trap poisons the instances of its store
        var store = _runtime.CreateStore(new WasmStoreOptions
        {
            CheckInterrupt = () =>
            {
                if (System.Diagnostics.Stopwatch.GetTimestamp() > Volatile.Read(ref _deadline))
                    throw new TimeoutException("The command runs for too long");
            },
        });
        _stores.Add(store);
        return store.InstantiateComponent(component);
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
        catch (Exception ex) when (ex is WasmException or TimeoutException or NotSupportedException or FormatException or InvalidOperationException)
        {
            throw new InvalidOperationException("expected " + typeof(TException).Name + " (" + message + "), got " + ex.GetType().Name + ": " + ex.Message, ex);
        }

        throw new InvalidOperationException("expected " + typeof(TException).Name + " (" + message + "), but it succeeded");
    }

    private ComponentValue? RunAction(SNode action)
    {
        var items = action.Items!;
        var index = 1;
        WasmComponentInstance instance;
        if (items[index] is { IsList: false, Kind: TokenKind.Id } id)
        {
            instance = _instances[id.Text];
            index++;
        }
        else
        {
            instance = _current ?? throw new InvalidOperationException("no component");
        }

        var name = items[index++].Text;
        var function = instance.GetFunction(name) ?? throw new InvalidOperationException("unknown export " + name);
        var arguments = items.Skip(index).Select(ParseValue).ToArray();
        return function.Call(arguments);
    }

    private static ComponentValue ParseValue(SNode node)
    {
        var items = node.Items ?? throw new FormatException("expected a value, got " + node);
        var text = items.Count > 1 ? items[1].Text : "";
        return node.Head switch
        {
            "bool.const" => ComponentValue.FromBool(text switch
            {
                "true" => true,
                "false" => false,
                _ => throw new FormatException(text),
            }),
            "s8.const" => ComponentValue.FromS8((sbyte)ParseInteger(text, 8)),
            "u8.const" => ComponentValue.FromU8((byte)ParseInteger(text, 8)),
            "s16.const" => ComponentValue.FromS16((short)ParseInteger(text, 16)),
            "u16.const" => ComponentValue.FromU16((ushort)ParseInteger(text, 16)),
            "s32.const" => ComponentValue.FromS32((int)ParseInteger(text, 32)),
            "u32.const" => ComponentValue.FromU32((uint)ParseInteger(text, 32)),
            "s64.const" => ComponentValue.FromS64(ParseInteger(text, 64)),
            "u64.const" => ComponentValue.FromU64((ulong)ParseInteger(text, 64)),
            "f32.const" => ComponentValue.FromF32(BitConverter.Int32BitsToSingle(WatNumbers.TryParseF32(text, out var f32) ? (int)f32 : throw new FormatException(text))),
            "f64.const" => ComponentValue.FromF64(BitConverter.Int64BitsToDouble(WatNumbers.TryParseF64(text, out var f64) ? (long)f64 : throw new FormatException(text))),
            "char.const" => ComponentValue.FromChar(Rune.GetRuneAt(text, 0)),
            "str.const" => ComponentValue.FromString(text),
            "list.const" => ComponentValue.FromList(items.Skip(1).Select(ParseValue)),
            "tuple.const" => ComponentValue.FromTuple([.. items.Skip(1).Select(ParseValue)]),
            "record.const" => ComponentValue.FromRecord([.. items.Skip(1).Select(ParseField)]),
            "variant.const" => ComponentValue.FromCase(text, items.Count > 2 ? ParseValue(items[2]) : null),
            "enum.const" => ComponentValue.FromEnum(text),
            "option.none" => ComponentValue.NoneValue,
            "option.some" => ComponentValue.Some(ParseValue(items[1])),
            "result.ok" => ComponentValue.Ok(items.Count > 1 ? ParseValue(items[1]) : null),
            "result.err" => ComponentValue.Error(items.Count > 1 ? ParseValue(items[1]) : null),
            "flags.const" => ComponentValue.FromFlags(items.Skip(1).Select(item => item.Text)),
            _ => throw new NotSupportedException("unsupported value " + node.Head),
        };
    }

    private static ComponentValue ParseField(SNode node)
    {
        // (field "name" type.const value...): the value is the rest of the list
        var items = node.Items ?? throw new FormatException("expected a field, got " + node);
        if (!node.IsListOf("field"))
            throw new FormatException("expected a field, got " + node);

        return ParseValue(SNode.List(node.Position, items[2..]));
    }

    private static long ParseInteger(string text, int bits) => WatNumbers.TryParseInteger(text, bits, out var value) ? value : throw new FormatException(text);
}
