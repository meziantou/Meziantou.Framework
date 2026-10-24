namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>
/// The instructions of the bytecode: a stack machine. An instruction is an opcode followed by its operands (ints). The
/// comments give the operands, then the stack effect (before -> after, top of the stack on the right).
/// </summary>
internal enum OpCode
{
    // ---- Constants -------------------------------------------------------------------------------------------------

    /// <summary>-> undefined</summary>
    PushUndefined,

    /// <summary>-> null</summary>
    PushNull,

    /// <summary>-> true</summary>
    PushTrue,

    /// <summary>-> false</summary>
    PushFalse,

    /// <summary>(int value) -> number</summary>
    PushInt,

    /// <summary>(constant) -> value</summary>
    PushConst,

    /// <summary>-> hole (an array elision, or the marker of an uninitialized binding)</summary>
    PushHole,

    // ---- Stack -----------------------------------------------------------------------------------------------------

    /// <summary>a -></summary>
    Pop,

    /// <summary>a -> a a</summary>
    Dup,

    /// <summary>a b -> a b a b</summary>
    Dup2,

    /// <summary>a b -> b a</summary>
    Swap,

    /// <summary>a b c -> c a b</summary>
    Rot3,

    /// <summary>a b c -> b c a</summary>
    Rot3Reverse,

    /// <summary>a b c d -> d a b c</summary>
    Rot4,

    /// <summary>a b c -> a b c a b c</summary>
    Dup3,

    /// <summary>a b c d e -> e a b c d</summary>
    Rot5,

    // ---- Registers and environments ---------------------------------------------------------------------------------

    /// <summary>(register) -> value</summary>
    LoadLocal,

    /// <summary>(register, name) -> value; ReferenceError when the binding is not initialized</summary>
    LoadLocalChecked,

    /// <summary>(register) value -></summary>
    StoreLocal,

    /// <summary>(register) ->: register = ToNumeric(register) + 1 (an update expression whose value is discarded)</summary>
    IncrementLocal,

    /// <summary>(register) ->: register = ToNumeric(register) - 1</summary>
    DecrementLocal,

    /// <summary>(register, name) value ->; ReferenceError when the binding is not initialized</summary>
    StoreLocalChecked,

    /// <summary>(depth, slot) -> value</summary>
    LoadContext,

    /// <summary>(depth, slot, name) -> value; ReferenceError when the binding is not initialized</summary>
    LoadContextChecked,

    /// <summary>(depth, slot, name) -> value: the value of an import binding (the binding of another module); ReferenceError when it is not initialized</summary>
    LoadImport,

    /// <summary>(depth, slot) value -></summary>
    StoreContext,

    /// <summary>(depth, slot, name) value ->; ReferenceError when the binding is not initialized</summary>
    StoreContextChecked,

    /// <summary>(register, name) ->; ReferenceError when not initialized, then TypeError (assignment to a constant)</summary>
    ThrowConstAssignLocal,

    /// <summary>(depth, slot, name) ->; ReferenceError when not initialized, then TypeError (assignment to a constant)</summary>
    ThrowConstAssignContext,

    /// <summary>(name) -> value: the global lexical bindings, then the global object; ReferenceError when missing</summary>
    LoadGlobal,

    /// <summary>(name) -> value: as LoadGlobal, undefined when missing (typeof)</summary>
    LoadGlobalOrUndefined,

    /// <summary>(name, strict) value -></summary>
    StoreGlobal,

    /// <summary>(name) -> value: resolved at runtime in the environments (with, eval)</summary>
    LoadName,

    /// <summary>(name) -> value: as LoadName, undefined when unresolvable (typeof)</summary>
    LoadNameOrUndefined,

    /// <summary>(name) -> function thisValue: the callee of a call resolved at runtime (the binding object of a with statement is the this value)</summary>
    LoadNameForCall,

    /// <summary>(name, strict) value -></summary>
    StoreName,

    /// <summary>(name) -> boolean (delete identifier in sloppy code)</summary>
    DeleteName,

    /// <summary>(name) value ->: initializes a binding resolved at runtime (the functions of eval code)</summary>
    InitializeName,

    /// <summary>(scopeInfo) ->: pushes a declarative environment whose bindings are uninitialized</summary>
    PushScope,

