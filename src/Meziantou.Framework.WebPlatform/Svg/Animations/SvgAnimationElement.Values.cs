using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Svg;
using Meziantou.Framework.WebPlatform.Svg.Animations;

namespace Meziantou.Framework.WebPlatform.Dom;

// The animation function of the animation elements (https://svgwg.org/specs/animations/#ValueAttributes, which refers to
// https://www.w3.org/TR/2001/REC-smil-animation-20010904/#AnimFuncValues): the target, the values (from, to, by, values), the
// interpolation (calcMode, keyTimes, keySplines) and the composition with the underlying value (additive, accumulate).
public partial class SvgAnimationElement
{
    private protected enum AnimationMode
    {
        None,
        FromTo,
        FromBy,
        To,
        By,
        Values,
        Path,
    }

    private protected enum CalcMode
    {
        Discrete,
        Linear,
        Paced,
        Spline,
    }

    private SvgElement? _target;
    private SmilTargetKind _targetKind;
    private string _targetAttribute = "";
    private int _valuesVersion;
    private (double Progress, long Repeat, int Version) _sampleState = (-1, -1, -1);

    /// <summary>
    /// https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__targetElement: the element referenced by the href
    /// attribute, or the parent element.
    /// </summary>
    [IdlMember("targetElement", "https://svgwg.org/specs/animations/#__svg__SVGAnimationElement__targetElement")]
    public SvgElement? TargetElement => IsConnected ? FindTargetElement() : null;

    /// <summary>What the element animates (resolved after the changes of the document).</summary>
    internal SmilTargetKind TargetKind => _targetKind;

    /// <summary>The target element the animated value is applied to.</summary>
    internal SvgElement? ResolvedTarget => _target;

    /// <summary>The attribute or the property the element animates.</summary>
    internal string TargetAttribute => _targetAttribute;

    private SvgElement? FindTargetElement()
    {
        var href = GetAttributeValue("href") ?? GetAttributeValue(Namespaces.XLink, "href");
        if (href is null)
            return ParentNode as SvgElement;

        // https://svgwg.org/specs/animations/#HrefAttribute: a same-document reference to an element of the tree
        href = href.Trim(' ', '\t', '\n', '\f', '\r');
        return href.Length > 1 && href[0] == '#' ? GetElementInTree(System.Text.Encoding.UTF8.GetString(Urls.PercentEncoding.PercentDecode(href[1..]))) as SvgElement : null;
    }

    /// <summary>https://svgwg.org/specs/animations/#TermAnimationTargetAttribute: resolves the target element and what is animated.</summary>
    private void ResolveTarget()
    {
        var target = IsConnected && TimeContainer is not null ? FindTargetElement() : null;

        var kind = SmilTargetKind.None;
        var attribute = "";

        // As in the browsers, the script elements are not animated (the animated href would not be fetched)
        if (target is not null and not SvgScriptElement)
        {
            if (this is SvgAnimateMotionElement)
            {
                kind = SmilTargetKind.Motion;
            }
            else
            {
                var name = GetAttributeValue("attributeName");
                if (name is not null)
                {
                    // A qualified name: the only animatable attribute with a namespace is xlink:href, an alias of href
                    name = name.Trim(' ', '\t', '\n', '\f', '\r');
                    var colon = name.IndexOf(':', StringComparison.Ordinal);
                    if (colon >= 0)
                    {
                        name = name[(colon + 1)..] == "href" && LookupNamespaceUri(name[..colon]) == Namespaces.XLink ? "href" : null;
                    }
                }

                if (this is SvgAnimateTransformElement)
                {
                    // Only animateTransform animates the transform lists
                    name ??= "transform";
                    if (target.GetAnimatedAttribute(name) is { Type: "SVGAnimatedTransformList" })
                    {
                        kind = SmilTargetKind.Attribute;
                        attribute = name;
                    }
                }
                else if (name is { Length: > 0 } and not "pathLength" && GetAttributeValue("attributeType") == "CSS" && CssPropertyRegistry.Get(name) is null)
                {
                    // https://svgwg.org/specs/animations/#AttributeTypeAttribute: the attribute is not a property (pathLength is the
                    // presentation attribute of the path-length property)
                }
                else if (name is { Length: > 0 })
                {
                    if (target.GetAnimatedAttribute(name) is { } animated)
                    {
                        if (animated.Type != "SVGAnimatedTransformList")
                        {
                            kind = SmilTargetKind.Attribute;
                            attribute = name;
                        }
                    }
                    else if (SmilValueType.IsPathDataAttribute(target, name))
                    {
                        kind = SmilTargetKind.Attribute;
                        attribute = name;
                    }
                    else if (PresentationalHints.IsSvgPresentationAttribute(name))
                    {
                        kind = SmilTargetKind.Css;
                        attribute = name;
                    }
                }
            }
        }

        if (!ReferenceEquals(target, _target) || kind != _targetKind || attribute != _targetAttribute)
        {
            _target = kind == SmilTargetKind.None ? null : target;
            _targetKind = kind;
            _targetAttribute = attribute;
            _valuesVersion++;
        }
    }

