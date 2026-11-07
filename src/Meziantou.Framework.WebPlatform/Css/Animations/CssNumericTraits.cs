using System.Collections.Concurrent;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Css.Animations;

/// <summary>
/// The numeric types of the grammar of a property, outside of the functional notations: whether its numbers are integers
/// (an interpolated integer is rounded, https://drafts.csswg.org/css-values-4/#combine-integers) and the range of each
/// canonical unit, which clamps the extrapolated values (e.g. a width cannot be negative).
/// </summary>
internal sealed class CssNumericTraits
{
    private static readonly ConcurrentDictionary<string, CssNumericTraits> Cache = new(StringComparer.Ordinal);

    private readonly Dictionary<string, UnitTraits> _units = new(StringComparer.Ordinal);

    /// <summary>The traits of a value without grammar: nothing is rounded or clamped.</summary>
    public static CssNumericTraits Empty { get; } = new();

    /// <summary>The traits of a &lt;length-percentage&gt; without range (e.g. the arguments of the transform functions).</summary>
    public static CssNumericTraits LengthPercentage { get; } = new() { PercentageUnit = "px" };

    /// <summary>
    /// The canonical unit of the percentages that can be combined with dimensions (e.g. "px" for &lt;length-percentage&gt;),
    /// or null when the percentages are their own type.
    /// </summary>
    public string? PercentageUnit { get; private set; }

    private struct UnitTraits
    {
        public bool Integer;
        public bool Consistent;
        public double? Min;
        public double? Max;
    }

    public static CssNumericTraits Get(string property) => Cache.GetOrAdd(property, static property =>
    {
        if (CssPropertyRegistry.Get(property) is not { } definition || CssPropertyRegistry.GetGrammar(definition) is not { } grammar)
            return Empty;

        var traits = new CssNumericTraits { PercentageUnit = CssValueComputer.GetPercentageUnit(definition) };
        traits.Collect(grammar, new HashSet<string>(StringComparer.Ordinal), depth: 0);
        return traits;
    });

    /// <summary>Returns the traits of a grammar (e.g. the syntax of a registered custom property).</summary>
    public static CssNumericTraits FromGrammar(GrammarNode grammar, string? percentageUnit)
    {
        var traits = new CssNumericTraits { PercentageUnit = percentageUnit };
        traits.Collect(grammar, new HashSet<string>(StringComparer.Ordinal), depth: 0);
        return traits;
    }

    /// <summary>Returns whether the numbers ("" unit) of the value are integers.</summary>
    public bool IsInteger(string unit) => _units.TryGetValue(unit, out var traits) && traits.Integer;

    /// <summary>Clamps a value of the unit ("" for numbers, "%" for percentages, or a canonical unit) to its range.</summary>
    public double Clamp(string unit, double value)
    {
        if (!_units.TryGetValue(unit, out var traits) || !traits.Consistent)
            return value;

        if (traits.Min is { } min && value < min)
            return min;

        if (traits.Max is { } max && value > max)
            return max;

        return value;
    }

    private void Collect(GrammarNode node, HashSet<string> visited, int depth)
    {
        if (depth > 24)
            return;

        switch (node)
        {
            case GroupNode group:
                foreach (var child in group.Children)
                {
                    Collect(child, visited, depth + 1);
                }

                break;

            case TypeNode { IsProperty: true } reference:
                if (visited.Add("'" + reference.Name) && CssPropertyRegistry.Get(reference.Name) is { } property && CssPropertyRegistry.GetGrammar(property) is { } propertyGrammar)
                {
                    Collect(propertyGrammar, visited, depth + 1);
                }

                break;

            case TypeNode type when !type.Name.EndsWith("()", StringComparison.Ordinal):
                switch (type.Name)
                {
                    case "number":
                        Add("", type, integer: false);
                        break;
                    case "integer":
                        Add("", type, integer: true);
                        break;
                    case "percentage":
                        Add("%", type, integer: false);
                        break;
                    case "length":
                        Add("px", type, integer: false);
                        break;
                    case "length-percentage":
                        Add("px", type, integer: false);
                        Add("%", type, integer: false);
                        break;
                    case "angle":
                        Add("deg", type, integer: false);
                        break;
                    case "angle-percentage":
                        Add("deg", type, integer: false);
                        Add("%", type, integer: false);
                        break;
                    case "time":
                        Add("s", type, integer: false);
                        break;
                    case "time-percentage":
                        Add("s", type, integer: false);
                        Add("%", type, integer: false);
                        break;
                    case "frequency":
                        Add("hz", type, integer: false);
                        break;
                    case "resolution":
                        Add("dppx", type, integer: false);
                        break;
                    case "flex":
                        Add("fr", type, integer: false);
                        break;

                    // The colors, images and shapes are combined by their own rules
                    case "color" or "image" or "url" or "basic-shape" or "transform-list" or "transform-function" or "filter-value-list":
                        break;

                    default:
                        if (visited.Add(type.Name) && CssPropertyRegistry.GetTypeGrammar(type.GrammarName) is { } typeGrammar)
                        {
                            Collect(typeGrammar, visited, depth + 1);
                        }

                        break;
                }

                break;
        }
    }

    private void Add(string unit, TypeNode type, bool integer)
    {
        if (!_units.TryGetValue(unit, out var traits))
        {
            _units[unit] = new UnitTraits { Integer = integer, Consistent = true, Min = type.RangeMin, Max = type.RangeMax };
            return;
        }

        traits.Integer &= integer;
        if (traits.Min != type.RangeMin || traits.Max != type.RangeMax)
        {
            traits.Consistent = false;
        }

        _units[unit] = traits;
    }
}