    /// <summary>->: pops the current environment</summary>
    PopScope,

    /// <summary>object ->: pushes the object environment of a with statement</summary>
    PushWith,

    /// <summary>->: replaces the current declarative environment by a copy (CreatePerIterationEnvironment)</summary>
    CopyScope,

    /// <summary>(index) -> value: an argument of the call (undefined when missing)</summary>
    LoadArgument,

    /// <summary>(index) -> array: the arguments from the index (rest parameter)</summary>
    LoadRestArguments,

    /// <summary>(mapped) -> object: the arguments object</summary>
    CreateArguments,

    /// <summary>-> this; ReferenceError when this is not initialized (derived constructor before super())</summary>
    LoadThis,

    /// <summary>-> the function being called</summary>
    LoadFunction,

    /// <summary>-> new.target</summary>
    LoadNewTarget,

    /// <summary>-> the [[HomeObject]] of the function being called</summary>
    LoadHomeObject,

    /// <summary>-> import.meta</summary>
    LoadImportMeta,

    /// <summary>-> the this value of the global code (the [[GlobalThisValue]] of the realm)</summary>
    LoadGlobalThis,

    /// <summary>(register) ->: saves the current environment in a register (try statements)</summary>
    SaveEnvironment,

    /// <summary>(register) ->: restores the current environment from a register</summary>
    RestoreEnvironment,

    /// <summary>value -> value: binds the this of the frame (super()); ReferenceError when it is already bound</summary>
    BindThis,

    /// <summary>value -> value: ReferenceError when the value is not a hole (super() when the this binding in the environment is already initialized)</summary>
    CheckThisUninitialized,

    /// <summary>->: the code that follows is strict (the class definitions of sloppy code: all parts of a class are strict)</summary>
    EnterStrictCode,

    /// <summary>->: the code that follows has the strictness of the function again</summary>
    ExitStrictCode,

    /// <summary>value -> value: binds the this binding found by name (super() in eval code); ReferenceError when it is already bound</summary>
    BindThisName,

    // ---- Operators ---------------------------------------------------------------------------------------------------

    /// <summary>a b -> a + b</summary>
    Add,

    /// <summary>a b -> a - b</summary>
    Subtract,

    /// <summary>a b -> a * b</summary>
    Multiply,

    /// <summary>a b -> a / b</summary>
    Divide,

    /// <summary>a b -> a % b</summary>
    Modulo,

    /// <summary>a b -> a ** b</summary>
    Exponentiate,

    /// <summary>a b -> a &lt;&lt; b</summary>
    LeftShift,

    /// <summary>a b -> a &gt;&gt; b</summary>
    RightShift,

    /// <summary>a b -> a &gt;&gt;&gt; b</summary>
    UnsignedRightShift,

    /// <summary>a b -> a &amp; b</summary>
    BitwiseAnd,

    /// <summary>a b -> a | b</summary>
    BitwiseOr,

    /// <summary>a b -> a ^ b</summary>
    BitwiseXor,

    /// <summary>a b -> a == b</summary>
    Equal,

    /// <summary>a b -> a != b</summary>
    NotEqual,

    /// <summary>a b -> a === b</summary>
    StrictEqual,

    /// <summary>a b -> a !== b</summary>
    StrictNotEqual,

    /// <summary>a b -> a &lt; b</summary>
    LessThan,

    /// <summary>a b -> a &lt;= b</summary>
    LessThanOrEqual,

    /// <summary>a b -> a &gt; b</summary>
    GreaterThan,

    /// <summary>a b -> a &gt;= b</summary>
    GreaterThanOrEqual,

    /// <summary>a b -> a instanceof b</summary>
    InstanceOf,

    /// <summary>key object -> key in object</summary>
    In,

    /// <summary>(privateName depth, slot) object -> #x in object</summary>
    PrivateIn,

    /// <summary>a -> -a</summary>
    Negate,

    /// <summary>a -> +a (ToNumber)</summary>
    ToNumber,

    /// <summary>a -> ToNumeric(a)</summary>
    ToNumeric,

    /// <summary>a -> ~a</summary>
    BitwiseNot,

    /// <summary>a -> !a</summary>
    LogicalNot,

    /// <summary>a -> typeof a</summary>
    TypeOf,