    /// <summary>Returns whether the progress, the iteration or the attributes of the animation changed since the last sample (the styles are computed again).</summary>
    internal bool UpdateSampleState()
    {
        var (progress, repeat) = CurrentProgress;
        var state = (progress, repeat, _valuesVersion);
        if (state == _sampleState)
            return false;

        _sampleState = state;
        return true;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The attributes of the animation function
    // ----------------------------------------------------------------------------------------------------------------

    /// <param name="emptyIsValue">Whether an empty attribute is a value of the type (e.g. an empty path), rather than a missing value.</param>
    private protected virtual AnimationMode GetAnimationMode(bool emptyIsValue = false)
    {
        // https://www.w3.org/TR/2001/REC-smil-animation-20010904/#AnimFuncValues: values wins over to, which wins over by
        if (GetAttributeValue("values") is not null)
            return AnimationMode.Values;

        var hasFrom = HasValue("from");
        if (HasValue("to"))
            return hasFrom ? AnimationMode.FromTo : AnimationMode.To;

        // An empty by value adds nothing
        if (!string.IsNullOrEmpty(GetAttributeValue("by")))
            return hasFrom ? AnimationMode.FromBy : AnimationMode.By;

        return AnimationMode.None;

        bool HasValue(string name) => GetAttributeValue(name) is { } value && (emptyIsValue || value.Length > 0);
    }

    private protected virtual CalcMode DefaultCalcMode => CalcMode.Linear;

    private protected CalcMode GetCalcMode() => GetAttributeValue("calcMode") switch
    {
        "discrete" => CalcMode.Discrete,
        "linear" => CalcMode.Linear,
        "paced" => CalcMode.Paced,
        "spline" => CalcMode.Spline,
        _ => DefaultCalcMode,
    };

    private protected bool IsAdditiveAnimation(AnimationMode mode) => mode == AnimationMode.By || GetAttributeValue("additive") == "sum";

    private protected bool IsCumulativeAnimation(AnimationMode mode) => mode != AnimationMode.To && GetAttributeValue("accumulate") == "sum";

    private protected static int GetKeyTimesIndex(double percent, List<double> keyTimes, CalcMode calcMode)
    {
        // The last key time of the linear and spline animations is 1: it is not considered, as the progress is never greater
        var count = keyTimes.Count;
        if (count > 0 && calcMode != CalcMode.Discrete)
        {
            count--;
        }

        int index;
        for (index = 1; index < count; index++)
        {
            if (keyTimes[index] > percent)
                break;
        }

        return index - 1;
    }

    private static double EvaluateSpline((double X1, double Y1, double X2, double Y2) spline, double percent)
    {
        var easing = CssEasing.Parse(string.Create(CultureInfo.InvariantCulture, $"cubic-bezier({spline.X1}, {spline.Y1}, {spline.X2}, {spline.Y2})"));
        return easing is null ? percent : Math.Clamp(easing.Evaluate(percent), 0, 1);
    }

    /// <summary>The position between the key points of an <c>animateMotion</c> element (https://svgwg.org/specs/animations/#KeyPointsAttribute).</summary>
    private protected static double GetPercentFromKeyPoints(double percent, List<double> keyTimes, List<double> keyPoints, CalcMode calcMode, List<(double X1, double Y1, double X2, double Y2)> keySplines)
    {
        if (percent == 1)
            return keyPoints[^1];

        var index = GetKeyTimesIndex(percent, keyTimes, calcMode);
        var fromKeyPoint = keyPoints[index];
        if (calcMode == CalcMode.Discrete || index + 1 >= keyTimes.Count)
            return fromKeyPoint;

        var fromPercent = keyTimes[index];
        var toPercent = keyTimes[index + 1];
        var keyPointPercent = toPercent > fromPercent ? (percent - fromPercent) / (toPercent - fromPercent) : 0;
        if (calcMode == CalcMode.Spline && index < keySplines.Count)
        {
            keyPointPercent = EvaluateSpline(keySplines[index], keyPointPercent);
        }

        return ((keyPoints[index + 1] - fromKeyPoint) * keyPointPercent) + fromKeyPoint;
    }

    /// <summary>The key times of a paced animation: proportional to the distances between the values, or null when the distances are not defined.</summary>
    private static List<double>? ComputePacedKeyTimes(SmilValueType type, object[] values)
    {
        if (values.Length < 2)
            return null;

        var keyTimes = new List<double>(values.Length) { 0 };
        var total = 0d;
        for (var i = 0; i < values.Length - 1; i++)
        {
            if (type.GetDistance(values[i], values[i + 1]) is not { } distance)
                return null;

            total += distance;
            keyTimes.Add(distance);
        }

        if (!double.IsFinite(total) || total == 0)
            return null;

        for (var i = 1; i < keyTimes.Count - 1; i++)
        {
            keyTimes[i] = keyTimes[i - 1] + (keyTimes[i] / total);
        }

        keyTimes[^1] = 1;
        return keyTimes;
    }

    /// <summary>The key times, the key points and the key splines of the element; false when they make the animation invalid.</summary>
    private protected bool TryGetKeys(AnimationMode mode, int valueCount, out CalcMode calcMode, out List<double> keyTimes, out List<double> keyPoints, out List<(double X1, double Y1, double X2, double Y2)> keySplines)
    {
        calcMode = GetCalcMode();
        var hasKeyTimes = GetAttributeValue("keyTimes") is not null;
        var hasKeyPoints = this is SvgAnimateMotionElement && GetAttributeValue("keyPoints") is not null;
        keyTimes = SmilParsing.ParseKeyTimes(GetAttributeValue("keyTimes"), verifyOrder: true);
        keyPoints = hasKeyPoints ? SmilParsing.ParseKeyTimes(GetAttributeValue("keyPoints"), verifyOrder: false) : [];
        keySplines = calcMode == CalcMode.Spline ? SmilParsing.ParseKeySplines(GetAttributeValue("keySplines")) : [];

        // The key points need as many key times
        if (hasKeyPoints && keyPoints.Count != keyTimes.Count)
            return false;

        if (calcMode == CalcMode.Spline)
        {
            var count = keySplines.Count;
            if (count == 0 || (hasKeyPoints && keyPoints.Count - 1 != count) || (mode == AnimationMode.Values && valueCount - 1 != count) || (hasKeyTimes && keyTimes.Count - 1 != count))
                return false;
        }

        switch (mode)
        {
            case AnimationMode.Values:
                // https://svgwg.org/specs/animations/#KeyTimesAttribute: as many key times as values, the last one being 1 unless
                // the animation is discrete
                if (!(calcMode == CalcMode.Paced || !hasKeyTimes || hasKeyPoints || valueCount == keyTimes.Count))
                    return false;

                if (!(calcMode == CalcMode.Discrete || keyTimes.Count == 0 || keyTimes[^1] == 1))
                    return false;

                if (hasKeyPoints && !(keyTimes.Count > 1 && keyTimes.Count == keyPoints.Count))
                    return false;

                break;

            case AnimationMode.Path:
                if (!(calcMode == CalcMode.Paced || !hasKeyPoints || (keyTimes.Count > 1 && keyTimes.Count == keyPoints.Count)))
                    return false;

                if (hasKeyPoints && calcMode != CalcMode.Discrete && keyTimes.Count > 0 && keyTimes[^1] != 1)
                    return false;

                break;
        }

        return true;
    }

    /// <summary>The values the last sample interpolated (the direction of the motion of an <c>animateMotion</c> element).</summary>
    private protected (object? From, object? To) LastInterval { get; private set; }

    /// <summary>
    /// Computes the value of the animation at the current time of the timeline, from the underlying value (the base value, or
    /// the result of the animations of lower priority). Returns null when the animation has no effect (e.g. an invalid value).
    /// </summary>
    /// <param name="compose">Whether the value of an additive animation is added to the underlying value.</param>
    internal object? ComputeAnimatedValue(SmilValueType type, object? underlying, bool compose = true)
    {
        var (percent, repeat) = CurrentProgress;
        LastInterval = (null, null);
        var mode = GetAnimationMode(type.IsEmptyValue);
        if (mode is AnimationMode.None or AnimationMode.Path)
            return null;

        var isSet = this is SvgSetElement;
        object? from;
        object? to;
        object? toAtEnd;
        object? by = null;
        double effective;
        CalcMode calcMode;
        if (mode == AnimationMode.Values)
        {
            var values = SmilParsing.SplitValues(GetAttributeValue("values") ?? "");
            var count = values.Count;
            if (count == 0 || !TryGetKeys(mode, count, out calcMode, out var keyTimes, out var keyPoints, out var keySplines))
                return null;

            // "If any values are not legal, the animation will have no effect"
            var parsed = new object[count];
            for (var i = 0; i < count; i++)
            {
                if (type.Parse(values[i]) is not { } item)
                    return null;

                parsed[i] = item;
            }

            // The values that cannot be interpolated are animated discretely
            if (!type.IsAdditive)
            {
                calcMode = CalcMode.Discrete;
            }

            if (calcMode == CalcMode.Paced)
            {
                // The key times of a paced animation are proportional to the distances between the values; a type without
                // distance is animated linearly
                keyTimes = ComputePacedKeyTimes(type, parsed) ?? (keyTimes.Count == count ? keyTimes : []);
            }
            else if (keyTimes.Count != count)
            {
                keyTimes = [];
            }

            toAtEnd = parsed[count - 1];
            if (percent == 1 || count == 1)
            {
                from = to = parsed[count - 1];
                effective = 1;
            }
            else if (keyPoints.Count > 0 && calcMode != CalcMode.Paced)
            {
                var keyPointPercent = GetPercentFromKeyPoints(percent, keyTimes, keyPoints, calcMode, keySplines);
                var index = keyPointPercent == 1 ? count - 2 : (int)(keyPointPercent * (count - 1));
                index = Math.Clamp(index, 0, count - 2);
                from = parsed[index];
                to = parsed[index + 1];
                effective = (keyPointPercent * (count - 1)) - index;
            }
            else
            {
                var index = GetKeyTimesIndex(percent, keyTimes, calcMode);
                if (calcMode == CalcMode.Discrete)
                {
                    if (keyTimes.Count == 0)
                    {
                        index = (int)(percent * count);
                    }

                    index = Math.Clamp(index, 0, count - 1);
                    from = to = parsed[index];
                    effective = 0;
                }
                else
                {
                    double fromPercent;
                    double toPercent;
                    if (keyTimes.Count > 0)
                    {
                        fromPercent = keyTimes[index];
                        toPercent = keyTimes[index + 1];
                    }
                    else
                    {
                        index = (int)Math.Floor(percent * (count - 1));
                        fromPercent = (double)index / (count - 1);
                        toPercent = (double)(index + 1) / (count - 1);
                    }

                    if (index == count - 1)
                    {
                        index--;
                    }

                    from = parsed[index];
                    to = parsed[index + 1];
                    effective = toPercent > fromPercent ? (percent - fromPercent) / (toPercent - fromPercent) : 0;
                    if (calcMode == CalcMode.Spline && index < keySplines.Count)
                    {
                        effective = EvaluateSpline(keySplines[index], effective);
                    }
                }
            }
        }
        else
        {
            if (!TryGetKeys(mode, 2, out calcMode, out var keyTimes, out var keyPoints, out var keySplines))
                return null;

            from = null;
            to = null;
            switch (mode)
            {
                case AnimationMode.To:
                    to = type.Parse(GetAttributeValue("to") ?? "");
                    from = underlying ?? to;
                    break;

                case AnimationMode.FromTo:
                    from = type.Parse(GetAttributeValue("from")!);
                    to = type.Parse(GetAttributeValue("to")!);
                    break;

                case AnimationMode.FromBy:
                    from = type.Parse(GetAttributeValue("from")!);
                    by = type.Parse(GetAttributeValue("by")!);

                    // The values that are not additive have no by animation
                    if (from is null || by is null || !type.IsAdditive || !type.CanAdd(from, by))
                        return null;

                    to = type.Add(from, by);
                    break;

                case AnimationMode.By:
                    by = type.Parse(GetAttributeValue("by")!);
                    if (by is null || !type.IsAdditive)
                        return null;

                    from = type.GetZero(by);
                    to = by;
                    break;
            }

            if (to is null || (from is null && mode != AnimationMode.By))
                return null;

            toAtEnd = to;
            if (keyPoints.Count > 0 && calcMode != CalcMode.Paced)
            {
                effective = GetPercentFromKeyPoints(percent, keyTimes, keyPoints, calcMode, keySplines);
            }
            else if (calcMode == CalcMode.Spline)
            {
                effective = EvaluateSpline(keySplines[keyTimes.Count > 1 ? Math.Min(GetKeyTimesIndex(percent, keyTimes, calcMode), keySplines.Count - 1) : 0], percent);
            }
            else if (mode is AnimationMode.FromTo or AnimationMode.To && calcMode == CalcMode.Discrete && keyTimes.Count == 2)
            {
                effective = percent > keyTimes[1] ? 1 : 0;
            }
            else
            {
                effective = percent;
            }
        }

        // https://svgwg.org/specs/animations/#SetElement: the value of the set element is its to value
        if (isSet)
        {
            effective = 1;
        }

        if (!type.IsAdditive)
        {
            // The discrete values: the to value is used from the middle of the simple duration (the from value of a to animation
            // is the underlying value)
            return effective <= 0.5 ? from : to;
        }

        if (calcMode == CalcMode.Discrete)
        {
            effective = effective < 0.5 ? 0 : 1;
        }

        object value;
        if (mode == AnimationMode.By && from is null)
        {
            // A type without zero: the by value is relative to the underlying value
            if (underlying is null || by is null || !type.CanAdd(underlying, by))
                return null;

            var end = type.AddToUnderlying(underlying, by);
            value = type.CanInterpolate(underlying, end) ? type.Interpolate(underlying, end, effective) : (effective < 0.5 ? underlying : end);
            if (IsCumulativeAnimation(mode) && repeat > 0)
            {
                value = type.Accumulate(value, by, repeat);
            }

            return value;
        }

        LastInterval = (from, to);
        if (!type.CanInterpolate(from!, to))
        {
            // The values that cannot be interpolated (e.g. lists of different lengths) are animated discretely
            return effective < 0.5 ? from : to;
        }

        value = type.Interpolate(from!, to, effective);
        if (IsCumulativeAnimation(mode) && repeat > 0 && toAtEnd is not null)
        {
            value = type.Accumulate(value, toAtEnd, repeat);
        }

        if (compose && mode != AnimationMode.To && IsAdditiveAnimation(mode) && underlying is not null)
        {
            // The values that cannot be added (e.g. paths with different commands): the animation has no effect
            if (!type.CanAdd(underlying, value))
                return null;

            value = type.AddToUnderlying(underlying, value);
        }

        return value;
    }
}

public partial class SvgSetElement
{
    // https://svgwg.org/specs/animations/#SetElement: the set element sets the to value for the duration of the animation
    private protected override AnimationMode GetAnimationMode(bool emptyIsValue = false) => AnimationMode.To;
}

public partial class SvgAnimateTransformElement
{
    /// <summary>The type of the transform of the values (https://svgwg.org/specs/animations/#AnimateTransformElementTypeAttribute): translate by default.</summary>
    internal ushort TransformType => GetAttributeValue("type") switch
    {
        null or "translate" => SvgTransform.SvgTransformTranslate,
        "scale" => SvgTransform.SvgTransformScale,
        "rotate" => SvgTransform.SvgTransformRotate,
        "skewX" => SvgTransform.SvgTransformSkewX,
        "skewY" => SvgTransform.SvgTransformSkewY,
        _ => SvgTransform.SvgTransformUnknown,
    };
}

public partial class SvgAnimateMotionElement
{
    private protected override CalcMode DefaultCalcMode => CalcMode.Paced;

