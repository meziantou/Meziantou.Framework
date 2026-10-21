namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>
/// The instructions of the bytecode: a stack machine. An instruction is an opcode followed by its operands (ints). The
/// comments give the operands, then the stack effect (before \u2192 after, top of the stack on the right).
/// </summary>
internal enum OpCode
{
    // ---- Constants -------------------------------------------------------------------------------------------------

    /// <summary>\u2192 undefined</summary>
    PushUndefined,

    /// <summary>\u2192 null</summary>
    PushNull,

    /// <summary>\u2192 true</summary>
    PushTrue,

    /// <summary>\u2192 false</summary>
    PushFalse,

    /// <summary>(int value) \u2192 number</summary>
    PushInt,

    /// <summary>(constant) \u2192 value</summary>
    PushConst,

    /// <summary>\u2192 hole (an array elision, or the marker of an uninitialized binding)</summary>
    PushHole,

    // ---- Stack -----------------------------------------------------------------------------------------------------

    /// <summary>a \u2192</summary>
    Pop,

    /// <summary>a \u2192 a a</summary>
    Dup,

    /// <summary>a b \u2192 a b a b</summary>
    Dup2,

    /// <summary>a b \u2192 b a</summary>
    Swap,

    /// <summary>a b c \u2192 c a b</summary>
    Rot3,

    /// <summary>a b c \u2192 b c a</summary>
    Rot3Reverse,

    /// <summary>a b c d \u2192 d a b c</summary>
    Rot4,

    // ---- Registers and environments ---------------------------------------------------------------------------------

    /// <summary>(register) \u2192 value</summary>
    LoadLocal,

    /// <summary>(register, name) \u2192 value; ReferenceError when the binding is not initialized</summary>
    LoadLocalChecked,

    /// <summary>(register) value \u2192</summary>
    StoreLocal,

    /// <summary>(register, name) value \u2192; ReferenceError when the binding is not initialized</summary>
    StoreLocalChecked,

    /// <summary>(depth, slot) \u2192 value</summary>
    LoadContext,

    /// <summary>(depth, slot, name) \u2192 value; ReferenceError when the binding is not initialized</summary>
    LoadContextChecked,

    /// <summary>(depth, slot) value \u2192</summary>
    StoreContext,

    /// <summary>(depth, slot, name) value \u2192; ReferenceError when the binding is not initialized</summary>
    StoreContextChecked,

    /// <summary>(register, name) \u2192; ReferenceError when not initialized, then TypeError (assignment to a constant)</summary>
    ThrowConstAssignLocal,

    /// <summary>(depth, slot, name) \u2192; ReferenceError when not initialized, then TypeError (assignment to a constant)</summary>
    ThrowConstAssignContext,

    /// <summary>(name) \u2192 value: the global lexical bindings, then the global object; ReferenceError when missing</summary>
    LoadGlobal,

    /// <summary>(name) \u2192 value: as LoadGlobal, undefined when missing (typeof)</summary>
    LoadGlobalOrUndefined,

    /// <summary>(name, strict) value \u2192</summary>
    StoreGlobal,

    /// <summary>(name) \u2192 value: resolved at runtime in the environments (with, eval)</summary>
    LoadName,

    /// <summary>(name) \u2192 value: as LoadName, undefined when unresolvable (typeof)</summary>
    LoadNameOrUndefined,

    /// <summary>(name) \u2192 function thisValue: the callee of a call resolved at runtime (the binding object of a with statement is the this value)</summary>
    LoadNameForCall,

    /// <summary>(name, strict) value \u2192</summary>
    StoreName,

    /// <summary>(name) \u2192 boolean (delete identifier in sloppy code)</summary>
    DeleteName,

    /// <summary>(name) value \u2192: initializes a binding resolved at runtime (the functions of eval code)</summary>
    InitializeName,

    /// <summary>(scopeInfo) \u2192: pushes a declarative environment whose bindings are uninitialized</summary>
    PushScope,