    /// <summary>a -> a + 1 (a is numeric)</summary>
    Increment,

    /// <summary>a -> a - 1 (a is numeric)</summary>
    Decrement,

    /// <summary>a -> ToString(a) (template literals)</summary>
    ToStringValue,

    /// <summary>a -> ToPropertyKey(a)</summary>
    ToPropertyKey,

    /// <summary>a -> ToObject(a); TypeError for undefined and null</summary>
    ToObject,

    /// <summary>value -> value; TypeError when the value is undefined or null (RequireObjectCoercible)</summary>
    RequireObjectCoercible,

    /// <summary>base key -> base key; TypeError when the base is undefined or null (before the key is converted)</summary>
    RequireObjectCoercibleBase,

    /// <summary>(name) -> reference: resolves a name in the environments (the environment that has the binding, or undefined)</summary>
    ResolveName,

    /// <summary>(name) -> reference: resolves an identifier that is not declared in strict code (the global environment, or undefined when unresolvable)</summary>
    ResolveGlobal,

    /// <summary>(name) reference -> value: GetValue of a resolved reference</summary>
    LoadResolved,

    /// <summary>(name, strict) reference value -> value: PutValue of a resolved reference</summary>
    StoreResolved,

    // ---- Control flow --------------------------------------------------------------------------------------------------

    /// <summary>(target) -></summary>
    Jump,

    /// <summary>(target) value -></summary>
    JumpIfTrue,

    /// <summary>(target) value -></summary>
    JumpIfFalse,

    /// <summary>(target) value -> value (the value is kept)</summary>
    JumpIfTrueKeep,

    /// <summary>(target) value -> value (the value is kept)</summary>
    JumpIfFalseKeep,

    /// <summary>(target) value -> value: jumps when the value is null or undefined (kept)</summary>
    JumpIfNullishKeep,

    /// <summary>(target) value -> value: jumps when the value is neither null nor undefined (kept)</summary>
    JumpIfNotNullishKeep,

    /// <summary>(target) value -> value: jumps when the value is not undefined (kept)</summary>
    JumpIfNotUndefinedKeep,

    /// <summary>(target) value -> value: when the value is undefined, it is popped and the jump is taken</summary>
    JumpIfUndefinedPop,

    // The comparisons followed by a conditional jump (a comparison, then JumpIfTrue or JumpIfFalse): left right ->

    /// <summary>(target) left right ->: jumps when left &lt; right</summary>
    JumpIfLessThan,

    /// <summary>(target) left right ->: jumps when left &lt; right is false</summary>
    JumpIfNotLessThan,

    /// <summary>(target) left right ->: jumps when left &lt;= right</summary>
    JumpIfLessThanOrEqual,

    /// <summary>(target) left right ->: jumps when left &lt;= right is false</summary>
    JumpIfNotLessThanOrEqual,

    /// <summary>(target) left right ->: jumps when left &gt; right</summary>
    JumpIfGreaterThan,

    /// <summary>(target) left right ->: jumps when left &gt; right is false</summary>
    JumpIfNotGreaterThan,

    /// <summary>(target) left right ->: jumps when left &gt;= right</summary>
    JumpIfGreaterThanOrEqual,

    /// <summary>(target) left right ->: jumps when left &gt;= right is false</summary>
    JumpIfNotGreaterThanOrEqual,

    /// <summary>(target) left right ->: jumps when left === right</summary>
    JumpIfStrictEqual,

    /// <summary>(target) left right ->: jumps when left !== right</summary>
    JumpIfStrictNotEqual,

    /// <summary>value ->: throws the value</summary>
    Throw,

    /// <summary>(errorType, message) ->: throws a new error</summary>
    ThrowError,

    /// <summary>value ->: returns from the function</summary>
    Return,

    /// <summary>-> (debugger statement)</summary>
    Debugger,

    // ---- Objects and properties ---------------------------------------------------------------------------------------

    /// <summary>-> object</summary>
    NewObject,

    /// <summary>-> array</summary>
    NewArray,

    /// <summary>array value -> array (value may be a hole: an elision)</summary>
    ArrayAppend,

    /// <summary>array iterable -> array</summary>
    ArrayAppendSpread,

