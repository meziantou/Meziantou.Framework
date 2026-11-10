namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The kind of a <see cref="JsDebugScope"/>.</summary>
public enum JsDebugScopeKind
{
    /// <summary>The variables of a block, a loop, a <c>switch</c> or a class (<c>let</c>, <c>const</c>, <c>class</c>, the functions declared in a block...).</summary>
    Block,

    /// <summary>The parameter of a <c>catch</c> clause.</summary>
    Catch,

    /// <summary>The parameters, the <c>var</c> declarations and the functions of the function of the frame, and its <c>arguments</c> object.</summary>
    Local,

    /// <summary>The variables of a function that encloses the function of the frame, or the name of a named function expression.</summary>
    Closure,

    /// <summary>The object of a <c>with</c> statement (<see cref="JsDebugScope.BindingObject"/>).</summary>
    With,

    /// <summary>The variables declared by strict eval code, and the lexical declarations of eval code.</summary>
    Eval,

    /// <summary>The variables, the functions and the imports of a module.</summary>
    Module,

    /// <summary>The <c>let</c>, <c>const</c> and <c>class</c> declarations of the scripts of the realm.</summary>
    Script,

    /// <summary>The global object (<see cref="JsDebugScope.BindingObject"/>): the built-ins, and the <c>var</c> and function declarations of the scripts.</summary>
    Global,
}
