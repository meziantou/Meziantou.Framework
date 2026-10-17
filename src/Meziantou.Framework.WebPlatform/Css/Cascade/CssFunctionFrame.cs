using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The evaluation of a custom function (https://drafts.csswg.org/css-mixins-1/#evaluate-a-custom-function): the function body
/// is applied to a "hypothetical element", child of the calling context, whose custom properties are the parameters and the
/// local variables of the function, and the value of its result descriptor is the value of the &lt;dashed-function&gt;.
/// </summary>
/// <remarks>
/// <para>
/// The arguments are resolved first, in the order of the parameters: a default value sees the previous parameters (a later
/// parameter is the guaranteed-invalid value). Then the local variables shadow the parameters, which shadow the variables of
/// the calling context: the local variables and the parameters of the calling functions, then the custom properties of the
/// element (dynamic scoping). The values are resolved lazily, but all the local variables are resolved, so that a cycle
/// through an unused local variable is detected.
/// </para>
/// <para>
/// The cycles (https://drafts.csswg.org/css-values-5/#cyclic-substitution-contexts) are detected with a stack of the
/// functions and the local variables being evaluated, shared by the nested calls: when a function or a local variable is
/// referenced while it is being evaluated, everything from it to the top of the stack is cyclic, and its value is the
/// guaranteed-invalid value. A cycle through a custom property of the element is detected by <see cref="ComputedStyle"/>.
/// </para>
/// <para>
/// The attr()-taint (https://drafts.csswg.org/css-values-5/#attr-security) of the arguments, the default values and the local
/// variables is tracked, so that a var() referencing them (in the function or in a called function) is tainted, as is the
/// result of the function when its value is.
/// </para>
/// <para>
/// A frame also evaluates the application of a mixin (https://drafts.csswg.org/css-mixins-1/#mixin-args) for an element: its
/// parameters and its local variables (the custom properties of the mixin body) shadow the variables of the applying context
/// (the mixin whose @result contains the @apply rule, then the element). A missing argument without default value is the
/// guaranteed-invalid value. The custom functions called by the values of the mixin do not see its variables.
/// </para>
/// </remarks>
internal sealed class CssFunctionFrame
{
    private readonly CssFunctionRule? _function;
    private readonly CssMixinApplication? _mixin;
    private readonly List<CssFunctionParameterDefinition> _parameterDefinitions;
    private readonly List<CssRule> _body;
    private readonly string? _baseUrl;
    private readonly CssFunctionFrame? _caller;
    private readonly SubstitutionContext _callerContext;
    private readonly SubstitutionContext _context;
    private readonly List<(CssFunctionFrame Frame, string? Local)> _stack;
    private readonly Dictionary<string, string?> _parameters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _locals = new(StringComparer.Ordinal);
    private HashSet<string>? _taintedParameters;
    private HashSet<string>? _taintedLocals;
    private Dictionary<string, string>? _declarations;
    private HashSet<string>? _cyclicLocals;

    /// <summary>
    /// The identifier of the invocation (its "hypothetical element"), which scopes its element-scoped random() functions: the
    /// invocation of the property or of the calling function, and the number of the invocation in it.
    /// </summary>
    private readonly string _invocation;
    private int _invocations;

    /// <summary>During the resolution of the arguments, the number of resolved parameters.</summary>
    private int _resolvedParameters;
    private bool _cyclic;

    private CssFunctionFrame(CssFunctionRule function, Node definitionScope, SubstitutionContext callerContext)
    {
        _function = function;
        _parameterDefinitions = function.Parameters;
        _body = function.ChildRules;
        _baseUrl = function.ParentStyleSheet?.BaseUrl;

        // The variables of a calling mixin are not visible in the function
        _caller = callerContext.Frame is { IsMixin: false } caller ? caller : null;
        _callerContext = callerContext;
        _stack = callerContext.Frame?._stack ?? [];
        _context = new SubstitutionContext(callerContext.Style, definitionScope, this, callerContext.AttrElement);
        _invocation = callerContext.Frame is { } frame ? frame._invocation + "-" + (++frame._invocations).ToString(System.Globalization.CultureInfo.InvariantCulture) : CssSubstitution.NextInvocation();
    }

    private CssFunctionFrame(CssMixinApplication mixin, SubstitutionContext callerContext)
    {
        _mixin = mixin;
        _parameterDefinitions = mixin.Mixin.Parameters;
        _body = mixin.Mixin.ChildRules;
        _baseUrl = mixin.Mixin.ParentStyleSheet?.BaseUrl;
        _caller = callerContext.Frame;
        _callerContext = callerContext;
        _stack = callerContext.Frame?._stack ?? [];
        _context = new SubstitutionContext(callerContext.Style, mixin.DefinitionScope, this, callerContext.AttrElement);
        _invocation = callerContext.Frame is { } frame ? frame._invocation + "-" + (++frame._invocations).ToString(System.Globalization.CultureInfo.InvariantCulture) : CssSubstitution.NextInvocation();
    }

    /// <summary>The name of the custom function or of the mixin.</summary>
    private string Name => _function?.Name ?? _mixin!.Mixin.Name;

    /// <summary>Whether the frame is the application of a mixin.</summary>
    public bool IsMixin => _mixin is not null;

    /// <summary>
    /// Creates the frame of the application of a mixin for an element, in the calling context: the element (the
    /// <see cref="SubstitutionContext.TreeScope"/> is the tree of the @apply rule), or the frame of the mixin whose @result
    /// contains the @apply rule. <see cref="InitializeMixin"/> resolves its arguments and collects its local variables.
    /// </summary>
    public static CssFunctionFrame CreateMixin(CssMixinApplication mixin, SubstitutionContext callerContext) => new(mixin, callerContext);

    /// <summary>
    /// Resolves the arguments of the application of a mixin in the calling context (https://drafts.csswg.org/css-mixins-1/#mixin-args),
    /// then collects the local variables of the mixin body.
    /// </summary>
    public void InitializeMixin()
    {
        var arguments = new List<(string? Value, bool Tainted)>();
        foreach (var argument in _mixin!.Arguments)
        {
            var value = CssSubstitution.Substitute(argument, _callerContext, out var taintedRanges);
            arguments.Add((value, taintedRanges is not null));
        }

        ResolveParameters(arguments, missingArgumentIsInvalid: false);
        _declarations = new(StringComparer.Ordinal);
        CollectDeclarations(_body, _declarations);
    }

    private ComputedStyle Style => _context.Style;

    /// <summary>The context of the substitutions in the function body.</summary>
    public SubstitutionContext Context => _context;

    /// <summary>
    /// Evaluates a custom function (https://drafts.csswg.org/css-mixins-1/#evaluate-a-custom-function) with the substituted
    /// arguments (null for the guaranteed-invalid value); returns null for the guaranteed-invalid value.
    /// </summary>
    /// <param name="function">The function.</param>
    /// <param name="definitionScope">The root of the tree of the @function rule.</param>
    /// <param name="arguments">The arguments, substituted in the calling context, and whether they are attr()-tainted.</param>
    /// <param name="callerContext">The calling context: the element, or the custom function that contains the &lt;dashed-function&gt;.</param>
    /// <param name="tainted">Whether the result is attr()-tainted.</param>
    public static string? Evaluate(CssFunctionRule function, Node definitionScope, List<(string? Value, bool Tainted)> arguments, SubstitutionContext callerContext, out bool tainted)
    {
        tainted = false;
        // The function is already being evaluated: it is in a cycle
        for (var caller = callerContext.Frame; caller is not null; caller = caller._caller)
        {
            if (caller._function == function)
            {
                MarkCyclic(caller._stack, caller._stack.IndexOf((caller, null)));
                return null;
            }
        }

        if (arguments.Count > function.Parameters.Count)
            return null;

        var frame = new CssFunctionFrame(function, definitionScope, callerContext);
        frame._stack.Add((frame, null));
        try
        {
            var result = frame.Evaluate(arguments, out tainted);
            return frame._cyclic ? null : result;
        }
        finally
        {
            frame._stack.RemoveAt(frame._stack.Count - 1);
        }
    }

    /// <summary>Marks the functions and the local variables of the stack, from the index, as cyclic.</summary>
    private static void MarkCyclic(List<(CssFunctionFrame Frame, string? Local)> stack, int index)
    {
        if (index < 0)
            return;

        for (var i = index; i < stack.Count; i++)
        {
            var (frame, local) = stack[i];
            if (local is null)
            {
                frame._cyclic = true;
            }
            else
            {
                (frame._cyclicLocals ??= new(StringComparer.Ordinal)).Add(local);
            }
        }
    }

    private string? Evaluate(List<(string? Value, bool Tainted)> arguments, out bool resultTainted)
    {
        resultTainted = false;
        if (!ResolveParameters(arguments, missingArgumentIsInvalid: true))
            return null;

        // The body: the declarations of the rules whose conditions are true; the last declaration of a name wins
        _declarations = new(StringComparer.Ordinal);
        CollectDeclarations(_body, _declarations);
        foreach (var name in _declarations.Keys)
        {
            if (CssPropertyRegistry.IsCustomProperty(name))
            {
                GetLocal(name);
            }
        }

        if (!_declarations.TryGetValue("result", out var resultText))
            return null;

        // The CSS-wide keywords of the result are left unresolved: the <dashed-function> is replaced by the keyword. An empty
        // result is a space, like an empty custom property.
        if (resultText.Length == 0)
        {
            resultText = " ";
        }

        var result = CssSubstitution.Substitute(resultText, _context, out var resultTaintedRanges);
        resultTainted = resultTaintedRanges is not null;
        return _function!.ReturnSyntax.IsUniversal ? result : ComputeTyped(_function.ReturnSyntax, result, "result");
    }

    /// <summary>
    /// Resolves the parameters: the argument when it matches the type of the parameter, the default value otherwise. A missing
    /// argument without default value makes the call of a function invalid (as the browsers do); the parameter of a mixin is
    /// then the guaranteed-invalid value. Returns false when the call is invalid.
    /// </summary>
    private bool ResolveParameters(List<(string? Value, bool Tainted)> arguments, bool missingArgumentIsInvalid)
    {
        var parameters = _parameterDefinitions;
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            if (i >= arguments.Count && parameter.DefaultValue is null && missingArgumentIsInvalid)
                return false;

            string? value = null;
            var tainted = false;
            if (i < arguments.Count && arguments[i] is { Value: { } argument } resolved)
            {
                tainted = resolved.Tainted;
                value = ResolveKeyword(parameter.Name, argument, initialValue: null, initialTainted: false, ref tainted);
                value = ComputeTyped(parameter.Type, value, parameter.Name);
            }

            if (value is null && parameter.DefaultValue is { } defaultValue)
            {
                value = CssSubstitution.Substitute(defaultValue, _context, out var taintedRanges);
                tainted = taintedRanges is not null;
                value = ResolveKeyword(parameter.Name, value, initialValue: null, initialTainted: false, ref tainted);
                value = ComputeTyped(parameter.Type, value, parameter.Name);
            }

            if (tainted && value is not null)
            {
                (_taintedParameters ??= new(StringComparer.Ordinal)).Add(parameter.Name);
            }

            _parameters[parameter.Name] = value;
            _resolvedParameters = i + 1;
        }

        return true;
    }

    /// <summary>
    /// Collects the declarations of the function body: the conditional group rules apply when their condition is true
    /// (https://drafts.csswg.org/css-mixins-1/#conditional-rules), and the queries refer to the element of the calling context.
    /// The local variables of a mixin are the custom properties of its body, outside of the @result rules.
    /// </summary>
    private void CollectDeclarations(List<CssRule> rules, Dictionary<string, string> declarations)
    {
        foreach (var rule in rules)
        {
            switch (rule)
            {
                case CssFunctionDeclarations functionDeclarations:
                    foreach (var declaration in functionDeclarations.Style.Declarations)
                    {
                        declarations[declaration.Name] = declaration.Value.Value;
                    }

                    break;

                case CssNestedDeclarations nestedDeclarations when IsMixin:
                    foreach (var declaration in nestedDeclarations.Style.Declarations)
                    {
                        if (!declaration.Important && CssPropertyRegistry.IsCustomProperty(declaration.Name))
                        {
                            declarations[declaration.Name] = declaration.Value.Value;
                        }
                    }

                    break;

                case CssMediaRule mediaRule when MediaQueryEvaluator.Matches(mediaRule.Media, Style.Environment):
                    CollectDeclarations(mediaRule.ChildRules, declarations);
                    break;

                case CssSupportsRule { Matches: true } supportsRule:
                    CollectDeclarations(supportsRule.ChildRules, declarations);
                    break;

                case CssContainerRule containerRule when containerRule.ParsedConditions.Any(condition => ContainerQueries.Evaluate(condition, Style.Engine, Style.Element, Style.PseudoElement) == true):
                    CollectDeclarations(containerRule.ChildRules, declarations);
                    break;
            }
        }
    }

    /// <summary>Returns the value of a variable in the function: a local variable, a parameter, or a variable of the calling context.</summary>
    public string? GetVariable(string name) => GetVariable(name, out _);

    /// <summary>
    /// Returns the value of a variable in the function: a local variable, a parameter, or a variable of the calling context,
    /// and whether it is attr()-tainted.
    /// </summary>
    public string? GetVariable(string name, out bool tainted)
    {
        if (_declarations is null)
        {
            // During the resolution of the arguments, the parameters that are not resolved yet are the guaranteed-invalid value
            var index = _parameterDefinitions.FindIndex(p => p.Name == name);
            if (index >= 0)
            {
                tainted = index < _resolvedParameters && IsTaintedParameter(name);
                return index < _resolvedParameters ? _parameters[name] : null;
            }

            return GetCallerVariable(name, out tainted);
        }

        if (_declarations.ContainsKey(name))
        {
            var local = GetLocal(name);
            tainted = _taintedLocals is not null && _taintedLocals.Contains(name);
            return local;
        }

        if (_parameters.TryGetValue(name, out var parameter))
        {
            tainted = IsTaintedParameter(name);
            return parameter;
        }

        return GetCallerVariable(name, out tainted);
    }

    private bool IsTaintedParameter(string name) => _taintedParameters is not null && _taintedParameters.Contains(name);

    /// <summary>Returns whether a variable is a local variable or a parameter of the function or of a calling function.</summary>
    public bool DeclaresVariable(string name) =>
        (_declarations?.ContainsKey(name) ?? false) || _parameterDefinitions.Exists(p => p.Name == name) || (_caller?.DeclaresVariable(name) ?? false);

    /// <summary>
    /// Evaluates a style feature of an if() test on a variable of the function (https://drafts.csswg.org/css-mixins-1/#resolve-function-styles):
    /// the value is substituted in the function and its CSS-wide keywords are interpreted locally (initial is the default value
    /// of the parameter, inherit the value in the calling context, unset the guaranteed-invalid value; the revert keywords
    /// never match). A &lt;style-feature-boolean&gt; is true when the variable is not the guaranteed-invalid value. The test
    /// uses an attr()-tainted value when the variable or the value is tainted.
    /// </summary>
    public bool EvaluateStyleFeature(string name, string? value, out bool tainted)
    {
        var frame = this;
        while (!(frame._declarations?.ContainsKey(name) ?? false) && !frame._parameterDefinitions.Exists(p => p.Name == name))
        {
            frame = frame._caller!;
        }

        var actual = GetVariable(name, out tainted)?.Trim();
        if (value is null)
            return actual is not null;

        List<(int Start, int End)>? taintedRanges = null;
        var substituted = CssSubstitution.MayContainSubstitution(value) ? CssSubstitution.Substitute(value, _context, out taintedRanges) : value;
        tainted |= taintedRanges is not null;
        if (substituted is null)
            return false;

        var parameter = frame._parameterDefinitions.Find(p => p.Name == name);
        string? expected;
        switch (InfraAscii.ToAsciiLowercase(substituted.Trim()))
        {
            case "initial":
                expected = parameter?.DefaultValue is { } defaultValue ? frame.ComputeTyped(parameter.Type, CssSubstitution.Substitute(defaultValue, frame._context), parameter.Name) : null;
                break;
            case "inherit":
                expected = frame.GetCallerVariable(name);
                break;
            case "unset":
                expected = null;
                break;
            case "revert" or "revert-layer" or "revert-rule":
                return false;
            default:
                expected = parameter is null ? substituted : frame.ComputeTyped(parameter.Type, substituted, parameter.Name);
                break;
        }

        return expected?.Trim() == actual;
    }

    /// <summary>Returns the value of a variable in the calling context (what the hypothetical element inherits).</summary>
    public string? GetCallerVariable(string name) => GetCallerVariable(name, out _);

    /// <summary>Returns the value of a variable in the calling context (what the hypothetical element inherits), and whether it is attr()-tainted.</summary>
    public string? GetCallerVariable(string name, out bool tainted)
    {
        if (_caller is not null)
            return _caller.GetVariable(name, out tainted);

        var value = _callerContext.Style.GetCustomPropertyValue(name);
        tainted = _callerContext.Style.IsCustomPropertyTainted(name);
        return value;
    }

    private string? GetLocal(string name)
    {
        if (_locals.TryGetValue(name, out var result))
            return result;

        var index = _stack.IndexOf((this, name));
        if (index >= 0)
        {
            MarkCyclic(_stack, index);
            return null;
        }

        _stack.Add((this, name));
        bool tainted;
        try
        {
            result = CssSubstitution.Substitute(_declarations![name], _context, out var taintedRanges);
            tainted = taintedRanges is not null;
        }
        finally
        {
            _stack.RemoveAt(_stack.Count - 1);
        }

        if (_cyclicLocals is not null && _cyclicLocals.Contains(name))
        {
            result = null;
        }
        else
        {
            // initial is the value of the parameter (the initial value of its registration); the type of a parameter applies
            // to the local variable of the same name
            var parameterIndex = _parameterDefinitions.FindIndex(p => p.Name == name);
            result = ResolveKeyword(name, result, parameterIndex >= 0 ? _parameters[name] : null, parameterIndex >= 0 && IsTaintedParameter(name), ref tainted);
            if (parameterIndex >= 0)
            {
                result = ComputeTyped(_parameterDefinitions[parameterIndex].Type, result, name);
            }

            if (tainted && result is not null)
            {
                (_taintedLocals ??= new(StringComparer.Ordinal)).Add(name);
            }
        }

        _locals[name] = result;
        return result;
    }

    /// <summary>
    /// Resolves the CSS-wide keywords of a parameter or a local variable (https://drafts.csswg.org/css-mixins-1/#resolve-function-styles):
    /// initial is the initial value of the registration, inherit is the value in the calling context, and the other keywords
    /// are the guaranteed-invalid value. The attr()-taint of a keyword is the one of the value it stands for.
    /// </summary>
    private string? ResolveKeyword(string name, string? value, string? initialValue, bool initialTainted, ref bool tainted)
    {
        if (value is null)
            return null;

        switch (InfraAscii.ToAsciiLowercase(value.Trim()))
        {
            case "initial":
                tainted = initialTainted;
                return initialValue;
            case "inherit":
                return GetCallerVariable(name, out tainted);
            case "unset" or "revert" or "revert-layer" or "revert-rule":
                tainted = false;
                return null;
            default:
                return value;
        }
    }

    /// <summary>
    /// Computes a value with a type (https://drafts.css-houdini.org/css-properties-values-api/#calculation-of-computed-values); returns
    /// null when it does not match the type. The random() functions are evaluated in the function: their caching keys are made
    /// of the name of the function and of the descriptor (result or the name of the parameter), and the element-scoped ones are
    /// scoped to the invocation.
    /// </summary>
    private string? ComputeTyped(CssSyntaxDefinition type, string? value, string descriptor)
    {
        if (value is null || type.IsUniversal)
            return value;

        if (value.Contains("random(", StringComparison.OrdinalIgnoreCase))
        {
            var values = CssParser.ParseListOfComponentValues(value);
            if (CssRandom.AssignCachingKeys(Name + "-" + descriptor, values, _invocation) is var keyed && !ReferenceEquals(keyed, values))
            {
                value = CssSerializer.SerializeValue(keyed, property: null);
            }
        }

        return CustomPropertySyntax.Parse(type.Text)?.Compute(Style, value, _baseUrl);
    }
}