    /// <summary>(key) object -> value</summary>
    GetProperty,

    /// <summary>(key) object -> function object: the callee and the this value of a method call (Dup, GetProperty, Swap)</summary>
    GetMethod,

    /// <summary>(key) object value -> value</summary>
    SetProperty,

    /// <summary>object key -> value</summary>
    GetElement,

    /// <summary>object key value -> value</summary>
    SetElement,

    /// <summary>(key, strict) object -> boolean</summary>
    DeleteProperty,

    /// <summary>(strict) object key -> boolean</summary>
    DeleteElement,

    /// <summary>(key) object value -> object: CreateDataPropertyOrThrow (object literals)</summary>
    DefineField,

    /// <summary>object key value -> object: CreateDataPropertyOrThrow with a computed key</summary>
    DefineFieldComputed,

    /// <summary>(kind, enumerable) object key function -> object: defines a method, getter or setter (kind: 0 method, 1 getter, 2 setter) and sets its home object</summary>
    DefineMethod,

    /// <summary>object value -> object: `__proto__: value` in an object literal</summary>
    SetPrototypeLiteral,

    /// <summary>object source -> object: CopyDataProperties (spread properties)</summary>
    CopyDataProperties,

    /// <summary>(count) source excludedKey1..N -> object: CopyDataProperties into a new object (object rest)</summary>
    CopyDataPropertiesExcluding,

    /// <summary>homeObject -> base: GetSuperBase (the prototype of the home object, or null)</summary>
    LoadSuperBase,

    /// <summary>(key) thisValue base -> value: super.key</summary>
    GetSuperProperty,

    /// <summary>thisValue key base -> value: super[key]</summary>
    GetSuperElement,

    /// <summary>(key) thisValue base value -> value</summary>
    SetSuperProperty,

    /// <summary>thisValue key base value -> value</summary>
    SetSuperElement,

    /// <summary>(depth, slot) object -> value: object.#name</summary>
    GetPrivate,

    /// <summary>(depth, slot) object value -> value</summary>
    SetPrivate,

    // ---- Calls -----------------------------------------------------------------------------------------------------

    /// <summary>(argc) function thisValue arg1..N -> result</summary>
    Call,

    /// <summary>function thisValue argumentsArray -> result</summary>
    CallSpread,

    /// <summary>(argc, flags) function thisValue arg1..N -> result: a call that is a direct eval when the function is %eval%</summary>
    CallEval,

    /// <summary>(flags) function thisValue argumentsArray -> result</summary>
    CallEvalSpread,

    /// <summary>
    /// (argc) function thisValue arg1..N -> result: a call in tail position (PrepareForTailCall), always followed by
    /// <see cref="Return"/>. The frame of the callee replaces the frame of the caller when it can (see
    /// JsInterpreter.CanReplaceFrame); otherwise it is a <see cref="Call"/>.
    /// </summary>
    TailCall,

    /// <summary>function thisValue argumentsArray -> result: <see cref="CallSpread"/> in tail position (see <see cref="TailCall"/>)</summary>
    TailCallSpread,

    /// <summary>(argc, flags) function thisValue arg1..N -> result: <see cref="CallEval"/> in tail position, a tail call when the function is not %eval%</summary>
    TailCallEval,

    /// <summary>(flags) function thisValue argumentsArray -> result: <see cref="CallEvalSpread"/> in tail position, a tail call when the function is not %eval%</summary>
    TailCallEvalSpread,

    /// <summary>(argc) constructor arg1..N -> object</summary>
    New,

    /// <summary>constructor argumentsArray -> object</summary>
    NewSpread,

    /// <summary>(argc) superConstructor newTarget arg1..N -> this: super(...) (binds this, initializes the fields)</summary>
    SuperCall,

    /// <summary>superConstructor newTarget argumentsArray -> this</summary>
    SuperCallSpread,

    /// <summary>-> array: the arguments of a derived default constructor (forwarded to super)</summary>
    LoadAllArguments,

    /// <summary>specifier options -> promise: import()</summary>
    ImportCall,

    /// <summary>specifier options -> promise: import.defer()</summary>
    ImportDeferCall,

    /// <summary>specifier options -> promise: import.source()</summary>
    ImportSourceCall,

    // ---- Functions and classes ---------------------------------------------------------------------------------------

