using System.Diagnostics;
using System.Runtime.CompilerServices;
using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>
/// The compiled code of a function, a script, a module or eval code. It is immutable and does not reference any realm:
/// it can be shared by the engines of all the threads.
/// </summary>
/// <remarks>
/// The body of a nested function is compiled on its first call (<see cref="EnsureCompiled"/>): until then, the template only
/// has what creating the function object needs (the name, the length, the flags, the source text and the environment of
/// the name), and the members of the code (<see cref="Code"/>...) are not set. The compiled code is the same whichever
/// thread compiles it, and it is published once.
/// </remarks>
internal sealed class FunctionTemplate
{
    private LazyFunction? _lazy;
    private int[] _code = null!;
    private JsValue[] _constants = null!;
    private PropertyKey[] _keys = null!;
    private object[] _objects = null!;
    private int _registerCount;
    private int _maxStack;
    private PropertyCacheEntry?[] _caches = [];
    private ExceptionHandler[] _handlers = null!;
    private int[] _positions = null!;
    private int[] _callSites = [];
    private string[]? _parameterNames;
    private int _completionRegister = -1;

    public required string Name { get; init; }

    /// <summary>Gets <see cref="Name"/> as a string value (shared by the functions created from the template).</summary>
    public JsString NameValue => field ??= JsString.Create(Name);

    /// <summary>Gets the value of the <c>length</c> property (ExpectedArgumentCount).</summary>
    public required int Length { get; init; }

    public required FunctionTemplateFlags Flags { get; init; }

    public int[] Code { get => _code; init => _code = value; }

    public JsValue[] Constants { get => _constants; init => _constants = value; }

    public PropertyKey[] Keys { get => _keys; init => _keys = value; }

    public object[] Objects { get => _objects; init => _objects = value; }

    public int RegisterCount { get => _registerCount; init => _registerCount = value; }

    public int MaxStack { get => _maxStack; init => _maxStack = value; }

    /// <summary>Gets the inline caches of the property accesses of the code (the last operand of these instructions is the index of their cache).</summary>
    public PropertyCacheEntry?[] Caches { get => _caches; init => _caches = value; }

    public ExceptionHandler[] Handlers { get => _handlers; init => _handlers = value; }

    /// <summary>Gets the pairs (pc, source offset) of the instructions, sorted by pc.</summary>
    public int[] Positions { get => _positions; init => _positions = value; }

    /// <summary>Gets the triples (pc, callee start, callee end) of the calls, sorted by pc (the callee of the error messages).</summary>
    public int[] CallSites { get => _callSites; init => _callSites = value; }

    public required SourceText Source { get; init; }

    /// <summary>Gets the start of the source text of the function (Function.prototype.toString).</summary>
    public required int SourceStart { get; init; }

    public required int SourceEnd { get; init; }

    /// <summary>Gets the environment of the name of a named function expression (null when the name is not captured).</summary>
    public ScopeInfo? NameScope { get; init; }

    /// <summary>Gets the names of the parameters of a function with a mapped arguments object (simple parameters).</summary>
    public string[]? ParameterNames { get => _parameterNames; init => _parameterNames = value; }

    /// <summary>Gets the register that holds the completion value of script and eval code (-1 otherwise).</summary>
    public int CompletionRegister { get => _completionRegister; init => _completionRegister = value; }

    /// <summary>Gets the function to compile on the first call (null when the code is compiled).</summary>
    public LazyFunction? Lazy { get => Volatile.Read(ref _lazy); init => _lazy = value; }

    /// <summary>Gets whether the code is compiled (the members of the code are set).</summary>
    public bool IsCompiled => Lazy is null;

    public bool IsStrict => (Flags & FunctionTemplateFlags.Strict) != 0;

    public bool IsArrow => (Flags & FunctionTemplateFlags.Arrow) != 0;

    public bool IsAsync => (Flags & FunctionTemplateFlags.Async) != 0;

    public bool IsGenerator => (Flags & FunctionTemplateFlags.Generator) != 0;

    public bool IsClassConstructor => (Flags & FunctionTemplateFlags.ClassConstructor) != 0;

    public bool IsDerivedConstructor => (Flags & FunctionTemplateFlags.DerivedConstructor) != 0;

    public string GetSourceText() => Source.Text[SourceStart..SourceEnd];

    /// <summary>Compiles the code of the function if it is not compiled yet (before the first call).</summary>
    /// <exception cref="JsException">The code is nested too deeply for the stack of the thread (a RangeError).</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void EnsureCompiled()
    {
        if (Volatile.Read(ref _lazy) is { } lazy)
        {
            Compile(lazy);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Compile(LazyFunction lazy)
    {
        lock (lazy.Lock)
        {
            if (_lazy is null)
                return;

            FunctionTemplate compiled;
            try
            {
                compiled = CodeGenerator.CompileLazyFunction(lazy);
            }
            catch (JsSyntaxErrorException ex) when (ex.Message.StartsWith(CompilerStack.NestedTooDeeplyMessage, StringComparison.Ordinal))
            {
                // The code was compiled with the rest of its source text: only the stack can be too small now (a call
                // nested in deep .NET calls). The function is compiled by a later call.
                ThrowHelper.ThrowRangeError("Maximum call stack size exceeded");
                return;
            }

            Debug.Assert(compiled.Flags == Flags && compiled.Length == Length && compiled.SourceStart == SourceStart && compiled.SourceEnd == SourceEnd, "The compiled function does not match its template");
            _code = compiled._code;
            _constants = compiled._constants;
            _keys = compiled._keys;
            _objects = compiled._objects;
            _registerCount = compiled._registerCount;
            _maxStack = compiled._maxStack;
            _caches = compiled._caches;
            _handlers = compiled._handlers;
            _positions = compiled._positions;
            _callSites = compiled._callSites;
            _parameterNames = compiled._parameterNames;
            _completionRegister = compiled._completionRegister;

            // The members of the code are written before the template is seen as compiled by the other threads
            Volatile.Write(ref _lazy, null);
        }
    }

    /// <summary>Gets the source range of the callee of a call instruction; false when it is unknown.</summary>
    public bool TryGetCallSite(int pc, out int calleeStart, out int calleeEnd)
    {
        var callSites = CallSites;
        var low = 0;
        var high = (callSites.Length / 3) - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            var sitePc = callSites[middle * 3];
            if (sitePc == pc)
            {
                calleeStart = callSites[(middle * 3) + 1];
                calleeEnd = callSites[(middle * 3) + 2];
                return true;
            }

            if (sitePc < pc)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        calleeStart = -1;
        calleeEnd = -1;
        return false;
    }

    /// <summary>Gets the source offset of an instruction.</summary>
    public int GetSourceOffset(int pc)
    {
        var positions = Positions;
        var low = 0;
        var high = (positions.Length / 2) - 1;
        var result = -1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (positions[middle * 2] <= pc)
            {
                result = positions[(middle * 2) + 1];
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return result < 0 ? SourceStart : result;
    }
}
