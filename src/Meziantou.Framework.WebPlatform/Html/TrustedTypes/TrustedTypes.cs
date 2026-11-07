using System.Diagnostics;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The names of the trusted types (https://w3c.github.io/trusted-types/dist/spec/#trusted-types).</summary>
internal enum TrustedTypeName
{
    TrustedHtml,
    TrustedScript,
    TrustedScriptUrl,
}

internal static class TrustedTypeNameExtensions
{
    /// <summary>Returns the IDL name of the trusted type (<c>"TrustedHTML"</c>, <c>"TrustedScript"</c> or <c>"TrustedScriptURL"</c>).</summary>
    public static string GetIdlName(this TrustedTypeName trustedTypeName) => trustedTypeName switch
    {
        TrustedTypeName.TrustedHtml => "TrustedHTML",
        TrustedTypeName.TrustedScript => "TrustedScript",
        TrustedTypeName.TrustedScriptUrl => "TrustedScriptURL",
        _ => throw new ArgumentOutOfRangeException(nameof(trustedTypeName)),
    };
}

/// <summary>
/// The <c>TrustedType</c> typedef (https://w3c.github.io/trusted-types/dist/spec/#typedefdef-trustedtype): a
/// <see cref="TrustedHtml"/>, a <see cref="TrustedScript"/> or a <see cref="TrustedScriptUrl"/>. The trusted types are
/// immutable wrappers around a string, created by the policies of a <see cref="TrustedTypePolicyFactory"/>.
/// </summary>
/// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#typedefdef-trustedtype"/>
[IdlInterface("TrustedType", "https://w3c.github.io/trusted-types/dist/spec/#typedefdef-trustedtype")]
public abstract class TrustedType
{
    private protected TrustedType(string data)
    {
        Data = data;
    }

    /// <summary>The associated data (https://w3c.github.io/trusted-types/dist/spec/#trustedhtml-data).</summary>
    internal string Data { get; }

    internal abstract TrustedTypeName Name { get; }

    /// <summary>The stringification behavior: returns the associated data.</summary>
    public override string ToString() => Data;

    internal static TrustedType Create(TrustedTypeName name, string data) => name switch
    {
        TrustedTypeName.TrustedHtml => new TrustedHtml(data),
        TrustedTypeName.TrustedScript => new TrustedScript(data),
        _ => new TrustedScriptUrl(data),
    };
}

/// <summary>https://w3c.github.io/trusted-types/dist/spec/#trusted-html</summary>
/// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#trustedhtml"/>
[IdlInterface("TrustedHTML", "https://w3c.github.io/trusted-types/dist/spec/#trustedhtml")]
public sealed class TrustedHtml : TrustedType
{
    internal TrustedHtml(string data)
        : base(data)
    {
    }

    internal override TrustedTypeName Name => TrustedTypeName.TrustedHtml;

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#dom-trustedhtml-tojson</summary>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#dom-trustedhtml-tojson"/>
    [IdlMember("toJSON", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedhtml-tojson")]
    public string ToJson() => Data;
}

/// <summary>https://w3c.github.io/trusted-types/dist/spec/#trusted-script</summary>
/// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#trustedscript"/>
[IdlInterface("TrustedScript", "https://w3c.github.io/trusted-types/dist/spec/#trustedscript")]
public sealed class TrustedScript : TrustedType
{
    internal TrustedScript(string data)
        : base(data)
    {
    }

    internal override TrustedTypeName Name => TrustedTypeName.TrustedScript;

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#dom-trustedscript-tojson</summary>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#dom-trustedscript-tojson"/>
    [IdlMember("toJSON", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedscript-tojson")]
    public string ToJson() => Data;
}

/// <summary>https://w3c.github.io/trusted-types/dist/spec/#trustedscripturl</summary>
/// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#trustedscripturl"/>
[IdlInterface("TrustedScriptURL", "https://w3c.github.io/trusted-types/dist/spec/#trustedscripturl")]
public sealed class TrustedScriptUrl : TrustedType
{
    internal TrustedScriptUrl(string data)
        : base(data)
    {
    }

    internal override TrustedTypeName Name => TrustedTypeName.TrustedScriptUrl;

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#dom-trustedscripturl-tojson</summary>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#dom-trustedscripturl-tojson"/>
    [IdlMember("toJSON", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedscripturl-tojson")]
    public string ToJson() => Data;
}

/// <summary>
/// https://w3c.github.io/trusted-types/dist/spec/#trusted-html-parser-options: the options that a policy vetted for the HTML
/// parsing sinks (e.g. <see cref="Element.SetHtmlUnsafe(string, SetHtmlUnsafeOptionsOrTrustedHtmlParserOptions?)"/>), created by
/// <see cref="TrustedTypePolicy.CreateParserOptions"/>. They satisfy the Trusted Types enforcement of these sinks without a
/// <see cref="TrustedHtml"/> value. The object is immutable and opaque: it has no member.
/// </summary>
/// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#trustedhtmlparseroptions"/>
[IdlInterface("TrustedHTMLParserOptions", "https://w3c.github.io/trusted-types/dist/spec/#trustedhtmlparseroptions")]
public sealed class TrustedHtmlParserOptions
{
    internal TrustedHtmlParserOptions(SanitizerConfiguration? sanitizerConfiguration, bool runScripts)
    {
        SanitizerConfiguration = sanitizerConfiguration;
        RunScripts = runScripts;
    }

    /// <summary>The sanitizer configuration (https://w3c.github.io/trusted-types/dist/spec/#trustedhtmlparseroptions-sanitizer-configuration), or null.</summary>
    internal SanitizerConfiguration? SanitizerConfiguration { get; }

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#trustedhtmlparseroptions-runscripts</summary>
    internal bool RunScripts { get; }
}

/// <summary>https://w3c.github.io/trusted-types/dist/spec/#callbackdef-createhtmlcallback</summary>
/// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#callbackdef-createhtmlcallback"/>
[IdlInterface("CreateHTMLCallback", "https://w3c.github.io/trusted-types/dist/spec/#callbackdef-createhtmlcallback")]
public delegate string? CreateHtmlCallback(string input, params object?[] arguments);

/// <summary>https://w3c.github.io/trusted-types/dist/spec/#callbackdef-createscriptcallback</summary>
/// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#callbackdef-createscriptcallback"/>
[IdlInterface("CreateScriptCallback", "https://w3c.github.io/trusted-types/dist/spec/#callbackdef-createscriptcallback")]
public delegate string? CreateScriptCallback(string input, params object?[] arguments);

/// <summary>https://w3c.github.io/trusted-types/dist/spec/#callbackdef-createscripturlcallback</summary>
/// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#callbackdef-createscripturlcallback"/>
[IdlInterface("CreateScriptURLCallback", "https://w3c.github.io/trusted-types/dist/spec/#callbackdef-createscripturlcallback")]
public delegate string? CreateScriptUrlCallback(string input, params object?[] arguments);

/// <summary>
/// https://w3c.github.io/trusted-types/dist/spec/#callbackdef-createparseroptionscallback: returns the options to use, or
/// null to reject the operation. The options received by the callback have a <see cref="SetHtmlUnsafeOptions.Sanitizer"/>
/// only when the options given to the sink have one (a new <see cref="Sanitizer"/>).
/// </summary>
/// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#callbackdef-createparseroptionscallback"/>
[IdlInterface("CreateParserOptionsCallback", "https://w3c.github.io/trusted-types/dist/spec/#callbackdef-createparseroptionscallback")]
public delegate SetHtmlUnsafeOptions? CreateParserOptionsCallback(SetHtmlUnsafeOptions options, params object?[] arguments);

/// <summary>https://w3c.github.io/trusted-types/dist/spec/#dictdef-trustedtypepolicyoptions</summary>
/// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#dictdef-trustedtypepolicyoptions"/>
[IdlInterface("TrustedTypePolicyOptions", "https://w3c.github.io/trusted-types/dist/spec/#dictdef-trustedtypepolicyoptions")]
public sealed class TrustedTypePolicyOptions
{
    /// <summary>Gets the callback used to create <c>TrustedHTML</c> values.</summary>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyoptions-createhtml"/>
    [IdlMember("createHTML", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyoptions-createhtml")]
    public CreateHtmlCallback? CreateHtml { get; init; }

    /// <summary>Gets the callback used to create <c>TrustedScript</c> values.</summary>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyoptions-createscript"/>
    [IdlMember("createScript", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyoptions-createscript")]
    public CreateScriptCallback? CreateScript { get; init; }

    /// <summary>Gets the callback used to create <c>TrustedScriptURL</c> values.</summary>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyoptions-createscripturl"/>
    [IdlMember("createScriptURL", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyoptions-createscripturl")]
    public CreateScriptUrlCallback? CreateScriptUrl { get; init; }

    /// <summary>Gets the callback used to create <c>TrustedHTMLParserOptions</c> values.</summary>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyoptions-createparseroptions"/>
    [IdlMember("createParserOptions", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicyoptions-createparseroptions")]
    public CreateParserOptionsCallback? CreateParserOptions { get; init; }
}

/// <summary>
/// https://w3c.github.io/trusted-types/dist/spec/#trusted-type-policy: creates trusted types by applying the rules of the
/// callbacks given to <see cref="TrustedTypePolicyFactory.CreatePolicy"/>.
/// </summary>
/// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#trustedtypepolicy"/>
[IdlInterface("TrustedTypePolicy", "https://w3c.github.io/trusted-types/dist/spec/#trustedtypepolicy")]
public sealed class TrustedTypePolicy
{
    // https://w3c.github.io/trusted-types/dist/spec/#trustedtypepolicy-options
    private readonly CreateHtmlCallback? _createHtml;
    private readonly CreateScriptCallback? _createScript;
    private readonly CreateScriptUrlCallback? _createScriptUrl;
    private readonly CreateParserOptionsCallback? _createParserOptions;

    internal TrustedTypePolicy(string name, TrustedTypePolicyOptions? options)
    {
        Name = name;
        _createHtml = options?.CreateHtml;
        _createScript = options?.CreateScript;
        _createScriptUrl = options?.CreateScriptUrl;
        _createParserOptions = options?.CreateParserOptions;
    }

    /// <summary>Whether the policy has a <c>createParserOptions</c> callback.</summary>
    internal bool HasCreateParserOptions => _createParserOptions is not null;

    /// <summary>Gets the name of the policy.</summary>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicy-name"/>
    [IdlMember("name", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicy-name")]
    public string Name { get; }

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicy-createhtml</summary>
    /// <exception cref="TypeErrorException">The policy has no <c>createHTML</c> callback.</exception>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicy-createhtml"/>
    [IdlMember("createHTML", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicy-createhtml")]
    public TrustedHtml CreateHtml(string input, params object?[] arguments) => (TrustedHtml)CreateTrustedType(TrustedTypeName.TrustedHtml, input, arguments);

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicy-createscript</summary>
    /// <exception cref="TypeErrorException">The policy has no <c>createScript</c> callback.</exception>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicy-createscript"/>
    [IdlMember("createScript", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicy-createscript")]
    public TrustedScript CreateScript(string input, params object?[] arguments) => (TrustedScript)CreateTrustedType(TrustedTypeName.TrustedScript, input, arguments);

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicy-createscripturl</summary>
    /// <exception cref="TypeErrorException">The policy has no <c>createScriptURL</c> callback.</exception>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicy-createscripturl"/>
    [IdlMember("createScriptURL", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicy-createscripturl")]
    public TrustedScriptUrl CreateScriptUrl(string input, params object?[] arguments) => (TrustedScriptUrl)CreateTrustedType(TrustedTypeName.TrustedScriptUrl, input, arguments);

    /// <summary>
    /// https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicy-createparseroptions: the
    /// <see cref="TrustedHtmlParserOptions"/> created from the options returned by the <c>createParserOptions</c> callback.
    /// </summary>
    /// <exception cref="TypeErrorException">The policy has no <c>createParserOptions</c> callback, or the callback returned null.</exception>
    /// <seealso href="https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicy-createparseroptions"/>
    [IdlMember("createParserOptions", "https://w3c.github.io/trusted-types/dist/spec/#dom-trustedtypepolicy-createparseroptions")]
    public TrustedHtmlParserOptions CreateParserOptions(SetHtmlUnsafeOptions? options = null, params object?[] arguments)
    {
        // https://w3c.github.io/trusted-types/dist/spec/#create-a-trusted-type-algorithm with "TrustedHTMLParserOptions"
        if (_createParserOptions is null)
            throw new TypeErrorException($"The policy '{Name}' does not implement createParserOptions");

        var policyValue = InvokeCreateParserOptions(options?.RunScripts ?? false, options?.Sanitizer, arguments ?? [])
            ?? throw new TypeErrorException($"The createParserOptions callback of the policy '{Name}' returned null");

        return TrustedTypesAlgorithms.CreateParserOptions(policyValue);
    }

    /// <summary>
    /// Invokes the <c>createParserOptions</c> callback (https://w3c.github.io/trusted-types/dist/spec/#get-trusted-type-policy-value-algorithm)
    /// with the parser options callback argument created from the <c>runScripts</c> and <c>sanitizer</c> of the options.
    /// </summary>
    internal SetHtmlUnsafeOptions? InvokeCreateParserOptions(bool runScripts, SanitizerOrSanitizerConfigOrSanitizerPresets? sanitizer, object?[] arguments)
    {
        Debug.Assert(_createParserOptions is not null);
        return _createParserOptions(TrustedTypesAlgorithms.CreateParserOptionsCallbackArgument(runScripts, sanitizer), arguments);
    }

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#create-a-trusted-type-algorithm</summary>
    private TrustedType CreateTrustedType(TrustedTypeName trustedTypeName, string input, object?[]? arguments)
    {
        ArgumentNullException.ThrowIfNull(input);
        var policyValue = GetPolicyValue(trustedTypeName, input, arguments ?? [], throwIfMissing: true);
        return TrustedType.Create(trustedTypeName, policyValue ?? "");
    }

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#get-trusted-type-policy-value-algorithm</summary>
    internal string? GetPolicyValue(TrustedTypeName trustedTypeName, string value, object?[] arguments, bool throwIfMissing)
    {
        Delegate? function = trustedTypeName switch
        {
            TrustedTypeName.TrustedHtml => _createHtml,
            TrustedTypeName.TrustedScript => _createScript,
            _ => _createScriptUrl,
        };

        if (function is null)
        {
            if (throwIfMissing)
                throw new TypeErrorException($"The policy '{Name}' does not implement {GetFunctionName(trustedTypeName)}");

            return null;
        }

        return trustedTypeName switch
        {
            TrustedTypeName.TrustedHtml => _createHtml!(value, arguments),
            TrustedTypeName.TrustedScript => _createScript!(value, arguments),
            _ => _createScriptUrl!(value, arguments),
        };
    }

    private static string GetFunctionName(TrustedTypeName trustedTypeName) => trustedTypeName switch
    {
        TrustedTypeName.TrustedHtml => "createHTML",
        TrustedTypeName.TrustedScript => "createScript",
        _ => "createScriptURL",
    };
}