    /// <summary>\u2192: pops the current environment</summary>
    PopScope,

    /// <summary>object \u2192: pushes the object environment of a with statement</summary>
    PushWith,

    /// <summary>\u2192: replaces the current declarative environment by a copy (CreatePerIterationEnvironment)</summary>
    CopyScope,

    /// <summary>(index) \u2192 value: an argument of the call (undefined when missing)</summary>
    LoadArgument,

    /// <summary>(index) \u2192 array: the arguments from the index (rest parameter)</summary>
    LoadRestArguments,

    /// <summary>(mapped) \u2192 object: the arguments object</summary>
    CreateArguments,

    /// <summary>\u2192 this; ReferenceError when this is not initialized (derived constructor before super())</summary>
    LoadThis,

    /// <summary>\u2192 the function being called</summary>
    LoadFunction,

    /// <summary>\u2192 new.target</summary>
    LoadNewTarget,

    /// <summary>\u2192 the [[HomeObject]] of the function being called</summary>
    LoadHomeObject,

    /// <summary>\u2192 import.meta</summary>
    LoadImportMeta,

    /// <summary>\u2192 the this value of the global code (the [[GlobalThisValue]] of the realm)</summary>
    LoadGlobalThis,

    /// <summary>(register) \u2192: saves the current environment in a register (try statements)</summary>
    SaveEnvironment,

    /// <summary>(register) \u2192: restores the current environment from a register</summary>
    RestoreEnvironment,

    /// <summary>value \u2192 value: binds the this of the frame (super()); ReferenceError when it is already bound</summary>
    BindThis,

    /// <summary>value \u2192 value: ReferenceError when the value is not a hole (super() when the this binding in the environment is already initialized)</summary>
    CheckThisUninitialized,

    // ---- Operators ---------------------------------------------------------------------------------------------------

    /// <summary>a b \u2192 a + b</summary>
    Add,

    /// <summary>a b \u2192 a - b</summary>
    Subtract,

    /// <summary>a b \u2192 a * b</summary>
    Multiply,

    /// <summary>a b \u2192 a / b</summary>
    Divide,

    /// <summary>a b \u2192 a % b</summary>
    Modulo,

    /// <summary>a b \u2192 a ** b</summary>
    Exponentiate,

    /// <summary>a b \u2192 a &lt;&lt; b</summary>
    LeftShift,

    /// <summary>a b \u2192 a &gt;&gt; b</summary>
    RightShift,

    /// <summary>a b \u2192 a &gt;&gt;&gt; b</summary>
    UnsignedRightShift,

    /// <summary>a b \u2192 a &amp; b</summary>
    BitwiseAnd,

    /// <summary>a b \u2192 a | b</summary>
    BitwiseOr,

    /// <summary>a b \u2192 a ^ b</summary>
    BitwiseXor,

    /// <summary>a b \u2192 a == b</summary>
    Equal,

    /// <summary>a b \u2192 a != b</summary>
    NotEqual,

    /// <summary>a b \u2192 a === b</summary>
    StrictEqual,

    /// <summary>a b \u2192 a !== b</summary>
    StrictNotEqual,

    /// <summary>a b \u2192 a &lt; b</summary>
    LessThan,

    /// <summary>a b \u2192 a &lt;= b</summary>
    LessThanOrEqual,

    /// <summary>a b \u2192 a &gt; b</summary>
    GreaterThan,

    /// <summary>a b \u2192 a &gt;= b</summary>
    GreaterThanOrEqual,

    /// <summary>a b \u2192 a instanceof b</summary>
    InstanceOf,

    /// <summary>key object \u2192 key in object</summary>
    In,

    /// <summary>(privateName depth, slot) object \u2192 #x in object</summary>
    PrivateIn,

    /// <summary>a \u2192 -a</summary>
    Negate,

    /// <summary>a \u2192 +a (ToNumber)</summary>
    ToNumber,

