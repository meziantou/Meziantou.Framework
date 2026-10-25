using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The console namespace of the Console Standard (https://console.spec.whatwg.org/). It is implemented by the bindings
// rather than by a .NET class: the formatter works on the JavaScript values (symbols, %String%, %parseInt%...), which the
// conversion of the IDL type any does not keep. The messages are given to the host (BrowsingContextOptions.ConsoleMessage).
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    // The limits of the formatting of the objects (the large arrays are not enumerated)
    private const int ConsoleMaxItems = 100;
    private const int ConsoleMaxDepth = 2;

    // https://console.spec.whatwg.org/#counting, https://console.spec.whatwg.org/#timing, https://console.spec.whatwg.org/#grouping:
    // the count map, the timer table and the group stack of the namespace object of the realm
    private readonly Dictionary<string, int> _consoleCountMap = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _consoleTimerTable = new(StringComparer.Ordinal);
    private int _consoleGroupDepth;

    private TValue? _consoleString;
    private TValue? _consoleParseInt;
    private TValue? _consoleParseFloat;
    private TValue? _consoleIsView;
    private TValue? _consoleObjectToString;
    private TValue? _consoleError;

    private void DefineConsoleGlobal()
    {
        // https://webidl.spec.whatwg.org/#es-namespaces: the property of the global object is writable and configurable
        Adapter.DefineLazyDataProperty(Global, Key("console"), () => Adapter.RunInRealm(EngineRealm, CreateConsoleNamespace), writable: true, enumerable: false, configurable: true);
    }

    /// <summary>Creates the console namespace object (https://console.spec.whatwg.org/#console-namespace).</summary>
    private TValue CreateConsoleNamespace()
    {
        // For historical reasons, the [[Prototype]] of the namespace object is an empty object created as if by
        // ObjectCreate(%ObjectPrototype%), instead of %ObjectPrototype%
        var console = Adapter.CreateObject(Adapter.CreateObject(ObjectPrototype));
        Adapter.DefineOwnProperty(console, Adapter.GetWellKnownSymbol(JsWellKnownSymbol.ToStringTag), JsPropertyDescriptor<TValue>.Data(Key("console"), writable: false, enumerable: false, configurable: true));

        // The intrinsics used by the formatter, got before the scripts can replace them
        _consoleString = EvaluateBuiltIn("String");
        _consoleParseInt = EvaluateBuiltIn("parseInt");
        _consoleParseFloat = EvaluateBuiltIn("parseFloat");
        _consoleIsView = EvaluateBuiltIn("ArrayBuffer.isView");
        _consoleObjectToString = EvaluateBuiltIn("Object.prototype.toString");
        _consoleError = EvaluateBuiltIn("Error");

        // The operations of the namespace: every argument is optional, so their length is 0
        void Define(string name, Action<ReadOnlySpan<TValue>> steps)
        {
            var function = Adapter.CreateFunction(name, 0, (_, args) =>
            {
                steps(args);
                return Adapter.Undefined;
            });
            Adapter.DefineOwnProperty(console, Key(name), JsPropertyDescriptor<TValue>.Data(function, writable: true, enumerable: true, configurable: true));
        }

        // Logging (https://console.spec.whatwg.org/#logging)
        Define("assert", ConsoleAssert);
        Define("clear", _ => ConsoleClear());
        Define("debug", args => ConsoleLogger("debug", ConsoleMessageLevel.Debug, args));
        Define("error", args => ConsoleLogger("error", ConsoleMessageLevel.Error, args));
        Define("info", args => ConsoleLogger("info", ConsoleMessageLevel.Info, args));
        Define("log", args => ConsoleLogger("log", ConsoleMessageLevel.Info, args));
        Define("table", ConsoleTable);
        Define("trace", ConsoleTrace);
        Define("warn", args => ConsoleLogger("warn", ConsoleMessageLevel.Warning, args));
        Define("dir", ConsoleDir);
        Define("dirxml", ConsoleDirxml);

        // Counting (https://console.spec.whatwg.org/#counting)
        Define("count", args => ConsoleCount(ConsoleLabel(args, 0)));
        Define("countReset", args => ConsoleCountReset(ConsoleLabel(args, 0)));

        // Grouping (https://console.spec.whatwg.org/#grouping)
        Define("group", args => ConsoleGroup("group", args));
        Define("groupCollapsed", args => ConsoleGroup("groupCollapsed", args));
        Define("groupEnd", _ => ConsoleGroupEnd());

        // Timing (https://console.spec.whatwg.org/#timing)
        Define("time", args => ConsoleTime(ConsoleLabel(args, 0)));
        Define("timeLog", args => ConsoleTimeLog(ConsoleLabel(args, 0), args.Length > 1 ? args[1..] : []));
        Define("timeEnd", args => ConsoleTimeEnd(ConsoleLabel(args, 0)));
        return console;
    }

    /// <summary>Converts an <c>optional DOMString label = "default"</c> argument.</summary>
    private string ConsoleLabel(ReadOnlySpan<TValue> args, int index) => IsMissing(args, index) ? "default" : ToDomString(args[index]);

    // https://console.spec.whatwg.org/#assert
    private void ConsoleAssert(ReadOnlySpan<TValue> args)
    {
        // 1. If condition is true, return.
        if (!IsMissing(args, 0) && Adapter.ToBoolean(args[0]))
            return;

        // 2. Let message be a string without any formatting specifiers indicating generically an assertion failure (such as "Assertion failed").
        const string Message = "Assertion failed";
        var data = new List<TValue>(args.Length > 1 ? args[1..].ToArray() : []);
        if (data.Count == 0)
        {
            // 3. If data is empty, append message to data.
            data.Add(Adapter.FromString(Message));
        }
        else if (!Adapter.IsString(data[0]))
        {
            // 4.2. If first is not a String, then prepend message to data.
            data.Insert(0, Adapter.FromString(Message));
        }
        else
        {
            // 4.3. Otherwise: let concat be the concatenation of message, U+003A (:), U+0020 SPACE, and first, and set data[0] to concat.
            data[0] = Adapter.FromString(Message + ": " + Adapter.AsString(data[0]));
        }

        // 5. Perform Logger("assert", data).
        ConsoleLogger("assert", ConsoleMessageLevel.Error, data.ToArray());
    }

    // https://console.spec.whatwg.org/#clear
    private void ConsoleClear()
    {
        // 1. Empty the appropriate group stack.
        _consoleGroupDepth = 0;

        // 2. If possible for the environment, clear the console: the host clears its console when it gets the message
        ConsolePrint("clear", ConsoleMessageLevel.Info, "", []);
    }

    // https://console.spec.whatwg.org/#table
    private void ConsoleTable(ReadOnlySpan<TValue> args)
    {
        // The arguments are converted as their IDL types (optional any tabularData, optional sequence<DOMString> properties)
        if (!IsMissing(args, 1))
        {
            ToSequence(args[1], static (r, v) => r.ToDomString(v));
        }

        // The tabular data is printed as a log: there is no table in a text console
        var tabularData = args.Length > 0 ? args[0] : Adapter.Undefined;
        ConsoleLogger("table", ConsoleMessageLevel.Info, [tabularData]);
    }

    // https://console.spec.whatwg.org/#trace
    private void ConsoleTrace(ReadOnlySpan<TValue> data)
    {
        if (!GlobalScope.HasConsoleMessageHandler)
            return;

        // 1. Let trace be some implementation-defined, potentially-interactive representation of the callstack from where this function was called.
        string? stackTrace = null;
        try
        {
            var error = Adapter.Construct(_consoleError!, [], _consoleError!);
            var stack = Adapter.Get(error, Key("stack"));
            if (Adapter.IsString(stack))
            {
                // The first line of the stack is the message of the error
                stackTrace = Adapter.AsString(stack);
                if (stackTrace.StartsWith("Error\n", StringComparison.Ordinal))
                {
                    stackTrace = stackTrace["Error\n".Length..];
                }
            }
        }
        catch (Exception ex) when (Adapter.TryGetThrownValue(ex, out _))
        {
        }

        // 2. Optionally, let formattedData be the result of Formatter(data), and incorporate formattedData as a label for trace.
        var formattedData = data.Length == 0 ? ["console.trace"] : ConsoleFormatter(data);

        // 3. Perform Printer("trace", « trace »).
        ConsolePrint("trace", ConsoleMessageLevel.Info, ConsoleJoin(formattedData), data, stackTrace);
    }

    // https://console.spec.whatwg.org/#dir
    private void ConsoleDir(ReadOnlySpan<TValue> args)
    {
        // The options argument is converted as the IDL type object?
        if (args.Length > 1 && !Adapter.IsUndefined(args[1]) && !Adapter.IsNull(args[1]) && !Adapter.IsObject(args[1]))
            throw CreateTypeError("The options are not an object");

        if (!GlobalScope.HasConsoleMessageHandler)
            return;

        // 1. Let object be item with generic JavaScript object formatting applied.
        // 2. Perform Printer("dir", « object », options).
        var item = args.Length > 0 ? args[0] : Adapter.Undefined;
        ConsolePrint("dir", ConsoleMessageLevel.Info, ConsoleInspect(item, nested: false), [item]);
    }

    // https://console.spec.whatwg.org/#dirxml
    private void ConsoleDirxml(ReadOnlySpan<TValue> data)
    {
        if (!GlobalScope.HasConsoleMessageHandler)
            return;

        // 1. Let finalList be a new list, initially empty.
        // 2. For each item of data: let converted be a DOM tree representation of item if possible; otherwise let converted
        //    be item with optimally useful formatting applied (the values are formatted when they are printed).
        var finalList = new TValue[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            finalList[i] = Adapter.TryGetPlatformObject(data[i], out var target) && target is Element element ? Adapter.FromString(element.OuterHtml) : data[i];
        }

        // 3. Perform Logger("dirxml", finalList).
        ConsoleLogger("dirxml", ConsoleMessageLevel.Info, finalList);
    }

    // https://console.spec.whatwg.org/#count
    private void ConsoleCount(string label)
    {
        // 1. Let map be the associated count map.
        // 2. If map[label] exists, set map[label] to map[label] + 1. 3. Otherwise, set map[label] to 1.
        var count = _consoleCountMap.GetValueOrDefault(label) + 1;
        _consoleCountMap[label] = count;

        // 4. Let concat be the concatenation of label, U+003A (:), U+0020 SPACE, and ToString(map[label]).
        // 5. Perform Logger("count", « concat »).
        ConsoleLogger("count", ConsoleMessageLevel.Info, [Adapter.FromString(label + ": " + count.ToString(CultureInfo.InvariantCulture))]);
    }

    // https://console.spec.whatwg.org/#countreset
    private void ConsoleCountReset(string label)
    {
        // 2. If map[label] exists, set map[label] to 0.
        if (_consoleCountMap.ContainsKey(label))
        {
            _consoleCountMap[label] = 0;
            return;
        }

        // 3. Otherwise: let message be a string without any formatting specifiers indicating generically that the given label
        //    does not have an associated count, and perform Logger("countReset", « message »).
        ConsoleLogger("countReset", ConsoleMessageLevel.Warning, [Adapter.FromString($"Count for '{label}' does not exist")]);
    }

    // https://console.spec.whatwg.org/#group, https://console.spec.whatwg.org/#groupcollapsed
    private void ConsoleGroup(string method, ReadOnlySpan<TValue> data)
    {
        if (GlobalScope.HasConsoleMessageHandler)
        {
            // 1. Let group be a new group.
            // 2. If data is not empty, let groupLabel be the result of Formatter(data). Otherwise, let groupLabel be an
            //    implementation-chosen label representing a group.
            var groupLabel = data.Length == 0 ? "console." + method : ConsoleJoin(ConsoleFormatter(data));

            // 5. Perform Printer("group", « group »).
            ConsolePrint(method, ConsoleMessageLevel.Info, groupLabel, data);
        }

        // 6. Push group onto the appropriate group stack.
        _consoleGroupDepth++;
    }

    // https://console.spec.whatwg.org/#groupend
    private void ConsoleGroupEnd()
    {
        // 1. Pop the last group from the group stack.
        if (_consoleGroupDepth > 0)
        {
            _consoleGroupDepth--;
        }
    }

    // https://console.spec.whatwg.org/#time
    private void ConsoleTime(string label)
    {
        // 1. If the associated timer table contains an entry with key label, return, optionally reporting a warning to the
        //    console indicating that a timer with label label has already been started.
        if (_consoleTimerTable.ContainsKey(label))
        {
            ConsoleLogger("time", ConsoleMessageLevel.Warning, [Adapter.FromString($"Timer '{label}' already exists")]);
            return;
        }

        // 2. Otherwise, set the value of the entry with key label in the associated timer table to the current time.
        _consoleTimerTable[label] = GlobalScope.EventLoop.TimeProvider.GetTimestamp();
    }

    // https://console.spec.whatwg.org/#timelog
    private void ConsoleTimeLog(string label, ReadOnlySpan<TValue> data)
    {
        // 1. Let timerTable be the associated timer table.
        // 2. Let startTime be timerTable[label].
        if (!_consoleTimerTable.TryGetValue(label, out var startTime))
        {
            ConsoleLogger("timeLog", ConsoleMessageLevel.Warning, [Adapter.FromString($"Timer '{label}' does not exist")]);
            return;
        }

        if (!GlobalScope.HasConsoleMessageHandler)
            return;

        // 3. Let duration be a string representing the difference between the current time and startTime, in an implementation-defined format.
        // 4. Let concat be the concatenation of label, U+003A (:), U+0020 SPACE, and duration.
        // 5. Prepend concat to data.
        // 6. Perform Printer("timeLog", data).
        var concat = Adapter.FromString(label + ": " + ConsoleDuration(startTime));
        var printed = new TValue[data.Length + 1];
        printed[0] = concat;
        data.CopyTo(printed.AsSpan(1));
        ConsolePrint("timeLog", ConsoleMessageLevel.Info, ConsoleJoin(printed), printed);
    }

    // https://console.spec.whatwg.org/#timeend
    private void ConsoleTimeEnd(string label)
    {
        // 1. Let timerTable be the associated timer table.
        // 2. Let startTime be timerTable[label].
        // 3. Remove timerTable[label].
        if (!_consoleTimerTable.Remove(label, out var startTime))
        {
            ConsoleLogger("timeEnd", ConsoleMessageLevel.Warning, [Adapter.FromString($"Timer '{label}' does not exist")]);
            return;
        }

        if (!GlobalScope.HasConsoleMessageHandler)
            return;

        // 4. Let duration be a string representing the difference between the current time and startTime, in an implementation-defined format.
        // 5. Let concat be the concatenation of label, U+003A (:), U+0020 SPACE, and duration.
        // 6. Perform Printer("timeEnd", « concat »).
        var concat = Adapter.FromString(label + ": " + ConsoleDuration(startTime));
        ConsolePrint("timeEnd", ConsoleMessageLevel.Info, Adapter.AsString(concat), [concat]);
    }

    private string ConsoleDuration(long startTime)
        => GlobalScope.EventLoop.TimeProvider.GetElapsedTime(startTime).TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture) + " ms";

    // https://console.spec.whatwg.org/#logger
    private void ConsoleLogger(string logLevel, ConsoleMessageLevel level, ReadOnlySpan<TValue> args)
    {
        // The messages are not formatted when the host does not get them (the formatting is implementation-defined)
        if (!GlobalScope.HasConsoleMessageHandler)
            return;

        // 1. If args is empty, return.
        if (args.Length == 0)
            return;

        // 2. Let first be args[0]. 3. Let rest be all elements following first in args.
        // 4. If rest is empty, perform Printer(logLevel, « first ») and return.
        // 5. Otherwise, perform Printer(logLevel, Formatter(args)).
        var printed = args.Length == 1 ? [ConsoleInspect(args[0], nested: false)] : ConsoleFormatter(args);
        ConsolePrint(logLevel, level, ConsoleJoin(printed), args);
    }

    /// <summary>
    /// The formatter (https://console.spec.whatwg.org/#formatter): replaces the format specifiers of the first argument, if it
    /// is a string, by the next arguments. Returns the formatted strings of the arguments to print.
    /// </summary>
    private List<string> ConsoleFormatter(ReadOnlySpan<TValue> args)
    {
        var result = new List<string>(args.Length);
        var index = 0;
        if (Adapter.IsString(args[0]))
        {
            // 1. Let target be the first element of args. 2. Let current be the second element of args.
            // 3. Find the first possible format specifier specifier, from the left to the right in target, and process it.
            // The search continues after the converted value, as in the browsers (the converted value is not formatted again).
            var target = Adapter.AsString(args[0]);
            var builder = new StringBuilder(target.Length);
            index = 1;
            var position = 0;
            while (position < target.Length)
            {
                var percent = target.IndexOf('%', position, StringComparison.Ordinal);
                if (percent < 0 || percent + 1 >= target.Length || index >= args.Length)
                    break;

                builder.Append(target, position, percent - position);
                var specifier = target[percent + 1];
                switch (specifier)
                {
                    case 's':
                        // Let converted be the result of Call(%String%, undefined, « current »).
                        builder.Append(Adapter.AsString(Adapter.Call(_consoleString!, Adapter.Undefined, [args[index++]])));
                        break;
                    case 'd' or 'i':
                        // If Type(current) is Symbol, let converted be NaN. Otherwise, let converted be the result of Call(%parseInt%, undefined, « current, 10 »).
                        builder.Append(Adapter.IsSymbol(args[index]) ? "NaN" : Adapter.ToString(Adapter.Call(_consoleParseInt!, Adapter.Undefined, [args[index], Adapter.FromNumber(10)])));
                        index++;
                        break;
                    case 'f':
                        // If Type(current) is Symbol, let converted be NaN. Otherwise, let converted be the result of Call(%parseFloat%, undefined, « current »).
                        builder.Append(Adapter.IsSymbol(args[index]) ? "NaN" : Adapter.ToString(Adapter.Call(_consoleParseFloat!, Adapter.Undefined, [args[index]])));
                        index++;
                        break;
                    case 'o' or 'O':
                        // Optionally, let converted be current with optimally useful (or generic JavaScript object) formatting applied.
                        builder.Append(ConsoleInspect(args[index++], nested: true));
                        break;
                    case 'c':
                        // Optionally, let converted be current with CSS styles applied: a text console has no styles
                        index++;
                        break;
                    default:
                        // Not a format specifier
                        builder.Append('%').Append(specifier);
                        break;
                }

                position = percent + 2;
            }

            builder.Append(target, position, target.Length - position);
            result.Add(builder.ToString());
        }

        // The remaining arguments are printed after the target
        for (; index < args.Length; index++)
        {
            result.Add(ConsoleInspect(args[index], nested: false));
        }

        return result;
    }

    private static string ConsoleJoin(List<string> values) => string.Join(' ', values);

    private string ConsoleJoin(ReadOnlySpan<TValue> values)
    {
        var result = new List<string>(values.Length);
        foreach (var value in values)
        {
            result.Add(ConsoleInspect(value, nested: false));
        }

        return ConsoleJoin(result);
    }

    // https://console.spec.whatwg.org/#printer
    private void ConsolePrint(string logLevel, ConsoleMessageLevel level, string text, ReadOnlySpan<TValue> args, string? stackTrace = null)
    {
        if (!GlobalScope.HasConsoleMessageHandler)
            return;

        var arguments = new object?[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            arguments[i] = ToAny(args[i]);
        }

        GlobalScope.ReportConsoleMessage(new ConsoleMessage(logLevel, level, text)
        {
            Arguments = arguments,
            GroupDepth = _consoleGroupDepth,
            StackTrace = stackTrace,
            Location = GlobalScope.ScriptEngine?.GetCurrentScriptLocation(),
        });
    }

    /// <summary>
    /// Formats a value as a browser console does (the "optimally useful formatting" of the Console Standard): the strings are
    /// printed as they are, except in an object or with <paramref name="nested"/>. The arrays and the objects are bounded
    /// (their first items only), and the getters are not called.
    /// </summary>
    private string ConsoleInspect(TValue value, bool nested)
    {
        var builder = new StringBuilder();
        try
        {
            ConsoleInspect(builder, value, nested, depth: 0, new HashSet<TValue>(ReferenceEqualityComparer.Instance));
        }
        catch (Exception ex) when (Adapter.TryGetThrownValue(ex, out _))
        {
            // A proxy trap threw
            builder.Append("[object]");
        }

        return builder.ToString();
    }

    private void ConsoleInspect(StringBuilder builder, TValue value, bool nested, int depth, HashSet<TValue> visited)
    {
        if (Adapter.IsString(value))
        {
            var s = Adapter.AsString(value);
            if (nested)
            {
                builder.Append('\'').Append(s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)).Append('\'');
            }
            else
            {
                builder.Append(s);
            }

            return;
        }

        if (Adapter.IsSymbol(value))
        {
            builder.Append(Adapter.AsString(Adapter.Call(_consoleString!, Adapter.Undefined, [value])));
            return;
        }

        if (Adapter.IsBigInt(value))
        {
            builder.Append(Adapter.ToString(value)).Append('n');
            return;
        }

        if (!Adapter.IsObject(value))
        {
            // undefined, null, booleans and numbers
            builder.Append(Adapter.ToString(value));
            return;
        }

        if (Adapter.IsCallable(value))
        {
            var name = ConsoleGetOwnDataProperty(value, "name");
            builder.Append("ƒ ").Append(name is not null && Adapter.IsString(name) ? Adapter.AsString(name) : "").Append("()");
            return;
        }

        var isPlatformObject = Adapter.TryGetPlatformObject(value, out var target);
        if (target is Node node)
        {
            builder.Append(node switch
            {
                Element element => ConsoleDescribeElement(element),
                Text text => "\"" + text.Data + "\"",
                _ => node.NodeName,
            });
            return;
        }

        // The other platform objects and the global objects are printed as their class: their properties are not
        // enumerated (the properties of a global object would create every interface object)
        if (isPlatformObject || Agent.GetRealmByGlobal(value) is not null)
        {
            var platformClassString = Adapter.AsString(Adapter.Call(_consoleObjectToString!, value, []));
            builder.Append(platformClassString.AsSpan(8, platformClassString.Length - 9));
            return;
        }

        if (!visited.Add(value))
        {
            builder.Append("[Circular]");
            return;
        }

        try
        {
            var classString = Adapter.AsString(Adapter.Call(_consoleObjectToString!, value, []));
            if (classString == "[object Error]")
            {
                builder.Append(Adapter.ToString(value));
                return;
            }

            // The arrays and the typed arrays: the items are got by index, without enumerating the keys
            var isArray = Adapter.IsArray(value);
            if (isArray || Adapter.ToBoolean(Adapter.Call(_consoleIsView!, Adapter.Undefined, [value])))
            {
                var lengthValue = isArray ? Adapter.Get(value, Key("length")) : ConsoleGetTypedArrayLength(value);
                if (lengthValue is not null && Adapter.IsNumber(lengthValue))
                {
                    var length = (long)Adapter.AsNumber(lengthValue);
                    if (!isArray)
                    {
                        builder.Append(classString.AsSpan(8, classString.Length - 9)).Append('(').Append(length.ToString(CultureInfo.InvariantCulture)).Append(") ");
                    }

                    if (depth >= ConsoleMaxDepth)
                    {
                        builder.Append("[…]");
                        return;
                    }

                    builder.Append('[');
                    var count = Math.Min(length, ConsoleMaxItems);
                    for (var i = 0; i < count; i++)
                    {
                        if (i > 0)
                        {
                            builder.Append(", ");
                        }

                        ConsoleInspect(builder, Adapter.Get(value, Key(i.ToString(CultureInfo.InvariantCulture))), nested: true, depth + 1, visited);
                    }

                    if (length > count)
                    {
                        builder.Append(", … ").Append((length - count).ToString(CultureInfo.InvariantCulture)).Append(" more");
                    }

                    builder.Append(']');
                    return;
                }
            }

            // The other objects: their own enumerable properties
            if (classString != "[object Object]")
            {
                builder.Append(classString.AsSpan(8, classString.Length - 9)).Append(' ');
            }

            if (depth >= ConsoleMaxDepth)
            {
                builder.Append("{…}");
                return;
            }

            builder.Append('{');
            var written = 0;
            foreach (var key in Adapter.OwnKeys(value))
            {
                if (!Adapter.GetOwnProperty(value, key, out var descriptor) || !descriptor.Enumerable)
                    continue;

                if (written == ConsoleMaxItems)
                {
                    builder.Append(", …");
                    break;
                }

                builder.Append(written == 0 ? "" : ", ");
                if (Adapter.IsSymbol(key))
                {
                    builder.Append('[').Append(Adapter.AsString(Adapter.Call(_consoleString!, Adapter.Undefined, [key]))).Append(']');
                }
                else
                {
                    builder.Append(Adapter.AsString(key));
                }

                builder.Append(": ");
                if (descriptor.Value is { } propertyValue && !descriptor.IsAccessorDescriptor)
                {
                    ConsoleInspect(builder, propertyValue, nested: true, depth + 1, visited);
                }
                else
                {
                    builder.Append("[Getter/Setter]");
                }

                written++;
            }

            builder.Append('}');
        }
        finally
        {
            visited.Remove(value);
        }
    }

    /// <summary>Returns the length of a typed array, without calling a getter defined by a script.</summary>
    private TValue? ConsoleGetTypedArrayLength(TValue value)
    {
        // %TypedArray%.prototype.length is an accessor of the intrinsic prototype
        var lengthValue = Adapter.Get(value, Key("length"));
        return Adapter.IsNumber(lengthValue) ? lengthValue : null;
    }

    private TValue? ConsoleGetOwnDataProperty(TValue value, string name)
        => Adapter.GetOwnProperty(value, Key(name), out var descriptor) && !descriptor.IsAccessorDescriptor ? descriptor.Value : null;

    private static string ConsoleDescribeElement(Element element)
    {
        // e.g. div#id.class1.class2
        var builder = new StringBuilder(element.LocalName);
        if (element.Id.Length > 0)
        {
            builder.Append('#').Append(element.Id);
        }

        foreach (var className in element.ClassList)
        {
            builder.Append('.').Append(className);
        }

        return builder.ToString();
    }
}
