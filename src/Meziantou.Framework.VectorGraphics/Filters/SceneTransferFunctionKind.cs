namespace Meziantou.Framework.VectorGraphics;

/// <summary>The kind of a <see cref="SceneTransferFunction"/>: the <c>type</c> of <c>feFuncR</c>, <c>feFuncG</c>, <c>feFuncB</c> and <c>feFuncA</c>.</summary>
public enum SceneTransferFunctionKind
{
    /// <summary>The value is kept (<c>identity</c>).</summary>
    Identity,

    /// <summary>The value is interpolated linearly between the values of a table (<c>table</c>).</summary>
    Table,

    /// <summary>The value is the value of a table of steps (<c>discrete</c>).</summary>
    Discrete,

    /// <summary><c>slope C + intercept</c> (<c>linear</c>).</summary>
    Linear,

    /// <summary><c>amplitude C^exponent + offset</c> (<c>gamma</c>).</summary>
    Gamma,
}
