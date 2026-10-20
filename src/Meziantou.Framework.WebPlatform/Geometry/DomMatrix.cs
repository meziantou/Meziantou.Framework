using System.Globalization;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://drafts.fxtf.org/geometry/#dictdef-dommatrix2dinit</summary>
/// <seealso href="https://drafts.fxtf.org/geometry/#dictdef-dommatrix2dinit"/>
[IdlInterface("DOMMatrix2DInit", "https://drafts.fxtf.org/geometry/#dictdef-dommatrix2dinit")]
public class DomMatrix2DInit
{
    /// <summary>Gets or sets the <c>a</c> component of the matrix, an alias of <c>m11</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-a"/>
    [IdlMember("a", "https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-a")]
    public double? A { get; init; }

    /// <summary>Gets or sets the <c>b</c> component of the matrix, an alias of <c>m12</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-b"/>
    [IdlMember("b", "https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-b")]
    public double? B { get; init; }

    /// <summary>Gets or sets the <c>c</c> component of the matrix, an alias of <c>m21</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-c"/>
    [IdlMember("c", "https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-c")]
    public double? C { get; init; }

    /// <summary>Gets or sets the <c>d</c> component of the matrix, an alias of <c>m22</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-d"/>
    [IdlMember("d", "https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-d")]
    public double? D { get; init; }

    /// <summary>Gets or sets the <c>e</c> component of the matrix, an alias of <c>m41</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-e"/>
    [IdlMember("e", "https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-e")]
    public double? E { get; init; }

    /// <summary>Gets or sets the <c>f</c> component of the matrix, an alias of <c>m42</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-f"/>
    [IdlMember("f", "https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-f")]
    public double? F { get; init; }

    /// <summary>Gets or sets the <c>m11</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-m11"/>
    [IdlMember("m11", "https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-m11")]
    public double? M11 { get; init; }

    /// <summary>Gets or sets the <c>m12</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-m12"/>
    [IdlMember("m12", "https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-m12")]
    public double? M12 { get; init; }

    /// <summary>Gets or sets the <c>m21</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-m21"/>
    [IdlMember("m21", "https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-m21")]
    public double? M21 { get; init; }

    /// <summary>Gets or sets the <c>m22</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-m22"/>
    [IdlMember("m22", "https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-m22")]
    public double? M22 { get; init; }

    /// <summary>Gets or sets the <c>m41</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-m41"/>
    [IdlMember("m41", "https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-m41")]
    public double? M41 { get; init; }

    /// <summary>Gets or sets the <c>m42</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-m42"/>
    [IdlMember("m42", "https://drafts.fxtf.org/geometry/#dom-dommatrix2dinit-m42")]
    public double? M42 { get; init; }
}

/// <summary>https://drafts.fxtf.org/geometry/#dictdef-dommatrixinit</summary>
/// <seealso href="https://drafts.fxtf.org/geometry/#dictdef-dommatrixinit"/>
[IdlInterface("DOMMatrixInit", "https://drafts.fxtf.org/geometry/#dictdef-dommatrixinit")]
public sealed class DomMatrixInit : DomMatrix2DInit
{
    /// <summary>Gets or sets the <c>m13</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m13"/>
    [IdlMember("m13", "https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m13")]
    public double M13 { get; init; }

    /// <summary>Gets or sets the <c>m14</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m14"/>
    [IdlMember("m14", "https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m14")]
    public double M14 { get; init; }

    /// <summary>Gets or sets the <c>m23</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m23"/>
    [IdlMember("m23", "https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m23")]
    public double M23 { get; init; }

    /// <summary>Gets or sets the <c>m24</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m24"/>
    [IdlMember("m24", "https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m24")]
    public double M24 { get; init; }

    /// <summary>Gets or sets the <c>m31</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m31"/>
    [IdlMember("m31", "https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m31")]
    public double M31 { get; init; }

    /// <summary>Gets or sets the <c>m32</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m32"/>
    [IdlMember("m32", "https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m32")]
    public double M32 { get; init; }

    /// <summary>Gets or sets the <c>m33</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m33"/>
    [IdlMember("m33", "https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m33")]
    public double M33 { get; init; } = 1;

    /// <summary>Gets or sets the <c>m34</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m34"/>
    [IdlMember("m34", "https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m34")]
    public double M34 { get; init; }

    /// <summary>Gets or sets the <c>m43</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m43"/>
    [IdlMember("m43", "https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m43")]
    public double M43 { get; init; }

    /// <summary>Gets or sets the <c>m44</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m44"/>
    [IdlMember("m44", "https://drafts.fxtf.org/geometry/#dom-dommatrixinit-m44")]
    public double M44 { get; init; } = 1;

    /// <summary>
    /// Gets or sets a value indicating whether the matrix is a 2D matrix, or null to infer it from the components.
    /// </summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixinit-is2d"/>
    [IdlMember("is2D", "https://drafts.fxtf.org/geometry/#dom-dommatrixinit-is2d")]
    public bool? Is2D { get; init; }
}

/// <summary>https://drafts.fxtf.org/geometry/#dommatrixreadonly</summary>
/// <seealso href="https://drafts.fxtf.org/geometry/#dommatrixreadonly"/>
[IdlInterface("DOMMatrixReadOnly", "https://drafts.fxtf.org/geometry/#dommatrixreadonly")]
public class DomMatrixReadOnly : Scripting.IStructuredSerializable
{
    // Column-major 4x4 matrix: m[0..3] = m11..m14, m[4..7] = m21..m24...
    private protected readonly double[] Values = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
    private protected bool Is2DFlag = true;