    /// <summary>a \u2192 ToNumeric(a)</summary>
    ToNumeric,

    /// <summary>a \u2192 ~a</summary>
    BitwiseNot,

    /// <summary>a \u2192 !a</summary>
    LogicalNot,

    /// <summary>a \u2192 typeof a</summary>
    TypeOf,

    /// <summary>a \u2192 a + 1 (a is numeric)</summary>
    Increment,

    /// <summary>a \u2192 a - 1 (a is numeric)</summary>
    Decrement,

    /// <summary>a \u2192 ToString(a) (template literals)</summary>
    ToStringValue,

    /// <summary>a \u2192 ToPropertyKey(a)</summary>
    ToPropertyKey,

    /// <summary>a \u2192 ToObject(a); TypeError for undefined and null</summary>
    ToObject,

    /// <summary>value \u2192 value; TypeError when the value is undefined or null (RequireObjectCoercible)</summary>
    RequireObjectCoercible,

    // ---- Control flow --------------------------------------------------------------------------------------------------

    /// <summary>(target) \u2192</summary>
    Jump,

    /// <summary>(target) value \u2192</summary>
    JumpIfTrue,

    /// <summary>(target) value \u2192</summary>
    JumpIfFalse,

    /// <summary>(target) value \u2192 value (the value is kept)</summary>
    JumpIfTrueKeep,

    /// <summary>(target) value \u2192 value (the value is kept)</summary>
    JumpIfFalseKeep,

    /// <summary>(target) value \u2192 value: jumps when the value is null or undefined (kept)</summary>
    JumpIfNullishKeep,

    /// <summary>(target) value \u2192 value: jumps when the value is neither null nor undefined (kept)</summary>
    JumpIfNotNullishKeep,

    /// <summary>(target) value \u2192 value: jumps when the value is not undefined (kept)</summary>
    JumpIfNotUndefinedKeep,

    /// <summary>(target) value \u2192 value: when the value is undefined, it is popped and the jump is taken</summary>
    JumpIfUndefinedPop,

    /// <summary>value \u2192: throws the value</summary>
    Throw,

    /// <summary>(errorType, message) \u2192: throws a new error</summary>
    ThrowError,

    /// <summary>value \u2192: returns from the function</summary>
    Return,

    /// <summary>\u2192 (debugger statement)</summary>
    Debugger,

    // ---- Objects and properties ---------------------------------------------------------------------------------------

    /// <summary>\u2192 object</summary>
    NewObject,

    /// <summary>\u2192 array</summary>
    NewArray,

    /// <summary>array value \u2192 array (value may be a hole: an elision)</summary>
    ArrayAppend,

    /// <summary>array iterable \u2192 array</summary>
    ArrayAppendSpread,

    /// <summary>(key) object \u2192 value</summary>
    GetProperty,

    /// <summary>(key) object value \u2192 value</summary>
    SetProperty,

    /// <summary>object key \u2192 value</summary>
    GetElement,

    /// <summary>object key value \u2192 value</summary>
    SetElement,

    /// <summary>(key, strict) object \u2192 boolean</summary>
    DeleteProperty,

    /// <summary>(strict) object key \u2192 boolean</summary>
    DeleteElement,

    /// <summary>(key) object value \u2192 object: CreateDataPropertyOrThrow (object literals)</summary>
    DefineField,

    /// <summary>object key value \u2192 object: CreateDataPropertyOrThrow with a computed key</summary>
    DefineFieldComputed,

    /// <summary>(kind, enumerable) object key function \u2192 object: defines a method, getter or setter (kind: 0 method, 1 getter, 2 setter) and sets its home object</summary>
    DefineMethod,

    /// <summary>object value \u2192 object: `__proto__: value` in an object literal</summary>
    SetPrototypeLiteral,

    /// <summary>object source \u2192 object: CopyDataProperties (spread properties)</summary>
    CopyDataProperties,