    /// <summary>(template) -> function: creates a closure in the current environment</summary>
    CreateClosure,

    /// <summary>(classTemplate) superclass -> constructor prototype (superclass is a hole without extends)</summary>
    CreateClass,

    /// <summary>constructor ->: initializes the instance elements of this (base class constructor prologue)</summary>
    InitializeInstanceElements,

    /// <summary>(description) -> private name</summary>
    CreatePrivateName,

    /// <summary>(kind, static) target function privateName ->: adds a private method/getter/setter (kind 0 method, 1 getter, 2 setter) to the class</summary>
    DefinePrivateMethod,

    /// <summary>constructor function ->: sets the [[Fields]] initializer of the class</summary>
    SetFieldInitializer,

    /// <summary>(key) object value ->: DefineField with a constant key (class fields)</summary>
    DefineClassField,

    /// <summary>object key value ->: DefineField with a computed key</summary>
    DefineClassFieldComputed,

    /// <summary>object privateName value ->: PrivateFieldAdd</summary>
    DefinePrivateField,

    /// <summary>function name ->  function: SetFunctionName (anonymous functions given a name at runtime)</summary>
    SetFunctionName,

    /// <summary>(prefix) function key -> function: SetFunctionName with a computed key and a prefix (0 none, 1 get, 2 set)</summary>
    SetFunctionNameComputed,

    /// <summary>constructor closure ->: sets the home object of the closure to the class and calls it with the class as this (static fields and blocks)</summary>
    CallClassStaticInitializer,

    /// <summary>function homeObject -> function: sets the [[HomeObject]] of a function</summary>
    SetHomeObject,

    /// <summary>(static) constructor homeObject key -> storage: defines the getter and the setter of an auto-accessor (key: a property key or a private name)</summary>
    DefineAutoAccessor,

    // ---- Decorators --------------------------------------------------------------------------------------------------

    /// <summary>-> classDecoration: the decoration state of a class that has decorators</summary>
    CreateClassDecoration,

    /// <summary>decoration function thisValue -> decoration: adds a decorator (and its receiver) to a class or a class element</summary>
    AddDecorator,

    /// <summary>(kind, static) homeObject -> decoration: the decoration state of a class element (kind: DecoratedElementKind)</summary>
    CreateElementDecoration,

    /// <summary>(nameFromKey) decoration key function ->: the method, getter or setter of an element of a decorated class</summary>
    SetDecoratedMethod,

    /// <summary>decoration key ->: the key of a decorated field</summary>
    SetDecoratedKey,

    /// <summary>decoration key -> storage: the getter and the setter of an auto-accessor of a decorated class</summary>
    SetDecoratedAccessor,

    /// <summary>classDecoration constructor ->: creates the metadata object, once the elements are evaluated</summary>
    BeginClassDecoration,

    /// <summary>classDecoration decoration ->: applies the decorators of an element, and defines it when it is not a field</summary>
    ApplyElementDecorators,

    /// <summary>(name) classDecoration -> class: applies the decorators of the class and defines its metadata</summary>
    ApplyClassDecorators,

    /// <summary>(class) classDecoration ->: calls the initializers of the static methods (0) or of the class (1), with the class as this</summary>
    RunClassDecorationInitializers,

    /// <summary>value receiver decoration -> value: the initializers added by the decorators of a field or an auto-accessor</summary>
    RunFieldInitializers,

    /// <summary>receiver decoration ->: the extra initializers (addInitializer) of a field or an auto-accessor</summary>
    RunExtraInitializers,

    /// <summary>(template) -> template object (tagged templates, cached per site and realm)</summary>
    GetTemplateObject,

    /// <summary>(pattern, flags) -> RegExp object</summary>
    CreateRegExp,

    // ---- Iteration ---------------------------------------------------------------------------------------------------

    /// <summary>iterable -> iteratorRecord</summary>
    GetIterator,

    /// <summary>iterable -> iteratorRecord (for await, yield* in async generators)</summary>
    GetAsyncIterator,

    /// <summary>(target) iteratorRecord -> value, or jumps to target (the record popped) when the iterator is done</summary>
    IteratorNext,

    /// <summary>iteratorRecord -> value: the next value, or undefined when done (destructuring)</summary>
    IteratorNextOrUndefined,

