namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class SwiftHighlighterTests
{
    [Fact]
    public void HelloWorld()
    {
        AssertHighlighter("swift",
"""
import Foundation

print("Hello, world!")
""",
"""
<span class="hljs-keyword">import</span> Foundation

<span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;Hello, world!&quot;</span>)
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("swift",
"""
// line comment
/* block /* nested */ still comment */
/// Documentation comment
/** Doc block
 - Parameter x: the value
 */
let x = 1 // trailing TODO: fix
""",
"""
<span class="hljs-comment">// line comment</span>
<span class="hljs-comment">/* block <span class="hljs-comment">/* nested */</span> still comment */</span>
<span class="hljs-comment">/// Documentation comment</span>
<span class="hljs-comment">/** Doc block
 - Parameter x: the value
 */</span>
<span class="hljs-keyword">let</span> x <span class="hljs-operator">=</span> <span class="hljs-number">1</span> <span class="hljs-comment">// trailing <span class="hljs-doctag">TODO:</span> fix</span>
""");
    }

    [Fact]
    public void Variables()
    {
        AssertHighlighter("swift",
"""
let answer = 42
var name: String = "Swift"
var optional: Int? = nil
let (a, b) = (1, 2)
var items = [String]()
var dict: [String: Int] = [:]
lazy var cache = [Int: String]()
""",
"""
<span class="hljs-keyword">let</span> answer <span class="hljs-operator">=</span> <span class="hljs-number">42</span>
<span class="hljs-keyword">var</span> name: <span class="hljs-type">String</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;Swift&quot;</span>
<span class="hljs-keyword">var</span> <span class="hljs-keyword">optional</span>: <span class="hljs-type">Int</span>? <span class="hljs-operator">=</span> <span class="hljs-literal">nil</span>
<span class="hljs-keyword">let</span> (a, b) <span class="hljs-operator">=</span> (<span class="hljs-number">1</span>, <span class="hljs-number">2</span>)
<span class="hljs-keyword">var</span> items <span class="hljs-operator">=</span> [<span class="hljs-type">String</span>]()
<span class="hljs-keyword">var</span> dict: [<span class="hljs-type">String</span>: <span class="hljs-type">Int</span>] <span class="hljs-operator">=</span> [:]
<span class="hljs-keyword">lazy</span> <span class="hljs-keyword">var</span> cache <span class="hljs-operator">=</span> [<span class="hljs-type">Int</span>: <span class="hljs-type">String</span>]()
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("swift",
"""
let a = 42
let b = -17
let c = 0b1010_1010
let d = 0o755
let e = 0xFF_FF
let f = 1_000_000
let g = 3.14159
let h = 1.5e10
let i = 2.5E-3
let j = 0x1.8p3
let k = 1_000.000_1
let l = 12abc
""",
"""
<span class="hljs-keyword">let</span> a <span class="hljs-operator">=</span> <span class="hljs-number">42</span>
<span class="hljs-keyword">let</span> b <span class="hljs-operator">=</span> <span class="hljs-operator">-</span><span class="hljs-number">17</span>
<span class="hljs-keyword">let</span> c <span class="hljs-operator">=</span> <span class="hljs-number">0b1010_1010</span>
<span class="hljs-keyword">let</span> d <span class="hljs-operator">=</span> <span class="hljs-number">0o755</span>
<span class="hljs-keyword">let</span> e <span class="hljs-operator">=</span> <span class="hljs-number">0xFF_FF</span>
<span class="hljs-keyword">let</span> f <span class="hljs-operator">=</span> <span class="hljs-number">1_000_000</span>
<span class="hljs-keyword">let</span> g <span class="hljs-operator">=</span> <span class="hljs-number">3.14159</span>
<span class="hljs-keyword">let</span> h <span class="hljs-operator">=</span> <span class="hljs-number">1.5e10</span>
<span class="hljs-keyword">let</span> i <span class="hljs-operator">=</span> <span class="hljs-number">2.5E-3</span>
<span class="hljs-keyword">let</span> j <span class="hljs-operator">=</span> <span class="hljs-number">0x1.8p3</span>
<span class="hljs-keyword">let</span> k <span class="hljs-operator">=</span> <span class="hljs-number">1_000.000_1</span>
<span class="hljs-keyword">let</span> l <span class="hljs-operator">=</span> 12abc
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("swift",
"""
let s = "Hello \(name)!"
let t = "Escapes: \n \t \\ \" \' \0 \u{1F600}"
let u = "Nested \(greet("x" + "y")) and \(a + (b * 2))"
""",
"""
<span class="hljs-keyword">let</span> s <span class="hljs-operator">=</span> <span class="hljs-string">&quot;Hello <span class="hljs-subst">\(name)</span>!&quot;</span>
<span class="hljs-keyword">let</span> t <span class="hljs-operator">=</span> <span class="hljs-string">&quot;Escapes: <span class="hljs-subst">\n</span> <span class="hljs-subst">\t</span> <span class="hljs-subst">\\</span> <span class="hljs-subst">\&quot;</span> <span class="hljs-subst">\&#x27;</span> <span class="hljs-subst">\0</span> <span class="hljs-subst">\u{1F600}</span>&quot;</span>
<span class="hljs-keyword">let</span> u <span class="hljs-operator">=</span> <span class="hljs-string">&quot;Nested <span class="hljs-subst">\(greet(<span class="hljs-string">&quot;x&quot;</span> <span class="hljs-operator">+</span> <span class="hljs-string">&quot;y&quot;</span>))</span> and <span class="hljs-subst">\(a <span class="hljs-operator">+</span> (b <span class="hljs-operator">*</span> <span class="hljs-number">2</span>))</span>&quot;</span>
""");
    }

    [Fact]
    public void MultilineString()
    {
        AssertHighlighter("swift",
""""
let text = """
    First line
    Second \(value) line \
    continued
    "quoted" text
    """
"""",
"""
<span class="hljs-keyword">let</span> text <span class="hljs-operator">=</span> <span class="hljs-string">&quot;&quot;&quot;
    First line
    Second <span class="hljs-subst">\(value)</span> line <span class="hljs-subst">\
</span>    continued
    &quot;quoted&quot; text
    &quot;&quot;&quot;</span>
""");
    }

    [Fact]
    public void RawStrings()
    {
        AssertHighlighter("swift",
""""
let raw = #"Raw \n string with "quotes" and \#(interpolated)"#
let raw2 = ##"Double # raw "# still"##
let raw3 = #"""
    Multiline raw \(not) \#(yes)
    """#
"""",
"""
<span class="hljs-keyword">let</span> raw <span class="hljs-operator">=</span> <span class="hljs-string">#&quot;Raw \n string with &quot;quotes&quot; and <span class="hljs-subst">\#(interpolated)</span>&quot;#</span>
<span class="hljs-keyword">let</span> raw2 <span class="hljs-operator">=</span> <span class="hljs-string">##&quot;Double # raw &quot;# still&quot;##</span>
<span class="hljs-keyword">let</span> raw3 <span class="hljs-operator">=</span> <span class="hljs-string">#&quot;&quot;&quot;
    Multiline raw \(not) <span class="hljs-subst">\#(yes)</span>
    &quot;&quot;&quot;#</span>
""");
    }

    [Fact]
    public void Functions()
    {
        AssertHighlighter("swift",
"""
func greet(person: String, from hometown: String) -> String {
    return "Hello \(person)!"
}

func add(_ a: Int, _ b: Int = 0) -> Int { a + b }

func variadic(_ numbers: Double...) -> Double { 0 }

func swapValues(_ a: inout Int, _ b: inout Int) {
    (a, b) = (b, a)
}
""",
"""
<span class="hljs-keyword">func</span> <span class="hljs-title function_">greet</span>(<span class="hljs-params">person</span>: <span class="hljs-type">String</span>, <span class="hljs-params">from</span> <span class="hljs-params">hometown</span>: <span class="hljs-type">String</span>) -&gt; <span class="hljs-type">String</span> {
    <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;Hello <span class="hljs-subst">\(person)</span>!&quot;</span>
}

<span class="hljs-keyword">func</span> <span class="hljs-title function_">add</span>(<span class="hljs-keyword">_</span> <span class="hljs-params">a</span>: <span class="hljs-type">Int</span>, <span class="hljs-keyword">_</span> <span class="hljs-params">b</span>: <span class="hljs-type">Int</span> <span class="hljs-operator">=</span> <span class="hljs-number">0</span>) -&gt; <span class="hljs-type">Int</span> { a <span class="hljs-operator">+</span> b }

<span class="hljs-keyword">func</span> <span class="hljs-title function_">variadic</span>(<span class="hljs-keyword">_</span> <span class="hljs-params">numbers</span>: <span class="hljs-type">Double</span>...) -&gt; <span class="hljs-type">Double</span> { <span class="hljs-number">0</span> }

<span class="hljs-keyword">func</span> <span class="hljs-title function_">swapValues</span>(<span class="hljs-keyword">_</span> <span class="hljs-params">a</span>: <span class="hljs-keyword">inout</span> <span class="hljs-type">Int</span>, <span class="hljs-keyword">_</span> <span class="hljs-params">b</span>: <span class="hljs-keyword">inout</span> <span class="hljs-type">Int</span>) {
    (a, b) <span class="hljs-operator">=</span> (b, a)
}
""");
    }

    [Fact]
    public void Generics()
    {
        AssertHighlighter("swift",
"""
func swapTwo<T>(_ a: inout T, _ b: inout T) where T: Equatable {
}

struct Stack<Element> {
    var items: [Element] = []
    mutating func push(_ item: Element) {
        items.append(item)
    }
}

func allItemsMatch<C1: Container, C2: Container>(_ one: C1, _ two: C2) -> Bool
    where C1.Item == C2.Item, C1.Item: Equatable {
    return true
}
let values: Array<Dictionary<String, Int>> = []
""",
"""
<span class="hljs-keyword">func</span> <span class="hljs-title function_">swapTwo</span>&lt;<span class="hljs-type">T</span>&gt;(<span class="hljs-keyword">_</span> <span class="hljs-params">a</span>: <span class="hljs-keyword">inout</span> <span class="hljs-type">T</span>, <span class="hljs-keyword">_</span> <span class="hljs-params">b</span>: <span class="hljs-keyword">inout</span> <span class="hljs-type">T</span>) <span class="hljs-keyword">where</span> <span class="hljs-type">T</span>: <span class="hljs-type">Equatable</span> {
}

<span class="hljs-keyword">struct</span> <span class="hljs-title class_">Stack</span>&lt;<span class="hljs-type">Element</span>&gt; {
    <span class="hljs-keyword">var</span> items: [<span class="hljs-type">Element</span>] <span class="hljs-operator">=</span> []
    <span class="hljs-keyword">mutating</span> <span class="hljs-keyword">func</span> <span class="hljs-title function_">push</span>(<span class="hljs-keyword">_</span> <span class="hljs-params">item</span>: <span class="hljs-type">Element</span>) {
        items.append(item)
    }
}

<span class="hljs-keyword">func</span> <span class="hljs-title function_">allItemsMatch</span>&lt;<span class="hljs-type">C1</span>: <span class="hljs-type">Container</span>, <span class="hljs-type">C2</span>: <span class="hljs-type">Container</span>&gt;(<span class="hljs-keyword">_</span> <span class="hljs-params">one</span>: <span class="hljs-type">C1</span>, <span class="hljs-keyword">_</span> <span class="hljs-params">two</span>: <span class="hljs-type">C2</span>) -&gt; <span class="hljs-type">Bool</span>
    <span class="hljs-keyword">where</span> <span class="hljs-type">C1</span>.<span class="hljs-type">Item</span> <span class="hljs-operator">==</span> <span class="hljs-type">C2</span>.<span class="hljs-type">Item</span>, <span class="hljs-type">C1</span>.<span class="hljs-type">Item</span>: <span class="hljs-type">Equatable</span> {
    <span class="hljs-keyword">return</span> <span class="hljs-literal">true</span>
}
<span class="hljs-keyword">let</span> values: <span class="hljs-type">Array</span>&lt;<span class="hljs-type">Dictionary</span>&lt;<span class="hljs-type">String</span>, <span class="hljs-type">Int</span>&gt;&gt; <span class="hljs-operator">=</span> []
""");
    }

    [Fact]
    public void AsyncAwait()
    {
        AssertHighlighter("swift",
"""
func fetchUser(id: Int) async throws -> User {
    let (data, _) = try await URLSession.shared.data(from: url)
    return try JSONDecoder().decode(User.self, from: data)
}

Task {
    async let first = fetchUser(id: 1)
    async let second = fetchUser(id: 2)
    let users = try await [first, second]
    await withTaskGroup(of: Int.self) { group in
        group.addTask { 1 }
    }
}
""",
"""
<span class="hljs-keyword">func</span> <span class="hljs-title function_">fetchUser</span>(<span class="hljs-params">id</span>: <span class="hljs-type">Int</span>) <span class="hljs-keyword">async</span> <span class="hljs-keyword">throws</span> -&gt; <span class="hljs-type">User</span> {
    <span class="hljs-keyword">let</span> (data, <span class="hljs-keyword">_</span>) <span class="hljs-operator">=</span> <span class="hljs-keyword">try</span> <span class="hljs-keyword">await</span> <span class="hljs-type">URLSession</span>.shared.data(from: url)
    <span class="hljs-keyword">return</span> <span class="hljs-keyword">try</span> <span class="hljs-type">JSONDecoder</span>().decode(<span class="hljs-type">User</span>.<span class="hljs-keyword">self</span>, from: data)
}

<span class="hljs-type">Task</span> {
    <span class="hljs-keyword">async</span> <span class="hljs-keyword">let</span> first <span class="hljs-operator">=</span> fetchUser(id: <span class="hljs-number">1</span>)
    <span class="hljs-keyword">async</span> <span class="hljs-keyword">let</span> second <span class="hljs-operator">=</span> fetchUser(id: <span class="hljs-number">2</span>)
    <span class="hljs-keyword">let</span> users <span class="hljs-operator">=</span> <span class="hljs-keyword">try</span> <span class="hljs-keyword">await</span> [first, second]
    <span class="hljs-keyword">await</span> withTaskGroup(of: <span class="hljs-type">Int</span>.<span class="hljs-keyword">self</span>) { group <span class="hljs-keyword">in</span>
        group.addTask { <span class="hljs-number">1</span> }
    }
}
""");
    }

    [Fact]
    public void Actors()
    {
        AssertHighlighter("swift",
"""
actor BankAccount {
    let accountNumber: Int
    private(set) var balance: Double

    init(accountNumber: Int, initialDeposit: Double) {
        self.accountNumber = accountNumber
        self.balance = initialDeposit
    }

    nonisolated func describe() -> String { "\(accountNumber)" }
}

@MainActor
final class ViewModel: ObservableObject {
    @Published var items: [Item] = []
}
""",
"""
<span class="hljs-keyword">actor</span> <span class="hljs-title class_">BankAccount</span> {
    <span class="hljs-keyword">let</span> accountNumber: <span class="hljs-type">Int</span>
    <span class="hljs-keyword">private(set)</span> <span class="hljs-keyword">var</span> balance: <span class="hljs-type">Double</span>

    <span class="hljs-keyword">init</span>(<span class="hljs-params">accountNumber</span>: <span class="hljs-type">Int</span>, <span class="hljs-params">initialDeposit</span>: <span class="hljs-type">Double</span>) {
        <span class="hljs-keyword">self</span>.accountNumber <span class="hljs-operator">=</span> accountNumber
        <span class="hljs-keyword">self</span>.balance <span class="hljs-operator">=</span> initialDeposit
    }

    <span class="hljs-keyword">nonisolated</span> <span class="hljs-keyword">func</span> <span class="hljs-title function_">describe</span>() -&gt; <span class="hljs-type">String</span> { <span class="hljs-string">&quot;<span class="hljs-subst">\(accountNumber)</span>&quot;</span> }
}

<span class="hljs-meta">@MainActor</span>
<span class="hljs-keyword">final</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">ViewModel</span>: <span class="hljs-title class_ inherited__">ObservableObject</span> {
    <span class="hljs-meta">@Published</span> <span class="hljs-keyword">var</span> items: [<span class="hljs-type">Item</span>] <span class="hljs-operator">=</span> []
}
""");
    }

    [Fact]
    public void Macros_FreestandingAndAttached()
    {
        AssertHighlighter("swift",
"""
@Observable
class Library {
    var books: [Book] = []
}

#Preview {
    ContentView()
}

#Preview("Dark mode", traits: .sizeThatFitsLayout) {
    ContentView()
}

let (value, code) = #stringify(x + y)

@freestanding(expression)
public macro stringify<T>(_ value: T) -> (T, String) = #externalMacro(module: "MyMacros", type: "StringifyMacro")

@attached(member, names: named(init))
macro AddInit() = #externalMacro(module: "M", type: "AddInitMacro")
""",
"""
<span class="hljs-meta">@Observable</span>
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Library</span> {
    <span class="hljs-keyword">var</span> books: [<span class="hljs-type">Book</span>] <span class="hljs-operator">=</span> []
}

<span class="hljs-meta">#Preview</span> {
    <span class="hljs-type">ContentView</span>()
}

<span class="hljs-meta">#Preview</span>(<span class="hljs-string">&quot;Dark mode&quot;</span>, traits: .sizeThatFitsLayout) {
    <span class="hljs-type">ContentView</span>()
}

<span class="hljs-keyword">let</span> (value, code) <span class="hljs-operator">=</span> <span class="hljs-meta">#stringify</span>(x <span class="hljs-operator">+</span> y)

<span class="hljs-keyword">@freestanding</span>(expression)
<span class="hljs-keyword">public</span> <span class="hljs-keyword">macro</span> <span class="hljs-title function_">stringify</span>&lt;<span class="hljs-type">T</span>&gt;(<span class="hljs-keyword">_</span> <span class="hljs-params">value</span>: <span class="hljs-type">T</span>) -&gt; (<span class="hljs-type">T</span>, <span class="hljs-type">String</span>) <span class="hljs-operator">=</span> <span class="hljs-meta">#externalMacro</span>(module: <span class="hljs-string">&quot;MyMacros&quot;</span>, type: <span class="hljs-string">&quot;StringifyMacro&quot;</span>)

<span class="hljs-keyword">@attached</span>(member, names: named(<span class="hljs-keyword">init</span>))
<span class="hljs-keyword">macro</span> <span class="hljs-title function_">AddInit</span>() <span class="hljs-operator">=</span> <span class="hljs-meta">#externalMacro</span>(module: <span class="hljs-string">&quot;M&quot;</span>, type: <span class="hljs-string">&quot;AddInitMacro&quot;</span>)
""");
    }

    [Fact]
    public void ResultBuilders()
    {
        AssertHighlighter("swift",
"""
@resultBuilder
struct StringBuilder {
    static func buildBlock(_ parts: String...) -> String {
        parts.joined(separator: "\n")
    }
}

struct ContentView: View {
    @State private var count = 0
    @Binding var isOn: Bool
    @Environment(\.colorScheme) var colorScheme

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Count: \(count)")
                .font(.title)
            Button("Increment") { count += 1 }
            Toggle("On", isOn: $isOn)
        }
        .padding()
    }
}
""",
"""
<span class="hljs-keyword">@resultBuilder</span>
<span class="hljs-keyword">struct</span> <span class="hljs-title class_">StringBuilder</span> {
    <span class="hljs-keyword">static</span> <span class="hljs-keyword">func</span> <span class="hljs-title function_">buildBlock</span>(<span class="hljs-keyword">_</span> <span class="hljs-params">parts</span>: <span class="hljs-type">String</span>...) -&gt; <span class="hljs-type">String</span> {
        parts.joined(separator: <span class="hljs-string">&quot;<span class="hljs-subst">\n</span>&quot;</span>)
    }
}

<span class="hljs-keyword">struct</span> <span class="hljs-title class_">ContentView</span>: <span class="hljs-title class_ inherited__">View</span> {
    <span class="hljs-meta">@State</span> <span class="hljs-keyword">private</span> <span class="hljs-keyword">var</span> count <span class="hljs-operator">=</span> <span class="hljs-number">0</span>
    <span class="hljs-meta">@Binding</span> <span class="hljs-keyword">var</span> isOn: <span class="hljs-type">Bool</span>
    <span class="hljs-meta">@Environment</span>(\.colorScheme) <span class="hljs-keyword">var</span> colorScheme

    <span class="hljs-keyword">var</span> body: <span class="hljs-keyword">some</span> <span class="hljs-type">View</span> {
        <span class="hljs-type">VStack</span>(alignment: .leading, spacing: <span class="hljs-number">8</span>) {
            <span class="hljs-type">Text</span>(<span class="hljs-string">&quot;Count: <span class="hljs-subst">\(count)</span>&quot;</span>)
                .font(.title)
            <span class="hljs-type">Button</span>(<span class="hljs-string">&quot;Increment&quot;</span>) { count <span class="hljs-operator">+=</span> <span class="hljs-number">1</span> }
            <span class="hljs-type">Toggle</span>(<span class="hljs-string">&quot;On&quot;</span>, isOn: <span class="hljs-variable">$isOn</span>)
        }
        .padding()
    }
}
""");
    }

    [Fact]
    public void PropertyWrappers()
    {
        AssertHighlighter("swift",
"""
@propertyWrapper
struct Clamped<Value: Comparable> {
    var wrappedValue: Value {
        get { value }
        set { value = min(max(newValue, range.lowerBound), range.upperBound) }
    }
    var projectedValue: Self { self }
    private var value: Value
    let range: ClosedRange<Value>
}

struct Player {
    @Clamped(0...100) var health: Int = 100
}
""",
"""
<span class="hljs-keyword">@propertyWrapper</span>
<span class="hljs-keyword">struct</span> <span class="hljs-title class_">Clamped</span>&lt;<span class="hljs-type">Value</span>: <span class="hljs-type">Comparable</span>&gt; {
    <span class="hljs-keyword">var</span> wrappedValue: <span class="hljs-type">Value</span> {
        <span class="hljs-keyword">get</span> { value }
        <span class="hljs-keyword">set</span> { value <span class="hljs-operator">=</span> <span class="hljs-built_in">min</span>(<span class="hljs-built_in">max</span>(newValue, range.lowerBound), range.upperBound) }
    }
    <span class="hljs-keyword">var</span> projectedValue: <span class="hljs-keyword">Self</span> { <span class="hljs-keyword">self</span> }
    <span class="hljs-keyword">private</span> <span class="hljs-keyword">var</span> value: <span class="hljs-type">Value</span>
    <span class="hljs-keyword">let</span> range: <span class="hljs-type">ClosedRange</span>&lt;<span class="hljs-type">Value</span>&gt;
}

<span class="hljs-keyword">struct</span> <span class="hljs-title class_">Player</span> {
    <span class="hljs-meta">@Clamped</span>(<span class="hljs-number">0</span><span class="hljs-operator">...</span><span class="hljs-number">100</span>) <span class="hljs-keyword">var</span> health: <span class="hljs-type">Int</span> <span class="hljs-operator">=</span> <span class="hljs-number">100</span>
}
""");
    }

    [Fact]
    public void ProtocolsExtensions()
    {
        AssertHighlighter("swift",
"""
protocol Shape: AnyObject, CustomStringConvertible {
    associatedtype Unit
    var area: Double { get }
    func draw() -> String
}

extension Int: Shape where Self: Equatable {
    var area: Double { Double(self) }
}

extension Collection where Element: Numeric {
    func sum() -> Element { reduce(0, +) }
}
""",
"""
<span class="hljs-keyword">protocol</span> <span class="hljs-title class_">Shape</span>: <span class="hljs-title class_ inherited__">AnyObject</span>, <span class="hljs-title class_ inherited__">CustomStringConvertible</span> {
    <span class="hljs-keyword">associatedtype</span> <span class="hljs-type">Unit</span>
    <span class="hljs-keyword">var</span> area: <span class="hljs-type">Double</span> { <span class="hljs-keyword">get</span> }
    <span class="hljs-keyword">func</span> <span class="hljs-title function_">draw</span>() -&gt; <span class="hljs-type">String</span>
}

<span class="hljs-keyword">extension</span> <span class="hljs-title class_">Int</span>: <span class="hljs-title class_ inherited__">Shape</span> <span class="hljs-keyword">where</span> <span class="hljs-title class_ inherited__">Self</span>: <span class="hljs-title class_ inherited__">Equatable</span> {
    <span class="hljs-keyword">var</span> area: <span class="hljs-type">Double</span> { <span class="hljs-type">Double</span>(<span class="hljs-keyword">self</span>) }
}

<span class="hljs-keyword">extension</span> <span class="hljs-title class_">Collection</span> <span class="hljs-keyword">where</span> <span class="hljs-type">Element</span>: <span class="hljs-type">Numeric</span> {
    <span class="hljs-keyword">func</span> <span class="hljs-title function_">sum</span>() -&gt; <span class="hljs-type">Element</span> { reduce(<span class="hljs-number">0</span>, <span class="hljs-operator">+</span>) }
}
""");
    }

    [Fact]
    public void Enums()
    {
        AssertHighlighter("swift",
"""
enum CompassPoint: String, CaseIterable {
    case north, south
    case east = "E"
    indirect case node(Int, CompassPoint)
}

switch point {
case .north:
    print("north")
case .east where x > 0, .south:
    fallthrough
case let .node(value, _):
    break
default:
    break
}
""",
"""
<span class="hljs-keyword">enum</span> <span class="hljs-title class_">CompassPoint</span>: <span class="hljs-title class_ inherited__">String</span>, <span class="hljs-title class_ inherited__">CaseIterable</span> {
    <span class="hljs-keyword">case</span> north, south
    <span class="hljs-keyword">case</span> east <span class="hljs-operator">=</span> <span class="hljs-string">&quot;E&quot;</span>
    <span class="hljs-keyword">indirect</span> <span class="hljs-keyword">case</span> node(<span class="hljs-type">Int</span>, <span class="hljs-type">CompassPoint</span>)
}

<span class="hljs-keyword">switch</span> point {
<span class="hljs-keyword">case</span> .north:
    <span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;north&quot;</span>)
<span class="hljs-keyword">case</span> .east <span class="hljs-keyword">where</span> x <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span>, .south:
    <span class="hljs-keyword">fallthrough</span>
<span class="hljs-keyword">case</span> <span class="hljs-keyword">let</span> .node(value, <span class="hljs-keyword">_</span>):
    <span class="hljs-keyword">break</span>
<span class="hljs-keyword">default</span>:
    <span class="hljs-keyword">break</span>
}
""");
    }

    [Fact]
    public void Closures()
    {
        AssertHighlighter("swift",
"""
let sorted = names.sorted { $0 < $1 }
let mapped = numbers.map { (number: Int) -> String in
    return "\(number)"
}
let filtered = items.filter { item in item.isEnabled }
let handler: (Int, String) -> Void = { [weak self, unowned(unowned) other] value, text in
    self?.update(value)
}
let escaping: @escaping @Sendable () async throws -> Void
""",
"""
<span class="hljs-keyword">let</span> sorted <span class="hljs-operator">=</span> names.sorted { <span class="hljs-variable">$0</span> <span class="hljs-operator">&lt;</span> <span class="hljs-variable">$1</span> }
<span class="hljs-keyword">let</span> mapped <span class="hljs-operator">=</span> numbers.map { (number: <span class="hljs-type">Int</span>) -&gt; <span class="hljs-type">String</span> <span class="hljs-keyword">in</span>
    <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;<span class="hljs-subst">\(number)</span>&quot;</span>
}
<span class="hljs-keyword">let</span> filtered <span class="hljs-operator">=</span> items.filter { item <span class="hljs-keyword">in</span> item.isEnabled }
<span class="hljs-keyword">let</span> handler: (<span class="hljs-type">Int</span>, <span class="hljs-type">String</span>) -&gt; <span class="hljs-type">Void</span> <span class="hljs-operator">=</span> { [<span class="hljs-keyword">weak</span> <span class="hljs-keyword">self</span>, <span class="hljs-keyword">unowned</span>(<span class="hljs-keyword">unowned</span>) other] value, text <span class="hljs-keyword">in</span>
    <span class="hljs-keyword">self</span><span class="hljs-operator">?</span>.update(value)
}
<span class="hljs-keyword">let</span> escaping: <span class="hljs-keyword">@escaping</span> <span class="hljs-keyword">@Sendable</span> () <span class="hljs-keyword">async</span> <span class="hljs-keyword">throws</span> -&gt; <span class="hljs-type">Void</span>
""");
    }

    [Fact]
    public void Optionals()
    {
        AssertHighlighter("swift",
"""
if let name = person?.name, !name.isEmpty {
    print(name)
}
guard let value = dict["key"] as? Int else { return }
let forced = value as! String
let result = try? compute()
let crash = try! compute()
let len = text?.count ?? 0
if case .some(let x) = optional { }
""",
"""
<span class="hljs-keyword">if</span> <span class="hljs-keyword">let</span> name <span class="hljs-operator">=</span> person<span class="hljs-operator">?</span>.name, <span class="hljs-operator">!</span>name.isEmpty {
    <span class="hljs-built_in">print</span>(name)
}
<span class="hljs-keyword">guard</span> <span class="hljs-keyword">let</span> value <span class="hljs-operator">=</span> dict[<span class="hljs-string">&quot;key&quot;</span>] <span class="hljs-keyword">as?</span> <span class="hljs-type">Int</span> <span class="hljs-keyword">else</span> { <span class="hljs-keyword">return</span> }
<span class="hljs-keyword">let</span> forced <span class="hljs-operator">=</span> value <span class="hljs-keyword">as!</span> <span class="hljs-type">String</span>
<span class="hljs-keyword">let</span> result <span class="hljs-operator">=</span> <span class="hljs-keyword">try?</span> compute()
<span class="hljs-keyword">let</span> crash <span class="hljs-operator">=</span> <span class="hljs-keyword">try!</span> compute()
<span class="hljs-keyword">let</span> len <span class="hljs-operator">=</span> text<span class="hljs-operator">?</span>.count <span class="hljs-operator">??</span> <span class="hljs-number">0</span>
<span class="hljs-keyword">if</span> <span class="hljs-keyword">case</span> .some(<span class="hljs-keyword">let</span> x) <span class="hljs-operator">=</span> <span class="hljs-keyword">optional</span> { }
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("swift",
"""
for i in 0..<10 where i % 2 == 0 {
    continue
}
for (index, value) in array.enumerated() { }
while x > 0 { x -= 1 }
repeat { x += 1 } while x < 10
defer { cleanup() }
do {
    try risky()
} catch let error as NSError {
    print(error)
} catch {
    throw MyError.failed
}
""",
"""
<span class="hljs-keyword">for</span> i <span class="hljs-keyword">in</span> <span class="hljs-number">0</span><span class="hljs-operator">..&lt;</span><span class="hljs-number">10</span> <span class="hljs-keyword">where</span> i <span class="hljs-operator">%</span> <span class="hljs-number">2</span> <span class="hljs-operator">==</span> <span class="hljs-number">0</span> {
    <span class="hljs-keyword">continue</span>
}
<span class="hljs-keyword">for</span> (index, value) <span class="hljs-keyword">in</span> array.enumerated() { }
<span class="hljs-keyword">while</span> x <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span> { x <span class="hljs-operator">-=</span> <span class="hljs-number">1</span> }
<span class="hljs-keyword">repeat</span> { x <span class="hljs-operator">+=</span> <span class="hljs-number">1</span> } <span class="hljs-keyword">while</span> x <span class="hljs-operator">&lt;</span> <span class="hljs-number">10</span>
<span class="hljs-keyword">defer</span> { cleanup() }
<span class="hljs-keyword">do</span> {
    <span class="hljs-keyword">try</span> risky()
} <span class="hljs-keyword">catch</span> <span class="hljs-keyword">let</span> error <span class="hljs-keyword">as</span> <span class="hljs-type">NSError</span> {
    <span class="hljs-built_in">print</span>(error)
} <span class="hljs-keyword">catch</span> {
    <span class="hljs-keyword">throw</span> <span class="hljs-type">MyError</span>.failed
}
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("swift",
"""
let range = 1...5
let halfOpen = 0..<count
let bits = a << 2 | b & ~c ^ d
let cmp = a === b && c !== d || e >= f
x += 1; y -= 2; z *= 3; w /= 4
let neg = -value
prefix operator +++
infix operator <=> : ComparisonPrecedence
precedencegroup ExponentPrecedence {
    higherThan: MultiplicationPrecedence
    associativity: right
    assignment: false
}
static func + (lhs: Vector, rhs: Vector) -> Vector { Vector() }
static prefix func - (v: Vector) -> Vector { v }
""",
"""
<span class="hljs-keyword">let</span> range <span class="hljs-operator">=</span> <span class="hljs-number">1</span><span class="hljs-operator">...</span><span class="hljs-number">5</span>
<span class="hljs-keyword">let</span> halfOpen <span class="hljs-operator">=</span> <span class="hljs-number">0</span><span class="hljs-operator">..&lt;</span>count
<span class="hljs-keyword">let</span> bits <span class="hljs-operator">=</span> a <span class="hljs-operator">&lt;&lt;</span> <span class="hljs-number">2</span> <span class="hljs-operator">|</span> b <span class="hljs-operator">&amp;</span> <span class="hljs-operator">~</span>c <span class="hljs-operator">^</span> d
<span class="hljs-keyword">let</span> cmp <span class="hljs-operator">=</span> a <span class="hljs-operator">===</span> b <span class="hljs-operator">&amp;&amp;</span> c <span class="hljs-operator">!==</span> d <span class="hljs-operator">||</span> e <span class="hljs-operator">&gt;=</span> f
x <span class="hljs-operator">+=</span> <span class="hljs-number">1</span>; y <span class="hljs-operator">-=</span> <span class="hljs-number">2</span>; z <span class="hljs-operator">*=</span> <span class="hljs-number">3</span>; w <span class="hljs-operator">/=</span> <span class="hljs-number">4</span>
<span class="hljs-keyword">let</span> neg <span class="hljs-operator">=</span> <span class="hljs-operator">-</span>value
<span class="hljs-keyword">prefix</span> <span class="hljs-keyword">operator</span> <span class="hljs-title">+++</span>
<span class="hljs-keyword">infix</span> <span class="hljs-keyword">operator</span> <span class="hljs-title">&lt;=&gt;</span> : <span class="hljs-type">ComparisonPrecedence</span>
<span class="hljs-keyword">precedencegroup</span> <span class="hljs-title">ExponentPrecedence</span> {
    <span class="hljs-keyword">higherThan</span>: <span class="hljs-type">MultiplicationPrecedence</span>
    <span class="hljs-keyword">associativity</span>: <span class="hljs-keyword">right</span>
    <span class="hljs-keyword">assignment</span>: <span class="hljs-keyword">false</span>
}
<span class="hljs-keyword">static</span> <span class="hljs-keyword">func</span> <span class="hljs-title function_">+</span> (<span class="hljs-params">lhs</span>: <span class="hljs-type">Vector</span>, <span class="hljs-params">rhs</span>: <span class="hljs-type">Vector</span>) -&gt; <span class="hljs-type">Vector</span> { <span class="hljs-type">Vector</span>() }
<span class="hljs-keyword">static</span> <span class="hljs-keyword">prefix</span> <span class="hljs-keyword">func</span> <span class="hljs-title function_">-</span> (<span class="hljs-params">v</span>: <span class="hljs-type">Vector</span>) -&gt; <span class="hljs-type">Vector</span> { v }
""");
    }

    [Fact]
    public void Availability()
    {
        AssertHighlighter("swift",
"""
@available(iOS 15.0, macOS 12, *)
func newAPI() { }

if #available(iOS 16, *) {
    use()
} else if #unavailable(watchOS 9) {
}

@available(*, deprecated, message: "Use other", renamed: "other()")
func old() { }
""",
"""
<span class="hljs-keyword">@available</span>(<span class="hljs-keyword">iOS</span> <span class="hljs-number">15.0</span>, <span class="hljs-keyword">macOS</span> <span class="hljs-number">12</span>, <span class="hljs-operator">*</span>)
<span class="hljs-keyword">func</span> <span class="hljs-title function_">newAPI</span>() { }

<span class="hljs-keyword">if</span> <span class="hljs-keyword">#available</span>(<span class="hljs-keyword">iOS</span> <span class="hljs-number">16</span>, <span class="hljs-operator">*</span>) {
    use()
} <span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span> <span class="hljs-keyword">#unavailable</span>(<span class="hljs-keyword">watchOS</span> <span class="hljs-number">9</span>) {
}

<span class="hljs-keyword">@available</span>(<span class="hljs-operator">*</span>, deprecated, message: <span class="hljs-string">&quot;Use other&quot;</span>, renamed: <span class="hljs-string">&quot;other()&quot;</span>)
<span class="hljs-keyword">func</span> <span class="hljs-title function_">old</span>() { }
""");
    }

    [Fact]
    public void CompilerDirectives()
    {
        AssertHighlighter("swift",
"""
#if DEBUG && os(iOS)
let mode = "debug"
#elseif canImport(UIKit)
let mode = "uikit"
#else
#warning("Unknown")
#endif
let file = #file, line = #line, fn = #function
let sel = #selector(tap(_:))
let path = #keyPath(Person.name)
""",
"""
<span class="hljs-keyword">#if</span> <span class="hljs-type">DEBUG</span> <span class="hljs-operator">&amp;&amp;</span> os(iOS)
<span class="hljs-keyword">let</span> mode <span class="hljs-operator">=</span> <span class="hljs-string">&quot;debug&quot;</span>
<span class="hljs-keyword">#elseif</span> canImport(<span class="hljs-type">UIKit</span>)
<span class="hljs-keyword">let</span> mode <span class="hljs-operator">=</span> <span class="hljs-string">&quot;uikit&quot;</span>
<span class="hljs-keyword">#else</span>
<span class="hljs-keyword">#warning</span>(<span class="hljs-string">&quot;Unknown&quot;</span>)
<span class="hljs-keyword">#endif</span>
<span class="hljs-keyword">let</span> file <span class="hljs-operator">=</span> <span class="hljs-keyword">#file</span>, line <span class="hljs-operator">=</span> <span class="hljs-keyword">#line</span>, fn <span class="hljs-operator">=</span> <span class="hljs-keyword">#function</span>
<span class="hljs-keyword">let</span> sel <span class="hljs-operator">=</span> <span class="hljs-keyword">#selector</span>(tap(<span class="hljs-keyword">_</span>:))
<span class="hljs-keyword">let</span> path <span class="hljs-operator">=</span> <span class="hljs-keyword">#keyPath</span>(<span class="hljs-type">Person</span>.name)
""");
    }

    [Fact]
    public void Attributes()
    {
        AssertHighlighter("swift",
"""
@objc(MyClass) class Legacy: NSObject {
    @objc dynamic var value = 0
    @IBOutlet weak var label: UILabel!
    @IBAction func tapped(_ sender: Any) { }
    @discardableResult func run() -> Int { 0 }
    @inlinable public func fast() { }
    @convention(c) typealias Callback = (Int32) -> Void
}
@main
struct App { }
""",
"""
<span class="hljs-keyword">@objc(MyClass)</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Legacy</span>: <span class="hljs-title class_ inherited__">NSObject</span> {
    <span class="hljs-keyword">@objc</span> <span class="hljs-keyword">dynamic</span> <span class="hljs-keyword">var</span> value <span class="hljs-operator">=</span> <span class="hljs-number">0</span>
    <span class="hljs-keyword">@IBOutlet</span> <span class="hljs-keyword">weak</span> <span class="hljs-keyword">var</span> label: <span class="hljs-type">UILabel</span>!
    <span class="hljs-keyword">@IBAction</span> <span class="hljs-keyword">func</span> <span class="hljs-title function_">tapped</span>(<span class="hljs-keyword">_</span> <span class="hljs-params">sender</span>: <span class="hljs-keyword">Any</span>) { }
    <span class="hljs-keyword">@discardableResult</span> <span class="hljs-keyword">func</span> <span class="hljs-title function_">run</span>() -&gt; <span class="hljs-type">Int</span> { <span class="hljs-number">0</span> }
    <span class="hljs-keyword">@inlinable</span> <span class="hljs-keyword">public</span> <span class="hljs-keyword">func</span> <span class="hljs-title function_">fast</span>() { }
    <span class="hljs-keyword">@convention(c)</span> <span class="hljs-keyword">typealias</span> <span class="hljs-type">Callback</span> <span class="hljs-operator">=</span> (<span class="hljs-type">Int32</span>) -&gt; <span class="hljs-type">Void</span>
}
<span class="hljs-keyword">@main</span>
<span class="hljs-keyword">struct</span> <span class="hljs-title class_">App</span> { }
""");
    }

    [Fact]
    public void AccessModifiers()
    {
        AssertHighlighter("swift",
"""
public struct Point {
    public private(set) var x: Double
    fileprivate var y: Double
    internal let z: Double
    open class Base { }
    package var pkg = 1
    public init(x: Double, y: Double, z: Double) {
        self.x = x
        self.y = y
        self.z = z
    }
    init?(string: String) { return nil }
    init!(forced: Int) { }
    deinit { }
    subscript(index: Int) -> Double { x }
    convenience required init() { }
}
""",
"""
<span class="hljs-keyword">public</span> <span class="hljs-keyword">struct</span> <span class="hljs-title class_">Point</span> {
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">private(set)</span> <span class="hljs-keyword">var</span> x: <span class="hljs-type">Double</span>
    <span class="hljs-keyword">fileprivate</span> <span class="hljs-keyword">var</span> y: <span class="hljs-type">Double</span>
    <span class="hljs-keyword">internal</span> <span class="hljs-keyword">let</span> z: <span class="hljs-type">Double</span>
    <span class="hljs-keyword">open</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Base</span> { }
    <span class="hljs-keyword">package</span> <span class="hljs-keyword">var</span> pkg <span class="hljs-operator">=</span> <span class="hljs-number">1</span>
    <span class="hljs-keyword">public</span> <span class="hljs-keyword">init</span>(<span class="hljs-params">x</span>: <span class="hljs-type">Double</span>, <span class="hljs-params">y</span>: <span class="hljs-type">Double</span>, <span class="hljs-params">z</span>: <span class="hljs-type">Double</span>) {
        <span class="hljs-keyword">self</span>.x <span class="hljs-operator">=</span> x
        <span class="hljs-keyword">self</span>.y <span class="hljs-operator">=</span> y
        <span class="hljs-keyword">self</span>.z <span class="hljs-operator">=</span> z
    }
    <span class="hljs-keyword">init?</span>(<span class="hljs-params">string</span>: <span class="hljs-type">String</span>) { <span class="hljs-keyword">return</span> <span class="hljs-literal">nil</span> }
    <span class="hljs-keyword">init!</span>(<span class="hljs-params">forced</span>: <span class="hljs-type">Int</span>) { }
    <span class="hljs-keyword">deinit</span> { }
    <span class="hljs-keyword">subscript</span>(<span class="hljs-params">index</span>: <span class="hljs-type">Int</span>) -&gt; <span class="hljs-type">Double</span> { x }
    <span class="hljs-keyword">convenience</span> <span class="hljs-keyword">required</span> <span class="hljs-keyword">init</span>() { }
}
""");
    }

    [Fact]
    public void ClassFunc()
    {
        AssertHighlighter("swift",
"""
class Factory {
    class func make() -> Factory { Factory() }
    class var shared: Factory { Factory() }
    static let instance = Factory()
    override func method() { super.method() }
}
""",
"""
<span class="hljs-keyword">class</span> <span class="hljs-title class_">Factory</span> {
    <span class="hljs-keyword">class</span> <span class="hljs-keyword">func</span> <span class="hljs-title function_">make</span>() -&gt; <span class="hljs-type">Factory</span> { <span class="hljs-type">Factory</span>() }
    <span class="hljs-keyword">class</span> <span class="hljs-keyword">var</span> shared: <span class="hljs-type">Factory</span> { <span class="hljs-type">Factory</span>() }
    <span class="hljs-keyword">static</span> <span class="hljs-keyword">let</span> instance <span class="hljs-operator">=</span> <span class="hljs-type">Factory</span>()
    <span class="hljs-keyword">override</span> <span class="hljs-keyword">func</span> <span class="hljs-title function_">method</span>() { <span class="hljs-keyword">super</span>.method() }
}
""");
    }

    [Fact]
    public void Typealias()
    {
        AssertHighlighter("swift",
"""
typealias Handler = (Result<Data, Error>) -> Void
typealias StringDictionary<Value> = Dictionary<String, Value>
""",
"""
<span class="hljs-keyword">typealias</span> <span class="hljs-type">Handler</span> <span class="hljs-operator">=</span> (<span class="hljs-type">Result</span>&lt;<span class="hljs-type">Data</span>, <span class="hljs-type">Error</span>&gt;) -&gt; <span class="hljs-type">Void</span>
<span class="hljs-keyword">typealias</span> <span class="hljs-type">StringDictionary</span>&lt;<span class="hljs-type">Value</span>&gt; <span class="hljs-operator">=</span> <span class="hljs-type">Dictionary</span>&lt;<span class="hljs-type">String</span>, <span class="hljs-type">Value</span>&gt;
""");
    }

    [Fact]
    public void KeypathsTypes()
    {
        AssertHighlighter("swift",
"""
let kp = \Person.name
let names = people.map(\.name)
let t: Person.Type = Person.self
let p: any Proto = value
let q: some Proto = value
let meta = Proto.Protocol.self
let composed: Codable & Hashable
let anyObj: AnyObject
let s: Self
""",
"""
<span class="hljs-keyword">let</span> kp <span class="hljs-operator">=</span> \<span class="hljs-type">Person</span>.name
<span class="hljs-keyword">let</span> names <span class="hljs-operator">=</span> people.map(\.name)
<span class="hljs-keyword">let</span> t: <span class="hljs-type">Person</span>.<span class="hljs-keyword">Type</span> <span class="hljs-operator">=</span> <span class="hljs-type">Person</span>.<span class="hljs-keyword">self</span>
<span class="hljs-keyword">let</span> p: <span class="hljs-keyword">any</span> <span class="hljs-type">Proto</span> <span class="hljs-operator">=</span> value
<span class="hljs-keyword">let</span> q: <span class="hljs-keyword">some</span> <span class="hljs-type">Proto</span> <span class="hljs-operator">=</span> value
<span class="hljs-keyword">let</span> meta <span class="hljs-operator">=</span> <span class="hljs-type">Proto</span>.<span class="hljs-keyword">Protocol</span>.<span class="hljs-keyword">self</span>
<span class="hljs-keyword">let</span> composed: <span class="hljs-type">Codable</span> &amp; <span class="hljs-type">Hashable</span>
<span class="hljs-keyword">let</span> anyObj: <span class="hljs-type">AnyObject</span>
<span class="hljs-keyword">let</span> s: <span class="hljs-keyword">Self</span>
""");
    }

    [Fact]
    public void RegexLiterals()
    {
        AssertHighlighter("swift",
"""
let regex = /\d+(?:\.\d+)?/
let extended = #/
  (?<year>\d{4}) # the year
  -(?<month>\d{2})
/#
let digits = try Regex("[0-9]+")
let words = /[a-z]+/.ignoresCase()
""",
"""
<span class="hljs-keyword">let</span> regex <span class="hljs-operator">=</span> <span class="hljs-regexp">/\d+(?:\.\d+)?/</span>
<span class="hljs-keyword">let</span> extended <span class="hljs-operator">=</span> <span class="hljs-regexp">#/
  (?&lt;year&gt;\d{4}) <span class="hljs-comment"># the year</span>
  -(?&lt;month&gt;\d{2})
/#</span>
<span class="hljs-keyword">let</span> digits <span class="hljs-operator">=</span> <span class="hljs-keyword">try</span> <span class="hljs-type">Regex</span>(<span class="hljs-string">&quot;[0-9]+&quot;</span>)
<span class="hljs-keyword">let</span> words <span class="hljs-operator">=</span> <span class="hljs-regexp">/[a-z]+/</span>.ignoresCase()
""");
    }

    [Fact]
    public void Division_WithoutSpacesIsARegexLiteral()
    {
        AssertHighlighter("swift",
"""
let ratio = total / count
let half = a/b/c
let x = y / 2 // comment
""",
"""
<span class="hljs-keyword">let</span> ratio <span class="hljs-operator">=</span> total <span class="hljs-operator">/</span> count
<span class="hljs-keyword">let</span> half <span class="hljs-operator">=</span> a<span class="hljs-regexp">/b/</span>c
<span class="hljs-keyword">let</span> x <span class="hljs-operator">=</span> y <span class="hljs-operator">/</span> <span class="hljs-number">2</span> <span class="hljs-comment">// comment</span>
""");
    }

    [Fact]
    public void QuotedIdentifiers()
    {
        AssertHighlighter("swift",
"""
let `default` = 1
let `class` = "x"
func `init`() { }
""",
"""
<span class="hljs-keyword">let</span> `default` <span class="hljs-operator">=</span> <span class="hljs-number">1</span>
<span class="hljs-keyword">let</span> `class` <span class="hljs-operator">=</span> <span class="hljs-string">&quot;x&quot;</span>
<span class="hljs-keyword">func</span> <span class="hljs-title function_">`init`</span>() { }
""");
    }

    [Fact]
    public void ParameterPacks()
    {
        AssertHighlighter("swift",
"""
func all<each T>(_ values: repeat each T) -> (repeat each T) {
    return (repeat each values)
}
""",
"""
<span class="hljs-keyword">func</span> <span class="hljs-title function_">all</span>&lt;<span class="hljs-keyword">each</span> <span class="hljs-type">T</span>&gt;(<span class="hljs-keyword">_</span> <span class="hljs-params">values</span>: <span class="hljs-keyword">repeat</span> <span class="hljs-keyword">each</span> <span class="hljs-type">T</span>) -&gt; (<span class="hljs-keyword">repeat</span> <span class="hljs-keyword">each</span> <span class="hljs-type">T</span>) {
    <span class="hljs-keyword">return</span> (<span class="hljs-keyword">repeat</span> <span class="hljs-keyword">each</span> values)
}
""");
    }

    [Fact]
    public void Ownership()
    {
        AssertHighlighter("swift",
"""
func consume(_ x: consuming String) { }
func borrow(_ x: borrowing String) { }
let y = consume x
let z = copy y
struct NC: ~Copyable { }
""",
"""
<span class="hljs-keyword">func</span> <span class="hljs-title function_">consume</span>(<span class="hljs-keyword">_</span> <span class="hljs-params">x</span>: <span class="hljs-keyword">consuming</span> <span class="hljs-type">String</span>) { }
<span class="hljs-keyword">func</span> <span class="hljs-title function_">borrow</span>(<span class="hljs-keyword">_</span> <span class="hljs-params">x</span>: <span class="hljs-keyword">borrowing</span> <span class="hljs-type">String</span>) { }
<span class="hljs-keyword">let</span> y <span class="hljs-operator">=</span> <span class="hljs-keyword">consume</span> x
<span class="hljs-keyword">let</span> z <span class="hljs-operator">=</span> <span class="hljs-keyword">copy</span> y
<span class="hljs-keyword">struct</span> <span class="hljs-title class_">NC</span>: ~<span class="hljs-title class_ inherited__">Copyable</span> { }
""");
    }

    [Fact]
    public void Distributed()
    {
        AssertHighlighter("swift",
"""
distributed actor Player {
    distributed func move() { }
}
""",
"""
<span class="hljs-keyword">distributed</span> <span class="hljs-keyword">actor</span> <span class="hljs-title class_">Player</span> {
    <span class="hljs-keyword">distributed</span> <span class="hljs-keyword">func</span> <span class="hljs-title function_">move</span>() { }
}
""");
    }

    [Fact]
    public void WhereClauses()
    {
        AssertHighlighter("swift",
"""
extension Array where Element == Int { }
func f<T>(_ t: T) where T: Hashable & Codable { }
""",
"""
<span class="hljs-keyword">extension</span> <span class="hljs-title class_">Array</span> <span class="hljs-keyword">where</span> <span class="hljs-type">Element</span> <span class="hljs-operator">==</span> <span class="hljs-type">Int</span> { }
<span class="hljs-keyword">func</span> <span class="hljs-title function_">f</span>&lt;<span class="hljs-type">T</span>&gt;(<span class="hljs-keyword">_</span> <span class="hljs-params">t</span>: <span class="hljs-type">T</span>) <span class="hljs-keyword">where</span> <span class="hljs-type">T</span>: <span class="hljs-type">Hashable</span> &amp; <span class="hljs-type">Codable</span> { }
""");
    }

    [Fact]
    public void Tuples()
    {
        AssertHighlighter("swift",
"""
let point = (x: 1, y: 2)
let named: (first: String, last: String) = ("a", "b")
print(point.x, point.0)
let nested = ((1, 2), (3, (4, 5)))
""",
"""
<span class="hljs-keyword">let</span> point <span class="hljs-operator">=</span> (x: <span class="hljs-number">1</span>, y: <span class="hljs-number">2</span>)
<span class="hljs-keyword">let</span> named: (first: <span class="hljs-type">String</span>, last: <span class="hljs-type">String</span>) <span class="hljs-operator">=</span> (<span class="hljs-string">&quot;a&quot;</span>, <span class="hljs-string">&quot;b&quot;</span>)
<span class="hljs-built_in">print</span>(point.x, point.<span class="hljs-number">0</span>)
<span class="hljs-keyword">let</span> nested <span class="hljs-operator">=</span> ((<span class="hljs-number">1</span>, <span class="hljs-number">2</span>), (<span class="hljs-number">3</span>, (<span class="hljs-number">4</span>, <span class="hljs-number">5</span>)))
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("swift",
"""
let π = 3.14
let café = "coffee"
let 你好 = "hi"
let emoji = "😀"
""",
"""
<span class="hljs-keyword">let</span> π <span class="hljs-operator">=</span> <span class="hljs-number">3.14</span>
<span class="hljs-keyword">let</span> café <span class="hljs-operator">=</span> <span class="hljs-string">&quot;coffee&quot;</span>
<span class="hljs-keyword">let</span> 你好 <span class="hljs-operator">=</span> <span class="hljs-string">&quot;hi&quot;</span>
<span class="hljs-keyword">let</span> emoji <span class="hljs-operator">=</span> <span class="hljs-string">&quot;😀&quot;</span>
""");
    }

    [Fact]
    public void ImplicitParams()
    {
        AssertHighlighter("swift",
"""
let sum = values.reduce(0) { $0 + $1 }
let binding = $viewModel.name
""",
"""
<span class="hljs-keyword">let</span> sum <span class="hljs-operator">=</span> values.reduce(<span class="hljs-number">0</span>) { <span class="hljs-variable">$0</span> <span class="hljs-operator">+</span> <span class="hljs-variable">$1</span> }
<span class="hljs-keyword">let</span> binding <span class="hljs-operator">=</span> <span class="hljs-variable">$viewModel</span>.name
""");
    }

    [Fact]
    public void StructInitCalls()
    {
        AssertHighlighter("swift",
"""
let date = Date()
let url = URL(string: "https://example.com")!
let view = UIView(frame: .zero)
let formatter = DateFormatter()
formatter.dateFormat = "yyyy"
""",
"""
<span class="hljs-keyword">let</span> date <span class="hljs-operator">=</span> <span class="hljs-type">Date</span>()
<span class="hljs-keyword">let</span> url <span class="hljs-operator">=</span> <span class="hljs-type">URL</span>(string: <span class="hljs-string">&quot;https://example.com&quot;</span>)<span class="hljs-operator">!</span>
<span class="hljs-keyword">let</span> view <span class="hljs-operator">=</span> <span class="hljs-type">UIView</span>(frame: .zero)
<span class="hljs-keyword">let</span> formatter <span class="hljs-operator">=</span> <span class="hljs-type">DateFormatter</span>()
formatter.dateFormat <span class="hljs-operator">=</span> <span class="hljs-string">&quot;yyyy&quot;</span>
""");
    }

    [Fact]
    public void DotMembers()
    {
        AssertHighlighter("swift",
"""
let a = view.self
let b = x.init(1)
let c = list.first.map { $0 }
let d = items.count.description
let e = obj.default
let f = value.isEmpty
let g = arr.min()
let h = value.print(1)
""",
"""
<span class="hljs-keyword">let</span> a <span class="hljs-operator">=</span> view.<span class="hljs-keyword">self</span>
<span class="hljs-keyword">let</span> b <span class="hljs-operator">=</span> x.<span class="hljs-keyword">init</span>(<span class="hljs-number">1</span>)
<span class="hljs-keyword">let</span> c <span class="hljs-operator">=</span> list.first.map { <span class="hljs-variable">$0</span> }
<span class="hljs-keyword">let</span> d <span class="hljs-operator">=</span> items.count.description
<span class="hljs-keyword">let</span> e <span class="hljs-operator">=</span> obj.default
<span class="hljs-keyword">let</span> f <span class="hljs-operator">=</span> value.isEmpty
<span class="hljs-keyword">let</span> g <span class="hljs-operator">=</span> arr.min()
<span class="hljs-keyword">let</span> h <span class="hljs-operator">=</span> value.print(<span class="hljs-number">1</span>)
""");
    }

    [Fact]
    public void Codable()
    {
        AssertHighlighter("swift",
"""
struct User: Codable, Identifiable {
    let id: UUID
    var name: String
    enum CodingKeys: String, CodingKey {
        case id
        case name = "full_name"
    }
}
""",
"""
<span class="hljs-keyword">struct</span> <span class="hljs-title class_">User</span>: <span class="hljs-title class_ inherited__">Codable</span>, <span class="hljs-title class_ inherited__">Identifiable</span> {
    <span class="hljs-keyword">let</span> id: <span class="hljs-type">UUID</span>
    <span class="hljs-keyword">var</span> name: <span class="hljs-type">String</span>
    <span class="hljs-keyword">enum</span> <span class="hljs-title class_">CodingKeys</span>: <span class="hljs-title class_ inherited__">String</span>, <span class="hljs-title class_ inherited__">CodingKey</span> {
        <span class="hljs-keyword">case</span> id
        <span class="hljs-keyword">case</span> name <span class="hljs-operator">=</span> <span class="hljs-string">&quot;full_name&quot;</span>
    }
}
""");
    }

    [Fact]
    public void ErrorHandling()
    {
        AssertHighlighter("swift",
"""
enum NetworkError: Error {
    case badURL
    case timeout(seconds: Int)
}

func load() throws(NetworkError) -> Data {
    throw .badURL
}

func rethrowing(_ f: () throws -> Void) rethrows { try f() }
""",
"""
<span class="hljs-keyword">enum</span> <span class="hljs-title class_">NetworkError</span>: <span class="hljs-title class_ inherited__">Error</span> {
    <span class="hljs-keyword">case</span> badURL
    <span class="hljs-keyword">case</span> timeout(seconds: <span class="hljs-type">Int</span>)
}

<span class="hljs-keyword">func</span> <span class="hljs-title function_">load</span>() <span class="hljs-keyword">throws</span>(<span class="hljs-type">NetworkError</span>) -&gt; <span class="hljs-type">Data</span> {
    <span class="hljs-keyword">throw</span> .badURL
}

<span class="hljs-keyword">func</span> <span class="hljs-title function_">rethrowing</span>(<span class="hljs-keyword">_</span> <span class="hljs-params">f</span>: () <span class="hljs-keyword">throws</span> -&gt; <span class="hljs-type">Void</span>) <span class="hljs-keyword">rethrows</span> { <span class="hljs-keyword">try</span> f() }
""");
    }

    [Fact]
    public void SwitchPatterns()
    {
        AssertHighlighter("swift",
"""
switch (x, y) {
case (0, 0):
    print("origin")
case (let a, 0) where a > 0:
    print(a)
case (_, _):
    break
}
if case let .success(value) = result { }
""",
"""
<span class="hljs-keyword">switch</span> (x, y) {
<span class="hljs-keyword">case</span> (<span class="hljs-number">0</span>, <span class="hljs-number">0</span>):
    <span class="hljs-built_in">print</span>(<span class="hljs-string">&quot;origin&quot;</span>)
<span class="hljs-keyword">case</span> (<span class="hljs-keyword">let</span> a, <span class="hljs-number">0</span>) <span class="hljs-keyword">where</span> a <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span>:
    <span class="hljs-built_in">print</span>(a)
<span class="hljs-keyword">case</span> (<span class="hljs-keyword">_</span>, <span class="hljs-keyword">_</span>):
    <span class="hljs-keyword">break</span>
}
<span class="hljs-keyword">if</span> <span class="hljs-keyword">case</span> <span class="hljs-keyword">let</span> .success(value) <span class="hljs-operator">=</span> result { }
""");
    }

    [Fact]
    public void WillsetDidset()
    {
        AssertHighlighter("swift",
"""
var score = 0 {
    willSet { print(newValue) }
    didSet(old) { print(old) }
}
""",
"""
<span class="hljs-keyword">var</span> score <span class="hljs-operator">=</span> <span class="hljs-number">0</span> {
    <span class="hljs-keyword">willSet</span> { <span class="hljs-built_in">print</span>(newValue) }
    <span class="hljs-keyword">didSet</span>(old) { <span class="hljs-built_in">print</span>(old) }
}
""");
    }

    [Fact]
    public void StaticSubscript()
    {
        AssertHighlighter("swift",
"""
struct Matrix {
    static subscript(n: Int) -> Int { n }
    subscript<T>(key: T) -> T { key }
}
""",
"""
<span class="hljs-keyword">struct</span> <span class="hljs-title class_">Matrix</span> {
    <span class="hljs-keyword">static</span> <span class="hljs-keyword">subscript</span>(<span class="hljs-params">n</span>: <span class="hljs-type">Int</span>) -&gt; <span class="hljs-type">Int</span> { n }
    <span class="hljs-keyword">subscript</span>&lt;<span class="hljs-type">T</span>&gt;(<span class="hljs-params">key</span>: <span class="hljs-type">T</span>) -&gt; <span class="hljs-type">T</span> { key }
}
""");
    }

    [Fact]
    public void GenericFunctionCall()
    {
        AssertHighlighter("swift",
"""
let a = Array<Int>(repeating: 0, count: 5)
let b = max(1, 2)
let c = zip(a, b).map(+)
let d = stride(from: 0, to: 10, by: 2)
let t = type(of: value)
assert(x > 0, "positive")
fatalError("boom")
""",
"""
<span class="hljs-keyword">let</span> a <span class="hljs-operator">=</span> <span class="hljs-type">Array</span>&lt;<span class="hljs-type">Int</span>&gt;(repeating: <span class="hljs-number">0</span>, count: <span class="hljs-number">5</span>)
<span class="hljs-keyword">let</span> b <span class="hljs-operator">=</span> <span class="hljs-built_in">max</span>(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>)
<span class="hljs-keyword">let</span> c <span class="hljs-operator">=</span> <span class="hljs-built_in">zip</span>(a, b).map(<span class="hljs-operator">+</span>)
<span class="hljs-keyword">let</span> d <span class="hljs-operator">=</span> <span class="hljs-built_in">stride</span>(from: <span class="hljs-number">0</span>, to: <span class="hljs-number">10</span>, by: <span class="hljs-number">2</span>)
<span class="hljs-keyword">let</span> t <span class="hljs-operator">=</span> <span class="hljs-built_in">type</span>(of: value)
<span class="hljs-built_in">assert</span>(x <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span>, <span class="hljs-string">&quot;positive&quot;</span>)
<span class="hljs-built_in">fatalError</span>(<span class="hljs-string">&quot;boom&quot;</span>)
""");
    }

    [Fact]
    public void WeakUnowned()
    {
        AssertHighlighter("swift",
"""
weak var delegate: Delegate?
unowned let owner: Owner
unowned(unsafe) var raw: Node
""",
"""
<span class="hljs-keyword">weak</span> <span class="hljs-keyword">var</span> delegate: <span class="hljs-type">Delegate</span>?
<span class="hljs-keyword">unowned</span> <span class="hljs-keyword">let</span> owner: <span class="hljs-type">Owner</span>
<span class="hljs-keyword">unowned(unsafe)</span> <span class="hljs-keyword">var</span> raw: <span class="hljs-type">Node</span>
""");
    }

    [Fact]
    public void Semicolons()
    {
        AssertHighlighter("swift",
"""
let a = 1; let b = 2; var c = a + b
""",
"""
<span class="hljs-keyword">let</span> a <span class="hljs-operator">=</span> <span class="hljs-number">1</span>; <span class="hljs-keyword">let</span> b <span class="hljs-operator">=</span> <span class="hljs-number">2</span>; <span class="hljs-keyword">var</span> c <span class="hljs-operator">=</span> a <span class="hljs-operator">+</span> b
""");
    }

    [Fact]
    public void InterpolationDeep()
    {
        AssertHighlighter("swift",
"""
let s = "Value: \(dict["key"] ?? "none") and \(obj.method(a, b: "c"))"
let t = "Nested: \("inner \(deep) text")"
let u = "Math: \(1 + 2 * 3) \(true) \(nil)"
""",
"""
<span class="hljs-keyword">let</span> s <span class="hljs-operator">=</span> <span class="hljs-string">&quot;Value: <span class="hljs-subst">\(dict[<span class="hljs-string">&quot;key&quot;</span>] <span class="hljs-operator">??</span> <span class="hljs-string">&quot;none&quot;</span>)</span> and <span class="hljs-subst">\(obj.method(a, b: <span class="hljs-string">&quot;c&quot;</span>))</span>&quot;</span>
<span class="hljs-keyword">let</span> t <span class="hljs-operator">=</span> <span class="hljs-string">&quot;Nested: <span class="hljs-subst">\(<span class="hljs-string">&quot;inner <span class="hljs-subst">\(deep)</span> text&quot;</span>)</span>&quot;</span>
<span class="hljs-keyword">let</span> u <span class="hljs-operator">=</span> <span class="hljs-string">&quot;Math: <span class="hljs-subst">\(<span class="hljs-number">1</span> <span class="hljs-operator">+</span> <span class="hljs-number">2</span> <span class="hljs-operator">*</span> <span class="hljs-number">3</span>)</span> <span class="hljs-subst">\(<span class="hljs-literal">true</span>)</span> <span class="hljs-subst">\(<span class="hljs-literal">nil</span>)</span>&quot;</span>
""");
    }

    [Fact]
    public void UnterminatedString_ContinuesOnTheNextLines()
    {
        AssertHighlighter("swift",
"""
let s = "no end
let x = 1
""",
"""
<span class="hljs-keyword">let</span> s <span class="hljs-operator">=</span> <span class="hljs-string">&quot;no end
let x = 1</span>
""");
    }

    [Fact]
    public void OperatorEdge()
    {
        AssertHighlighter("swift",
"""
let a = b->c
func f() -> Int
let opt = x!
let chain = a?.b?.c
let tern = cond ? 1 : 2
let d = -1.5
""",
"""
<span class="hljs-keyword">let</span> a <span class="hljs-operator">=</span> b-&gt;c
<span class="hljs-keyword">func</span> <span class="hljs-title function_">f</span>() -&gt; <span class="hljs-type">Int</span>
<span class="hljs-keyword">let</span> opt <span class="hljs-operator">=</span> x<span class="hljs-operator">!</span>
<span class="hljs-keyword">let</span> chain <span class="hljs-operator">=</span> a<span class="hljs-operator">?</span>.b<span class="hljs-operator">?</span>.c
<span class="hljs-keyword">let</span> tern <span class="hljs-operator">=</span> cond <span class="hljs-operator">?</span> <span class="hljs-number">1</span> : <span class="hljs-number">2</span>
<span class="hljs-keyword">let</span> d <span class="hljs-operator">=</span> <span class="hljs-operator">-</span><span class="hljs-number">1.5</span>
""");
    }

    [Fact]
    public void SwiftuiApp()
    {
        AssertHighlighter("swift",
"""
import SwiftUI

@main
struct MyApp: App {
    @StateObject private var store = Store()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environmentObject(store)
        }
    }
}
""",
"""
<span class="hljs-keyword">import</span> SwiftUI

<span class="hljs-keyword">@main</span>
<span class="hljs-keyword">struct</span> <span class="hljs-title class_">MyApp</span>: <span class="hljs-title class_ inherited__">App</span> {
    <span class="hljs-meta">@StateObject</span> <span class="hljs-keyword">private</span> <span class="hljs-keyword">var</span> store <span class="hljs-operator">=</span> <span class="hljs-type">Store</span>()

    <span class="hljs-keyword">var</span> body: <span class="hljs-keyword">some</span> <span class="hljs-type">Scene</span> {
        <span class="hljs-type">WindowGroup</span> {
            <span class="hljs-type">ContentView</span>()
                .environmentObject(store)
        }
    }
}
""");
    }

    [Fact]
    public void Sendable()
    {
        AssertHighlighter("swift",
"""
final class Box: @unchecked Sendable { }
struct S: Sendable { }
func run(_ f: @Sendable @escaping () -> Void) { }
""",
"""
<span class="hljs-keyword">final</span> <span class="hljs-keyword">class</span> <span class="hljs-title class_">Box</span>: @unchecked <span class="hljs-title class_ inherited__">Sendable</span> { }
<span class="hljs-keyword">struct</span> <span class="hljs-title class_">S</span>: <span class="hljs-title class_ inherited__">Sendable</span> { }
<span class="hljs-keyword">func</span> <span class="hljs-title function_">run</span>(<span class="hljs-keyword">_</span> <span class="hljs-params">f</span>: <span class="hljs-keyword">@Sendable</span> <span class="hljs-keyword">@escaping</span> () -&gt; <span class="hljs-type">Void</span>) { }
""");
    }

    [Fact]
    public void Isolated()
    {
        AssertHighlighter("swift",
"""
func work(actor: isolated MyActor) { }
nonisolated(unsafe) var global = 0
""",
"""
<span class="hljs-keyword">func</span> <span class="hljs-title function_">work</span>(<span class="hljs-params">actor</span>: <span class="hljs-keyword">isolated</span> <span class="hljs-type">MyActor</span>) { }
<span class="hljs-keyword">nonisolated</span>(unsafe) <span class="hljs-keyword">var</span> global <span class="hljs-operator">=</span> <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void FuncEdge()
    {
        AssertHighlighter("swift",
"""
func noParams() {}
func generic<T: Comparable>(a: T) {}
func spaced (x: Int) {}
func withDefault(x: Int = 5, y: String = "s") {}
func closureParam(completion: @escaping (Result<Int, Error>) -> Void) {}
func tupleReturn() -> (Int, String) { (1, "") }
func `quoted`() {}
func ==(lhs: A, rhs: A) -> Bool { true }
""",
"""
<span class="hljs-keyword">func</span> <span class="hljs-title function_">noParams</span>() {}
<span class="hljs-keyword">func</span> <span class="hljs-title function_">generic</span>&lt;<span class="hljs-type">T</span>: <span class="hljs-type">Comparable</span>&gt;(<span class="hljs-params">a</span>: <span class="hljs-type">T</span>) {}
<span class="hljs-keyword">func</span> <span class="hljs-title function_">spaced</span> (<span class="hljs-params">x</span>: <span class="hljs-type">Int</span>) {}
<span class="hljs-keyword">func</span> <span class="hljs-title function_">withDefault</span>(<span class="hljs-params">x</span>: <span class="hljs-type">Int</span> <span class="hljs-operator">=</span> <span class="hljs-number">5</span>, <span class="hljs-params">y</span>: <span class="hljs-type">String</span> <span class="hljs-operator">=</span> <span class="hljs-string">&quot;s&quot;</span>) {}
<span class="hljs-keyword">func</span> <span class="hljs-title function_">closureParam</span>(<span class="hljs-params">completion</span>: <span class="hljs-keyword">@escaping</span> (<span class="hljs-type">Result</span>&lt;<span class="hljs-type">Int</span>, <span class="hljs-type">Error</span>&gt;) -&gt; <span class="hljs-type">Void</span>) {}
<span class="hljs-keyword">func</span> <span class="hljs-title function_">tupleReturn</span>() -&gt; (<span class="hljs-type">Int</span>, <span class="hljs-type">String</span>) { (<span class="hljs-number">1</span>, <span class="hljs-string">&quot;&quot;</span>) }
<span class="hljs-keyword">func</span> <span class="hljs-title function_">`quoted`</span>() {}
<span class="hljs-keyword">func</span> <span class="hljs-title function_">==</span>(<span class="hljs-params">lhs</span>: <span class="hljs-type">A</span>, <span class="hljs-params">rhs</span>: <span class="hljs-type">A</span>) -&gt; <span class="hljs-type">Bool</span> { <span class="hljs-literal">true</span> }
""");
    }

    [Fact]
    public void SwiftTesting()
    {
        AssertHighlighter("swift",
"""
import Testing

@Test("Addition works", arguments: [1, 2, 3])
func addition(value: Int) async throws {
    #expect(value + 1 > value)
    let user = try #require(await fetch())
    #if os(macOS)
    #warning("mac only")
    #endif
}
""",
"""
<span class="hljs-keyword">import</span> Testing

<span class="hljs-meta">@Test</span>(<span class="hljs-string">&quot;Addition works&quot;</span>, arguments: [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>])
<span class="hljs-keyword">func</span> <span class="hljs-title function_">addition</span>(<span class="hljs-params">value</span>: <span class="hljs-type">Int</span>) <span class="hljs-keyword">async</span> <span class="hljs-keyword">throws</span> {
    <span class="hljs-meta">#expect</span>(value <span class="hljs-operator">+</span> <span class="hljs-number">1</span> <span class="hljs-operator">&gt;</span> value)
    <span class="hljs-keyword">let</span> user <span class="hljs-operator">=</span> <span class="hljs-keyword">try</span> <span class="hljs-meta">#require</span>(<span class="hljs-keyword">await</span> fetch())
    <span class="hljs-keyword">#if</span> os(macOS)
    <span class="hljs-keyword">#warning</span>(<span class="hljs-string">&quot;mac only&quot;</span>)
    <span class="hljs-keyword">#endif</span>
}
""");
    }

    [Fact]
    public void GenericWhereNested()
    {
        AssertHighlighter("swift",
"""
func process<T: Collection>(items: T) -> [T.Element] where T.Element: Hashable {
    Array(Set(items))
}
""",
"""
<span class="hljs-keyword">func</span> <span class="hljs-title function_">process</span>&lt;<span class="hljs-type">T</span>: <span class="hljs-type">Collection</span>&gt;(<span class="hljs-params">items</span>: <span class="hljs-type">T</span>) -&gt; [<span class="hljs-type">T</span>.<span class="hljs-type">Element</span>] <span class="hljs-keyword">where</span> <span class="hljs-type">T</span>.<span class="hljs-type">Element</span>: <span class="hljs-type">Hashable</span> {
    <span class="hljs-type">Array</span>(<span class="hljs-type">Set</span>(items))
}
""");
    }

    [Fact]
    public void EnumAssociated()
    {
        AssertHighlighter("swift",
"""
enum Result<Success, Failure: Error> {
    case success(Success)
    case failure(Failure)
}
let r: Result<Int, Error> = .success(1)
""",
"""
<span class="hljs-keyword">enum</span> <span class="hljs-title class_">Result</span>&lt;<span class="hljs-type">Success</span>, <span class="hljs-type">Failure</span>: <span class="hljs-type">Error</span>&gt; {
    <span class="hljs-keyword">case</span> success(<span class="hljs-type">Success</span>)
    <span class="hljs-keyword">case</span> failure(<span class="hljs-type">Failure</span>)
}
<span class="hljs-keyword">let</span> r: <span class="hljs-type">Result</span>&lt;<span class="hljs-type">Int</span>, <span class="hljs-type">Error</span>&gt; <span class="hljs-operator">=</span> .success(<span class="hljs-number">1</span>)
""");
    }

    [Fact]
    public void ClosureCaptureList()
    {
        AssertHighlighter("swift",
"""
lazy var handler: () -> Void = { [unowned self] in
    self.run()
}
""",
"""
<span class="hljs-keyword">lazy</span> <span class="hljs-keyword">var</span> handler: () -&gt; <span class="hljs-type">Void</span> <span class="hljs-operator">=</span> { [<span class="hljs-keyword">unowned</span> <span class="hljs-keyword">self</span>] <span class="hljs-keyword">in</span>
    <span class="hljs-keyword">self</span>.run()
}
""");
    }

    [Fact]
    public void StringSpecial()
    {
        AssertHighlighter("swift",
""""
let a = "Tab\there"
let b = "Unicode: \u{2665} \u{1F496}"
let c = "\\(not interpolated)"
let d = #"Regex: \d+ \#n"#
let e = ""
let f = """
"""
"""",
"""
<span class="hljs-keyword">let</span> a <span class="hljs-operator">=</span> <span class="hljs-string">&quot;Tab<span class="hljs-subst">\t</span>here&quot;</span>
<span class="hljs-keyword">let</span> b <span class="hljs-operator">=</span> <span class="hljs-string">&quot;Unicode: <span class="hljs-subst">\u{2665}</span> <span class="hljs-subst">\u{1F496}</span>&quot;</span>
<span class="hljs-keyword">let</span> c <span class="hljs-operator">=</span> <span class="hljs-string">&quot;<span class="hljs-subst">\\</span>(not interpolated)&quot;</span>
<span class="hljs-keyword">let</span> d <span class="hljs-operator">=</span> <span class="hljs-string">#&quot;Regex: \d+ <span class="hljs-subst">\#n</span>&quot;#</span>
<span class="hljs-keyword">let</span> e <span class="hljs-operator">=</span> <span class="hljs-string">&quot;&quot;</span>
<span class="hljs-keyword">let</span> f <span class="hljs-operator">=</span> <span class="hljs-string">&quot;&quot;&quot;
&quot;&quot;&quot;</span>
""");
    }

    [Fact]
    public void FreestandingMacros_DoNotHideNumberSignKeywords()
    {
        AssertHighlighter("swift",
"""
let a = #iffy
let b = #elseifx
#sourceLocation(file: "a.swift", line: 1)
let c = #fileID
let d = #colorLiteral(red: 1, green: 0, blue: 0, alpha: 1)
let e = (#line, #Macro(x))
func f(x: Int = #line) { }
""",
"""
<span class="hljs-keyword">let</span> a <span class="hljs-operator">=</span> <span class="hljs-meta">#iffy</span>
<span class="hljs-keyword">let</span> b <span class="hljs-operator">=</span> <span class="hljs-meta">#elseifx</span>
<span class="hljs-keyword">#sourceLocation</span>(file: <span class="hljs-string">&quot;a.swift&quot;</span>, line: <span class="hljs-number">1</span>)
<span class="hljs-keyword">let</span> c <span class="hljs-operator">=</span> <span class="hljs-keyword">#fileID</span>
<span class="hljs-keyword">let</span> d <span class="hljs-operator">=</span> <span class="hljs-keyword">#colorLiteral</span>(red: <span class="hljs-number">1</span>, green: <span class="hljs-number">0</span>, blue: <span class="hljs-number">0</span>, alpha: <span class="hljs-number">1</span>)
<span class="hljs-keyword">let</span> e <span class="hljs-operator">=</span> (<span class="hljs-keyword">#line</span>, <span class="hljs-meta">#Macro</span>(x))
<span class="hljs-keyword">func</span> <span class="hljs-title function_">f</span>(<span class="hljs-params">x</span>: <span class="hljs-type">Int</span> <span class="hljs-operator">=</span> <span class="hljs-keyword">#line</span>) { }
""");
    }
}