    /// <summary>(count) source excludedKey1..N \u2192 object: CopyDataProperties into a new object (object rest)</summary>
    CopyDataPropertiesExcluding,

    /// <summary>(key) thisValue \u2192 value: super.key</summary>
    GetSuperProperty,

    /// <summary>thisValue key \u2192 value: super[key]</summary>
    GetSuperElement,

    /// <summary>(key) thisValue value \u2192 value</summary>
    SetSuperProperty,

    /// <summary>thisValue key value \u2192 value</summary>
    SetSuperElement,

    /// <summary>(depth, slot) object \u2192 value: object.#name</summary>
    GetPrivate,

    /// <summary>(depth, slot) object value \u2192 value</summary>
    SetPrivate,

    // ---- Calls -----------------------------------------------------------------------------------------------------

    /// <summary>(argc) function thisValue arg1..N \u2192 result</summary>
    Call,

    /// <summary>function thisValue argumentsArray \u2192 result</summary>
    CallSpread,

    /// <summary>(argc, flags) function thisValue arg1..N \u2192 result: a call that is a direct eval when the function is %eval%</summary>
    CallEval,

    /// <summary>(flags) function thisValue argumentsArray \u2192 result</summary>
    CallEvalSpread,

    /// <summary>(argc) constructor arg1..N \u2192 object</summary>
    New,

    /// <summary>constructor argumentsArray \u2192 object</summary>
    NewSpread,

    /// <summary>(argc) arg1..N \u2192 this: super(...) (binds this, initializes the fields)</summary>
    SuperCall,

    /// <summary>argumentsArray \u2192 this</summary>
    SuperCallSpread,

    /// <summary>\u2192 array: the arguments of a derived default constructor (forwarded to super)</summary>
    LoadAllArguments,

    /// <summary>specifier options \u2192 promise: import()</summary>
    ImportCall,

    // ---- Functions and classes ---------------------------------------------------------------------------------------

    /// <summary>(template) \u2192 function: creates a closure in the current environment</summary>
    CreateClosure,

    /// <summary>(classTemplate) superclass \u2192 constructor prototype (superclass is a hole without extends)</summary>
    CreateClass,

    /// <summary>constructor \u2192: initializes the instance elements of this (base class constructor prologue)</summary>
    InitializeInstanceElements,

    /// <summary>(description) \u2192 private name</summary>
    CreatePrivateName,

    /// <summary>(kind, static) target function privateName \u2192: adds a private method/getter/setter (kind 0 method, 1 getter, 2 setter) to the class</summary>
    DefinePrivateMethod,

    /// <summary>constructor function \u2192: sets the [[Fields]] initializer of the class</summary>
    SetFieldInitializer,

    /// <summary>(key) object value \u2192: DefineField with a constant key (class fields)</summary>
    DefineClassField,

    /// <summary>object key value \u2192: DefineField with a computed key</summary>
    DefineClassFieldComputed,

    /// <summary>object privateName value \u2192: PrivateFieldAdd</summary>
    DefinePrivateField,

    /// <summary>function name \u2192  function: SetFunctionName (anonymous functions given a name at runtime)</summary>
    SetFunctionName,

    /// <summary>(prefix) function key \u2192 function: SetFunctionName with a computed key and a prefix (0 none, 1 get, 2 set)</summary>
    SetFunctionNameComputed,

    /// <summary>constructor closure \u2192: sets the home object of the closure to the class and calls it with the class as this (static fields and blocks)</summary>
    CallClassStaticInitializer,

    /// <summary>function homeObject \u2192 function: sets the [[HomeObject]] of a function</summary>
    SetHomeObject,

    /// <summary>(template) \u2192 template object (tagged templates, cached per site and realm)</summary>
    GetTemplateObject,

    /// <summary>(pattern, flags) \u2192 RegExp object</summary>
    CreateRegExp,

    // ---- Iteration ---------------------------------------------------------------------------------------------------

