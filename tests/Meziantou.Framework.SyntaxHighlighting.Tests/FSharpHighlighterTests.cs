namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class FSharpHighlighterTests
{

    [Fact]
    public void Let_Simple()
    {
        AssertHighlighter("fsharp",
"""
let x = 42
""",
"""
<span class="hljs-keyword">let</span> x <span class="hljs-operator">=</span> <span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void Let_Typed()
    {
        AssertHighlighter("fsharp",
"""
let x : int = 42
""",
"""
<span class="hljs-keyword">let</span> x <span class="hljs-operator">:</span> <span class="hljs-type">int</span> <span class="hljs-operator">=</span> <span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void Let_Mutable()
    {
        AssertHighlighter("fsharp",
"""
let mutable counter = 0
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-keyword">mutable</span> counter <span class="hljs-operator">=</span> <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void Let_Function()
    {
        AssertHighlighter("fsharp",
"""
let add a b = a + b
""",
"""
<span class="hljs-keyword">let</span> add a b <span class="hljs-operator">=</span> a <span class="hljs-operator">+</span> b
""");
    }

    [Fact]
    public void Let_FunctionTyped()
    {
        AssertHighlighter("fsharp",
"""
let add (a: int) (b: int) : int = a + b
""",
"""
<span class="hljs-keyword">let</span> add (a<span class="hljs-operator">:</span> <span class="hljs-type">int</span>) (b<span class="hljs-operator">:</span> <span class="hljs-type">int</span>) <span class="hljs-operator">:</span> <span class="hljs-type">int</span> <span class="hljs-operator">=</span> a <span class="hljs-operator">+</span> b
""");
    }

    [Fact]
    public void Let_NestedLet()
    {
        AssertHighlighter("fsharp",
"""
let outer x =
    let inner y = y * 2
    inner x + 1
""",
"""
<span class="hljs-keyword">let</span> outer x <span class="hljs-operator">=</span>
    <span class="hljs-keyword">let</span> inner y <span class="hljs-operator">=</span> y <span class="hljs-operator">*</span> <span class="hljs-number">2</span>
    inner x <span class="hljs-operator">+</span> <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Let_Recursive()
    {
        AssertHighlighter("fsharp",
"""
let rec factorial n =
    if n <= 1 then 1
    else n * factorial (n - 1)
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-keyword">rec</span> factorial n <span class="hljs-operator">=</span>
    <span class="hljs-keyword">if</span> n <span class="hljs-operator">&lt;=</span> <span class="hljs-number">1</span> <span class="hljs-keyword">then</span> <span class="hljs-number">1</span>
    <span class="hljs-keyword">else</span> n <span class="hljs-operator">*</span> factorial (n <span class="hljs-operator">-</span> <span class="hljs-number">1</span>)
""");
    }

    [Fact]
    public void Let_MutuallyRecursive()
    {
        AssertHighlighter("fsharp",
"""
let rec isEven n =
    if n = 0 then true else isOdd (n - 1)
and isOdd n =
    if n = 0 then false else isEven (n - 1)
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-keyword">rec</span> isEven n <span class="hljs-operator">=</span>
    <span class="hljs-keyword">if</span> n <span class="hljs-operator">=</span> <span class="hljs-number">0</span> <span class="hljs-keyword">then</span> <span class="hljs-literal">true</span> <span class="hljs-keyword">else</span> isOdd (n <span class="hljs-operator">-</span> <span class="hljs-number">1</span>)
<span class="hljs-keyword">and</span> isOdd n <span class="hljs-operator">=</span>
    <span class="hljs-keyword">if</span> n <span class="hljs-operator">=</span> <span class="hljs-number">0</span> <span class="hljs-keyword">then</span> <span class="hljs-literal">false</span> <span class="hljs-keyword">else</span> isEven (n <span class="hljs-operator">-</span> <span class="hljs-number">1</span>)
""");
    }

    [Fact]
    public void Let_Private()
    {
        AssertHighlighter("fsharp",
"""
let private secret = 42
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-keyword">private</span> secret <span class="hljs-operator">=</span> <span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void Let_Inline()
    {
        AssertHighlighter("fsharp",
"""
let inline square x = x * x
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-keyword">inline</span> square x <span class="hljs-operator">=</span> x <span class="hljs-operator">*</span> x
""");
    }

    [Fact]
    public void Let_TupleDestructure()
    {
        AssertHighlighter("fsharp",
"""
let (a, b) = pair
""",
"""
<span class="hljs-keyword">let</span> (a, b) <span class="hljs-operator">=</span> pair
""");
    }

    [Fact]
    public void Let_RecordDestructure()
    {
        AssertHighlighter("fsharp",
"""
let { Name = name; Age = age } = user
""",
"""
<span class="hljs-keyword">let</span> { Name <span class="hljs-operator">=</span> name; Age <span class="hljs-operator">=</span> age } <span class="hljs-operator">=</span> user
""");
    }

    [Fact]
    public void Let_WildcardBind()
    {
        AssertHighlighter("fsharp",
"""
let _ = ignoreMe ()
""",
"""
<span class="hljs-keyword">let</span> _ <span class="hljs-operator">=</span> ignoreMe ()
""");
    }

    [Fact]
    public void Operator_Pipe()
    {
        AssertHighlighter("fsharp",
"""
let result = users |> List.filter (fun u -> u.IsActive)
""",
"""
<span class="hljs-keyword">let</span> result <span class="hljs-operator">=</span> users <span class="hljs-operator">|&gt;</span> List.filter (<span class="hljs-keyword">fun</span> u <span class="hljs-operator">-&gt;</span> u.IsActive)
""");
    }

    [Fact]
    public void Operator_PipeMultiple()
    {
        AssertHighlighter("fsharp",
"""
let result =
    users
    |> List.filter (fun u -> u.IsActive)
    |> List.map (fun u -> u.Name)
    |> List.sort
""",
"""
<span class="hljs-keyword">let</span> result <span class="hljs-operator">=</span>
    users
    <span class="hljs-operator">|&gt;</span> List.filter (<span class="hljs-keyword">fun</span> u <span class="hljs-operator">-&gt;</span> u.IsActive)
    <span class="hljs-operator">|&gt;</span> List.map (<span class="hljs-keyword">fun</span> u <span class="hljs-operator">-&gt;</span> u.Name)
    <span class="hljs-operator">|&gt;</span> List.sort
""");
    }

    [Fact]
    public void Operator_ReversePipe()
    {
        AssertHighlighter("fsharp",
"""
let result = List.head <| List.filter active users
""",
"""
<span class="hljs-keyword">let</span> result <span class="hljs-operator">=</span> List.head <span class="hljs-operator">&lt;|</span> List.filter active users
""");
    }

    [Fact]
    public void Operator_Compose()
    {
        AssertHighlighter("fsharp",
"""
let process = parse >> validate >> store
""",
"""
<span class="hljs-keyword">let</span> process <span class="hljs-operator">=</span> parse <span class="hljs-operator">&gt;&gt;</span> validate <span class="hljs-operator">&gt;&gt;</span> store
""");
    }

    [Fact]
    public void Operator_ComposeReverse()
    {
        AssertHighlighter("fsharp",
"""
let process = store << validate << parse
""",
"""
<span class="hljs-keyword">let</span> process <span class="hljs-operator">=</span> store <span class="hljs-operator">&lt;&lt;</span> validate <span class="hljs-operator">&lt;&lt;</span> parse
""");
    }

    [Fact]
    public void Operator_Concat()
    {
        AssertHighlighter("fsharp",
"""
let s = "hello" + " " + name
""",
"""
<span class="hljs-keyword">let</span> s <span class="hljs-operator">=</span> <span class="hljs-string">&quot;hello&quot;</span> <span class="hljs-operator">+</span> <span class="hljs-string">&quot; &quot;</span> <span class="hljs-operator">+</span> name
""");
    }

    [Fact]
    public void Operator_CompareEqual()
    {
        AssertHighlighter("fsharp",
"""
let r = a = b
""",
"""
<span class="hljs-keyword">let</span> r <span class="hljs-operator">=</span> a <span class="hljs-operator">=</span> b
""");
    }

    [Fact]
    public void Operator_NotEqual()
    {
        AssertHighlighter("fsharp",
"""
let r = a <> b
""",
"""
<span class="hljs-keyword">let</span> r <span class="hljs-operator">=</span> a <span class="hljs-operator">&lt;&gt;</span> b
""");
    }

    [Fact]
    public void Operator_ListCons()
    {
        AssertHighlighter("fsharp",
"""
let list = 1 :: rest
""",
"""
<span class="hljs-keyword">let</span> list <span class="hljs-operator">=</span> <span class="hljs-number">1</span> <span class="hljs-operator">::</span> rest
""");
    }

    [Fact]
    public void Operator_ListAppend()
    {
        AssertHighlighter("fsharp",
"""
let combined = a @ b
""",
"""
<span class="hljs-keyword">let</span> combined <span class="hljs-operator">=</span> a <span class="hljs-operator">@</span> b
""");
    }

    [Fact]
    public void Operator_CustomOperator()
    {
        AssertHighlighter("fsharp",
"""
let (++) a b = a + b + 1
""",
"""
<span class="hljs-keyword">let</span> (<span class="hljs-operator">++</span>) a b <span class="hljs-operator">=</span> a <span class="hljs-operator">+</span> b <span class="hljs-operator">+</span> <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Operator_PowerNumeric()
    {
        AssertHighlighter("fsharp",
"""
let r = 2.0 ** 10.0
""",
"""
<span class="hljs-keyword">let</span> r <span class="hljs-operator">=</span> <span class="hljs-number">2.0</span> <span class="hljs-operator">**</span> <span class="hljs-number">10.0</span>
""");
    }

    [Fact]
    public void Type_RecordSimple()
    {
        AssertHighlighter("fsharp",
"""
type User = { Name: string; Age: int }
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">User</span> <span class="hljs-operator">=</span> { Name<span class="hljs-operator">:</span> <span class="hljs-type">string</span>; Age<span class="hljs-operator">:</span> <span class="hljs-type">int</span> }
""");
    }

    [Fact]
    public void Type_RecordWithCtor()
    {
        AssertHighlighter("fsharp",
"""
type User = {
    Name: string
    Age: int
    CreatedAt: System.DateTime
}
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">User</span> <span class="hljs-operator">=</span> {
    Name<span class="hljs-operator">:</span> <span class="hljs-type">string</span>
    Age<span class="hljs-operator">:</span> <span class="hljs-type">int</span>
    CreatedAt<span class="hljs-operator">:</span> System.DateTime
}
""");
    }

    [Fact]
    public void Type_AnonymousRecord()
    {
        AssertHighlighter("fsharp",
"""
let user = {| Name = "alice"; Age = 30 |}
""",
"""
<span class="hljs-keyword">let</span> user <span class="hljs-operator">=</span> {<span class="hljs-operator">|</span> Name <span class="hljs-operator">=</span> <span class="hljs-string">&quot;alice&quot;</span>; Age <span class="hljs-operator">=</span> <span class="hljs-number">30</span> <span class="hljs-operator">|</span>}
""");
    }

    [Fact]
    public void Type_AnonymousStructRecord()
    {
        AssertHighlighter("fsharp",
"""
let point = struct {| X = 1.0; Y = 2.0 |}
""",
"""
<span class="hljs-keyword">let</span> point <span class="hljs-operator">=</span> <span class="hljs-keyword">struct</span> {<span class="hljs-operator">|</span> X <span class="hljs-operator">=</span> <span class="hljs-number">1.0</span>; Y <span class="hljs-operator">=</span> <span class="hljs-number">2.0</span> <span class="hljs-operator">|</span>}
""");
    }

    [Fact]
    public void Type_RecordStruct()
    {
        AssertHighlighter("fsharp",
"""
[<Struct>]
type Point = { X: double; Y: double }
""",
"""
<span class="hljs-meta">[&lt;Struct&gt;]</span>
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Point</span> <span class="hljs-operator">=</span> { X<span class="hljs-operator">:</span> <span class="hljs-type">double</span>; Y<span class="hljs-operator">:</span> <span class="hljs-type">double</span> }
""");
    }

    [Fact]
    public void Type_RecordWithMembers()
    {
        AssertHighlighter("fsharp",
"""
type User = {
    Name: string
    Age: int
}
    member this.Display = sprintf "%s (%d)" this.Name this.Age
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">User</span> <span class="hljs-operator">=</span> {
    Name<span class="hljs-operator">:</span> <span class="hljs-type">string</span>
    Age<span class="hljs-operator">:</span> <span class="hljs-type">int</span>
}
    <span class="hljs-keyword">member</span> this.Display <span class="hljs-operator">=</span> <span class="hljs-built_in">sprintf</span> <span class="hljs-string">&quot;%s (%d)&quot;</span> this.Name this.Age
""");
    }

    [Fact]
    public void Type_DiscriminatedUnion()
    {
        AssertHighlighter("fsharp",
"""
type Shape =
    | Circle of radius: double
    | Rectangle of width: double * height: double
    | Triangle of double * double
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Shape</span> <span class="hljs-operator">=</span>
    <span class="hljs-operator">|</span> Circle <span class="hljs-keyword">of</span> radius<span class="hljs-operator">:</span> <span class="hljs-type">double</span>
    <span class="hljs-operator">|</span> Rectangle <span class="hljs-keyword">of</span> width<span class="hljs-operator">:</span> <span class="hljs-type">double</span> <span class="hljs-operator">*</span> height<span class="hljs-operator">:</span> <span class="hljs-type">double</span>
    <span class="hljs-operator">|</span> Triangle <span class="hljs-keyword">of</span> <span class="hljs-type">double</span> <span class="hljs-operator">*</span> <span class="hljs-type">double</span>
""");
    }

    [Fact]
    public void Type_OptionUnion()
    {
        AssertHighlighter("fsharp",
"""
type 'a Tree =
    | Leaf
    | Node of 'a Tree * 'a * 'a Tree
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-symbol">&#x27;a</span> Tree <span class="hljs-operator">=</span>
    <span class="hljs-operator">|</span> Leaf
    <span class="hljs-operator">|</span> Node <span class="hljs-keyword">of</span> <span class="hljs-symbol">&#x27;a</span> Tree <span class="hljs-operator">*</span> <span class="hljs-symbol">&#x27;a</span> <span class="hljs-operator">*</span> <span class="hljs-symbol">&#x27;a</span> Tree
""");
    }

    [Fact]
    public void Type_Enum()
    {
        AssertHighlighter("fsharp",
"""
type Color =
    | Red = 1
    | Green = 2
    | Blue = 4
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Color</span> <span class="hljs-operator">=</span>
    <span class="hljs-operator">|</span> Red <span class="hljs-operator">=</span> <span class="hljs-number">1</span>
    <span class="hljs-operator">|</span> Green <span class="hljs-operator">=</span> <span class="hljs-number">2</span>
    <span class="hljs-operator">|</span> Blue <span class="hljs-operator">=</span> <span class="hljs-number">4</span>
""");
    }

    [Fact]
    public void Type_Abbreviation()
    {
        AssertHighlighter("fsharp",
"""
type UserId = int
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">UserId</span> <span class="hljs-operator">=</span> int
""");
    }

    [Fact]
    public void Type_AbbreviationGeneric()
    {
        AssertHighlighter("fsharp",
"""
type StringList = List<string>
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">StringList</span> <span class="hljs-operator">=</span> List<span class="hljs-operator">&lt;</span>string<span class="hljs-operator">&gt;</span>
""");
    }

    [Fact]
    public void Type_Class()
    {
        AssertHighlighter("fsharp",
"""
type Logger(name: string) =
    member this.Name = name
    member this.Log(msg: string) =
        printfn "[%s] %s" name msg
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Logger</span>(name<span class="hljs-operator">:</span> <span class="hljs-type">string</span>) <span class="hljs-operator">=</span>
    <span class="hljs-keyword">member</span> this.Name <span class="hljs-operator">=</span> name
    <span class="hljs-keyword">member</span> this.Log(msg<span class="hljs-operator">:</span> <span class="hljs-type">string</span>) <span class="hljs-operator">=</span>
        <span class="hljs-built_in">printfn</span> <span class="hljs-string">&quot;[%s] %s&quot;</span> name msg
""");
    }

    [Fact]
    public void Type_ClassPrimaryCtor()
    {
        AssertHighlighter("fsharp",
"""
type User(name: string, age: int) =
    let mutable _name = name
    member this.Name with get() = _name and set v = _name <- v
    member this.Age = age
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">User</span>(name<span class="hljs-operator">:</span> <span class="hljs-type">string</span>, age<span class="hljs-operator">:</span> <span class="hljs-type">int</span>) <span class="hljs-operator">=</span>
    <span class="hljs-keyword">let</span> <span class="hljs-keyword">mutable</span> _name <span class="hljs-operator">=</span> name
    <span class="hljs-keyword">member</span> this.Name <span class="hljs-keyword">with</span> <span class="hljs-built_in">get</span>() <span class="hljs-operator">=</span> _name <span class="hljs-keyword">and</span> <span class="hljs-built_in">set</span> v <span class="hljs-operator">=</span> _name <span class="hljs-operator">&lt;-</span> v
    <span class="hljs-keyword">member</span> this.Age <span class="hljs-operator">=</span> age
""");
    }

    [Fact]
    public void Type_ClassMultipleCtors()
    {
        AssertHighlighter("fsharp",
"""
type User(name: string, age: int) =
    new(name) = User(name, 0)
    member this.Display = sprintf "%s (%d)" name age
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">User</span>(name<span class="hljs-operator">:</span> <span class="hljs-type">string</span>, age<span class="hljs-operator">:</span> <span class="hljs-type">int</span>) <span class="hljs-operator">=</span>
    <span class="hljs-keyword">new</span>(name) <span class="hljs-operator">=</span> User(name, <span class="hljs-number">0</span>)
    <span class="hljs-keyword">member</span> this.Display <span class="hljs-operator">=</span> <span class="hljs-built_in">sprintf</span> <span class="hljs-string">&quot;%s (%d)&quot;</span> name age
""");
    }

    [Fact]
    public void Type_Inheritance()
    {
        AssertHighlighter("fsharp",
"""
type Employee(name) =
    member this.Name = name

type Manager(name, team: string list) =
    inherit Employee(name)
    member this.Team = team
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Employee</span>(name) <span class="hljs-operator">=</span>
    <span class="hljs-keyword">member</span> this.Name <span class="hljs-operator">=</span> name

<span class="hljs-keyword">type</span> <span class="hljs-title class_">Manager</span>(name, team<span class="hljs-operator">:</span> <span class="hljs-type">string</span> <span class="hljs-type">list</span>) <span class="hljs-operator">=</span>
    <span class="hljs-keyword">inherit</span> Employee(name)
    <span class="hljs-keyword">member</span> this.Team <span class="hljs-operator">=</span> team
""");
    }

    [Fact]
    public void Type_Interface()
    {
        AssertHighlighter("fsharp",
"""
type IShape =
    abstract member Area : double
    abstract member Draw : unit -> unit
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">IShape</span> <span class="hljs-operator">=</span>
    <span class="hljs-keyword">abstract</span> <span class="hljs-keyword">member</span> Area <span class="hljs-operator">:</span> <span class="hljs-type">double</span>
    <span class="hljs-keyword">abstract</span> <span class="hljs-keyword">member</span> Draw <span class="hljs-operator">:</span> <span class="hljs-type">unit</span> <span class="hljs-operator">-&gt;</span> <span class="hljs-type">unit</span>
""");
    }

    [Fact]
    public void Type_InterfaceImpl()
    {
        AssertHighlighter("fsharp",
"""
type Circle(radius: double) =
    interface IShape with
        member this.Area = System.Math.PI * radius * radius
        member this.Draw () = printfn "circle"
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Circle</span>(radius<span class="hljs-operator">:</span> <span class="hljs-type">double</span>) <span class="hljs-operator">=</span>
    <span class="hljs-keyword">interface</span> IShape <span class="hljs-keyword">with</span>
        <span class="hljs-keyword">member</span> this.Area <span class="hljs-operator">=</span> System.Math.PI <span class="hljs-operator">*</span> radius <span class="hljs-operator">*</span> radius
        <span class="hljs-keyword">member</span> this.Draw () <span class="hljs-operator">=</span> <span class="hljs-built_in">printfn</span> <span class="hljs-string">&quot;circle&quot;</span>
""");
    }

    [Fact]
    public void Type_ObjectExpression()
    {
        AssertHighlighter("fsharp",
"""
let logger =
    { new ILogger with
        member _.Log(msg) = printfn "%s" msg
        member _.Close() = () }
""",
"""
<span class="hljs-keyword">let</span> logger <span class="hljs-operator">=</span>
    { <span class="hljs-keyword">new</span> ILogger <span class="hljs-keyword">with</span>
        <span class="hljs-keyword">member</span> _.Log(msg) <span class="hljs-operator">=</span> <span class="hljs-built_in">printfn</span> <span class="hljs-string">&quot;%s&quot;</span> msg
        <span class="hljs-keyword">member</span> _.Close() <span class="hljs-operator">=</span> () }
""");
    }

    [Fact]
    public void Type_StructFields()
    {
        AssertHighlighter("fsharp",
"""
[<Struct>]
type Vec2 = { X: float; Y: float }
""",
"""
<span class="hljs-meta">[&lt;Struct&gt;]</span>
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Vec2</span> <span class="hljs-operator">=</span> { X<span class="hljs-operator">:</span> <span class="hljs-type">float</span>; Y<span class="hljs-operator">:</span> <span class="hljs-type">float</span> }
""");
    }

    [Fact]
    public void Pattern_MatchLiteral()
    {
        AssertHighlighter("fsharp",
"""
let label x =
    match x with
    | 0 -> "zero"
    | 1 -> "one"
    | _ -> "many"
""",
"""
<span class="hljs-keyword">let</span> label x <span class="hljs-operator">=</span>
    <span class="hljs-keyword">match</span> x <span class="hljs-keyword">with</span>
    <span class="hljs-operator">|</span> <span class="hljs-number">0</span> <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;zero&quot;</span>
    <span class="hljs-operator">|</span> <span class="hljs-number">1</span> <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;one&quot;</span>
    <span class="hljs-operator">|</span> _ <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;many&quot;</span>
""");
    }

    [Fact]
    public void Pattern_MatchTuple()
    {
        AssertHighlighter("fsharp",
"""
let quadrant (x, y) =
    match x, y with
    | x, y when x > 0 && y > 0 -> "Q1"
    | x, y when x < 0 && y > 0 -> "Q2"
    | _ -> "elsewhere"
""",
"""
<span class="hljs-keyword">let</span> quadrant (x, y) <span class="hljs-operator">=</span>
    <span class="hljs-keyword">match</span> x, y <span class="hljs-keyword">with</span>
    <span class="hljs-operator">|</span> x, y <span class="hljs-keyword">when</span> x <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span> <span class="hljs-operator">&amp;&amp;</span> y <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span> <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;Q1&quot;</span>
    <span class="hljs-operator">|</span> x, y <span class="hljs-keyword">when</span> x <span class="hljs-operator">&lt;</span> <span class="hljs-number">0</span> <span class="hljs-operator">&amp;&amp;</span> y <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span> <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;Q2&quot;</span>
    <span class="hljs-operator">|</span> _ <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;elsewhere&quot;</span>
""");
    }

    [Fact]
    public void Pattern_MatchRecord()
    {
        AssertHighlighter("fsharp",
"""
match user with
| { Age = age } when age >= 18 -> "adult"
| _ -> "minor"
""",
"""
<span class="hljs-keyword">match</span> user <span class="hljs-keyword">with</span>
<span class="hljs-operator">|</span> { Age <span class="hljs-operator">=</span> age } <span class="hljs-keyword">when</span> age <span class="hljs-operator">&gt;=</span> <span class="hljs-number">18</span> <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;adult&quot;</span>
<span class="hljs-operator">|</span> _ <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;minor&quot;</span>
""");
    }

    [Fact]
    public void Pattern_MatchUnion()
    {
        AssertHighlighter("fsharp",
"""
let area shape =
    match shape with
    | Circle r -> System.Math.PI * r * r
    | Rectangle (w, h) -> w * h
    | Triangle (b, h) -> b * h / 2.0
""",
"""
<span class="hljs-keyword">let</span> area shape <span class="hljs-operator">=</span>
    <span class="hljs-keyword">match</span> shape <span class="hljs-keyword">with</span>
    <span class="hljs-operator">|</span> Circle r <span class="hljs-operator">-&gt;</span> System.Math.PI <span class="hljs-operator">*</span> r <span class="hljs-operator">*</span> r
    <span class="hljs-operator">|</span> Rectangle (w, h) <span class="hljs-operator">-&gt;</span> w <span class="hljs-operator">*</span> h
    <span class="hljs-operator">|</span> Triangle (b, h) <span class="hljs-operator">-&gt;</span> b <span class="hljs-operator">*</span> h <span class="hljs-operator">/</span> <span class="hljs-number">2.0</span>
""");
    }

    [Fact]
    public void Pattern_MatchList()
    {
        AssertHighlighter("fsharp",
"""
let firstOrZero list =
    match list with
    | [] -> 0
    | head :: _ -> head
""",
"""
<span class="hljs-keyword">let</span> firstOrZero list <span class="hljs-operator">=</span>
    <span class="hljs-keyword">match</span> list <span class="hljs-keyword">with</span>
    <span class="hljs-operator">|</span> [] <span class="hljs-operator">-&gt;</span> <span class="hljs-number">0</span>
    <span class="hljs-operator">|</span> head <span class="hljs-operator">::</span> _ <span class="hljs-operator">-&gt;</span> head
""");
    }

    [Fact]
    public void Pattern_MatchListPair()
    {
        AssertHighlighter("fsharp",
"""
let rec sumPairs list =
    match list with
    | [] -> 0
    | a :: b :: rest -> a + b + sumPairs rest
    | _ :: rest -> sumPairs rest
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-keyword">rec</span> sumPairs list <span class="hljs-operator">=</span>
    <span class="hljs-keyword">match</span> list <span class="hljs-keyword">with</span>
    <span class="hljs-operator">|</span> [] <span class="hljs-operator">-&gt;</span> <span class="hljs-number">0</span>
    <span class="hljs-operator">|</span> a <span class="hljs-operator">::</span> b <span class="hljs-operator">::</span> rest <span class="hljs-operator">-&gt;</span> a <span class="hljs-operator">+</span> b <span class="hljs-operator">+</span> sumPairs rest
    <span class="hljs-operator">|</span> _ <span class="hljs-operator">::</span> rest <span class="hljs-operator">-&gt;</span> sumPairs rest
""");
    }

    [Fact]
    public void Pattern_MatchArray()
    {
        AssertHighlighter("fsharp",
"""
match arr with
| [| a; b; c |] -> printfn "triplet"
| _ -> printfn "other"
""",
"""
<span class="hljs-keyword">match</span> arr <span class="hljs-keyword">with</span>
<span class="hljs-operator">|</span> [<span class="hljs-operator">|</span> a; b; c <span class="hljs-operator">|</span>] <span class="hljs-operator">-&gt;</span> <span class="hljs-built_in">printfn</span> <span class="hljs-string">&quot;triplet&quot;</span>
<span class="hljs-operator">|</span> _ <span class="hljs-operator">-&gt;</span> <span class="hljs-built_in">printfn</span> <span class="hljs-string">&quot;other&quot;</span>
""");
    }

    [Fact]
    public void Pattern_MatchWhen()
    {
        AssertHighlighter("fsharp",
"""
match x with
| n when n > 0 -> "positive"
| n when n < 0 -> "negative"
| _            -> "zero"
""",
"""
<span class="hljs-keyword">match</span> x <span class="hljs-keyword">with</span>
<span class="hljs-operator">|</span> n <span class="hljs-keyword">when</span> n <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span> <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;positive&quot;</span>
<span class="hljs-operator">|</span> n <span class="hljs-keyword">when</span> n <span class="hljs-operator">&lt;</span> <span class="hljs-number">0</span> <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;negative&quot;</span>
<span class="hljs-operator">|</span> _            <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;zero&quot;</span>
""");
    }

    [Fact]
    public void Pattern_MatchTypeTest()
    {
        AssertHighlighter("fsharp",
"""
match obj with
| :? string as s -> sprintf "string: %s" s
| :? int as n    -> sprintf "int: %d" n
| _              -> "other"
""",
"""
<span class="hljs-keyword">match</span> obj <span class="hljs-keyword">with</span>
<span class="hljs-operator">|</span> <span class="hljs-operator">:?</span> string <span class="hljs-keyword">as</span> s <span class="hljs-operator">-&gt;</span> <span class="hljs-built_in">sprintf</span> <span class="hljs-string">&quot;string: %s&quot;</span> s
<span class="hljs-operator">|</span> <span class="hljs-operator">:?</span> int <span class="hljs-keyword">as</span> n    <span class="hljs-operator">-&gt;</span> <span class="hljs-built_in">sprintf</span> <span class="hljs-string">&quot;int: %d&quot;</span> n
<span class="hljs-operator">|</span> _              <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;other&quot;</span>
""");
    }

    [Fact]
    public void Pattern_MatchOr()
    {
        AssertHighlighter("fsharp",
"""
match status with
| "open" | "pending" -> active ()
| _                  -> inactive ()
""",
"""
<span class="hljs-keyword">match</span> status <span class="hljs-keyword">with</span>
<span class="hljs-operator">|</span> <span class="hljs-string">&quot;open&quot;</span> <span class="hljs-operator">|</span> <span class="hljs-string">&quot;pending&quot;</span> <span class="hljs-operator">-&gt;</span> active ()
<span class="hljs-operator">|</span> _                  <span class="hljs-operator">-&gt;</span> inactive ()
""");
    }

    [Fact]
    public void Pattern_MatchAnd()
    {
        AssertHighlighter("fsharp",
"""
match value with
| Some x & Some y -> printfn "%A %A" x y
| _ -> ()
""",
"""
<span class="hljs-keyword">match</span> value <span class="hljs-keyword">with</span>
<span class="hljs-operator">|</span> <span class="hljs-literal">Some</span> x <span class="hljs-operator">&amp;</span> <span class="hljs-literal">Some</span> y <span class="hljs-operator">-&gt;</span> <span class="hljs-built_in">printfn</span> <span class="hljs-string">&quot;%A %A&quot;</span> x y
<span class="hljs-operator">|</span> _ <span class="hljs-operator">-&gt;</span> ()
""");
    }

    [Fact]
    public void Pattern_MatchAs()
    {
        AssertHighlighter("fsharp",
"""
match shape with
| Circle r as c when r > 10.0 -> use c
| _ -> ()
""",
"""
<span class="hljs-keyword">match</span> shape <span class="hljs-keyword">with</span>
<span class="hljs-operator">|</span> Circle r <span class="hljs-keyword">as</span> c <span class="hljs-keyword">when</span> r <span class="hljs-operator">&gt;</span> <span class="hljs-number">10.0</span> <span class="hljs-operator">-&gt;</span> <span class="hljs-keyword">use</span> c
<span class="hljs-operator">|</span> _ <span class="hljs-operator">-&gt;</span> ()
""");
    }

    [Fact]
    public void Pattern_FunctionMatch()
    {
        AssertHighlighter("fsharp",
"""
let label = function
    | 0 -> "zero"
    | n when n > 0 -> "positive"
    | _ -> "negative"
""",
"""
<span class="hljs-keyword">let</span> label <span class="hljs-operator">=</span> <span class="hljs-keyword">function</span>
    <span class="hljs-operator">|</span> <span class="hljs-number">0</span> <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;zero&quot;</span>
    <span class="hljs-operator">|</span> n <span class="hljs-keyword">when</span> n <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span> <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;positive&quot;</span>
    <span class="hljs-operator">|</span> _ <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;negative&quot;</span>
""");
    }

    [Fact]
    public void Pattern_ActivePattern()
    {
        AssertHighlighter("fsharp",
"""
let (|Even|Odd|) n =
    if n % 2 = 0 then Even else Odd
""",
"""
<span class="hljs-keyword">let</span> (<span class="hljs-operator">|</span>Even<span class="hljs-operator">|</span>Odd<span class="hljs-operator">|</span>) n <span class="hljs-operator">=</span>
    <span class="hljs-keyword">if</span> n <span class="hljs-operator">%</span> <span class="hljs-number">2</span> <span class="hljs-operator">=</span> <span class="hljs-number">0</span> <span class="hljs-keyword">then</span> Even <span class="hljs-keyword">else</span> Odd
""");
    }

    [Fact]
    public void Pattern_ActivePatternParam()
    {
        AssertHighlighter("fsharp",
"""
let (|StartsWith|_|) (prefix: string) (s: string) =
    if s.StartsWith(prefix) then Some(s.Substring(prefix.Length))
    else None
""",
"""
<span class="hljs-keyword">let</span> (<span class="hljs-operator">|</span>StartsWith<span class="hljs-operator">|</span>_<span class="hljs-operator">|</span>) (prefix<span class="hljs-operator">:</span> <span class="hljs-type">string</span>) (s<span class="hljs-operator">:</span> <span class="hljs-type">string</span>) <span class="hljs-operator">=</span>
    <span class="hljs-keyword">if</span> s.StartsWith(prefix) <span class="hljs-keyword">then</span> <span class="hljs-literal">Some</span>(s.Substring(prefix.Length))
    <span class="hljs-keyword">else</span> <span class="hljs-literal">None</span>
""");
    }

    [Fact]
    public void Pattern_ActivePatternUsage()
    {
        AssertHighlighter("fsharp",
"""
match path with
| StartsWith "/api/" rest -> handleApi rest
| _                       -> handleStatic path
""",
"""
<span class="hljs-keyword">match</span> path <span class="hljs-keyword">with</span>
<span class="hljs-operator">|</span> StartsWith <span class="hljs-string">&quot;/api/&quot;</span> rest <span class="hljs-operator">-&gt;</span> handleApi rest
<span class="hljs-operator">|</span> _                       <span class="hljs-operator">-&gt;</span> handleStatic path
""");
    }

    [Fact]
    public void Lambda_Simple()
    {
        AssertHighlighter("fsharp",
"""
let square = fun x -> x * x
""",
"""
<span class="hljs-keyword">let</span> square <span class="hljs-operator">=</span> <span class="hljs-keyword">fun</span> x <span class="hljs-operator">-&gt;</span> x <span class="hljs-operator">*</span> x
""");
    }

    [Fact]
    public void Lambda_TwoArgs()
    {
        AssertHighlighter("fsharp",
"""
let add = fun a b -> a + b
""",
"""
<span class="hljs-keyword">let</span> add <span class="hljs-operator">=</span> <span class="hljs-keyword">fun</span> a b <span class="hljs-operator">-&gt;</span> a <span class="hljs-operator">+</span> b
""");
    }

    [Fact]
    public void Lambda_Typed()
    {
        AssertHighlighter("fsharp",
"""
let add = fun (a: int) (b: int) -> a + b
""",
"""
<span class="hljs-keyword">let</span> add <span class="hljs-operator">=</span> <span class="hljs-keyword">fun</span> (a<span class="hljs-operator">:</span> <span class="hljs-type">int</span>) (b<span class="hljs-operator">:</span> <span class="hljs-type">int</span>) <span class="hljs-operator">-&gt;</span> a <span class="hljs-operator">+</span> b
""");
    }

    [Fact]
    public void Lambda_WithMatch()
    {
        AssertHighlighter("fsharp",
"""
let label = fun n ->
    match n with
    | 0 -> "zero"
    | _ -> "non-zero"
""",
"""
<span class="hljs-keyword">let</span> label <span class="hljs-operator">=</span> <span class="hljs-keyword">fun</span> n <span class="hljs-operator">-&gt;</span>
    <span class="hljs-keyword">match</span> n <span class="hljs-keyword">with</span>
    <span class="hljs-operator">|</span> <span class="hljs-number">0</span> <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;zero&quot;</span>
    <span class="hljs-operator">|</span> _ <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;non-zero&quot;</span>
""");
    }

    [Fact]
    public void Lambda_UnderscoreShortcut()
    {
        AssertHighlighter("fsharp",
"""
let names = users |> List.map _.Name
""",
"""
<span class="hljs-keyword">let</span> names <span class="hljs-operator">=</span> users <span class="hljs-operator">|&gt;</span> List.map _.Name
""");
    }

    [Fact]
    public void String_Simple()
    {
        AssertHighlighter("fsharp",
"""
let s = "hello"
""",
"""
<span class="hljs-keyword">let</span> s <span class="hljs-operator">=</span> <span class="hljs-string">&quot;hello&quot;</span>
""");
    }

    [Fact]
    public void String_EscapeQuote()
    {
        AssertHighlighter("fsharp",
"""
let s = "She said \"hi\""
""",
"""
<span class="hljs-keyword">let</span> s <span class="hljs-operator">=</span> <span class="hljs-string">&quot;She said \&quot;hi\&quot;&quot;</span>
""");
    }

    [Fact]
    public void String_EscapeNewline()
    {
        AssertHighlighter("fsharp",
"""
let s = "line1\nline2"
""",
"""
<span class="hljs-keyword">let</span> s <span class="hljs-operator">=</span> <span class="hljs-string">&quot;line1\nline2&quot;</span>
""");
    }

    [Fact]
    public void String_Verbatim()
    {
        AssertHighlighter("fsharp",
"""
let path = @"C:\Users\alice"
""",
"""
<span class="hljs-keyword">let</span> path <span class="hljs-operator">=</span> <span class="hljs-string">@&quot;C:\Users\alice&quot;</span>
""");
    }

    [Fact]
    public void String_TripleQuoted()
    {
        AssertHighlighter("fsharp",
""""
let json = """{ "name": "alice" }"""
"""",
"""
<span class="hljs-keyword">let</span> json <span class="hljs-operator">=</span> <span class="hljs-string">&quot;&quot;&quot;{ &quot;name&quot;: &quot;alice&quot; }&quot;&quot;&quot;</span>
""");
    }

    [Fact]
    public void String_TripleMultiLine()
    {
        AssertHighlighter("fsharp",
""""
let body = """
First line
Second line
"""
"""",
"""
<span class="hljs-keyword">let</span> body <span class="hljs-operator">=</span> <span class="hljs-string">&quot;&quot;&quot;
First line
Second line
&quot;&quot;&quot;</span>
""");
    }

    [Fact]
    public void String_Interpolation()
    {
        AssertHighlighter("fsharp",
"""
let msg = $"Hello {name}"
""",
"""
<span class="hljs-keyword">let</span> msg <span class="hljs-operator">=</span> <span class="hljs-string">$&quot;Hello <span class="hljs-subst">{name}</span>&quot;</span>
""");
    }

    [Fact]
    public void String_InterpolationFormat()
    {
        AssertHighlighter("fsharp",
"""
let msg = $"Price: {price:F2}"
""",
"""
<span class="hljs-keyword">let</span> msg <span class="hljs-operator">=</span> <span class="hljs-string">$&quot;Price: <span class="hljs-subst">{price<span class="hljs-operator">:</span>F2}&quot;</span></span>
""");
    }

    [Fact]
    public void String_InterpolationVerbatim()
    {
        AssertHighlighter("fsharp",
"""
let msg = $@"Path: {dir}\file.txt"
""",
"""
<span class="hljs-keyword">let</span> msg <span class="hljs-operator">=</span> <span class="hljs-string">$@&quot;Path: <span class="hljs-subst">{dir}</span>\file.txt&quot;</span>
""");
    }

    [Fact]
    public void String_InterpolationTriple()
    {
        AssertHighlighter("fsharp",
""""
let msg = $"""multi-line {name}
and more"""
"""",
"""
<span class="hljs-keyword">let</span> msg <span class="hljs-operator">=</span> <span class="hljs-string">$&quot;&quot;&quot;multi-line <span class="hljs-subst">{name}</span>
and more&quot;&quot;&quot;</span>
""");
    }

    [Fact]
    public void String_Utf8Literal()
    {
        AssertHighlighter("fsharp",
"""
let bytes = "hello"B
""",
"""
<span class="hljs-keyword">let</span> bytes <span class="hljs-operator">=</span> <span class="hljs-string">&quot;hello&quot;</span>B
""");
    }

    [Fact]
    public void Number_Integer()
    {
        AssertHighlighter("fsharp",
"""
let n = 42
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> <span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void Number_Int32()
    {
        AssertHighlighter("fsharp",
"""
let n = 42l
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> <span class="hljs-number">42</span>l
""");
    }

    [Fact]
    public void Number_Int64()
    {
        AssertHighlighter("fsharp",
"""
let n = 42L
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> <span class="hljs-number">42</span>L
""");
    }

    [Fact]
    public void Number_UInt32()
    {
        AssertHighlighter("fsharp",
"""
let n = 42u
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> <span class="hljs-number">42</span>u
""");
    }

    [Fact]
    public void Number_UInt64()
    {
        AssertHighlighter("fsharp",
"""
let n = 42UL
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> <span class="hljs-number">42</span>UL
""");
    }

    [Fact]
    public void Number_BigInteger()
    {
        AssertHighlighter("fsharp",
"""
let n = 42I
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> <span class="hljs-number">42</span>I
""");
    }

    [Fact]
    public void Number_Byte()
    {
        AssertHighlighter("fsharp",
"""
let n = 42uy
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> <span class="hljs-number">42</span>uy
""");
    }

    [Fact]
    public void Number_Sbyte()
    {
        AssertHighlighter("fsharp",
"""
let n = 42y
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> <span class="hljs-number">42</span>y
""");
    }

    [Fact]
    public void Number_Float()
    {
        AssertHighlighter("fsharp",
"""
let n = 3.14
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> <span class="hljs-number">3.14</span>
""");
    }

    [Fact]
    public void Number_FloatSuffix()
    {
        AssertHighlighter("fsharp",
"""
let n = 3.14f
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> <span class="hljs-number">3.14</span>f
""");
    }

    [Fact]
    public void Number_Decimal()
    {
        AssertHighlighter("fsharp",
"""
let n = 9.99m
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> <span class="hljs-number">9.99</span>m
""");
    }

    [Fact]
    public void Number_Hex()
    {
        AssertHighlighter("fsharp",
"""
let n = 0xDEADBEEF
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> <span class="hljs-number">0xDEADBEEF</span>
""");
    }

    [Fact]
    public void Number_Octal()
    {
        AssertHighlighter("fsharp",
"""
let n = 0o755
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> <span class="hljs-number">0</span>o755
""");
    }

    [Fact]
    public void Number_Binary()
    {
        AssertHighlighter("fsharp",
"""
let n = 0b1010_1100
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> <span class="hljs-number">0b1010</span>_1100
""");
    }

    [Fact]
    public void Number_Exponent()
    {
        AssertHighlighter("fsharp",
"""
let n = 1.5e10
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> <span class="hljs-number">1.5e10</span>
""");
    }

    [Fact]
    public void Number_UnitOfMeasure()
    {
        AssertHighlighter("fsharp",
"""
let distance = 5.0<m>
let time = 2.0<s>
let speed = distance / time
""",
"""
<span class="hljs-keyword">let</span> distance <span class="hljs-operator">=</span> <span class="hljs-number">5.0</span><span class="hljs-operator">&lt;</span>m<span class="hljs-operator">&gt;</span>
<span class="hljs-keyword">let</span> time <span class="hljs-operator">=</span> <span class="hljs-number">2.0</span><span class="hljs-operator">&lt;</span>s<span class="hljs-operator">&gt;</span>
<span class="hljs-keyword">let</span> speed <span class="hljs-operator">=</span> distance <span class="hljs-operator">/</span> time
""");
    }

    [Fact]
    public void ComputationExpression_Async()
    {
        AssertHighlighter("fsharp",
"""
let asyncWork =
    async {
        let! data = client.GetStringAsync(url)
        return data.Length
    }
""",
"""
<span class="hljs-keyword">let</span> asyncWork <span class="hljs-operator">=</span>
    <span class="hljs-keyword">async</span> {
        <span class="hljs-keyword">let!</span> data <span class="hljs-operator">=</span> client.GetStringAsync(url)
        <span class="hljs-keyword">return</span> data.Length
    }
""");
    }

    [Fact]
    public void ComputationExpression_Task()
    {
        AssertHighlighter("fsharp",
"""
let getDataAsync () =
    task {
        let! response = httpClient.GetAsync(url)
        let! body = response.Content.ReadAsStringAsync()
        return body
    }
""",
"""
<span class="hljs-keyword">let</span> getDataAsync () <span class="hljs-operator">=</span>
    <span class="hljs-keyword">task</span> {
        <span class="hljs-keyword">let!</span> response <span class="hljs-operator">=</span> httpClient.GetAsync(url)
        <span class="hljs-keyword">let!</span> body <span class="hljs-operator">=</span> response.Content.ReadAsStringAsync()
        <span class="hljs-keyword">return</span> body
    }
""");
    }

    [Fact]
    public void ComputationExpression_Seq()
    {
        AssertHighlighter("fsharp",
"""
let nums =
    seq {
        for i in 1 .. 10 do
            yield i * i
    }
""",
"""
<span class="hljs-keyword">let</span> nums <span class="hljs-operator">=</span>
    <span class="hljs-keyword">seq</span> {
        <span class="hljs-keyword">for</span> i <span class="hljs-keyword">in</span> <span class="hljs-number">1</span> <span class="hljs-operator">..</span> <span class="hljs-number">10</span> <span class="hljs-keyword">do</span>
            <span class="hljs-keyword">yield</span> i <span class="hljs-operator">*</span> i
    }
""");
    }

    [Fact]
    public void ComputationExpression_List()
    {
        AssertHighlighter("fsharp",
"""
let nums = [ for i in 1 .. 5 do yield i * 2 ]
""",
"""
<span class="hljs-keyword">let</span> nums <span class="hljs-operator">=</span> [ <span class="hljs-keyword">for</span> i <span class="hljs-keyword">in</span> <span class="hljs-number">1</span> <span class="hljs-operator">..</span> <span class="hljs-number">5</span> <span class="hljs-keyword">do</span> <span class="hljs-keyword">yield</span> i <span class="hljs-operator">*</span> <span class="hljs-number">2</span> ]
""");
    }

    [Fact]
    public void ComputationExpression_YieldFrom()
    {
        AssertHighlighter("fsharp",
"""
let nested =
    seq {
        yield 1
        yield! [2; 3; 4]
        yield 5
    }
""",
"""
<span class="hljs-keyword">let</span> nested <span class="hljs-operator">=</span>
    <span class="hljs-keyword">seq</span> {
        <span class="hljs-keyword">yield</span> <span class="hljs-number">1</span>
        <span class="hljs-keyword">yield!</span> [<span class="hljs-number">2</span>; <span class="hljs-number">3</span>; <span class="hljs-number">4</span>]
        <span class="hljs-keyword">yield</span> <span class="hljs-number">5</span>
    }
""");
    }

    [Fact]
    public void ComputationExpression_Query()
    {
        AssertHighlighter("fsharp",
"""
let q =
    query {
        for u in users do
        where u.IsActive
        sortBy u.Age
        select u.Name
    }
""",
"""
<span class="hljs-keyword">let</span> q <span class="hljs-operator">=</span>
    <span class="hljs-keyword">query</span> {
        <span class="hljs-keyword">for</span> u <span class="hljs-keyword">in</span> users <span class="hljs-keyword">do</span>
        where u.IsActive
        sortBy u.Age
        select u.Name
    }
""");
    }

    [Fact]
    public void ComputationExpression_Option()
    {
        AssertHighlighter("fsharp",
"""
let lookup =
    option {
        let! name = tryGetName ()
        let! age = tryGetAge ()
        return name, age
    }
""",
"""
<span class="hljs-keyword">let</span> lookup <span class="hljs-operator">=</span>
    <span class="hljs-keyword">option</span> {
        <span class="hljs-keyword">let!</span> name <span class="hljs-operator">=</span> tryGetName ()
        <span class="hljs-keyword">let!</span> age <span class="hljs-operator">=</span> tryGetAge ()
        <span class="hljs-keyword">return</span> name, age
    }
""");
    }

    [Fact]
    public void Collection_List()
    {
        AssertHighlighter("fsharp",
"""
let nums = [1; 2; 3; 4; 5]
""",
"""
<span class="hljs-keyword">let</span> nums <span class="hljs-operator">=</span> [<span class="hljs-number">1</span>; <span class="hljs-number">2</span>; <span class="hljs-number">3</span>; <span class="hljs-number">4</span>; <span class="hljs-number">5</span>]
""");
    }

    [Fact]
    public void Collection_ListRange()
    {
        AssertHighlighter("fsharp",
"""
let nums = [1 .. 100]
""",
"""
<span class="hljs-keyword">let</span> nums <span class="hljs-operator">=</span> [<span class="hljs-number">1</span> <span class="hljs-operator">..</span> <span class="hljs-number">100</span>]
""");
    }

    [Fact]
    public void Collection_ListRangeStep()
    {
        AssertHighlighter("fsharp",
"""
let nums = [1 .. 2 .. 100]
""",
"""
<span class="hljs-keyword">let</span> nums <span class="hljs-operator">=</span> [<span class="hljs-number">1</span> <span class="hljs-operator">..</span> <span class="hljs-number">2</span> <span class="hljs-operator">..</span> <span class="hljs-number">100</span>]
""");
    }

    [Fact]
    public void Collection_ListComprehension()
    {
        AssertHighlighter("fsharp",
"""
let evens = [ for n in 1 .. 20 do if n % 2 = 0 then yield n ]
""",
"""
<span class="hljs-keyword">let</span> evens <span class="hljs-operator">=</span> [ <span class="hljs-keyword">for</span> n <span class="hljs-keyword">in</span> <span class="hljs-number">1</span> <span class="hljs-operator">..</span> <span class="hljs-number">20</span> <span class="hljs-keyword">do</span> <span class="hljs-keyword">if</span> n <span class="hljs-operator">%</span> <span class="hljs-number">2</span> <span class="hljs-operator">=</span> <span class="hljs-number">0</span> <span class="hljs-keyword">then</span> <span class="hljs-keyword">yield</span> n ]
""");
    }

    [Fact]
    public void Collection_Array()
    {
        AssertHighlighter("fsharp",
"""
let arr = [| 1; 2; 3 |]
""",
"""
<span class="hljs-keyword">let</span> arr <span class="hljs-operator">=</span> [<span class="hljs-operator">|</span> <span class="hljs-number">1</span>; <span class="hljs-number">2</span>; <span class="hljs-number">3</span> <span class="hljs-operator">|</span>]
""");
    }

    [Fact]
    public void Collection_ArrayRange()
    {
        AssertHighlighter("fsharp",
"""
let arr = [| 1 .. 100 |]
""",
"""
<span class="hljs-keyword">let</span> arr <span class="hljs-operator">=</span> [<span class="hljs-operator">|</span> <span class="hljs-number">1</span> <span class="hljs-operator">..</span> <span class="hljs-number">100</span> <span class="hljs-operator">|</span>]
""");
    }

    [Fact]
    public void Collection_Tuple()
    {
        AssertHighlighter("fsharp",
"""
let pair = (1, "one")
""",
"""
<span class="hljs-keyword">let</span> pair <span class="hljs-operator">=</span> (<span class="hljs-number">1</span>, <span class="hljs-string">&quot;one&quot;</span>)
""");
    }

    [Fact]
    public void Collection_TupleStruct()
    {
        AssertHighlighter("fsharp",
"""
let pair = struct (1, "one")
""",
"""
<span class="hljs-keyword">let</span> pair <span class="hljs-operator">=</span> <span class="hljs-keyword">struct</span> (<span class="hljs-number">1</span>, <span class="hljs-string">&quot;one&quot;</span>)
""");
    }

    [Fact]
    public void Collection_Sequence()
    {
        AssertHighlighter("fsharp",
"""
let s = seq { 1; 2; 3 }
""",
"""
<span class="hljs-keyword">let</span> s <span class="hljs-operator">=</span> <span class="hljs-keyword">seq</span> { <span class="hljs-number">1</span>; <span class="hljs-number">2</span>; <span class="hljs-number">3</span> }
""");
    }

    [Fact]
    public void Collection_Set()
    {
        AssertHighlighter("fsharp",
"""
let s = Set.ofList [1; 2; 3]
""",
"""
<span class="hljs-keyword">let</span> s <span class="hljs-operator">=</span> Set.ofList [<span class="hljs-number">1</span>; <span class="hljs-number">2</span>; <span class="hljs-number">3</span>]
""");
    }

    [Fact]
    public void Collection_Map()
    {
        AssertHighlighter("fsharp",
"""
let m = Map [ "alice", 30; "bob", 25 ]
""",
"""
<span class="hljs-keyword">let</span> m <span class="hljs-operator">=</span> Map [ <span class="hljs-string">&quot;alice&quot;</span>, <span class="hljs-number">30</span>; <span class="hljs-string">&quot;bob&quot;</span>, <span class="hljs-number">25</span> ]
""");
    }

    [Fact]
    public void Async_Bind()
    {
        AssertHighlighter("fsharp",
"""
let getDataAsync url =
    async {
        let! response = httpClient.GetStringAsync(url) |> Async.AwaitTask
        return response
    }
""",
"""
<span class="hljs-keyword">let</span> getDataAsync url <span class="hljs-operator">=</span>
    <span class="hljs-keyword">async</span> {
        <span class="hljs-keyword">let!</span> response <span class="hljs-operator">=</span> httpClient.GetStringAsync(url) <span class="hljs-operator">|&gt;</span> Async.AwaitTask
        <span class="hljs-keyword">return</span> response
    }
""");
    }

    [Fact]
    public void Async_Start()
    {
        AssertHighlighter("fsharp",
"""
Async.Start (printfn "hello" |> async.Return)
""",
"""
Async.Start (<span class="hljs-built_in">printfn</span> <span class="hljs-string">&quot;hello&quot;</span> <span class="hljs-operator">|&gt;</span> async.Return)
""");
    }

    [Fact]
    public void Async_Parallel()
    {
        AssertHighlighter("fsharp",
"""
let results =
    [ for url in urls -> fetchAsync url ]
    |> Async.Parallel
    |> Async.RunSynchronously
""",
"""
<span class="hljs-keyword">let</span> results <span class="hljs-operator">=</span>
    [ <span class="hljs-keyword">for</span> url <span class="hljs-keyword">in</span> urls <span class="hljs-operator">-&gt;</span> fetchAsync url ]
    <span class="hljs-operator">|&gt;</span> Async.Parallel
    <span class="hljs-operator">|&gt;</span> Async.RunSynchronously
""");
    }

    [Fact]
    public void Async_Catch()
    {
        AssertHighlighter("fsharp",
"""
async {
    try
        do! work ()
    with
    | ex -> printfn "%s" ex.Message
}
""",
"""
<span class="hljs-keyword">async</span> {
    <span class="hljs-keyword">try</span>
        <span class="hljs-keyword">do!</span> work ()
    <span class="hljs-keyword">with</span>
    <span class="hljs-operator">|</span> ex <span class="hljs-operator">-&gt;</span> <span class="hljs-built_in">printfn</span> <span class="hljs-string">&quot;%s&quot;</span> ex.Message
}
""");
    }

    [Fact]
    public void ControlFlow_IfThen()
    {
        AssertHighlighter("fsharp",
"""
let n = if x > 0 then 1 else -1
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> <span class="hljs-keyword">if</span> x <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span> <span class="hljs-keyword">then</span> <span class="hljs-number">1</span> <span class="hljs-keyword">else</span> <span class="hljs-number">-1</span>
""");
    }

    [Fact]
    public void ControlFlow_IfElIfElse()
    {
        AssertHighlighter("fsharp",
"""
let label =
    if x > 0 then "positive"
    elif x < 0 then "negative"
    else "zero"
""",
"""
<span class="hljs-keyword">let</span> label <span class="hljs-operator">=</span>
    <span class="hljs-keyword">if</span> x <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span> <span class="hljs-keyword">then</span> <span class="hljs-string">&quot;positive&quot;</span>
    <span class="hljs-keyword">elif</span> x <span class="hljs-operator">&lt;</span> <span class="hljs-number">0</span> <span class="hljs-keyword">then</span> <span class="hljs-string">&quot;negative&quot;</span>
    <span class="hljs-keyword">else</span> <span class="hljs-string">&quot;zero&quot;</span>
""");
    }

    [Fact]
    public void ControlFlow_ForLoop()
    {
        AssertHighlighter("fsharp",
"""
for i in 1 .. 10 do
    printfn "%d" i
""",
"""
<span class="hljs-keyword">for</span> i <span class="hljs-keyword">in</span> <span class="hljs-number">1</span> <span class="hljs-operator">..</span> <span class="hljs-number">10</span> <span class="hljs-keyword">do</span>
    <span class="hljs-built_in">printfn</span> <span class="hljs-string">&quot;%d&quot;</span> i
""");
    }

    [Fact]
    public void ControlFlow_ForLoopDownTo()
    {
        AssertHighlighter("fsharp",
"""
for i = 10 downto 1 do
    printfn "%d" i
""",
"""
<span class="hljs-keyword">for</span> i <span class="hljs-operator">=</span> <span class="hljs-number">10</span> <span class="hljs-keyword">downto</span> <span class="hljs-number">1</span> <span class="hljs-keyword">do</span>
    <span class="hljs-built_in">printfn</span> <span class="hljs-string">&quot;%d&quot;</span> i
""");
    }

    [Fact]
    public void ControlFlow_ForInList()
    {
        AssertHighlighter("fsharp",
"""
for user in users do
    printfn "%s" user.Name
""",
"""
<span class="hljs-keyword">for</span> user <span class="hljs-keyword">in</span> users <span class="hljs-keyword">do</span>
    <span class="hljs-built_in">printfn</span> <span class="hljs-string">&quot;%s&quot;</span> user.Name
""");
    }

    [Fact]
    public void ControlFlow_WhileLoop()
    {
        AssertHighlighter("fsharp",
"""
while count > 0 do
    pop ()
    count <- count - 1
""",
"""
<span class="hljs-keyword">while</span> count <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span> <span class="hljs-keyword">do</span>
    pop ()
    count <span class="hljs-operator">&lt;-</span> count <span class="hljs-operator">-</span> <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void ControlFlow_TryWith()
    {
        AssertHighlighter("fsharp",
"""
try
    risky ()
with
| :? System.IO.IOException as ex -> log ex
| ex -> reraise ()
""",
"""
<span class="hljs-keyword">try</span>
    risky ()
<span class="hljs-keyword">with</span>
<span class="hljs-operator">|</span> <span class="hljs-operator">:?</span> System.IO.IOException <span class="hljs-keyword">as</span> ex <span class="hljs-operator">-&gt;</span> log ex
<span class="hljs-operator">|</span> ex <span class="hljs-operator">-&gt;</span> <span class="hljs-built_in">reraise</span> ()
""");
    }

    [Fact]
    public void ControlFlow_TryFinally()
    {
        AssertHighlighter("fsharp",
"""
try
    work ()
finally
    cleanup ()
""",
"""
<span class="hljs-keyword">try</span>
    work ()
<span class="hljs-keyword">finally</span>
    cleanup ()
""");
    }

    [Fact]
    public void ControlFlow_Raise()
    {
        AssertHighlighter("fsharp",
"""
raise (System.InvalidOperationException "bad state")
""",
"""
<span class="hljs-built_in">raise</span> (System.InvalidOperationException <span class="hljs-string">&quot;bad state&quot;</span>)
""");
    }

    [Fact]
    public void ControlFlow_FailWith()
    {
        AssertHighlighter("fsharp",
"""
failwith "something went wrong"
""",
"""
<span class="hljs-built_in">failwith</span> <span class="hljs-string">&quot;something went wrong&quot;</span>
""");
    }

    [Fact]
    public void ControlFlow_FailWithFormat()
    {
        AssertHighlighter("fsharp",
"""
failwithf "value out of range: %d" n
""",
"""
<span class="hljs-built_in">failwithf</span> <span class="hljs-string">&quot;value out of range: %d&quot;</span> n
""");
    }

    [Fact]
    public void Module_Definition()
    {
        AssertHighlighter("fsharp",
"""
module MyApp.Utils

let add a b = a + b
""",
"""
<span class="hljs-keyword">module</span> MyApp.Utils

<span class="hljs-keyword">let</span> add a b <span class="hljs-operator">=</span> a <span class="hljs-operator">+</span> b
""");
    }

    [Fact]
    public void Module_Nested()
    {
        AssertHighlighter("fsharp",
"""
module Outer =
    module Inner =
        let x = 1
""",
"""
<span class="hljs-keyword">module</span> Outer <span class="hljs-operator">=</span>
    <span class="hljs-keyword">module</span> Inner <span class="hljs-operator">=</span>
        <span class="hljs-keyword">let</span> x <span class="hljs-operator">=</span> <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Module_RecBinding()
    {
        AssertHighlighter("fsharp",
"""
module rec Tree =
    type Node = { Value: int; Children: Node list }
""",
"""
<span class="hljs-keyword">module</span> <span class="hljs-keyword">rec</span> Tree <span class="hljs-operator">=</span>
    <span class="hljs-keyword">type</span> <span class="hljs-title class_">Node</span> <span class="hljs-operator">=</span> { Value<span class="hljs-operator">:</span> <span class="hljs-type">int</span>; Children<span class="hljs-operator">:</span> Node <span class="hljs-type">list</span> }
""");
    }

    [Fact]
    public void Module_Open()
    {
        AssertHighlighter("fsharp",
"""
open System
open System.IO
""",
"""
<span class="hljs-keyword">open</span> System
<span class="hljs-keyword">open</span> System.IO
""");
    }

    [Fact]
    public void Module_OpenAlias()
    {
        AssertHighlighter("fsharp",
"""
module Sys = System.Diagnostics
""",
"""
<span class="hljs-keyword">module</span> Sys <span class="hljs-operator">=</span> System.Diagnostics
""");
    }

    [Fact]
    public void Module_AutoOpen()
    {
        AssertHighlighter("fsharp",
"""
[<AutoOpen>]
module Prelude

let inline cast<'T> x = x :> obj :?> 'T
""",
"""
<span class="hljs-meta">[&lt;AutoOpen&gt;]</span>
<span class="hljs-keyword">module</span> Prelude

<span class="hljs-keyword">let</span> <span class="hljs-keyword">inline</span> cast<span class="hljs-operator">&lt;</span><span class="hljs-symbol">&#x27;T</span><span class="hljs-operator">&gt;</span> x <span class="hljs-operator">=</span> x <span class="hljs-operator">:&gt;</span> obj <span class="hljs-operator">:?&gt;</span> <span class="hljs-symbol">&#x27;T</span>
""");
    }

    [Fact]
    public void Module_CompilationOrder()
    {
        AssertHighlighter("fsharp",
"""
namespace MyApp

module Helpers =
    let log msg = printfn "%s" msg
""",
"""
<span class="hljs-keyword">namespace</span> MyApp

<span class="hljs-keyword">module</span> Helpers <span class="hljs-operator">=</span>
    <span class="hljs-keyword">let</span> log msg <span class="hljs-operator">=</span> <span class="hljs-built_in">printfn</span> <span class="hljs-string">&quot;%s&quot;</span> msg
""");
    }

    [Fact]
    public void Generics_GenericFunction()
    {
        AssertHighlighter("fsharp",
"""
let identity (x: 'T) : 'T = x
""",
"""
<span class="hljs-keyword">let</span> identity (x<span class="hljs-operator">:</span> <span class="hljs-symbol">&#x27;T</span>) <span class="hljs-operator">:</span> <span class="hljs-symbol">&#x27;T</span> <span class="hljs-operator">=</span> x
""");
    }

    [Fact]
    public void Generics_GenericFunctionTwo()
    {
        AssertHighlighter("fsharp",
"""
let pair (a: 'a) (b: 'b) : 'a * 'b = (a, b)
""",
"""
<span class="hljs-keyword">let</span> pair (a<span class="hljs-operator">:</span> <span class="hljs-symbol">&#x27;a</span>) (b<span class="hljs-operator">:</span> <span class="hljs-symbol">&#x27;b</span>) <span class="hljs-operator">:</span> <span class="hljs-symbol">&#x27;a</span> <span class="hljs-operator">*</span> <span class="hljs-symbol">&#x27;b</span> <span class="hljs-operator">=</span> (a, b)
""");
    }

    [Fact]
    public void Generics_GenericConstraint()
    {
        AssertHighlighter("fsharp",
"""
let inline tryCompare<'T when 'T : comparison> (a: 'T) (b: 'T) = compare a b
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-keyword">inline</span> tryCompare<span class="hljs-operator">&lt;</span><span class="hljs-symbol">&#x27;T</span> <span class="hljs-keyword">when</span> <span class="hljs-symbol">&#x27;T</span> <span class="hljs-operator">:</span> comparison<span class="hljs-operator">&gt;</span> (a<span class="hljs-operator">:</span> <span class="hljs-symbol">&#x27;T</span>) (b<span class="hljs-operator">:</span> <span class="hljs-symbol">&#x27;T</span>) <span class="hljs-operator">=</span> compare a b
""");
    }

    [Fact]
    public void Generics_StaticallyResolved()
    {
        AssertHighlighter("fsharp",
"""
let inline add (a: ^T) (b: ^T) : ^T = a + b
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-keyword">inline</span> add (a<span class="hljs-operator">:</span> <span class="hljs-symbol">^T</span>) (b<span class="hljs-operator">:</span> <span class="hljs-symbol">^T</span>) <span class="hljs-operator">:</span> <span class="hljs-symbol">^T</span> <span class="hljs-operator">=</span> a <span class="hljs-operator">+</span> b
""");
    }

    [Fact]
    public void Quotation_Typed()
    {
        AssertHighlighter("fsharp",
"""
let expr = <@ 1 + 2 @>
""",
"""
<span class="hljs-keyword">let</span> expr <span class="hljs-operator">=</span> <span class="hljs-operator">&lt;@</span> <span class="hljs-number">1</span> <span class="hljs-operator">+</span> <span class="hljs-number">2</span> <span class="hljs-operator">@&gt;</span>
""");
    }

    [Fact]
    public void Quotation_Untyped()
    {
        AssertHighlighter("fsharp",
"""
let expr = <@@ 1 + 2 @@>
""",
"""
<span class="hljs-keyword">let</span> expr <span class="hljs-operator">=</span> <span class="hljs-operator">&lt;@@</span> <span class="hljs-number">1</span> <span class="hljs-operator">+</span> <span class="hljs-number">2</span> <span class="hljs-operator">@@&gt;</span>
""");
    }

    [Fact]
    public void Attribute_Simple()
    {
        AssertHighlighter("fsharp",
"""
[<Obsolete>]
let oldFunction () = ()
""",
"""
<span class="hljs-meta">[&lt;Obsolete&gt;]</span>
<span class="hljs-keyword">let</span> oldFunction () <span class="hljs-operator">=</span> ()
""");
    }

    [Fact]
    public void Attribute_WithArgs()
    {
        AssertHighlighter("fsharp",
"""
[<Obsolete("Use newFunction instead", true)>]
let oldFunction () = ()
""",
"""
<span class="hljs-meta">[&lt;Obsolete(<span class="hljs-string">&quot;Use newFunction instead&quot;</span>, true)&gt;]</span>
<span class="hljs-keyword">let</span> oldFunction () <span class="hljs-operator">=</span> ()
""");
    }

    [Fact]
    public void Attribute_MultipleAttrs()
    {
        AssertHighlighter("fsharp",
"""
[<EntryPoint; CompilerMessage("internal")>]
let main args = 0
""",
"""
<span class="hljs-meta">[&lt;EntryPoint; CompilerMessage(<span class="hljs-string">&quot;internal&quot;</span>)&gt;]</span>
<span class="hljs-keyword">let</span> main args <span class="hljs-operator">=</span> <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void Attribute_Module()
    {
        AssertHighlighter("fsharp",
"""
[<AutoOpen>]
module Helpers
""",
"""
<span class="hljs-meta">[&lt;AutoOpen&gt;]</span>
<span class="hljs-keyword">module</span> Helpers
""");
    }

    [Fact]
    public void Attribute_Struct()
    {
        AssertHighlighter("fsharp",
"""
[<Struct>]
type Point = { X: float; Y: float }
""",
"""
<span class="hljs-meta">[&lt;Struct&gt;]</span>
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Point</span> <span class="hljs-operator">=</span> { X<span class="hljs-operator">:</span> <span class="hljs-type">float</span>; Y<span class="hljs-operator">:</span> <span class="hljs-type">float</span> }
""");
    }

    [Fact]
    public void Attribute_Assembly()
    {
        AssertHighlighter("fsharp",
"""
[<assembly: AssemblyVersion("1.0.0.0")>]
do ()
""",
"""
<span class="hljs-meta">[&lt;assembly: AssemblyVersion(<span class="hljs-string">&quot;1.0.0.0&quot;</span>)&gt;]</span>
<span class="hljs-keyword">do</span> ()
""");
    }

    [Fact]
    public void Attribute_EntryPoint()
    {
        AssertHighlighter("fsharp",
"""
[<EntryPoint>]
let main argv =
    printfn "Hello, world!"
    0
""",
"""
<span class="hljs-meta">[&lt;EntryPoint&gt;]</span>
<span class="hljs-keyword">let</span> main argv <span class="hljs-operator">=</span>
    <span class="hljs-built_in">printfn</span> <span class="hljs-string">&quot;Hello, world!&quot;</span>
    <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void Comment_Line()
    {
        AssertHighlighter("fsharp",
"""
// this is a comment
""",
"""
<span class="hljs-comment">// this is a comment</span>
""");
    }

    [Fact]
    public void Comment_Block()
    {
        AssertHighlighter("fsharp",
"""
(* this is a block comment *)
""",
"""
<span class="hljs-comment">(* this is a block comment *)</span>
""");
    }

    [Fact]
    public void Comment_BlockMultiLine()
    {
        AssertHighlighter("fsharp",
"""
(*
   multi-line
   block comment
*)
""",
"""
<span class="hljs-comment">(*
   multi-line
   block comment
*)</span>
""");
    }

    [Fact]
    public void Comment_XmlDoc()
    {
        AssertHighlighter("fsharp",
"""
/// <summary>
/// Adds two integers.
/// </summary>
let add a b = a + b
""",
"""
<span class="hljs-comment">/// &lt;summary&gt;</span>
<span class="hljs-comment">/// Adds two integers.</span>
<span class="hljs-comment">/// &lt;/summary&gt;</span>
<span class="hljs-keyword">let</span> add a b <span class="hljs-operator">=</span> a <span class="hljs-operator">+</span> b
""");
    }

    [Fact]
    public void OperatorSpecial_CastTo()
    {
        AssertHighlighter("fsharp",
"""
let n = (5 :> obj) :?> int
""",
"""
<span class="hljs-keyword">let</span> n <span class="hljs-operator">=</span> (<span class="hljs-number">5</span> <span class="hljs-operator">:&gt;</span> obj) <span class="hljs-operator">:?&gt;</span> int
""");
    }

    [Fact]
    public void OperatorSpecial_TypeTest()
    {
        AssertHighlighter("fsharp",
"""
if value :? string then printfn "is string"
""",
"""
<span class="hljs-keyword">if</span> value <span class="hljs-operator">:?</span> string <span class="hljs-keyword">then</span> <span class="hljs-built_in">printfn</span> <span class="hljs-string">&quot;is string&quot;</span>
""");
    }

    [Fact]
    public void OperatorSpecial_Bang()
    {
        AssertHighlighter("fsharp",
"""
let value = !cell
""",
"""
<span class="hljs-keyword">let</span> value <span class="hljs-operator">=</span> <span class="hljs-operator">!</span>cell
""");
    }

    [Fact]
    public void OperatorSpecial_AssignRef()
    {
        AssertHighlighter("fsharp",
"""
cell := value
""",
"""
cell <span class="hljs-operator">:=</span> value
""");
    }

    [Fact]
    public void OperatorSpecial_BackPipeMulti()
    {
        AssertHighlighter("fsharp",
"""
let result = printf "%d" <| List.sum nums
""",
"""
<span class="hljs-keyword">let</span> result <span class="hljs-operator">=</span> <span class="hljs-built_in">printf</span> <span class="hljs-string">&quot;%d&quot;</span> <span class="hljs-operator">&lt;|</span> List.sum nums
""");
    }

    [Fact]
    public void Composite_ConsoleApp()
    {
        AssertHighlighter("fsharp",
"""
open System

[<EntryPoint>]
let main argv =
    let name =
        match argv with
        | [| name |] -> name
        | _          -> "world"

    printfn "Hello, %s!" name
    0
""",
"""
<span class="hljs-keyword">open</span> System

<span class="hljs-meta">[&lt;EntryPoint&gt;]</span>
<span class="hljs-keyword">let</span> main argv <span class="hljs-operator">=</span>
    <span class="hljs-keyword">let</span> name <span class="hljs-operator">=</span>
        <span class="hljs-keyword">match</span> argv <span class="hljs-keyword">with</span>
        <span class="hljs-operator">|</span> [<span class="hljs-operator">|</span> name <span class="hljs-operator">|</span>] <span class="hljs-operator">-&gt;</span> name
        <span class="hljs-operator">|</span> _          <span class="hljs-operator">-&gt;</span> <span class="hljs-string">&quot;world&quot;</span>

    <span class="hljs-built_in">printfn</span> <span class="hljs-string">&quot;Hello, %s!&quot;</span> name
    <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void Composite_WebFetcher()
    {
        AssertHighlighter("fsharp",
"""
open System.Net.Http
open System.Threading.Tasks

type ApiClient(client: HttpClient) =
    member _.FetchAsync(url: string) =
        task {
            use! response = client.GetAsync(url)
            response.EnsureSuccessStatusCode() |> ignore
            let! body = response.Content.ReadAsStringAsync()
            return body
        }
""",
"""
<span class="hljs-keyword">open</span> System.Net.Http
<span class="hljs-keyword">open</span> System.Threading.Tasks

<span class="hljs-keyword">type</span> <span class="hljs-title class_">ApiClient</span>(client<span class="hljs-operator">:</span> HttpClient) <span class="hljs-operator">=</span>
    <span class="hljs-keyword">member</span> _.FetchAsync(url<span class="hljs-operator">:</span> <span class="hljs-type">string</span>) <span class="hljs-operator">=</span>
        <span class="hljs-keyword">task</span> {
            <span class="hljs-keyword">use!</span> response <span class="hljs-operator">=</span> client.GetAsync(url)
            response.EnsureSuccessStatusCode() <span class="hljs-operator">|&gt;</span> <span class="hljs-built_in">ignore</span>
            <span class="hljs-keyword">let!</span> body <span class="hljs-operator">=</span> response.Content.ReadAsStringAsync()
            <span class="hljs-keyword">return</span> body
        }
""");
    }

    [Fact]
    public void Composite_TreeFold()
    {
        AssertHighlighter("fsharp",
"""
type Tree<'a> =
    | Leaf of 'a
    | Node of Tree<'a> * Tree<'a>

let rec fold f acc tree =
    match tree with
    | Leaf x          -> f acc x
    | Node (left, right) ->
        let leftAcc = fold f acc left
        fold f leftAcc right
""",
"""
<span class="hljs-keyword">type</span> <span class="hljs-title class_">Tree</span><span class="hljs-operator">&lt;</span><span class="hljs-symbol">&#x27;a</span><span class="hljs-operator">&gt;</span> <span class="hljs-operator">=</span>
    <span class="hljs-operator">|</span> Leaf <span class="hljs-keyword">of</span> <span class="hljs-symbol">&#x27;a</span>
    <span class="hljs-operator">|</span> Node <span class="hljs-keyword">of</span> Tree<span class="hljs-operator">&lt;</span><span class="hljs-symbol">&#x27;a</span><span class="hljs-operator">&gt;</span> <span class="hljs-operator">*</span> Tree<span class="hljs-operator">&lt;</span><span class="hljs-symbol">&#x27;a</span><span class="hljs-operator">&gt;</span>

<span class="hljs-keyword">let</span> <span class="hljs-keyword">rec</span> fold f acc tree <span class="hljs-operator">=</span>
    <span class="hljs-keyword">match</span> tree <span class="hljs-keyword">with</span>
    <span class="hljs-operator">|</span> Leaf x          <span class="hljs-operator">-&gt;</span> f acc x
    <span class="hljs-operator">|</span> Node (left, right) <span class="hljs-operator">-&gt;</span>
        <span class="hljs-keyword">let</span> leftAcc <span class="hljs-operator">=</span> fold f acc left
        fold f leftAcc right
""");
    }

    [Fact]
    public void Composite_LinqStyleQuery()
    {
        AssertHighlighter("fsharp",
"""
let topUsers =
    users
    |> List.filter (fun u -> u.IsActive)
    |> List.sortByDescending (fun u -> u.Score)
    |> List.truncate 10
    |> List.map (fun u -> {| Name = u.Name; Score = u.Score |})
""",
"""
<span class="hljs-keyword">let</span> topUsers <span class="hljs-operator">=</span>
    users
    <span class="hljs-operator">|&gt;</span> List.filter (<span class="hljs-keyword">fun</span> u <span class="hljs-operator">-&gt;</span> u.IsActive)
    <span class="hljs-operator">|&gt;</span> List.sortByDescending (<span class="hljs-keyword">fun</span> u <span class="hljs-operator">-&gt;</span> u.Score)
    <span class="hljs-operator">|&gt;</span> List.truncate <span class="hljs-number">10</span>
    <span class="hljs-operator">|&gt;</span> List.map (<span class="hljs-keyword">fun</span> u <span class="hljs-operator">-&gt;</span> {<span class="hljs-operator">|</span> Name <span class="hljs-operator">=</span> u.Name; Score <span class="hljs-operator">=</span> u.Score <span class="hljs-operator">|</span>})
""");
    }

    [Fact]
    public void Composite_AsyncWorkflow()
    {
        AssertHighlighter("fsharp",
"""
let processAll urls =
    urls
    |> List.map (fun url -> async {
        let! body = httpClient.GetStringAsync(url) |> Async.AwaitTask
        return url, body.Length
    })
    |> Async.Parallel
    |> Async.RunSynchronously
""",
"""
<span class="hljs-keyword">let</span> processAll urls <span class="hljs-operator">=</span>
    urls
    <span class="hljs-operator">|&gt;</span> List.map (<span class="hljs-keyword">fun</span> url <span class="hljs-operator">-&gt;</span> <span class="hljs-keyword">async</span> {
        <span class="hljs-keyword">let!</span> body <span class="hljs-operator">=</span> httpClient.GetStringAsync(url) <span class="hljs-operator">|&gt;</span> Async.AwaitTask
        <span class="hljs-keyword">return</span> url, body.Length
    })
    <span class="hljs-operator">|&gt;</span> Async.Parallel
    <span class="hljs-operator">|&gt;</span> Async.RunSynchronously
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("fsharp",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("fsharp",
"""
// nothing here
""",
"""
<span class="hljs-comment">// nothing here</span>
""");
    }

    [Fact]
    public void SpecialEdge_OnlyOpen()
    {
        AssertHighlighter("fsharp",
"""
open System
""",
"""
<span class="hljs-keyword">open</span> System
""");
    }

    [Fact]
    public void SpecialEdge_IndentSensitive()
    {
        AssertHighlighter("fsharp",
"""
let outer =
    let inner = 1
    inner + 1
""",
"""
<span class="hljs-keyword">let</span> outer <span class="hljs-operator">=</span>
    <span class="hljs-keyword">let</span> inner <span class="hljs-operator">=</span> <span class="hljs-number">1</span>
    inner <span class="hljs-operator">+</span> <span class="hljs-number">1</span>
""");
    }
}