    private protected override AnimationMode GetAnimationMode(bool emptyIsValue = false)
    {
        // https://svgwg.org/specs/animations/#AnimateMotionElement: the mpath child wins over the path attribute, which wins
        // over the values
        if (GetAttributeValue("path") is not null || GetMPath() is not null)
            return AnimationMode.Path;

        return base.GetAnimationMode(emptyIsValue);
    }

    private SvgMPathElement? GetMPath()
    {
        for (var child = FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is SvgMPathElement mpath)
                return mpath;
        }

        return null;
    }

    /// <summary>The motion path: the geometry of the element referenced by the mpath child, or the path attribute.</summary>
    private SvgPathGeometry? GetMotionPath()
    {
        List<SvgPathCommand> commands;
        if (GetMPath() is { } mpath)
        {
            if (SvgLinking.GetReferencedElement(mpath) is not SvgGeometryElement referenced)
                return null;

            commands = SvgShapeGeometry.GetPath(referenced);
        }
        else
        {
            commands = SvgPathData.Parse(GetAttributeValue("path") ?? "");
        }

        return commands.Count == 0 ? null : new SvgPathGeometry(commands);
    }

    /// <summary>
    /// Applies the supplemental transform of the element at the current time (https://svgwg.org/specs/animations/#AnimateMotionElement)
    /// to the transform of the animations of lower priority: the translation to the position on the motion path, and the
    /// rotation of the <c>rotate</c> attribute.
    /// </summary>
    internal SvgAffine ComputeTransform(SvgAffine underlying)
    {
        var mode = GetAnimationMode();
        var transform = IsAdditiveAnimation(mode) ? underlying : SvgAffine.Identity;
        var (percent, repeat) = CurrentProgress;
        double x;
        double y;
        double? tangentAngle = null;
        if (mode == AnimationMode.Path)
        {
            if (GetMotionPath() is not { } path || !TryGetKeys(mode, 0, out var calcMode, out var keyTimes, out var keyPoints, out var keySplines))
                return underlying;

            var total = path.TotalLength;
            SvgPathData.SvgPoint point;
            SvgPathData.SvgPoint tangent = default;
            if (keyPoints.Count > 0)
            {
                (point, tangent) = path.GetPointAndTangentAtLength(total * GetPercentFromKeyPoints(percent, keyTimes, keyPoints, calcMode, keySplines));
            }
            else if (calcMode == CalcMode.Discrete)
            {
                // The positions of a discrete animation are the end points of the commands of the path, each for the same duration
                var vertices = path.GetVertices();
                var index = percent == 1 ? vertices.Count - 1
                    : keyTimes.Count == vertices.Count ? GetKeyTimesIndex(percent, keyTimes, calcMode)
                    : (int)(percent * vertices.Count);
                point = vertices[Math.Clamp(index, 0, vertices.Count - 1)];
            }
            else
            {
                (point, tangent) = path.GetPointAndTangentAtLength(total * percent);
            }

            x = point.X;
            y = point.Y;
            if (IsCumulativeAnimation(mode) && repeat > 0)
            {
                var end = path.GetPointAtLength(total);
                x += end.X * repeat;
                y += end.Y * repeat;
            }

            if (tangent.X != 0 || tangent.Y != 0)
            {
                tangentAngle = Math.Atan2(tangent.Y, tangent.X) * 180 / Math.PI;
            }
        }
        else
        {
            var type = new SmilNumberListType(this, "", SmilNumberKind.Point, defaultValue: null);
            if (ComputeAnimatedValue(type, new double[2], compose: false) is not double[] { Length: 2 } position)
                return underlying;

            x = position[0];
            y = position[1];
            if (LastInterval is (double[] { Length: 2 } from, double[] { Length: 2 } to) && (from[0] != to[0] || from[1] != to[1]))
            {
                tangentAngle = Math.Atan2(to[1] - from[1], to[0] - from[0]) * 180 / Math.PI;
            }
        }

        var motion = SvgAffine.Translation(x, y);
        var rotate = GetAttributeValue("rotate")?.Trim(' ', '\t', '\n', '\f', '\r');
        double angle = 0;
        if (rotate is "auto" or "auto-reverse")
        {
            angle = (tangentAngle ?? 0) + (rotate == "auto-reverse" ? 180 : 0);
        }
        else if (rotate is not null && SvgParsing.TryParseSingleNumber(rotate, out var number))
        {
            angle = number;
        }

        if (angle != 0)
        {
            motion = SvgAffine.Rotation(angle).Then(motion);
        }

        return motion.Then(transform);
    }
}

public partial class SvgMPathElement
{
    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (localName == "href" && IsConnected)
        {
            NodeDocument.SmilOrNull?.SetDirty();
        }
    }
}
