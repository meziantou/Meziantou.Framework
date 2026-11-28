namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// Marks a method as the host function of an import of a WebAssembly module. The source generator writes a
/// <c>DefineWasmImports(WasmStore store, WasmImports? imports = null)</c> method in the partial type that declares the
/// method: it creates a host function per marked method, whose type and conversions are written at build time.
/// </summary>
/// <remarks>
/// The parameters and the results are <see cref="int"/> or <see cref="uint"/> (<c>i32</c>), <see cref="long"/> or
/// <see cref="ulong"/> (<c>i64</c>), <see cref="float"/> (<c>f32</c>), <see cref="double"/> (<c>f64</c>),
/// <see cref="object"/> (<c>externref</c>: the host value of a <see cref="WasmHostReference"/>) and
/// <see cref="WasmFunction"/> (<c>funcref</c>). A reference type annotated as not nullable is a non-nullable reference
/// (<c>(ref extern)</c>, <c>(ref func)</c>). A method with several results returns a tuple. A first parameter of type
/// <see cref="WasmCaller"/> is not a parameter of the WebAssembly function: it receives the caller of the function, such
/// as the instance whose memory the function reads.
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.WasmEngine.Tests/WasmSourceGeneratorTests.cs" ref:region="WasmEngine_TypedImportsHost" ref:dedent="true">
/// // A partial class: the generator adds the DefineWasmImports method
/// public sealed partial class Logger
/// {
///     private readonly List&lt;int&gt; _values = [];
///
///     public IReadOnlyList&lt;int&gt; Values =&gt; _values;
///
///     // A host function of the import "env" "log" whose type is (func (param i32))
///     [WasmImport("env", "log")]
///     public void Log(int value) =&gt; _values.Add(value);
///
///     // A static method: (func (param f64) (result f64))
///     [WasmImport("env", "scale")]
///     public static double Scale(double value) =&gt; value * 2;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class WasmImportAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="WasmImportAttribute"/> class.</summary>
    /// <param name="module">The module name of the import.</param>
    /// <param name="name">The name of the import.</param>
    public WasmImportAttribute(string module, string name)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(name);
        Module = module;
        Name = name;
    }

    /// <summary>Gets the module name of the import.</summary>
    public string Module { get; }

    /// <summary>Gets the name of the import.</summary>
    public string Name { get; }
}
