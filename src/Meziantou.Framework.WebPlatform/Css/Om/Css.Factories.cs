namespace Meziantou.Framework.WebPlatform.Css.Om;

// The numeric factory functions of CSS Typed OM (https://drafts.css-houdini.org/css-typed-om-1/#numeric-factory): each
// function returns a new CSSUnitValue whose unit is the name of the function (ASCII lowercase, e.g. CSS.Hz(1) is 1hz).
public static partial class Css
{
    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a <c>number</c> (unitless) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-number"/>
    [IdlMember("number", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-number")]
    public static TypedOm.CssUnitValue Number(double value) => new(value, "number");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a percentage (<c>%</c>) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-percent"/>
    [IdlMember("percent", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-percent")]
    public static TypedOm.CssUnitValue Percent(double value) => new(value, "percent");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>cap</c> units (cap height of the font)
    /// with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-cap"/>
    [IdlMember("cap", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-cap")]
    public static TypedOm.CssUnitValue Cap(double value) => new(value, "cap");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>ch</c> units (advance of the zero
    /// glyph) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-ch"/>
    [IdlMember("ch", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-ch")]
    public static TypedOm.CssUnitValue Ch(double value) => new(value, "ch");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>em</c> units (font size) with the given
    /// value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-em"/>
    [IdlMember("em", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-em")]
    public static TypedOm.CssUnitValue Em(double value) => new(value, "em");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>ex</c> units (x-height of the font)
    /// with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-ex"/>
    [IdlMember("ex", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-ex")]
    public static TypedOm.CssUnitValue Ex(double value) => new(value, "ex");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>ic</c> units (advance of the water
    /// ideograph) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-ic"/>
    [IdlMember("ic", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-ic")]
    public static TypedOm.CssUnitValue Ic(double value) => new(value, "ic");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>lh</c> units (line height) with the
    /// given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-lh"/>
    [IdlMember("lh", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-lh")]
    public static TypedOm.CssUnitValue Lh(double value) => new(value, "lh");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>rcap</c> units (cap height of the root
    /// element font) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-rcap"/>
    [IdlMember("rcap", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-rcap")]
    public static TypedOm.CssUnitValue Rcap(double value) => new(value, "rcap");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>rch</c> units (zero glyph advance of
    /// the root element font) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-rch"/>
    [IdlMember("rch", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-rch")]
    public static TypedOm.CssUnitValue Rch(double value) => new(value, "rch");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>rem</c> units (font size of the root
    /// element) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-rem"/>
    [IdlMember("rem", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-rem")]
    public static TypedOm.CssUnitValue Rem(double value) => new(value, "rem");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>rex</c> units (x-height of the root
    /// element font) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-rex"/>
    [IdlMember("rex", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-rex")]
    public static TypedOm.CssUnitValue Rex(double value) => new(value, "rex");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>ric</c> units (water ideograph advance
    /// of the root element font) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-ric"/>
    [IdlMember("ric", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-ric")]
    public static TypedOm.CssUnitValue Ric(double value) => new(value, "ric");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>rlh</c> units (line height of the root
    /// element) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-rlh"/>
    [IdlMember("rlh", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-rlh")]
    public static TypedOm.CssUnitValue Rlh(double value) => new(value, "rlh");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>vw</c> units (1% of the viewport width)
    /// with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-vw"/>
    [IdlMember("vw", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-vw")]
    public static TypedOm.CssUnitValue Vw(double value) => new(value, "vw");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>vh</c> units (1% of the viewport
    /// height) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-vh"/>
    [IdlMember("vh", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-vh")]
    public static TypedOm.CssUnitValue Vh(double value) => new(value, "vh");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>vi</c> units (1% of the viewport size
    /// in the inline axis) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-vi"/>
    [IdlMember("vi", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-vi")]
    public static TypedOm.CssUnitValue Vi(double value) => new(value, "vi");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>vb</c> units (1% of the viewport size
    /// in the block axis) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-vb"/>
    [IdlMember("vb", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-vb")]
    public static TypedOm.CssUnitValue Vb(double value) => new(value, "vb");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>vmin</c> units (the smaller of
    /// <c>vw</c> and <c>vh</c>) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-vmin"/>
    [IdlMember("vmin", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-vmin")]
    public static TypedOm.CssUnitValue Vmin(double value) => new(value, "vmin");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>vmax</c> units (the larger of <c>vw</c>
    /// and <c>vh</c>) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-vmax"/>
    [IdlMember("vmax", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-vmax")]
    public static TypedOm.CssUnitValue Vmax(double value) => new(value, "vmax");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>svw</c> units (1% of the small viewport
    /// width) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-svw"/>
    [IdlMember("svw", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-svw")]
    public static TypedOm.CssUnitValue Svw(double value) => new(value, "svw");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>svh</c> units (1% of the small viewport
    /// height) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-svh"/>
    [IdlMember("svh", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-svh")]
    public static TypedOm.CssUnitValue Svh(double value) => new(value, "svh");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>svi</c> units (1% of the small viewport
    /// inline-axis size) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-svi"/>
    [IdlMember("svi", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-svi")]
    public static TypedOm.CssUnitValue Svi(double value) => new(value, "svi");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>svb</c> units (1% of the small viewport
    /// block-axis size) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-svb"/>
    [IdlMember("svb", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-svb")]
    public static TypedOm.CssUnitValue Svb(double value) => new(value, "svb");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>svmin</c> units (the smaller of
    /// <c>svw</c> and <c>svh</c>) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-svmin"/>
    [IdlMember("svmin", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-svmin")]
    public static TypedOm.CssUnitValue Svmin(double value) => new(value, "svmin");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>svmax</c> units (the larger of
    /// <c>svw</c> and <c>svh</c>) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-svmax"/>
    [IdlMember("svmax", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-svmax")]
    public static TypedOm.CssUnitValue Svmax(double value) => new(value, "svmax");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>lvw</c> units (1% of the large viewport
    /// width) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-lvw"/>
    [IdlMember("lvw", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-lvw")]
    public static TypedOm.CssUnitValue Lvw(double value) => new(value, "lvw");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>lvh</c> units (1% of the large viewport
    /// height) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-lvh"/>
    [IdlMember("lvh", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-lvh")]
    public static TypedOm.CssUnitValue Lvh(double value) => new(value, "lvh");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>lvi</c> units (1% of the large viewport
    /// inline-axis size) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-lvi"/>
    [IdlMember("lvi", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-lvi")]
    public static TypedOm.CssUnitValue Lvi(double value) => new(value, "lvi");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>lvb</c> units (1% of the large viewport
    /// block-axis size) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-lvb"/>
    [IdlMember("lvb", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-lvb")]
    public static TypedOm.CssUnitValue Lvb(double value) => new(value, "lvb");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>lvmin</c> units (the smaller of
    /// <c>lvw</c> and <c>lvh</c>) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-lvmin"/>
    [IdlMember("lvmin", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-lvmin")]
    public static TypedOm.CssUnitValue Lvmin(double value) => new(value, "lvmin");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>lvmax</c> units (the larger of
    /// <c>lvw</c> and <c>lvh</c>) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-lvmax"/>
    [IdlMember("lvmax", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-lvmax")]
    public static TypedOm.CssUnitValue Lvmax(double value) => new(value, "lvmax");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>dvw</c> units (1% of the dynamic
    /// viewport width) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dvw"/>
    [IdlMember("dvw", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dvw")]
    public static TypedOm.CssUnitValue Dvw(double value) => new(value, "dvw");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>dvh</c> units (1% of the dynamic
    /// viewport height) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dvh"/>
    [IdlMember("dvh", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dvh")]
    public static TypedOm.CssUnitValue Dvh(double value) => new(value, "dvh");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>dvi</c> units (1% of the dynamic
    /// viewport inline-axis size) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dvi"/>
    [IdlMember("dvi", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dvi")]
    public static TypedOm.CssUnitValue Dvi(double value) => new(value, "dvi");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>dvb</c> units (1% of the dynamic
    /// viewport block-axis size) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dvb"/>
    [IdlMember("dvb", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dvb")]
    public static TypedOm.CssUnitValue Dvb(double value) => new(value, "dvb");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>dvmin</c> units (the smaller of
    /// <c>dvw</c> and <c>dvh</c>) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dvmin"/>
    [IdlMember("dvmin", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dvmin")]
    public static TypedOm.CssUnitValue Dvmin(double value) => new(value, "dvmin");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>dvmax</c> units (the larger of
    /// <c>dvw</c> and <c>dvh</c>) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dvmax"/>
    [IdlMember("dvmax", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dvmax")]
    public static TypedOm.CssUnitValue Dvmax(double value) => new(value, "dvmax");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>cqw</c> units (1% of the query
    /// container width) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-cqw"/>
    [IdlMember("cqw", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-cqw")]
    public static TypedOm.CssUnitValue Cqw(double value) => new(value, "cqw");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>cqh</c> units (1% of the query
    /// container height) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-cqh"/>
    [IdlMember("cqh", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-cqh")]
    public static TypedOm.CssUnitValue Cqh(double value) => new(value, "cqh");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>cqi</c> units (1% of the query
    /// container inline-axis size) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-cqi"/>
    [IdlMember("cqi", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-cqi")]
    public static TypedOm.CssUnitValue Cqi(double value) => new(value, "cqi");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>cqb</c> units (1% of the query
    /// container block-axis size) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-cqb"/>
    [IdlMember("cqb", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-cqb")]
    public static TypedOm.CssUnitValue Cqb(double value) => new(value, "cqb");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>cqmin</c> units (the smaller of
    /// <c>cqi</c> and <c>cqb</c>) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-cqmin"/>
    [IdlMember("cqmin", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-cqmin")]
    public static TypedOm.CssUnitValue Cqmin(double value) => new(value, "cqmin");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>cqmax</c> units (the larger of
    /// <c>cqi</c> and <c>cqb</c>) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-cqmax"/>
    [IdlMember("cqmax", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-cqmax")]
    public static TypedOm.CssUnitValue Cqmax(double value) => new(value, "cqmax");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>cm</c> units (centimeters) with the
    /// given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-cm"/>
    [IdlMember("cm", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-cm")]
    public static TypedOm.CssUnitValue Cm(double value) => new(value, "cm");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>mm</c> units (millimeters) with the
    /// given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-mm"/>
    [IdlMember("mm", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-mm")]
    public static TypedOm.CssUnitValue Mm(double value) => new(value, "mm");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>Q</c> units (quarter-millimeters) with
    /// the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-q"/>
    [IdlMember("Q", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-q")]
    public static TypedOm.CssUnitValue Q(double value) => new(value, "q");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>in</c> units (inches) with the given
    /// value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-in"/>
    [IdlMember("in", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-in")]
    public static TypedOm.CssUnitValue In(double value) => new(value, "in");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>pt</c> units (points) with the given
    /// value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-pt"/>
    [IdlMember("pt", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-pt")]
    public static TypedOm.CssUnitValue Pt(double value) => new(value, "pt");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>pc</c> units (picas) with the given
    /// value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-pc"/>
    [IdlMember("pc", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-pc")]
    public static TypedOm.CssUnitValue Pc(double value) => new(value, "pc");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a length in <c>px</c> units (pixels) with the given
    /// value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-px"/>
    [IdlMember("px", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-px")]
    public static TypedOm.CssUnitValue Px(double value) => new(value, "px");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing an angle in <c>deg</c> units (degrees) with the given
    /// value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-deg"/>
    [IdlMember("deg", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-deg")]
    public static TypedOm.CssUnitValue Deg(double value) => new(value, "deg");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing an angle in <c>grad</c> units (gradians) with the
    /// given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-grad"/>
    [IdlMember("grad", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-grad")]
    public static TypedOm.CssUnitValue Grad(double value) => new(value, "grad");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing an angle in <c>rad</c> units (radians) with the given
    /// value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-rad"/>
    [IdlMember("rad", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-rad")]
    public static TypedOm.CssUnitValue Rad(double value) => new(value, "rad");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing an angle in <c>turn</c> units (turns) with the given
    /// value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-turn"/>
    [IdlMember("turn", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-turn")]
    public static TypedOm.CssUnitValue Turn(double value) => new(value, "turn");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a time in <c>s</c> units (seconds) with the given
    /// value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-s"/>
    [IdlMember("s", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-s")]
    public static TypedOm.CssUnitValue S(double value) => new(value, "s");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a time in <c>ms</c> units (milliseconds) with the
    /// given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-ms"/>
    [IdlMember("ms", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-ms")]
    public static TypedOm.CssUnitValue Ms(double value) => new(value, "ms");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a frequency in <c>Hz</c> units (hertz) with the given
    /// value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-hz"/>
    [IdlMember("Hz", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-hz")]
    public static TypedOm.CssUnitValue Hz(double value) => new(value, "hz");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a frequency in <c>kHz</c> units (kilohertz) with the
    /// given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-khz"/>
    [IdlMember("kHz", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-khz")]
    public static TypedOm.CssUnitValue KHz(double value) => new(value, "khz");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a resolution in <c>dpi</c> units (dots per inch) with
    /// the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dpi"/>
    [IdlMember("dpi", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dpi")]
    public static TypedOm.CssUnitValue Dpi(double value) => new(value, "dpi");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a resolution in <c>dpcm</c> units (dots per
    /// centimeter) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dpcm"/>
    [IdlMember("dpcm", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dpcm")]
    public static TypedOm.CssUnitValue Dpcm(double value) => new(value, "dpcm");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a resolution in <c>dppx</c> units (dots per pixel)
    /// with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dppx"/>
    [IdlMember("dppx", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-dppx")]
    public static TypedOm.CssUnitValue Dppx(double value) => new(value, "dppx");

    /// <summary>
    /// Creates a <see cref="TypedOm.CssUnitValue"/> representing a flexible length in <c>fr</c> units (fractions of the
    /// free space of a grid) with the given value.
    /// </summary>
    /// <seealso href="https://drafts.css-houdini.org/css-typed-om-1/#dom-css-fr"/>
    [IdlMember("fr", "https://drafts.css-houdini.org/css-typed-om-1/#dom-css-fr")]
    public static TypedOm.CssUnitValue Fr(double value) => new(value, "fr");
}