    /// <summary>iteratorRecord -> array: the remaining values (rest element)</summary>
    IteratorRest,

    /// <summary>iteratorRecord -> result: calls next() without checking the result (for await)</summary>
    IteratorCallNext,

    /// <summary>(target) iteratorRecord result -> value: checks the (awaited) result; jumps to target (both popped) when done</summary>
    IteratorStepFinish,

    /// <summary>iteratorRecord ->: IteratorClose for a normal completion when the iterator is not done</summary>
    IteratorClose,

    /// <summary>iteratorRecord ->: IteratorClose for a throw completion (errors of return() ignored) when not done</summary>
    IteratorCloseOnThrow,

    /// <summary>iteratorRecord -> done: whether the iterator is done</summary>
    IteratorIsDone,

    /// <summary>(kind: 0 next, 1 throw, 2 return) iteratorRecord received -> result: one step of yield* (calls next, throw or return)</summary>
    IteratorDelegateStep,

    /// <summary>iteratorRecord -> result: calls the return method of the iterator (undefined when it has none, or when the iterator is done)</summary>
    IteratorCallReturn,

    /// <summary>value -> value: a TypeError when the value is not an object (the results of the iterators)</summary>
    ThrowIfNotObject,

    /// <summary>result -> done: IteratorComplete</summary>
    IteratorComplete,

    /// <summary>result -> value: IteratorValue</summary>
    IteratorValue,

    /// <summary>object -> enumerator: for-in</summary>
    ForInPrepare,

    /// <summary>(target) enumerator -> key, or jumps to target (the enumerator popped) when done</summary>
    ForInNext,

    // ---- Generators and async functions --------------------------------------------------------------------------------

    /// <summary>->: the start of the body of a generator: creates the generator object and suspends</summary>
    GeneratorStart,

    /// <summary>value -> received resumeKind: suspends the generator; resumes with the received value and how (0 next, 1 throw, 2 return)</summary>
    Yield,

    /// <summary>value -> result: awaits the value (resumes with the value, or throws the reason)</summary>
    Await,

    /// <summary>value -> value: CreateIterResultObject(value, false) (the values of the yields of async generators are not wrapped)</summary>
    CreateIterResult,

    /// <summary>(done) value -> result: CreateIterResultObject(value, done)</summary>
    CreateIterResultDone,

    // ---- Declarations --------------------------------------------------------------------------------------------------

    /// <summary>(declarations) ->: GlobalDeclarationInstantiation of a script</summary>
    GlobalDeclarationInstantiation,

    /// <summary>(declarations) ->: EvalDeclarationInstantiation of sloppy eval code</summary>
    EvalDeclarationInstantiation,

    /// <summary>(name) value ->: initializes a global lexical binding (let, const, class at the top level of a script)</summary>
    InitializeGlobalLexical,

    /// <summary>(name) value ->: CreateGlobalFunctionBinding (the function declarations of a script)</summary>
    InitializeGlobalFunction,

    /// <summary>(name) value ->: sets a global var (Annex B function in block, and the var declarations with an initializer)</summary>
    SetGlobalVar,

    /// <summary>(name) value ->: sets a var in the variable environment, not in the scopes in between (Annex B functions of eval code)</summary>
    SetVarBinding,

    // ---- Explicit resource management ------------------------------------------------------------------------------------

    /// <summary>(async) capability value ->: AddDisposableResource for a using declaration</summary>
    AddDisposableResource,

    /// <summary>-> capability: NewDisposeCapability</summary>
    NewDisposeCapability,

    /// <summary>capability type value ->: DisposeResources with the completion of the scope; throws the resulting exception</summary>
    DisposeResources,

    /// <summary>capability type value -> operation: starts the disposal of the resources of an await using scope</summary>
    AsyncDisposeStart,

    /// <summary>(target) operation -> value: the value to await, or jumps to target (the operation popped) when the resources are disposed</summary>
    AsyncDisposeNext,

    /// <summary>operation thrown ->: the result of the await (a hole when it completed normally)</summary>
    AsyncDisposeSetResult,

    /// <summary>operation ->: throws the resulting exception of the disposal, if any</summary>
    AsyncDisposeFinish,
}