    /// <summary>Creates the 2D identity matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-dommatrixreadonly"/>
    [IdlMember("constructor", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-dommatrixreadonly")]
    public DomMatrixReadOnly()
    {
    }

    /// <summary>https://drafts.fxtf.org/geometry/#structured-serialization: the 2D matrices keep their 6 components.</summary>
    object Scripting.IStructuredSerializable.CloneForStructuredSerialization()
    {
        var clone = this is DomMatrix ? new DomMatrix() : new DomMatrixReadOnly();
        if (Is2DFlag)
        {
            clone.Set2D(A, B, C, D, E, F);
        }
        else
        {
            Values.CopyTo(clone.Values, 0);
            clone.Is2DFlag = false;
        }

        return clone;
    }

    /// <summary>Creates a matrix by parsing the given CSS transform list.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-dommatrixreadonly"/>
    [IdlMember("constructor", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-dommatrixreadonly")]
    public DomMatrixReadOnly(string init)
    {
        ArgumentNullException.ThrowIfNull(init);

        // https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-dommatrixreadonly: the CSS syntax is only parsed in the windows
        if (Dom.GlobalScope.Current is Dom.WorkerGlobalScope)
            throw new Dom.TypeErrorException("A matrix cannot be created from a string in a worker");

        SetFromString(init);
    }

    /// <summary>
    /// https://drafts.fxtf.org/geometry/#parse-a-string-into-an-abstract-matrix: the string is a CSS &lt;transform-list&gt; (or
    /// none) whose lengths are absolute; the matrix is 2D when the list has no 3D transform function.
    /// </summary>
    private void SetFromString(string transformList)
    {
        double[] matrix = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        var is2D = true;
        if (transformList.Length > 0)
        {
            var values = Css.Syntax.CssParser.ParseListOfComponentValues(transformList);
            matrix = Css.Values.CssTransforms.ToMatrix(values, Css.Values.CssCalcContext.Default, out is2D) ?? throw DomException.Syntax($"'{transformList}' is not a valid transform list");
        }

        matrix.CopyTo(Values, 0);
        Is2DFlag = is2D;
    }

    /// <summary>Creates a matrix from a sequence of 6 (2D) or 16 (3D) values.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-dommatrixreadonly"/>
    [IdlMember("constructor", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-dommatrixreadonly")]
    public DomMatrixReadOnly(IReadOnlyList<double> init)
    {
        ArgumentNullException.ThrowIfNull(init);
        InitializeFromSequence(init);
    }

    private protected void InitializeFromSequence(IReadOnlyList<double> init)
    {
        if (init.Count == 6)
        {
            Set2D(init[0], init[1], init[2], init[3], init[4], init[5]);
        }
        else if (init.Count == 16)
        {
            for (var i = 0; i < 16; i++)
            {
                Values[i] = init[i];
            }

            Is2DFlag = false;
        }
        else
        {
            throw new TypeErrorException("The sequence must contain 6 or 16 elements");
        }
    }

    private protected void Set2D(double a, double b, double c, double d, double e, double f)
    {
        Array.Clear(Values);
        Values[0] = a;
        Values[1] = b;
        Values[4] = c;
        Values[5] = d;
        Values[10] = 1;
        Values[12] = e;
        Values[13] = f;
        Values[15] = 1;
        Is2DFlag = true;
    }

    /// <summary>https://drafts.fxtf.org/geometry/#create-a-dommatrix-from-the-dictionary</summary>
    internal static DomMatrix FromMatrixInit(DomMatrix2DInit? init)
    {
        var result = new DomMatrix();
        if (init is null)
            return result;

        // Validate and fixup
        static void Check(double? a, double? b, string name)
        {
            if (a is { } x && b is { } y && !(x == y || (double.IsNaN(x) && double.IsNaN(y))))
                throw new TypeErrorException($"Inconsistent values for {name}");
        }

        Check(init.A, init.M11, "a/m11");
        Check(init.B, init.M12, "b/m12");
        Check(init.C, init.M21, "c/m21");
        Check(init.D, init.M22, "d/m22");
        Check(init.E, init.M41, "e/m41");
        Check(init.F, init.M42, "f/m42");
        var m11 = init.M11 ?? init.A ?? 1;
        var m12 = init.M12 ?? init.B ?? 0;
        var m21 = init.M21 ?? init.C ?? 0;
        var m22 = init.M22 ?? init.D ?? 1;
        var m41 = init.M41 ?? init.E ?? 0;
        var m42 = init.M42 ?? init.F ?? 0;
        result.Set2D(m11, m12, m21, m22, m41, m42);
        if (init is DomMatrixInit full)
        {
            var is3D = full.M13 != 0 || full.M14 != 0 || full.M23 != 0 || full.M24 != 0 || full.M31 != 0 || full.M32 != 0 || full.M34 != 0 || full.M43 != 0 || full.M33 != 1 || full.M44 != 1;
            if (full.Is2D == true && is3D)
                throw new TypeErrorException("is2D is true but the matrix is 3D");

            result.Values[2] = full.M13;
            result.Values[3] = full.M14;
            result.Values[6] = full.M23;
            result.Values[7] = full.M24;
            result.Values[8] = full.M31;
            result.Values[9] = full.M32;
            result.Values[10] = full.M33;
            result.Values[11] = full.M34;
            result.Values[14] = full.M43;
            result.Values[15] = full.M44;
            result.Is2DFlag = full.Is2D ?? !is3D;
        }

        return result;
    }

    /// <summary>Creates a read-only matrix from the components of the given dictionary.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-frommatrix"/>
    [IdlMember("fromMatrix", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-frommatrix")]
    public static DomMatrixReadOnly FromMatrix(DomMatrixInit? other = null)
    {
        var matrix = FromMatrixInit(other);
        var result = new DomMatrixReadOnly();
        matrix.Values.CopyTo(result.Values, 0);
        result.Is2DFlag = matrix.Is2DFlag;
        return result;
    }

    /// <summary>Creates a read-only matrix from an array of 6 (2D) or 16 (3D) single-precision values.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-fromfloat32array"/>
    [IdlMember("fromFloat32Array", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-fromfloat32array")]
    public static DomMatrixReadOnly FromFloat32Array(float[] array32)
    {
        ArgumentNullException.ThrowIfNull(array32);
        return new DomMatrixReadOnly(Array.ConvertAll(array32, v => (double)v));
    }

    /// <summary>Creates a read-only matrix from an array of 6 (2D) or 16 (3D) double-precision values.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-fromfloat64array"/>
    [IdlMember("fromFloat64Array", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-fromfloat64array")]
    public static DomMatrixReadOnly FromFloat64Array(double[] array64)
    {
        ArgumentNullException.ThrowIfNull(array64);
        return new DomMatrixReadOnly(array64);
    }

    /// <summary>Gets the <c>a</c> component of the matrix, an alias of <c>m11</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-a"/>
    [IdlMember("a", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-a")]
    public double A => Values[0];

    /// <summary>Gets the <c>b</c> component of the matrix, an alias of <c>m12</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-b"/>
    [IdlMember("b", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-b")]
    public double B => Values[1];

    /// <summary>Gets the <c>c</c> component of the matrix, an alias of <c>m21</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-c"/>
    [IdlMember("c", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-c")]
    public double C => Values[4];

    /// <summary>Gets the <c>d</c> component of the matrix, an alias of <c>m22</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-d"/>
    [IdlMember("d", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-d")]
    public double D => Values[5];

    /// <summary>Gets the <c>e</c> component of the matrix, an alias of <c>m41</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-e"/>
    [IdlMember("e", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-e")]
    public double E => Values[12];

    /// <summary>Gets the <c>f</c> component of the matrix, an alias of <c>m42</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-f"/>
    [IdlMember("f", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-f")]
    public double F => Values[13];

    /// <summary>Gets the <c>m11</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m11"/>
    [IdlMember("m11", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m11")]
    public double M11 => Values[0];

    /// <summary>Gets the <c>m12</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m12"/>
    [IdlMember("m12", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m12")]
    public double M12 => Values[1];

    /// <summary>Gets the <c>m13</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m13"/>
    [IdlMember("m13", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m13")]
    public double M13 => Values[2];

    /// <summary>Gets the <c>m14</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m14"/>
    [IdlMember("m14", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m14")]
    public double M14 => Values[3];

    /// <summary>Gets the <c>m21</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m21"/>
    [IdlMember("m21", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m21")]
    public double M21 => Values[4];

    /// <summary>Gets the <c>m22</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m22"/>
    [IdlMember("m22", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m22")]
    public double M22 => Values[5];

    /// <summary>Gets the <c>m23</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m23"/>
    [IdlMember("m23", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m23")]
    public double M23 => Values[6];

    /// <summary>Gets the <c>m24</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m24"/>
    [IdlMember("m24", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m24")]
    public double M24 => Values[7];

    /// <summary>Gets the <c>m31</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m31"/>
    [IdlMember("m31", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m31")]
    public double M31 => Values[8];

    /// <summary>Gets the <c>m32</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m32"/>
    [IdlMember("m32", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m32")]
    public double M32 => Values[9];

    /// <summary>Gets the <c>m33</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m33"/>
    [IdlMember("m33", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m33")]
    public double M33 => Values[10];

    /// <summary>Gets the <c>m34</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m34"/>
    [IdlMember("m34", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m34")]
    public double M34 => Values[11];

    /// <summary>Gets the <c>m41</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m41"/>
    [IdlMember("m41", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m41")]
    public double M41 => Values[12];

    /// <summary>Gets the <c>m42</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m42"/>
    [IdlMember("m42", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m42")]
    public double M42 => Values[13];

    /// <summary>Gets the <c>m43</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m43"/>
    [IdlMember("m43", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m43")]
    public double M43 => Values[14];

    /// <summary>Gets the <c>m44</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m44"/>
    [IdlMember("m44", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-m44")]
    public double M44 => Values[15];

    /// <summary>Gets a value indicating whether the matrix is a 2D matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-is2d"/>
    [IdlMember("is2D", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-is2d")]
    public bool Is2D => Is2DFlag;

    /// <summary>Gets a value indicating whether the matrix is the identity matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-isidentity"/>
    [IdlMember("isIdentity", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-isidentity")]
    public bool IsIdentity
    {
        get
        {
            for (var i = 0; i < 16; i++)
            {
                if (Values[i] != (i % 5 == 0 ? 1 : 0))
                    return false;
            }

            return true;
        }
    }

    internal DomMatrix CloneMutable()
    {
        var result = new DomMatrix();
        Values.CopyTo(result.Values, 0);
        result.Is2DFlag = Is2DFlag;
        return result;
    }

    /// <summary>Creates a matrix from its 16 elements in column-major order (m11, m12... m44) and its is 2D flag.</summary>
    internal static DomMatrix Create(ReadOnlySpan<double> elements, bool is2D)
    {
        var result = new DomMatrix();
        elements.CopyTo(result.Values);
        result.Is2DFlag = is2D;
        return result;
    }

    /// <summary>Gets the 16 elements in column-major order (m11, m12... m44).</summary>
    internal ReadOnlySpan<double> Elements => Values;

    /// <summary>Gets whether the matrix is 2D (https://drafts.fxtf.org/geometry/#matrix-is-2d).</summary>
    internal bool Is2DMatrix => Is2DFlag;

    /// <summary>Returns a new matrix translated by the given offsets.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-translate"/>
    [IdlMember("translate", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-translate")]
    public DomMatrix Translate(double tx = 0, double ty = 0, double tz = 0) => CloneMutable().TranslateSelf(tx, ty, tz);

    /// <summary>Returns a new matrix scaled by the given factors around the given origin.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-scale"/>
    [IdlMember("scale", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-scale")]
    public DomMatrix Scale(double scaleX = 1, double? scaleY = null, double scaleZ = 1, double originX = 0, double originY = 0, double originZ = 0) => CloneMutable().ScaleSelf(scaleX, scaleY, scaleZ, originX, originY, originZ);

    /// <summary>Returns a new matrix scaled by the given factors on the X and Y axes.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-scalenonuniform"/>
    [IdlMember("scaleNonUniform", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-scalenonuniform")]
    public DomMatrix ScaleNonUniform(double scaleX = 1, double scaleY = 1) => CloneMutable().ScaleSelf(scaleX, scaleY, 1, 0, 0, 0);

    /// <summary>Returns a new matrix scaled uniformly in 3D around the given origin.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-scale3d"/>
    [IdlMember("scale3d", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-scale3d")]
    public DomMatrix Scale3d(double scale = 1, double originX = 0, double originY = 0, double originZ = 0) => CloneMutable().Scale3dSelf(scale, originX, originY, originZ);

    /// <summary>Returns a new matrix rotated by the given angles, in degrees, around the X, Y and Z axes.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-rotate"/>
    [IdlMember("rotate", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-rotate")]
    public DomMatrix Rotate(double rotX = 0, double? rotY = null, double? rotZ = null) => CloneMutable().RotateSelf(rotX, rotY, rotZ);

    /// <summary>Returns a new matrix rotated by the angle between the positive X axis and the given vector.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-rotatefromvector"/>
    [IdlMember("rotateFromVector", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-rotatefromvector")]
    public DomMatrix RotateFromVector(double x = 0, double y = 0) => CloneMutable().RotateFromVectorSelf(x, y);

    /// <summary>Returns a new matrix rotated by the given angle, in degrees, around the given axis vector.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-rotateaxisangle"/>
    [IdlMember("rotateAxisAngle", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-rotateaxisangle")]
    public DomMatrix RotateAxisAngle(double x = 0, double y = 0, double z = 0, double angle = 0) => CloneMutable().RotateAxisAngleSelf(x, y, z, angle);

    /// <summary>Returns a new matrix skewed along the X axis by the given angle in degrees.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-skewx"/>
    [IdlMember("skewX", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-skewx")]
    public DomMatrix SkewX(double sx = 0) => CloneMutable().SkewXSelf(sx);

    /// <summary>Returns a new matrix skewed along the Y axis by the given angle in degrees.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-skewy"/>
    [IdlMember("skewY", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-skewy")]
    public DomMatrix SkewY(double sy = 0) => CloneMutable().SkewYSelf(sy);

    /// <summary>Returns a new matrix that is the product of this matrix and the given matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-multiply"/>
    [IdlMember("multiply", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-multiply")]
    public DomMatrix Multiply(DomMatrixInit? other = null) => CloneMutable().MultiplySelf(other);

    /// <summary>Returns a new matrix flipped along the X axis.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-flipx"/>
    [IdlMember("flipX", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-flipx")]
    public DomMatrix FlipX()
    {
        var result = CloneMutable();
        result.PostMultiply([-1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
        return result;
    }

    /// <summary>Returns a new matrix flipped along the Y axis.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-flipy"/>
    [IdlMember("flipY", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-flipy")]
    public DomMatrix FlipY()
    {
        var result = CloneMutable();
        result.PostMultiply([1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);
        return result;
    }

    /// <summary>Returns the inverse of the matrix, or a matrix filled with NaN when it is not invertible.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-inverse"/>
    [IdlMember("inverse", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-inverse")]
    public DomMatrix Inverse() => CloneMutable().InvertSelf();

    /// <summary>Returns the point obtained by transforming the given point with the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-transformpoint"/>
    [IdlMember("transformPoint", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-transformpoint")]
    public DomPoint TransformPoint(DomPointInit? point = null)
    {
        point ??= new DomPointInit();
        return TransformPointCore(point.X, point.Y, point.Z, point.W);
    }

    internal DomPoint TransformPointCore(double x, double y, double z, double w)
    {
        return new DomPoint(
            (Values[0] * x) + (Values[4] * y) + (Values[8] * z) + (Values[12] * w),
            (Values[1] * x) + (Values[5] * y) + (Values[9] * z) + (Values[13] * w),
            (Values[2] * x) + (Values[6] * y) + (Values[10] * z) + (Values[14] * w),
            (Values[3] * x) + (Values[7] * y) + (Values[11] * z) + (Values[15] * w));
    }

    /// <summary>Returns the 16 components of the matrix as single-precision values, in column-major order.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-tofloat32array"/>
    [IdlMember("toFloat32Array", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-tofloat32array")]
    public float[] ToFloat32Array() => Array.ConvertAll(Values, v => (float)v);

    /// <summary>Returns the 16 components of the matrix as double-precision values, in column-major order.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-tofloat64array"/>
    [IdlMember("toFloat64Array", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-tofloat64array")]
    public double[] ToFloat64Array() => (double[])Values.Clone();

    /// <summary>Returns a dictionary representation of the matrix, as used by <c>JSON.stringify()</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-tojson"/>
    [IdlMember("toJSON", "https://drafts.fxtf.org/geometry/#dom-dommatrixreadonly-tojson")]
    public DomMatrixInit ToJson() => new()
    {
        A = A, B = B, C = C, D = D, E = E, F = F,
        M11 = M11, M12 = M12, M13 = M13, M14 = M14, M21 = M21, M22 = M22, M23 = M23, M24 = M24,
        M31 = M31, M32 = M32, M33 = M33, M34 = M34, M41 = M41, M42 = M42, M43 = M43, M44 = M44,
        Is2D = Is2D,
    };

    /// <summary>https://drafts.fxtf.org/geometry/#dommatrixreadonly-stringification-behavior</summary>
    [NotInIdl("The stringifier of DOMMatrixReadOnly")]
    public override string ToString()
    {
        foreach (var value in Values)
        {
            if (!double.IsFinite(value))
                throw DomException.InvalidState("The matrix contains non-finite values");
        }

        var builder = new StringBuilder(Is2DFlag ? "matrix(" : "matrix3d(");
        IEnumerable<double> values = Is2DFlag ? [A, B, C, D, E, F] : Values;
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                builder.Append(", ");
            }

            first = false;
            builder.Append(Html.Microsyntaxes.ToBestFloatingPointRepresentation(value));
        }

        return builder.Append(')').ToString();
    }

    // Post-multiplies this matrix by the other matrix (this = this × other)
    private protected void PostMultiply(ReadOnlySpan<double> other)
    {
        Span<double> result = stackalloc double[16];
        for (var col = 0; col < 4; col++)
        {
            for (var row = 0; row < 4; row++)
            {
                double sum = 0;
                for (var k = 0; k < 4; k++)
                {
                    sum += Values[(k * 4) + row] * other[(col * 4) + k];
                }

                result[(col * 4) + row] = sum;
            }
        }

        result.CopyTo(Values);
    }

    private protected void PreMultiply(ReadOnlySpan<double> other)
    {
        Span<double> result = stackalloc double[16];
        for (var col = 0; col < 4; col++)
        {
            for (var row = 0; row < 4; row++)
            {
                double sum = 0;
                for (var k = 0; k < 4; k++)
                {
                    sum += other[(k * 4) + row] * Values[(col * 4) + k];
                }

                result[(col * 4) + row] = sum;
            }
        }

        result.CopyTo(Values);
    }

    internal static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>https://drafts.fxtf.org/geometry/#dommatrix</summary>
/// <seealso href="https://drafts.fxtf.org/geometry/#dommatrix"/>
[IdlInterface("DOMMatrix", "https://drafts.fxtf.org/geometry/#dommatrix")]
public class DomMatrix : DomMatrixReadOnly
{
    /// <summary>Creates the 2D identity matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-dommatrix"/>
    [IdlMember("constructor", "https://drafts.fxtf.org/geometry/#dom-dommatrix-dommatrix")]
    public DomMatrix()
    {
    }

    /// <summary>Creates a matrix by parsing the given CSS transform list.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-dommatrix"/>
    [IdlMember("constructor", "https://drafts.fxtf.org/geometry/#dom-dommatrix-dommatrix")]
    public DomMatrix(string init)
        : base(init)
    {
    }

    /// <summary>Creates a matrix from a sequence of 6 (2D) or 16 (3D) values.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-dommatrix"/>
    [IdlMember("constructor", "https://drafts.fxtf.org/geometry/#dom-dommatrix-dommatrix")]
    public DomMatrix(IReadOnlyList<double> init)
        : base(init)
    {
    }

    /// <summary>Called after the matrix is modified (used by SVG objects that reflect the matrix).</summary>
    private protected virtual void OnChanged()
    {
    }

    /// <summary>Called before the matrix is modified; throws when the matrix is read only.</summary>
    private protected virtual void OnChanging()
    {
    }

    /// <summary>Creates a mutable matrix from the components of the given dictionary.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-frommatrix"/>
    [IdlMember("fromMatrix", "https://drafts.fxtf.org/geometry/#dom-dommatrix-frommatrix")]
    public static new DomMatrix FromMatrix(DomMatrixInit? other = null) => FromMatrixInit(other);

    /// <summary>Creates a mutable matrix from an array of 6 (2D) or 16 (3D) single-precision values.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-fromfloat32array"/>
    [IdlMember("fromFloat32Array", "https://drafts.fxtf.org/geometry/#dom-dommatrix-fromfloat32array")]
    public static new DomMatrix FromFloat32Array(float[] array32)
    {
        ArgumentNullException.ThrowIfNull(array32);
        return new DomMatrix(Array.ConvertAll(array32, v => (double)v));
    }

    /// <summary>Creates a mutable matrix from an array of 6 (2D) or 16 (3D) double-precision values.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-fromfloat64array"/>
    [IdlMember("fromFloat64Array", "https://drafts.fxtf.org/geometry/#dom-dommatrix-fromfloat64array")]
    public static new DomMatrix FromFloat64Array(double[] array64)
    {
        ArgumentNullException.ThrowIfNull(array64);
        return new DomMatrix(array64);
    }

    private double Get(int index) => Values[index];

    private void Set(int index, double value, bool breaks2D)
    {
        OnChanging();
        Values[index] = value;
        if (breaks2D && value != (index % 5 == 0 ? 1 : 0))
        {
            Is2DFlag = false;
        }

        OnChanged();
    }

    /// <summary>Gets or sets the <c>a</c> component of the matrix, an alias of <c>m11</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-a"/>
    [IdlMember("a", "https://drafts.fxtf.org/geometry/#dom-dommatrix-a")]
    public new double A { get => Get(0); set => Set(0, value, breaks2D: false); }

    /// <summary>Gets or sets the <c>b</c> component of the matrix, an alias of <c>m12</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-b"/>
    [IdlMember("b", "https://drafts.fxtf.org/geometry/#dom-dommatrix-b")]
    public new double B { get => Get(1); set => Set(1, value, breaks2D: false); }

    /// <summary>Gets or sets the <c>c</c> component of the matrix, an alias of <c>m21</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-c"/>
    [IdlMember("c", "https://drafts.fxtf.org/geometry/#dom-dommatrix-c")]
    public new double C { get => Get(4); set => Set(4, value, breaks2D: false); }

    /// <summary>Gets or sets the <c>d</c> component of the matrix, an alias of <c>m22</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-d"/>
    [IdlMember("d", "https://drafts.fxtf.org/geometry/#dom-dommatrix-d")]
    public new double D { get => Get(5); set => Set(5, value, breaks2D: false); }

    /// <summary>Gets or sets the <c>e</c> component of the matrix, an alias of <c>m41</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-e"/>
    [IdlMember("e", "https://drafts.fxtf.org/geometry/#dom-dommatrix-e")]
    public new double E { get => Get(12); set => Set(12, value, breaks2D: false); }

    /// <summary>Gets or sets the <c>f</c> component of the matrix, an alias of <c>m42</c>.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-f"/>
    [IdlMember("f", "https://drafts.fxtf.org/geometry/#dom-dommatrix-f")]
    public new double F { get => Get(13); set => Set(13, value, breaks2D: false); }

    /// <summary>Gets or sets the <c>m11</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-m11"/>
    [IdlMember("m11", "https://drafts.fxtf.org/geometry/#dom-dommatrix-m11")]
    public new double M11 { get => Get(0); set => Set(0, value, breaks2D: false); }

    /// <summary>Gets or sets the <c>m12</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-m12"/>
    [IdlMember("m12", "https://drafts.fxtf.org/geometry/#dom-dommatrix-m12")]
    public new double M12 { get => Get(1); set => Set(1, value, breaks2D: false); }

    /// <summary>Gets or sets the <c>m13</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-m13"/>
    [IdlMember("m13", "https://drafts.fxtf.org/geometry/#dom-dommatrix-m13")]
    public new double M13 { get => Get(2); set => Set(2, value, breaks2D: true); }

    /// <summary>Gets or sets the <c>m14</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-m14"/>
    [IdlMember("m14", "https://drafts.fxtf.org/geometry/#dom-dommatrix-m14")]
    public new double M14 { get => Get(3); set => Set(3, value, breaks2D: true); }

    /// <summary>Gets or sets the <c>m21</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-m21"/>
    [IdlMember("m21", "https://drafts.fxtf.org/geometry/#dom-dommatrix-m21")]
    public new double M21 { get => Get(4); set => Set(4, value, breaks2D: false); }

    /// <summary>Gets or sets the <c>m22</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-m22"/>
    [IdlMember("m22", "https://drafts.fxtf.org/geometry/#dom-dommatrix-m22")]
    public new double M22 { get => Get(5); set => Set(5, value, breaks2D: false); }

    /// <summary>Gets or sets the <c>m23</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-m23"/>
    [IdlMember("m23", "https://drafts.fxtf.org/geometry/#dom-dommatrix-m23")]
    public new double M23 { get => Get(6); set => Set(6, value, breaks2D: true); }

    /// <summary>Gets or sets the <c>m24</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-m24"/>
    [IdlMember("m24", "https://drafts.fxtf.org/geometry/#dom-dommatrix-m24")]
    public new double M24 { get => Get(7); set => Set(7, value, breaks2D: true); }

    /// <summary>Gets or sets the <c>m31</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-m31"/>
    [IdlMember("m31", "https://drafts.fxtf.org/geometry/#dom-dommatrix-m31")]
    public new double M31 { get => Get(8); set => Set(8, value, breaks2D: true); }

    /// <summary>Gets or sets the <c>m32</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-m32"/>
    [IdlMember("m32", "https://drafts.fxtf.org/geometry/#dom-dommatrix-m32")]
    public new double M32 { get => Get(9); set => Set(9, value, breaks2D: true); }

    /// <summary>Gets or sets the <c>m33</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-m33"/>
    [IdlMember("m33", "https://drafts.fxtf.org/geometry/#dom-dommatrix-m33")]
    public new double M33 { get => Get(10); set => Set(10, value, breaks2D: true); }

    /// <summary>Gets or sets the <c>m34</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-m34"/>
    [IdlMember("m34", "https://drafts.fxtf.org/geometry/#dom-dommatrix-m34")]
    public new double M34 { get => Get(11); set => Set(11, value, breaks2D: true); }

    /// <summary>Gets or sets the <c>m41</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-m41"/>
    [IdlMember("m41", "https://drafts.fxtf.org/geometry/#dom-dommatrix-m41")]
    public new double M41 { get => Get(12); set => Set(12, value, breaks2D: false); }

    /// <summary>Gets or sets the <c>m42</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-m42"/>
    [IdlMember("m42", "https://drafts.fxtf.org/geometry/#dom-dommatrix-m42")]
    public new double M42 { get => Get(13); set => Set(13, value, breaks2D: false); }

    /// <summary>Gets or sets the <c>m43</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-m43"/>
    [IdlMember("m43", "https://drafts.fxtf.org/geometry/#dom-dommatrix-m43")]
    public new double M43 { get => Get(14); set => Set(14, value, breaks2D: true); }

    /// <summary>Gets or sets the <c>m44</c> component of the matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-m44"/>
    [IdlMember("m44", "https://drafts.fxtf.org/geometry/#dom-dommatrix-m44")]
    public new double M44 { get => Get(15); set => Set(15, value, breaks2D: true); }

    private DomMatrix Mutate(Action action)
    {
        OnChanging();
        action();
        OnChanged();
        return this;
    }

    /// <summary>Post-multiplies this matrix by the given matrix and returns this matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-multiplyself"/>
    [IdlMember("multiplySelf", "https://drafts.fxtf.org/geometry/#dom-dommatrix-multiplyself")]
    public DomMatrix MultiplySelf(DomMatrixInit? other = null)
    {
        var matrix = FromMatrixInit(other);
        return Mutate(() =>
        {
            PostMultiply(matrix.Values);
            Is2DFlag &= matrix.Is2DFlag;
        });
    }

    /// <summary>Pre-multiplies this matrix by the given matrix and returns this matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-premultiplyself"/>
    [IdlMember("preMultiplySelf", "https://drafts.fxtf.org/geometry/#dom-dommatrix-premultiplyself")]
    public DomMatrix PreMultiplySelf(DomMatrixInit? other = null)
    {
        var matrix = FromMatrixInit(other);
        return Mutate(() =>
        {
            PreMultiply(matrix.Values);
            Is2DFlag &= matrix.Is2DFlag;
        });
    }

    /// <summary>Translates this matrix by the given offsets and returns this matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-translateself"/>
    [IdlMember("translateSelf", "https://drafts.fxtf.org/geometry/#dom-dommatrix-translateself")]
    public DomMatrix TranslateSelf(double tx = 0, double ty = 0, double tz = 0) => Mutate(() =>
    {
        PostMultiply([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, tx, ty, tz, 1]);
        if (tz != 0)
        {
            Is2DFlag = false;
        }
    });

    /// <summary>Scales this matrix by the given factors around the given origin and returns this matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-scaleself"/>
    [IdlMember("scaleSelf", "https://drafts.fxtf.org/geometry/#dom-dommatrix-scaleself")]
    public DomMatrix ScaleSelf(double scaleX = 1, double? scaleY = null, double scaleZ = 1, double originX = 0, double originY = 0, double originZ = 0) => Mutate(() =>
    {
        PostMultiply([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, originX, originY, originZ, 1]);
        PostMultiply([scaleX, 0, 0, 0, 0, scaleY ?? scaleX, 0, 0, 0, 0, scaleZ, 0, 0, 0, 0, 1]);
        PostMultiply([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -originX, -originY, -originZ, 1]);
        if (scaleZ != 1 || originZ != 0)
        {
            Is2DFlag = false;
        }
    });

    /// <summary>Scales this matrix uniformly in 3D around the given origin and returns this matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-scale3dself"/>
    [IdlMember("scale3dSelf", "https://drafts.fxtf.org/geometry/#dom-dommatrix-scale3dself")]
    public DomMatrix Scale3dSelf(double scale = 1, double originX = 0, double originY = 0, double originZ = 0) => ScaleSelf(scale, scale, scale, originX, originY, originZ);

    /// <summary>
    /// Rotates this matrix by the given angles, in degrees, around the X, Y and Z axes and returns this matrix.
    /// </summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-rotateself"/>
    [IdlMember("rotateSelf", "https://drafts.fxtf.org/geometry/#dom-dommatrix-rotateself")]
    public DomMatrix RotateSelf(double rotX = 0, double? rotY = null, double? rotZ = null)
    {
        // If rotY and rotZ are both missing, rotZ = rotX and rotX = 0
        if (rotY is null && rotZ is null)
        {
            rotZ = rotX;
            rotX = 0;
        }

        var y = rotY ?? 0;
        var z = rotZ ?? 0;
        return Mutate(() =>
        {
            if (rotX != 0 || y != 0)
            {
                Is2DFlag = false;
            }

            PostMultiply(Css.Values.CssTransforms.Rotation(0, 0, 1, z));
            PostMultiply(Css.Values.CssTransforms.Rotation(0, 1, 0, y));
            PostMultiply(Css.Values.CssTransforms.Rotation(1, 0, 0, rotX));
        });
    }

    /// <summary>
    /// Rotates this matrix by the angle between the positive X axis and the given vector and returns this matrix.
    /// </summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-rotatefromvectorself"/>
    [IdlMember("rotateFromVectorSelf", "https://drafts.fxtf.org/geometry/#dom-dommatrix-rotatefromvectorself")]
    public DomMatrix RotateFromVectorSelf(double x = 0, double y = 0)
    {
        var angle = x == 0 && y == 0 ? 0 : Math.Atan2(y, x) * 180 / Math.PI;
        return Mutate(() => PostMultiply(Css.Values.CssTransforms.Rotation(0, 0, 1, angle)));
    }

    /// <summary>
    /// Rotates this matrix by the given angle, in degrees, around the given axis vector and returns this matrix.
    /// </summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-rotateaxisangleself"/>
    [IdlMember("rotateAxisAngleSelf", "https://drafts.fxtf.org/geometry/#dom-dommatrix-rotateaxisangleself")]
    public DomMatrix RotateAxisAngleSelf(double x = 0, double y = 0, double z = 0, double angle = 0) => Mutate(() =>
    {
        PostMultiply(Css.Values.CssTransforms.Rotation(x, y, z, angle));
        if (x != 0 || y != 0)
        {
            Is2DFlag = false;
        }
    });

    /// <summary>Skews this matrix along the X axis by the given angle in degrees and returns this matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-skewxself"/>
    [IdlMember("skewXSelf", "https://drafts.fxtf.org/geometry/#dom-dommatrix-skewxself")]
    public DomMatrix SkewXSelf(double sx = 0) => Mutate(() => PostMultiply([1, 0, 0, 0, Math.Tan(sx * Math.PI / 180), 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]));

    /// <summary>Skews this matrix along the Y axis by the given angle in degrees and returns this matrix.</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-skewyself"/>
    [IdlMember("skewYSelf", "https://drafts.fxtf.org/geometry/#dom-dommatrix-skewyself")]
    public DomMatrix SkewYSelf(double sy = 0) => Mutate(() => PostMultiply([1, Math.Tan(sy * Math.PI / 180), 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]));

    /// <summary>https://drafts.fxtf.org/geometry/#dom-dommatrix-invertself</summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-invertself"/>
    [IdlMember("invertSelf", "https://drafts.fxtf.org/geometry/#dom-dommatrix-invertself")]
    public DomMatrix InvertSelf() => Mutate(() =>
    {
        if (!TryInvert(Values, out var inverse))
        {
            Array.Fill(Values, double.NaN);
            Is2DFlag = false;
            return;
        }

        inverse.CopyTo(Values, 0);
    });

    /// <summary>
    /// Replaces the components of this matrix with the result of parsing the given CSS transform list.
    /// </summary>
    /// <seealso href="https://drafts.fxtf.org/geometry/#dom-dommatrix-setmatrixvalue"/>
    [IdlMember("setMatrixValue", "https://drafts.fxtf.org/geometry/#dom-dommatrix-setmatrixvalue")]
    public DomMatrix SetMatrixValue(string transformList)
    {
        ArgumentNullException.ThrowIfNull(transformList);

        // The string is parsed before the matrix changes (a SyntaxError leaves it unchanged)
        var parsed = new DomMatrix(transformList);
        return Mutate(() =>
        {
            parsed.Values.CopyTo(Values, 0);
            Is2DFlag = parsed.Is2DFlag;
        });
    }

    /// <summary>Sets the 2D components (used by SVG transforms).</summary>
    internal void SetComponents(double a, double b, double c, double d, double e, double f) => Mutate(() => Set2D(a, b, c, d, e, f));

    private static bool TryInvert(double[] m, out double[] result)
    {
        result = new double[16];
        result[0] = (m[5] * m[10] * m[15]) - (m[5] * m[11] * m[14]) - (m[9] * m[6] * m[15]) + (m[9] * m[7] * m[14]) + (m[13] * m[6] * m[11]) - (m[13] * m[7] * m[10]);
        result[4] = (-m[4] * m[10] * m[15]) + (m[4] * m[11] * m[14]) + (m[8] * m[6] * m[15]) - (m[8] * m[7] * m[14]) - (m[12] * m[6] * m[11]) + (m[12] * m[7] * m[10]);
        result[8] = (m[4] * m[9] * m[15]) - (m[4] * m[11] * m[13]) - (m[8] * m[5] * m[15]) + (m[8] * m[7] * m[13]) + (m[12] * m[5] * m[11]) - (m[12] * m[7] * m[9]);
        result[12] = (-m[4] * m[9] * m[14]) + (m[4] * m[10] * m[13]) + (m[8] * m[5] * m[14]) - (m[8] * m[6] * m[13]) - (m[12] * m[5] * m[10]) + (m[12] * m[6] * m[9]);
        result[1] = (-m[1] * m[10] * m[15]) + (m[1] * m[11] * m[14]) + (m[9] * m[2] * m[15]) - (m[9] * m[3] * m[14]) - (m[13] * m[2] * m[11]) + (m[13] * m[3] * m[10]);
        result[5] = (m[0] * m[10] * m[15]) - (m[0] * m[11] * m[14]) - (m[8] * m[2] * m[15]) + (m[8] * m[3] * m[14]) + (m[12] * m[2] * m[11]) - (m[12] * m[3] * m[10]);
        result[9] = (-m[0] * m[9] * m[15]) + (m[0] * m[11] * m[13]) + (m[8] * m[1] * m[15]) - (m[8] * m[3] * m[13]) - (m[12] * m[1] * m[11]) + (m[12] * m[3] * m[9]);
        result[13] = (m[0] * m[9] * m[14]) - (m[0] * m[10] * m[13]) - (m[8] * m[1] * m[14]) + (m[8] * m[2] * m[13]) + (m[12] * m[1] * m[10]) - (m[12] * m[2] * m[9]);
        result[2] = (m[1] * m[6] * m[15]) - (m[1] * m[7] * m[14]) - (m[5] * m[2] * m[15]) + (m[5] * m[3] * m[14]) + (m[13] * m[2] * m[7]) - (m[13] * m[3] * m[6]);
        result[6] = (-m[0] * m[6] * m[15]) + (m[0] * m[7] * m[14]) + (m[4] * m[2] * m[15]) - (m[4] * m[3] * m[14]) - (m[12] * m[2] * m[7]) + (m[12] * m[3] * m[6]);
        result[10] = (m[0] * m[5] * m[15]) - (m[0] * m[7] * m[13]) - (m[4] * m[1] * m[15]) + (m[4] * m[3] * m[13]) + (m[12] * m[1] * m[7]) - (m[12] * m[3] * m[5]);
        result[14] = (-m[0] * m[5] * m[14]) + (m[0] * m[6] * m[13]) + (m[4] * m[1] * m[14]) - (m[4] * m[2] * m[13]) - (m[12] * m[1] * m[6]) + (m[12] * m[2] * m[5]);
        result[3] = (-m[1] * m[6] * m[11]) + (m[1] * m[7] * m[10]) + (m[5] * m[2] * m[11]) - (m[5] * m[3] * m[10]) - (m[9] * m[2] * m[7]) + (m[9] * m[3] * m[6]);
        result[7] = (m[0] * m[6] * m[11]) - (m[0] * m[7] * m[10]) - (m[4] * m[2] * m[11]) + (m[4] * m[3] * m[10]) + (m[8] * m[2] * m[7]) - (m[8] * m[3] * m[6]);
        result[11] = (-m[0] * m[5] * m[11]) + (m[0] * m[7] * m[9]) + (m[4] * m[1] * m[11]) - (m[4] * m[3] * m[9]) - (m[8] * m[1] * m[7]) + (m[8] * m[3] * m[5]);
        result[15] = (m[0] * m[5] * m[10]) - (m[0] * m[6] * m[9]) - (m[4] * m[1] * m[10]) + (m[4] * m[2] * m[9]) + (m[8] * m[1] * m[6]) - (m[8] * m[2] * m[5]);

        var determinant = (m[0] * result[0]) + (m[1] * result[4]) + (m[2] * result[8]) + (m[3] * result[12]);
        if (determinant == 0 || !double.IsFinite(determinant))
            return false;

        for (var i = 0; i < 16; i++)
        {
            result[i] /= determinant;
        }

        return true;
    }
}