    /// <summary>iterable \u2192 iteratorRecord</summary>
    GetIterator,

    /// <summary>iterable \u2192 iteratorRecord (for await, yield* in async generators)</summary>
    GetAsyncIterator,

    /// <summary>(target) iteratorRecord \u2192 value, or jumps to target (the record popped) when the iterator is done</summary>
    IteratorNext,

    /// <summary>iteratorRecord \u2192 value: the next value, or undefined when done (destructuring)</summary>
    IteratorNextOrUndefined,

    /// <summary>iteratorRecord \u2192 array: the remaining values (rest element)</summary>
    IteratorRest,

    /// <summary>iteratorRecord \u2192 result: calls next() without checking the result (for await)</summary>
    IteratorCallNext,

    /// <summary>(target) iteratorRecord result \u2192 value: checks the (awaited) result; jumps to target (both popped) when done</summary>
    IteratorStepFinish,

    /// <summary>iteratorRecord \u2192: IteratorClose for a normal completion when the iterator is not done</summary>
    IteratorClose,

    /// <summary>iteratorRecord \u2192: IteratorClose for a throw completion (errors of return() ignored) when not done</summary>
    IteratorCloseOnThrow,

    /// <summary>iteratorRecord \u2192 done: whether the iterator is done</summary>
    IteratorIsDone,

    /// <summary>(kind: 0 next, 1 throw, 2 return) iteratorRecord received \u2192 result: one step of yield* (calls next, throw or return)</summary>
    IteratorDelegateStep,

    /// <summary>iteratorRecord \u2192 result: calls the return method of the iterator (undefined when it has none, or when the iterator is done)</summary>
    IteratorCallReturn,

    /// <summary>value \u2192 value: a TypeError when the value is not an object (the results of the iterators)</summary>
    ThrowIfNotObject,

    /// <summary>result \u2192 done: IteratorComplete</summary>
    IteratorComplete,

    /// <summary>result \u2192 value: IteratorValue</summary>
    IteratorValue,

    /// <summary>object \u2192 enumerator: for-in</summary>
    ForInPrepare,

    /// <summary>(target) enumerator \u2192 key, or jumps to target (the enumerator popped) when done</summary>
    ForInNext,

    // ---- Generators and async functions --------------------------------------------------------------------------------

    /// <summary>\u2192: the start of the body of a generator: creates the generator object and suspends</summary>
    GeneratorStart,

    /// <summary>value \u2192 received resumeKind: suspends the generator; resumes with the received value and how (0 next, 1 throw, 2 return)</summary>
    Yield,

    /// <summary>value \u2192 result: awaits the value (resumes with the value, or throws the reason)</summary>
    Await,

    /// <summary>value \u2192 value: CreateIterResultObject(value, false) (the values of the yields of async generators are not wrapped)</summary>
    CreateIterResult,

    /// <summary>(done) value \u2192 result: CreateIterResultObject(value, done)</summary>
    CreateIterResultDone,

    // ---- Declarations --------------------------------------------------------------------------------------------------

    /// <summary>(declarations) \u2192: GlobalDeclarationInstantiation of a script</summary>
    GlobalDeclarationInstantiation,

    /// <summary>(declarations) \u2192: EvalDeclarationInstantiation of sloppy eval code</summary>
    EvalDeclarationInstantiation,

    /// <summary>(name) value \u2192: initializes a global lexical binding (let, const, class at the top level of a script)</summary>
    InitializeGlobalLexical,

    /// <summary>(name) value \u2192: CreateGlobalFunctionBinding (the function declarations of a script)</summary>
    InitializeGlobalFunction,

    /// <summary>(name) value \u2192: sets a global var (Annex B function in block, and the var declarations with an initializer)</summary>
    SetGlobalVar,

    // ---- Explicit resource management ------------------------------------------------------------------------------------

    /// <summary>(async) value \u2192 value: AddDisposableResource for a using declaration in the current scope</summary>
    AddDisposableResource,
}
