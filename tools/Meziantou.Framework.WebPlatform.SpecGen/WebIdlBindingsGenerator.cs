using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.WebPlatform;
using Meziantou.Framework.WebPlatform.Dom;
using SpecGen.Idl;

namespace SpecGen;

/// <summary>
/// Generates the engine-agnostic WebIDL bindings of src/Meziantou.Framework.WebPlatform/Scripting/WebIdl (Generated/*.g.cs):
/// for each interface implemented by the library, the interface object, the prototype and the members, as strongly-typed
/// code over <c>IJsAdapter&lt;TValue&gt;</c>. The mapping between the IDL (webref) and the .NET members annotated with
/// [IdlMember] is the one of <see cref="BindingModel"/>; the generated code uses no reflection.
/// </summary>
internal sealed partial class WebIdlBindingsGenerator
{
    private const string Realm = "global::Meziantou.Framework.WebPlatform.Scripting.WebIdlRealm<TAdapter, TValue>";
    private const string Constraints = "where TAdapter : struct, global::Meziantou.Framework.WebPlatform.Scripting.IJsAdapter<TValue> where TValue : class";

    private readonly GeneratorContext _context;
    private readonly BindingModel _model;
    private readonly IdlModel _idl;
    private readonly SortedDictionary<string, string> _helpers = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> _enumConverters = [];
    private readonly Dictionary<Type, string> _dictionaryConverters = [];
    private readonly Dictionary<Type, string> _dictionaryToJs = [];
    private readonly Dictionary<Type, string> _delegateConverters = [];
    private readonly Dictionary<Type, string> _unionConverters = [];
    private readonly Dictionary<string, string> _unionNaturals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unionNaturalKeys = new(StringComparer.Ordinal);
    private readonly HashSet<Type> _taskResultTypes = [];
    private readonly List<string> _errors = [];

    private WebIdlBindingsGenerator(GeneratorContext context, BindingModel model)
    {
        _context = context;
        _model = model;
        _idl = model.Idl;
    }

    public static void Generate(GeneratorContext context)
    {
        var idl = IdlModel.Load(context.SpecData("webref", "ed", "idlparsed"));
        var generator = new WebIdlBindingsGenerator(context, new BindingModel(idl));
        generator.Run();
    }

    private string OutputDirectory => Path.Combine(_context.Root, "src", "Meziantou.Framework.WebPlatform", "Scripting", "WebIdl", "Generated");

    private void Run()
    {
        var interfaces = _model.Interfaces.Values.Where(i => i.ClrType is not null).OrderBy(i => i.Name, StringComparer.Ordinal).ToList();
        CheckCoverage(interfaces);
        CheckExperimentalApis();
        CheckCssStyleProperties();
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var model in interfaces)
        {
            files[model.Name + ".g.cs"] = GenerateInterface(model);
        }

        files["WebIdlBindings.g.cs"] = GenerateRegistry(interfaces);
        files["WebIdlBindings.Helpers.g.cs"] = GenerateHelpers();

        if (_errors.Count > 0)
            throw new InvalidOperationException("The bindings cannot be generated:\n" + string.Join("\n", _errors.Distinct()));

        foreach (var (name, content) in files)
        {
            _context.WriteFile(Path.Combine(OutputDirectory, name), content);
        }

        // The files of the interfaces that no longer exist are removed
        if (Directory.Exists(OutputDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(OutputDirectory, "*.g.cs"))
            {
                if (!files.ContainsKey(Path.GetFileName(file)))
                {
                    _context.DeleteFile(file);
                }
            }
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Type names
    // ----------------------------------------------------------------------------------------------------------------

    internal static string TypeName(Type type)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return TypeName(underlying) + "?";

        if (type == typeof(void))
            return "void";

        if (type == typeof(object))
            return "object";

        if (type == typeof(string))
            return "string";

        if (type == typeof(bool))
            return "bool";

        if (type == typeof(int))
            return "int";

        if (type == typeof(uint))
            return "uint";

        if (type == typeof(short))
            return "short";

        if (type == typeof(ushort))
            return "ushort";

        if (type == typeof(long))
            return "long";

        if (type == typeof(ulong))
            return "ulong";

        if (type == typeof(byte))
            return "byte";

        if (type == typeof(sbyte))
            return "sbyte";

        if (type == typeof(float))
            return "float";

        if (type == typeof(double))
            return "double";

        if (type.IsArray)
            return TypeName(type.GetElementType()!) + "[]";

        var name = type.IsGenericType ? type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)] : type.Name;
        var prefix = type.IsNested ? TypeName(type.DeclaringType!) + "." : "global::" + type.Namespace + ".";
        if (type.IsGenericType)
            return prefix + name + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">";

        return prefix + name;
    }

    private static string Literal(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when c < 0x20 || c > 0x7E => "\\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }

    private static string Literal(double value)
    {
        if (double.IsPositiveInfinity(value))
            return "double.PositiveInfinity";

        if (double.IsNegativeInfinity(value))
            return "double.NegativeInfinity";

        if (double.IsNaN(value))
            return "double.NaN";

        return value.ToString("R", CultureInfo.InvariantCulture) + "d";
    }

    private static bool IsNumeric(Type type) => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) || type == typeof(int)
        || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double);

    private static string SafeName(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name)
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        return builder.ToString();
    }

    private static string SafeName(Type type) => SafeName(TypeName(type).Replace("global::", "", StringComparison.Ordinal).Replace("Meziantou.Framework.WebPlatform.", "", StringComparison.Ordinal));

    private void Error(string message) => _errors.Add(message);

    // ----------------------------------------------------------------------------------------------------------------
    // IDL types
    // ----------------------------------------------------------------------------------------------------------------

    private IdlDefinition? Definition(IdlType type) => type.Name is null ? null : _idl.Get(type.Name);

    private bool IsDictionaryType(IdlType type) => Definition(type)?.Kind == IdlDefinitionKind.Dictionary;

    private IEnumerable<IdlType> FlattenUnion(IdlType type)
    {
        type = _idl.ResolveTypedefs(type);
        if (!type.Union)
        {
            yield return type;
            yield break;
        }

        foreach (var subtype in type.Subtypes)
        {
            foreach (var flattened in FlattenUnion(subtype))
            {
                yield return flattened;
            }
        }
    }

    private static bool IsNumericIdlType(IdlType type) => type.Name is "byte" or "octet" or "short" or "unsigned short" or "long" or "unsigned long" or "long long" or "unsigned long long" or "float" or "unrestricted float" or "double" or "unrestricted double";

    private bool IsStringIdlType(IdlType type) => type.Name is "DOMString" or "USVString" or "ByteString" or "CSSOMString" || Definition(type)?.Kind == IdlDefinitionKind.Enum;

    private static bool HasExtAttr(IdlType type, IReadOnlyList<IdlExtendedAttribute>? extAttrs, string name) => type.HasExtAttr(name) || (extAttrs?.Any(a => a.Name == name) ?? false);

    /// <summary>Returns the .NET type of an IDL interface type (including WindowProxy), or null.</summary>
    private Type? GetInterfaceClrType(IdlType type)
    {
        if (type.Name == "WindowProxy")
            return typeof(Window);

        return Definition(type) is { Kind: IdlDefinitionKind.Interface } definition ? GetClrType(definition.Name) : null;
    }

    private bool IsInterfaceIdlType(IdlType type) => type.Name == "WindowProxy" || Definition(type)?.Kind == IdlDefinitionKind.Interface;

    /// <summary>Returns the .NET type of an IDL interface, dictionary, enumeration or callback, or null.</summary>
    private Type? GetClrType(string name)
    {
        if (_model.Interfaces.TryGetValue(name, out var model) && model.ClrType is not null)
            return model.ClrType;

        if (_model.ClrTypes.TryGetValue(name, out var type))
            return type;

        if (_idl.Get(name) is { } definition && !ReferenceEquals(definition.Name, name) && definition.Name != name)
            return GetClrType(definition.Name);

        return null;
    }
}
