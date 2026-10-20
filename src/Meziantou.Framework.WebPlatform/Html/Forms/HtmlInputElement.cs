using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;
using File = Meziantou.Framework.WebPlatform.Dom.FileApi.File;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The states of the type attribute of input elements (https://html.spec.whatwg.org/multipage/input.html#attr-input-type), in the order of EnumeratedAttributes.InputType.</summary>
internal enum InputType : byte
{
    Hidden,
    Text,
    Search,
    Tel,
    Url,
    Email,
    Password,
    Date,
    Month,
    Week,
    Time,
    DateTimeLocal,
    Number,
    Range,
    Color,
    Checkbox,
    Radio,
    File,
    Submit,
    Image,
    Reset,
    Button,
}

/// <summary>The modes of the value IDL attribute (https://html.spec.whatwg.org/multipage/input.html#dom-input-value).</summary>
internal enum InputValueMode : byte
{
    Value,
    Default,
    DefaultOn,
    Filename,
}

public partial class HtmlInputElement : IEditableTextControl
{
    private const double MillisecondsPerDay = 86_400_000;

    private InputType _type = InputType.Text;
    private string _value = "";
    private bool _dirtyValue;
    private bool _checkedness;
    private bool _dirtyCheckedness;
    private bool _indeterminate;
    private readonly List<File> _selectedFiles = [];

    /// <summary>
    /// The selected coordinate of an image button (https://html.spec.whatwg.org/multipage/input.html#concept-input-type-image-coordinate):
    /// the offset of the mouse event that activates the button in the image (as in the browsers, the event may be untrusted;
    /// click() selects (0,0)).
    /// </summary>
    internal (int X, int Y) SelectedCoordinate { get; private set; }
    private FileList? _fileList;

    // The selection of text controls (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#textFieldSelection)
    private int _selectionStart;
    private int _selectionEnd;
    private string _selectionDirection = "none";

    // The compiled pattern regular expression, and the value of the pattern attribute it was compiled from
    private string? _compiledPatternSource;
    private Regex? _compiledPattern;

    // The legacy-pre-activation state
    private bool _preActivationCheckedness;
    private bool _preActivationIndeterminate;
    private HtmlInputElement? _preActivationCheckedRadio;
    private InputType? _preActivationType;

    internal InputType InputType => _type;

    internal bool Checkedness => _checkedness;

    internal string ValueRaw => _value;

    internal override bool IsLabelable => _type != InputType.Hidden;

    internal static InputValueMode GetValueMode(InputType type) => type switch
    {
        InputType.Hidden or InputType.Submit or InputType.Image or InputType.Reset or InputType.Button => InputValueMode.Default,
        InputType.Checkbox or InputType.Radio => InputValueMode.DefaultOn,
        InputType.File => InputValueMode.Filename,
        _ => InputValueMode.Value,
    };

    private InputValueMode ValueMode => GetValueMode(_type);

    private static InputType ParseType(string? value) => (InputType)EnumeratedAttributes.InputType.GetState(value);

    partial void OnCreated() => SanitizeValue();

    /// <summary>The parser sets the attributes of the element before it runs the reset algorithm.</summary>
    internal override void ResetFromParser()
    {
        _type = ParseType(GetAttributeValue("type"));
        ResetAlgorithm();

        // The image of an element created in the Image Button state
        if (_type == InputType.Image && _imageButtonRequest is null && _imageButtonSource is null)
        {
            OnImageButtonMutation(typeChanged: true);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#the-input-element:concept-form-reset-control</summary>
    internal override void ResetAlgorithm()
    {
        TextControlEditing.ClearHistory(this);
        UserValidity = false;
        _dirtyValue = false;
        _dirtyCheckedness = false;
        _value = GetAttributeValue("value") ?? "";
        SetCheckedness(HasAttributeWithLocalName("checked"));
        _selectedFiles.Clear();
        _fileList = null;
        SanitizeValue();
    }

    internal override void AttributeChangeSteps(string localName, string? oldValue, string? value, string? @namespace)
    {
        base.AttributeChangeSteps(localName, oldValue, value, @namespace);
        if (@namespace is not null)
            return;

        switch (localName)
        {
            case "type":
                ChangeType(ParseType(value));
                break;

            case "src":
                OnImageButtonMutation(typeChanged: false);
                break;

            case "alt" when _type == InputType.Image:
                InvalidateImageButtonRendering(altTextChanged: true);
                break;

            case "value":
                // https://html.spec.whatwg.org/multipage/input.html#the-input-element:concept-input-value-dirty-flag-4
                if (!_dirtyValue)
                {
                    _value = value ?? "";
                    SanitizeValue();
                }

                break;

            case "alpha" or "colorspace" when _type == InputType.Color:
                // https://html.spec.whatwg.org/multipage/input.html#attr-input-colorspace: update the color well control color
                SanitizeValue();
                break;

            case "checked":
                if (!_dirtyCheckedness)
                {
                    SetCheckedness(value is not null);
                }

                break;

            case "name" or "form":
                if (_type == InputType.Radio && _checkedness)
                {
                    UncheckOtherRadiosInGroup();
                }

                break;

            case "min" or "max" or "step" or "multiple":
                if (_type is InputType.Range or InputType.Email)
                {
                    SanitizeValue();
                }

                break;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#input-type-change</summary>
    private void ChangeType(InputType newType)
    {
        var oldType = _type;
        if (oldType == newType)
            return;

        var oldMode = GetValueMode(oldType);
        var newMode = GetValueMode(newType);
        if (oldMode == InputValueMode.Value && _value.Length != 0 && newMode is InputValueMode.Default or InputValueMode.DefaultOn)
        {
            _type = newType;
            SetAttributeValue("value", _value);
        }
        else if (oldMode != InputValueMode.Value && newMode == InputValueMode.Value)
        {
            _value = GetAttributeValue("value") ?? "";
            _dirtyValue = false;
        }
        else if (oldMode == InputValueMode.Value && newMode == InputValueMode.Value && !_dirtyValue)
        {
            // As in Gecko and WebKit, the value of a control whose dirty value flag is false is the value of the value content
            // attribute sanitized for the new type (e.g. the #000000 of an empty color input does not stay)
            _value = GetAttributeValue("value") ?? "";
        }
        else if (oldMode != InputValueMode.Filename && newMode == InputValueMode.Filename)
        {
            _value = "";
        }

        var previouslySelectable = SelectionApplies(oldType);
        _type = newType;
        TextControlEditing.ClearHistory(this);
        if (newType == InputType.Radio && _checkedness)
        {
            UncheckOtherRadiosInGroup();
        }

        SanitizeValue();
        if (!previouslySelectable && SelectionApplies(newType))
        {
            _selectionStart = _selectionEnd = 0;
            _selectionDirection = "none";
        }

        OnImageButtonMutation(typeChanged: true);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#the-input-element:concept-node-clone-ext</summary>
    internal override void CloningSteps(Node copy, bool subtree)
    {
        base.CloningSteps(copy, subtree);
        var input = (HtmlInputElement)copy;
        input._type = _type;
        input._value = _value;
        input._dirtyValue = _dirtyValue;
        input._checkedness = _checkedness;
        input._dirtyCheckedness = _dirtyCheckedness;
        input._indeterminate = _indeterminate;
    }

    internal override void InsertionSteps()
    {
        base.InsertionSteps();

        // The other radio buttons of the group are unchecked when the element becomes connected or when its form owner
        // changes, not when it is inserted in a disconnected tree without a form
        if (_type == InputType.Radio && _checkedness && (IsConnected || FormOwner is not null))
        {
            UncheckOtherRadiosInGroup();
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Value
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#dom-input-value</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/input.html#dom-input-value"/>
    [IdlMember("value", "https://html.spec.whatwg.org/multipage/input.html#dom-input-value")]
    public string Value
    {
        get => ValueMode switch
        {
            InputValueMode.Value => _value,
            InputValueMode.Default => GetAttributeValue("value") ?? "",
            InputValueMode.DefaultOn => GetAttributeValue("value") ?? "on",
            _ => _selectedFiles.Count > 0 ? "C:\\fakepath\\" + _selectedFiles[0].Name : "",
        };
        set
        {
            value ??= "";
            switch (ValueMode)
            {
                case InputValueMode.Value:
                    var oldValue = _value;
                    _value = value;
                    _dirtyValue = true;
                    SanitizeValue();
                    NodeDocument.IncrementElementStateVersion();
                    TextControlEditing.ClearHistory(this);

                    // The caret of the types whose selection is not exposed (e.g. email) also moves to the end of the value
                    if (_value != oldValue && (SelectionApplies(_type) || AllowsTextEntry))
                    {
                        _selectionStart = _selectionEnd = _value.Length;
                        _selectionDirection = "none";
                    }

                    break;

                case InputValueMode.Default or InputValueMode.DefaultOn:
                    SetAttributeValue("value", value);
                    break;

                default:
                    if (value.Length != 0)
                        throw DomException.InvalidState("The value of a file input can only be set to the empty string");

                    _selectedFiles.Clear();
                    _fileList = null;
                    break;
            }
        }
    }

    /// <summary>Sets the value (as a user agent would) and runs the value sanitization algorithm.</summary>
    internal void SetValueInternal(string value)
    {
        _value = value;
        SanitizeValue();
        NodeDocument.IncrementElementStateVersion();
    }

    /// <summary>The value sanitization algorithm of the type (https://html.spec.whatwg.org/multipage/input.html#value-sanitization-algorithm).</summary>
    private void SanitizeValue()
    {
        switch (_type)
        {
            case InputType.Text or InputType.Search or InputType.Tel or InputType.Password:
                _value = StripNewlines(_value);
                break;

            case InputType.Url:
                _value = StripWhitespace(StripNewlines(_value));
                break;

            case InputType.Email:
                if (HasAttributeWithLocalName("multiple"))
                {
                    _value = string.Join(',', _value.Split(',').Select(StripWhitespace));
                }
                else
                {
                    _value = StripWhitespace(StripNewlines(_value));
                }

                break;

            case InputType.Date:
                if (!Microsyntaxes.TryParseDateString(_value, out _, out _, out _))
                {
                    _value = "";
                }

                break;

            case InputType.Month:
                if (!Microsyntaxes.TryParseMonthString(_value, out _, out _))
                {
                    _value = "";
                }

                break;

            case InputType.Week:
                if (!Microsyntaxes.TryParseWeekString(_value, out _, out _))
                {
                    _value = "";
                }

                break;

            case InputType.Time:
                if (!Microsyntaxes.TryParseTimeString(_value, out _, out _, out _))
                {
                    _value = "";
                }

                break;

            case InputType.DateTimeLocal:
                _value = Microsyntaxes.TryParseLocalDateAndTimeString(_value, out var milliseconds) ? Microsyntaxes.FormatLocalDateAndTime(milliseconds) : "";
                break;

            case InputType.Number:
                if (!Microsyntaxes.IsValidFloatingPointNumber(_value) || !Microsyntaxes.TryParseFloat(_value, out _))
                {
                    _value = "";
                }

                break;

            case InputType.Range:
                SanitizeRange();
                break;

            case InputType.Color:
                _value = UpdateColorWellControlColor();
                break;
        }

        // The selection stays within the bounds of the value when the value changes without the setter (e.g. a type change, a reset)
        _selectionStart = Math.Min(_selectionStart, _value.Length);
        _selectionEnd = Math.Min(_selectionEnd, _value.Length);
    }

    private static string StripNewlines(string value) => value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal) ? value.Replace("\n", "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal) : value;

    private static string StripWhitespace(string value) => InfraAscii.StripLeadingAndTrailingAsciiWhitespace(value).ToString();

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#range-state-(type=range)</summary>
    private void SanitizeRange()
    {
        var minimum = GetMinimum() ?? 0;
        var maximum = GetMaximum() ?? 100;
        double value;
        if (Microsyntaxes.IsValidFloatingPointNumber(_value) && Microsyntaxes.TryParseFloat(_value, out var parsed))
        {
            value = parsed;
        }
        else
        {
            value = maximum < minimum ? minimum : minimum + ((maximum - minimum) / 2);
        }

        // Underflow, overflow and step mismatch are not possible: the value is clamped to the range and to the step
        if (value < minimum)
        {
            value = minimum;
        }
        else if (maximum >= minimum && value > maximum)
        {
            value = maximum;
        }

        if (GetAllowedValueStep() is { } step)
        {
            var stepBase = GetStepBase();
            var steps = Math.Round((value - stepBase) / step, MidpointRounding.AwayFromZero);
            var rounded = stepBase + (steps * step);
            if (rounded > maximum && maximum >= minimum)
            {
                rounded -= step;
            }

            value = RoundToStepPrecision(rounded, stepBase, step);
        }

        _value = Microsyntaxes.ToBestFloatingPointRepresentation(value);
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/input.html#update-a-color-well-control-color: the value (the value content
    /// attribute when the value is not dirty) is parsed as a CSS color (opaque black on failure) and serialized in the color
    /// space of the colorspace attribute.
    /// </summary>
    private string UpdateColorWellControlColor()
    {
        var value = _dirtyValue ? _value : GetAttributeValue("value");
        var color = value is null ? null : CssValueComputer.ParseCssColorValue(value);
        return SerializeColorWellControlColor(color is null ? null : CssColorConversion.FromColor(color));
    }

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#serialize-a-color-well-control-color</summary>
    private string SerializeColorWellControlColor(CssColorComponents? color)
    {
        var black = new CssColorComponents("srgb", [0, 0, 0], 1);
        var hasAlpha = HasAttributeWithLocalName("alpha");
        if (ColorSpace != "display-p3")
        {
            var srgb = CssColorConversion.Convert(color ?? black, "srgb");

            // The components are rounded to 8 bits (towards +∞)
            static double Round(double? value) => Math.Clamp(Math.Floor((Math.Round((value ?? 0) * 255, 6, MidpointRounding.AwayFromZero) + 0.5)), 0, 255);
            var channels = srgb.Channels.Select(Round).ToArray();
            if (!hasAlpha)
            {
                // The HTML-compatible serialization (https://drafts.csswg.org/css-color-4/#serializing-color-values)
                return string.Create(CultureInfo.InvariantCulture, $"#{(int)channels[0]:x2}{(int)channels[1]:x2}{(int)channels[2]:x2}");
            }

            return CssColor.Create("srgb", [.. channels.Select(c => (double?)(c / 255))], Round(srgb.Alpha) / 255).Serialize();
        }

        var p3 = CssColorConversion.Convert(color ?? black, "display-p3");
        return CssColor.Create("display-p3", [.. p3.Channels.Select(c => (double?)(c ?? 0))], hasAlpha ? p3.Alpha ?? 0 : 1).Serialize();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#dom-input-checked</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/input.html#dom-input-checked"/>
    [IdlMember("checked", "https://html.spec.whatwg.org/multipage/input.html#dom-input-checked")]
    public bool Checked
    {
        get => _checkedness;
        set
        {
            _dirtyCheckedness = true;
            SetCheckedness(value);
        }
    }

    private void SetCheckedness(bool value)
    {
        _checkedness = value;
        NodeDocument.IncrementElementStateVersion();
        if (value && _type == InputType.Radio)
        {
            UncheckOtherRadiosInGroup();
        }
    }

    /// <summary>Gets or sets a value indicating whether the checkbox is in the indeterminate state.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/input.html#dom-input-indeterminate"/>
    [IdlMember("indeterminate", "https://html.spec.whatwg.org/multipage/input.html#dom-input-indeterminate")]
    public bool Indeterminate
    {
        get => _indeterminate;
        set
        {
            _indeterminate = value;
            NodeDocument.IncrementElementStateVersion();
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/input.html#radio-button-group: the other elements of the radio button group of
    /// the element, in tree order (with the element itself when <paramref name="includeSelf"/> is set); empty when the element
    /// is not a radio button or has no name.
    /// </summary>
    internal List<HtmlInputElement> GetRadioButtonGroup(bool includeSelf = false)
    {
        var result = new List<HtmlInputElement>();
        var name = GetAttributeValue("name");
        if (_type != InputType.Radio || string.IsNullOrEmpty(name))
            return result;

        var form = FormOwner;
        var root = GetRoot();
        for (var node = root is Element rootElement ? rootElement : root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is HtmlInputElement { _type: InputType.Radio } radio && (includeSelf || !ReferenceEquals(radio, this)) && radio.GetAttributeValue("name") == name && ReferenceEquals(radio.FormOwner, form))
            {
                result.Add(radio);
            }
        }

        return result;
    }

    private void UncheckOtherRadiosInGroup()
    {
        foreach (var radio in GetRadioButtonGroup())
        {
            radio._checkedness = false;
        }
    }

    /// <summary>The selected files of a file input (https://html.spec.whatwg.org/multipage/input.html#concept-input-type-file-selected).</summary>
    internal IReadOnlyList<File> SelectedFiles => _selectedFiles;

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#dom-input-files</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/input.html#dom-input-files"/>
    [IdlMember("files", "https://html.spec.whatwg.org/multipage/input.html#dom-input-files")]
    public FileList? Files
    {
        get => _type == InputType.File ? _fileList ??= new FileList(_selectedFiles) : null;
        set
        {
            if (_type != InputType.File || value is null)
                return;

            _selectedFiles.Clear();
            _selectedFiles.AddRange(value);
            _fileList = value;
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#dom-input-list</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/input.html#dom-input-list"/>
    [IdlMember("list", "https://html.spec.whatwg.org/multipage/input.html#dom-input-list")]
    public HtmlDataListElement? List
    {
        get
        {
            if (_type is InputType.Hidden or InputType.Password or InputType.Checkbox or InputType.Radio or InputType.File or InputType.Submit or InputType.Image or InputType.Reset or InputType.Button)
                return null;

            if (GetAttributeValue("list") is not { } id)
                return null;

            var root = GetRoot();
            for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
            {
                if (node is Element element && element.IdValue == id)
                    return element as HtmlDataListElement;
            }

            return null;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Numbers and dates
    // ----------------------------------------------------------------------------------------------------------------

    private bool ValueAsNumberApplies => _type is InputType.Date or InputType.Month or InputType.Week or InputType.Time or InputType.DateTimeLocal or InputType.Number or InputType.Range;

    private bool ValueAsDateApplies => _type is InputType.Date or InputType.Month or InputType.Week or InputType.Time;

    /// <summary>The algorithm to convert a string to a number of the type (https://html.spec.whatwg.org/multipage/input.html#concept-input-value-string-number).</summary>
    internal double? ConvertStringToNumber(string value)
    {
        switch (_type)
        {
            case InputType.Date:
                return Microsyntaxes.TryParseDateString(value, out var year, out var month, out var day) ? Microsyntaxes.DateToMilliseconds(year, month, day) : null;
            case InputType.Month:
                return Microsyntaxes.TryParseMonthString(value, out year, out month) ? ((year - 1970) * 12) + month - 1 : null;
            case InputType.Week:
                return Microsyntaxes.TryParseWeekString(value, out year, out var week) ? Microsyntaxes.WeekToMilliseconds(year, week) : null;
            case InputType.Time:
                return Microsyntaxes.TryParseTimeString(value, out var hour, out var minute, out var second) ? Microsyntaxes.TimeToMilliseconds(hour, minute, second) : null;
            case InputType.DateTimeLocal:
                return Microsyntaxes.TryParseLocalDateAndTimeString(value, out var milliseconds) ? milliseconds : null;
            case InputType.Number or InputType.Range:
                return Microsyntaxes.TryParseFloat(value, out var number) ? number : null;
            default:
                return null;
        }
    }

    /// <summary>The algorithm to convert a number to a string of the type (https://html.spec.whatwg.org/multipage/input.html#concept-input-value-number-string).</summary>
    private string ConvertNumberToString(double value)
    {
        switch (_type)
        {
            case InputType.Date:
            {
                var (year, month, day) = Microsyntaxes.DateFromDays((long)Math.Floor(value / MillisecondsPerDay));
                return year > 0 ? Microsyntaxes.FormatDate(year, month, day) : "";
            }

            case InputType.Month:
            {
                var months = (long)Math.Floor(value);
                var year = 1970 + (int)Math.Floor(months / 12.0);
                var month = (int)(((months % 12) + 12) % 12) + 1;
                return year > 0 ? Microsyntaxes.FormatMonth(year, month) : "";
            }

            case InputType.Week:
            {
                var (year, week) = Microsyntaxes.WeekFromMilliseconds(value);
                return year > 0 ? Microsyntaxes.FormatWeek(year, week) : "";
            }

            case InputType.Time:
                return Microsyntaxes.FormatTime(((value % MillisecondsPerDay) + MillisecondsPerDay) % MillisecondsPerDay);
            case InputType.DateTimeLocal:
                return Microsyntaxes.FormatLocalDateAndTime(value);
            default:
                return Microsyntaxes.ToBestFloatingPointRepresentation(value);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#dom-input-valueasnumber</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/input.html#dom-input-valueasnumber"/>
    [IdlMember("valueAsNumber", "https://html.spec.whatwg.org/multipage/input.html#dom-input-valueasnumber")]
    public double ValueAsNumber
    {
        get => ValueAsNumberApplies ? ConvertStringToNumber(_value) ?? double.NaN : double.NaN;
        set
        {
            if (double.IsInfinity(value))
                throw new TypeErrorException("The value must be finite");

            if (!ValueAsNumberApplies)
                throw DomException.InvalidState("valueAsNumber does not apply to this input type");

            Value = double.IsNaN(value) ? "" : ConvertNumberToString(value);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#dom-input-valueasdate</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/input.html#dom-input-valueasdate"/>
    [IdlMember("valueAsDate", "https://html.spec.whatwg.org/multipage/input.html#dom-input-valueasdate")]
    public DateTime? ValueAsDate
    {
        get
        {
            if (!ValueAsDateApplies || ConvertStringToNumber(_value) is not { } number)
                return null;

            if (_type == InputType.Month)
            {
                var months = (long)number;
                number = Microsyntaxes.DateToMilliseconds(1970 + (int)Math.Floor(months / 12.0), (int)(((months % 12) + 12) % 12) + 1, 1);
            }

            return DateTime.UnixEpoch.AddMilliseconds(number);
        }
        set
        {
            if (!ValueAsDateApplies)
                throw DomException.InvalidState("valueAsDate does not apply to this input type");

            if (value is null)
            {
                Value = "";
                return;
            }

            var milliseconds = (value.Value.ToUniversalTime() - DateTime.UnixEpoch).TotalMilliseconds;
            if (_type == InputType.Month)
            {
                var (year, month, _) = Microsyntaxes.DateFromDays((long)Math.Floor(milliseconds / MillisecondsPerDay));
                Value = year > 0 ? Microsyntaxes.FormatMonth(year, month) : "";
                return;
            }

            Value = ConvertNumberToString(milliseconds);
        }
    }

    /// <summary>The value to set when valueAsDate is a date with the NaN time value.</summary>
    internal void SetValueAsInvalidDate()
    {
        if (!ValueAsDateApplies)
            throw DomException.InvalidState("valueAsDate does not apply to this input type");

        Value = "";
    }

    private double? GetMinimum() => GetAttributeValue("min") is { } min && ConvertStringToNumber(min) is { } value ? value : _type == InputType.Range ? 0 : null;

    private double? GetMaximum() => GetAttributeValue("max") is { } max && ConvertStringToNumber(max) is { } value ? value : _type == InputType.Range ? 100 : null;

    private bool HasPeriodicDomain => _type == InputType.Time;

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#have-range-limitations</summary>
    internal bool HasRangeLimitations => ValueAsNumberApplies && (GetMinimum() is not null || GetMaximum() is not null);

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#concept-input-step</summary>
    private double? GetAllowedValueStep()
    {
        var (defaultStep, scale) = _type switch
        {
            InputType.Date => (1.0, MillisecondsPerDay),
            InputType.Month => (1.0, 1.0),
            InputType.Week => (1.0, 604_800_000.0),
            InputType.Time or InputType.DateTimeLocal => (60.0, 1000.0),
            InputType.Number or InputType.Range => (1.0, 1.0),
            _ => (0.0, 0.0),
        };

        if (scale == 0)
            return null;

        var step = GetAttributeValue("step");
        if (step is null)
            return defaultStep * scale;

        if (InfraAscii.EqualsIgnoreAsciiCase(step, "any"))
            return null;

        if (!Microsyntaxes.TryParseFloat(step, out var parsed) || parsed <= 0)
            return defaultStep * scale;

        // Dates, months and weeks: the step is an integer
        if (_type is InputType.Date or InputType.Month or InputType.Week)
        {
            parsed = Math.Max(1, Math.Round(parsed, MidpointRounding.AwayFromZero));
        }

        return parsed * scale;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#concept-input-min-zero</summary>
    private double GetStepBase()
    {
        if (GetAttributeValue("min") is { } min && ConvertStringToNumber(min) is { } minValue)
            return minValue;

        if (GetAttributeValue("value") is { } value && ConvertStringToNumber(value) is { } valueNumber)
            return valueNumber;

        return _type == InputType.Week ? -259_200_000 : 0;
    }

    /// <summary>
    /// Rounds a value computed from the step base and the step to their number of decimal places: as the browsers, which
    /// compute with decimal numbers, the floating-point error of the arithmetic is removed (e.g. 1.2 - 0.3 is 0.9, not 0.8999999999999999).
    /// </summary>
    private static double RoundToStepPrecision(double value, double stepBase, double step)
    {
        if (GetDecimalPlaces(stepBase) is { } stepBasePlaces && GetDecimalPlaces(step) is { } stepPlaces)
        {
            var digits = Math.Max(stepBasePlaces, stepPlaces);
            if (digits <= 15)
                return Math.Round(value, digits, MidpointRounding.AwayFromZero);
        }

        return value;

        // The number of decimal places of the value rounded to 15 significant digits, or null when it is not representable
        static int? GetDecimalPlaces(double value)
        {
            if (value == 0)
                return 0;

            var magnitude = Math.Abs(value);
            if (magnitude is < 1e-15 or >= 1e27)
                return null;

            // Dividing by 1 with trailing zeros removes the trailing zeros of the decimal
            return (ToDecimalWith15SignificantDigits(value) / 1.000000000000000000000000000000000m).Scale;
        }
    }

    /// <summary>
    /// Converts a double to a decimal rounded to 15 significant digits, so that the decimal fractions such as 0.1 are exact.
    /// The explicit conversion only rounds this way up to .NET 10: .NET 11 converts the double exactly.
    /// </summary>
    private static decimal ToDecimalWith15SignificantDigits(double value)
    {
        Span<char> buffer = stackalloc char[32];
        _ = value.TryFormat(buffer, out var length, "E14", CultureInfo.InvariantCulture);
        return decimal.Parse(buffer[..length], NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>Whether <paramref name="value"/> subtracted from <paramref name="stepBase"/> is an integral multiple of <paramref name="step"/>.</summary>
    private static bool IsIntegralMultiple(double value, double stepBase, double step)
    {
        // As the browsers, compute the remainder with decimal numbers (the conversion rounds the doubles to 15 significant digits,
        // so that the decimal fractions such as 0.1 are exact), and accept the errors that IEEE 754 single precision cannot represent
        if (TryConvertToDecimal(value, out var decimalValue) && TryConvertToDecimal(stepBase, out var decimalStepBase) && TryConvertToDecimal(step, out var decimalStep) && decimalStep != 0)
        {
            try
            {
                var remainder = Math.Abs((decimalValue - decimalStepBase) % decimalStep);
                var acceptableError = decimalStep / (1 << 24);
                return remainder <= acceptableError || remainder >= decimalStep - acceptableError;
            }
            catch (OverflowException)
            {
            }
        }

        var ratio = (value - stepBase) / step;
        return Math.Abs(ratio - Math.Round(ratio, MidpointRounding.ToEven)) < 1e-9 * Math.Max(1, Math.Abs(ratio));

        static bool TryConvertToDecimal(double value, out decimal result)
        {
            if (Math.Abs(value) < (double)decimal.MaxValue)
            {
                result = ToDecimalWith15SignificantDigits(value);
                return true;
            }

            result = 0;
            return false;
        }
    }

    /// <summary>
    /// Increases the value by the given number of steps (one by default) of the <c>step</c> attribute.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/input.html#dom-input-stepup"/>
    [IdlMember("stepUp", "https://html.spec.whatwg.org/multipage/input.html#dom-input-stepup")]
    public void StepUp(int n = 1) => StepValue(n, up: true);

    /// <summary>
    /// Decreases the value by the given number of steps (one by default) of the <c>step</c> attribute.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/input.html#dom-input-stepdown"/>
    [IdlMember("stepDown", "https://html.spec.whatwg.org/multipage/input.html#dom-input-stepdown")]
    public void StepDown(int n = 1) => StepValue(n, up: false);

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#dom-input-stepup</summary>
    private void StepValue(int n, bool up)
    {
        if (!ValueAsNumberApplies)
            throw DomException.InvalidState("stepUp and stepDown do not apply to this input type");

        if (GetAllowedValueStep() is null)
            throw DomException.InvalidState("The element has no allowed value step");

        if (TryGetSteppedValue(n, up, out var value))
        {
            Value = value;
        }
    }

    /// <summary>The value after <paramref name="n"/> steps up or down (https://html.spec.whatwg.org/multipage/input.html#dom-input-stepup), when the value can be stepped.</summary>
    private bool TryGetSteppedValue(int n, bool up, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? steppedValue)
    {
        steppedValue = null;
        if (!ValueAsNumberApplies || GetAllowedValueStep() is not { } step)
            return false;

        var minimum = GetMinimum();
        var maximum = GetMaximum();
        if (minimum > maximum)
            return false;

        var stepBase = GetStepBase();
        if (minimum is { } min && maximum is { } max)
        {
            var firstValid = stepBase + (Math.Ceiling((min - stepBase) / step) * step);
            if (firstValid > max)
                return false;
        }

        var currentValue = ConvertStringToNumber(_value);
        var value = currentValue ?? 0;
        var valueBeforeStepping = value;
        if (!IsIntegralMultiple(value, stepBase, step))
        {
            value = up
                ? stepBase + (Math.Ceiling((value - stepBase) / step) * step)
                : stepBase + (Math.Floor((value - stepBase) / step) * step);
        }
        else
        {
            var delta = step * n;
            value += up ? delta : -delta;
        }

        if (minimum is { } minimumValue && value < minimumValue)
        {
            value = stepBase + (Math.Ceiling((minimumValue - stepBase) / step) * step);
        }

        if (maximum is { } maximumValue && value > maximumValue)
        {
            value = stepBase + (Math.Floor((maximumValue - stepBase) / step) * step);
        }

        // As in browsers, a control without a value is stepped to the minimum even when it is greater than zero
        if (currentValue is not null && ((!up && value > valueBeforeStepping) || (up && value < valueBeforeStepping)))
            return false;

        steppedValue = ConvertNumberToString(RoundToStepPrecision(value, stepBase, step));
        return true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#dom-input-showpicker</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/input.html#dom-input-showpicker"/>
    [IdlMember("showPicker", "https://html.spec.whatwg.org/multipage/input.html#dom-input-showpicker")]
    public void ShowPicker()
    {
        if (!IsMutable)
            throw DomException.InvalidState("The element is not mutable");

        // The pickers of the file and color inputs are not guarded by an origin check (their activation behavior shows them)
        if (_type is not (InputType.File or InputType.Color) && !OriginRecord.IsSameOrigin(NodeDocument.Origin, NodeDocument.TopLevelOrigin))
            throw new DomException("showPicker() cannot be called in a cross-origin iframe", "SecurityError");

        // There is no transient user activation
        throw new DomException("showPicker() requires a user activation", "NotAllowedError");
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Constraint validation
    // ----------------------------------------------------------------------------------------------------------------

    private bool ReadonlyApplies => _type is InputType.Text or InputType.Search or InputType.Url or InputType.Tel or InputType.Email or InputType.Password
        or InputType.Date or InputType.Month or InputType.Week or InputType.Time or InputType.DateTimeLocal or InputType.Number;

    private bool RequiredApplies => ReadonlyApplies || _type is InputType.Checkbox or InputType.Radio or InputType.File;

    private bool PatternApplies => _type is InputType.Text or InputType.Search or InputType.Url or InputType.Tel or InputType.Email or InputType.Password;

    /// <summary>Whether the dirname attribute applies to the type of the input (https://html.spec.whatwg.org/multipage/input.html#input-type-attr-summary).</summary>
    internal bool DirNameApplies => _type is InputType.Hidden or InputType.Text or InputType.Search or InputType.Url or InputType.Tel or InputType.Email or InputType.Password
        or InputType.Submit;

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#concept-fe-mutable</summary>
    internal bool IsMutable => !Css.Selectors.HtmlElementStates.IsActuallyDisabled(this) && !(ReadonlyApplies && HasAttributeWithLocalName("readonly"));

    // The readonly attribute bars the element from constraint validation even when it does not apply
    private protected override bool IsBarredFromConstraintValidation =>
        _type is InputType.Hidden or InputType.Reset or InputType.Button || HasAttributeWithLocalName("readonly");

    private protected override ValidityFlags ComputeValidity()
    {
        var flags = ValidityFlags.None;

        // valueMissing
        // Checkboxes, radio buttons and file inputs are missing even when they are not mutable
        if (RequiredApplies && HasAttributeWithLocalName("required") && (IsMutable || _type is InputType.Checkbox or InputType.Radio or InputType.File))
        {
            var missing = _type switch
            {
                InputType.Checkbox => !_checkedness,
                // As the browsers, a radio button without a name is not in a radio button group, so it is never missing
                InputType.Radio => !_checkedness && !string.IsNullOrEmpty(GetAttributeValue("name")) && !GetRadioButtonGroup().Exists(r => r._checkedness),
                InputType.File => _selectedFiles.Count == 0,
                _ => _value.Length == 0,
            };

            if (missing)
            {
                flags |= ValidityFlags.ValueMissing;
            }
        }
        else if (_type == InputType.Radio && !_checkedness && GetRadioButtonGroup().Exists(r => r.HasAttributeWithLocalName("required")) && !GetRadioButtonGroup().Exists(r => r._checkedness))
        {
            // Any required radio button makes the whole group required
            flags |= ValidityFlags.ValueMissing;
        }

        if (_value.Length == 0)
            return flags;

        // typeMismatch
        if (_type == InputType.Email)
        {
            var valid = HasAttributeWithLocalName("multiple") ? _value.Split(',').All(IsValidEmail) : IsValidEmail(_value);
            if (!valid)
            {
                flags |= ValidityFlags.TypeMismatch;
            }
        }
        else if (_type == InputType.Url && !IsValidAbsoluteUrl(_value))
        {
            flags |= ValidityFlags.TypeMismatch;
        }

        // patternMismatch
        if (PatternApplies && GetAttributeValue("pattern") is { } pattern && GetCompiledPattern(pattern) is { } regex)
        {
            var values = _type == InputType.Email && HasAttributeWithLocalName("multiple") ? _value.Split(',') : [_value];
            if (!values.All(value => IsPatternMatch(regex, value)))
            {
                flags |= ValidityFlags.PatternMismatch;
            }
        }

        // rangeUnderflow, rangeOverflow, stepMismatch
        if (ValueAsNumberApplies && ConvertStringToNumber(_value) is { } number)
        {
            var minimum = GetMinimum();
            var maximum = GetMaximum();
            var reversed = HasPeriodicDomain && minimum is not null && maximum is not null && maximum < minimum;
            if (reversed)
            {
                if (number > maximum && number < minimum)
                {
                    flags |= ValidityFlags.RangeUnderflow | ValidityFlags.RangeOverflow;
                }
            }
            else
            {
                if (number < minimum)
                {
                    flags |= ValidityFlags.RangeUnderflow;
                }

                if (number > maximum)
                {
                    flags |= ValidityFlags.RangeOverflow;
                }
            }

            if (GetAllowedValueStep() is { } step && !IsIntegralMultiple(number, GetStepBase(), step))
            {
                flags |= ValidityFlags.StepMismatch;
            }
        }

        return flags;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#valid-e-mail-address</summary>
    private static bool IsValidEmail(string value) => EmailRegex().IsMatch(value);

    private static bool IsValidAbsoluteUrl(string value) => Urls.UrlParser.Parse(value, baseUrl: null) is not null;

    /// <summary>
    /// Returns the compiled pattern regular expression of the pattern attribute, compiled again only when the attribute
    /// changes: the validity is computed often (e.g. by the :valid and :invalid pseudo-classes when the styles are computed).
    /// </summary>
    private Regex? GetCompiledPattern(string pattern)
    {
        if (!ReferenceEquals(pattern, _compiledPatternSource))
        {
            _compiledPattern = CompilePattern(pattern);
            _compiledPatternSource = pattern;
        }

        return _compiledPattern;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#compiled-pattern-regular-expression</summary>
    private static Regex? CompilePattern(string pattern)
    {
        // The pattern must be valid on its own (e.g. "a)(b" is invalid even though "^(?:a)(b)$" is valid)
        if (EcmaScriptRegex.Translate(pattern) is null)
            return null;

        // The pattern is compiled with the v flag and matches the entire value
        return EcmaScriptRegex.Create("^(?:" + pattern + ")$", ignoreCase: false, TimeSpan.FromSeconds(1));
    }

    private static bool IsPatternMatch(Regex regex, string value)
    {
        try
        {
            return regex.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            // As the browsers when the backtracking limit is exceeded (catastrophic backtracking), the value does not match
            return false;
        }
    }

    [GeneratedRegex(@"^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EmailRegex();

    // ----------------------------------------------------------------------------------------------------------------
    // Activation behavior
    // ----------------------------------------------------------------------------------------------------------------

    // As in browsers, a hidden input has no activation behavior (e.g. a click on it activates its label ancestor)
    internal override bool HasActivationBehavior => _type != InputType.Hidden;

    internal override bool HasLegacyPreActivationBehavior => _type is InputType.Checkbox or InputType.Radio;

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#the-input-element:legacy-pre-activation-behavior</summary>
    internal override void LegacyPreActivationBehavior()
    {
        _preActivationType = _type;
        if (_type == InputType.Checkbox)
        {
            _preActivationCheckedness = _checkedness;
            _preActivationIndeterminate = _indeterminate;
            _indeterminate = false;
            SetCheckedness(!_checkedness);
        }
        else if (_type == InputType.Radio)
        {
            _preActivationCheckedness = _checkedness;
            _preActivationCheckedRadio = _checkedness ? this : GetRadioButtonGroup().Find(r => r._checkedness);
            SetCheckedness(true);
        }
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/input.html#the-input-element:legacy-canceled-activation-behavior: the changes of
    /// the legacy-pre-activation behavior are reverted according to the type the element had then, as in the browsers, even
    /// when a listener changed the type (WPT dom/events/Event-dispatch-click.tentative.html).
    /// </summary>
    internal override void LegacyCanceledActivationBehavior()
    {
        var preActivationType = _preActivationType;
        _preActivationType = null;
        if (preActivationType == InputType.Checkbox)
        {
            _checkedness = _preActivationCheckedness;
            _indeterminate = _preActivationIndeterminate;
            NodeDocument.IncrementElementStateVersion();
        }
        else if (preActivationType == InputType.Radio)
        {
            // The previously checked element must still be in the radio button group of this element, or in its own group
            // when this element is no longer a radio button
            var previous = _preActivationCheckedRadio;
            _preActivationCheckedRadio = null;
            if (previous is not null && (ReferenceEquals(previous, this) || (_type == InputType.Radio ? GetRadioButtonGroup().Contains(previous) : previous._type == InputType.Radio)))
            {
                previous.SetCheckedness(true);
                if (!ReferenceEquals(previous, this))
                {
                    _checkedness = false;
                }
            }
            else
            {
                _checkedness = false;
            }

            NodeDocument.IncrementElementStateVersion();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/input.html#the-input-element:activation-behaviour</summary>
    internal override void ActivationBehavior(Event @event)
    {
        if (!IsMutable && _type is not (InputType.Checkbox or InputType.Radio))
            return;

        switch (_type)
        {
            case InputType.Checkbox or InputType.Radio:
                // https://html.spec.whatwg.org/multipage/input.html#checkbox-state-(type=checkbox):input-activation-behavior
                if (!IsConnected)
                    return;

                // As in browsers, a click on a radio button that was already checked does not fire input and change events
                if (_type == InputType.Radio && _preActivationType == InputType.Radio && _preActivationCheckedness)
                    return;

                // As in browsers, a change of the checkedness by the user is committed: the user validity becomes true
                if (@event.IsTrusted)
                {
                    UserValidity = true;
                }

                FireEvent(new Event("input", new EventInit { Bubbles = true, Composed = true }));
                FireEvent(new Event("change", new EventInit { Bubbles = true }));
                break;

            case InputType.Submit or InputType.Image:
                if (FormOwner is { } form && NodeDocument.IsFullyActive)
                {
                    if (_type is InputType.Image)
                    {
                        SelectedCoordinate = @event is MouseEvent { IsPositionless: false } mouseEvent && MouseEventGeometry.GetOffsetPosition(mouseEvent, this) is { } offset
                            ? ((int)Math.Round(offset.X, MidpointRounding.AwayFromZero), (int)Math.Round(offset.Y, MidpointRounding.AwayFromZero))
                            : (0, 0);
                    }

                    form.SubmitForm(this, submittedFromSubmitMethod: false);
                }

                break;

            case InputType.Reset:
                if (FormOwner is { } resetForm && NodeDocument.IsFullyActive)
                {
                    resetForm.ResetForm();
                }

                break;
        }

        if (FormOwner is not null && _type != InputType.Button)
            return;

        RunPopoverTargetAttributeActivationBehavior(this, @event.Target);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Selection (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#textFieldSelection)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Whether the type of the input allows text entry (the priority candidates of the scroll anchoring, https://drafts.csswg.org/css-scroll-anchoring/#anchor-priority-candidates).</summary>
    internal bool AllowsTextEntry => SelectionApplies(_type) || _type is InputType.Email or InputType.Number;

    private static bool SelectionApplies(InputType type) => type is InputType.Text or InputType.Search or InputType.Url or InputType.Tel or InputType.Password;

    private bool SelectApplies => SelectionApplies(_type) || _type is InputType.Email or InputType.Date or InputType.Month or InputType.Week or InputType.Time or InputType.DateTimeLocal or InputType.Number or InputType.Color or InputType.File;

    bool IEditableTextControl.IsTextEditor => AllowsTextEntry;

    bool IEditableTextControl.IsEditMutable => IsMutable;

    bool IEditableTextControl.IsMultiline => false;

    bool IEditableTextControl.IsPassword => _type == InputType.Password;

    string IEditableTextControl.EditValue => _value;

    int IEditableTextControl.EditSelectionStart => Math.Min(_selectionStart, _value.Length);

    int IEditableTextControl.EditSelectionEnd => Math.Min(_selectionEnd, _value.Length);

    string IEditableTextControl.EditSelectionDirection => _selectionDirection;

    int? IEditableTextControl.EditMaxLength => _type != InputType.Number && GetAttributeValue("maxlength") is { } maxLength && Microsyntaxes.TryParseNonNegativeInteger(maxLength, out var value) ? (int)Math.Min(value, int.MaxValue) : null;

    string IEditableTextControl.ApplyUserEdit(string value, int selectionStart, int selectionEnd, string direction)
    {
        // The sanitization clamps the selection to the new value: the selection before the edit is compared
        var (oldStart, oldEnd) = (_selectionStart, _selectionEnd);
        _value = value;
        _dirtyValue = true;
        SanitizeValue();
        NodeDocument.IncrementElementStateVersion();
        var length = _value.Length;
        (selectionStart, selectionEnd) = (Math.Min(selectionStart, length), Math.Min(selectionEnd, length));
        TextControlSelection.SetUserSelection(Selection, selectionStart, selectionEnd, direction, changed: oldStart != selectionStart || oldEnd != selectionEnd);
        return _value;
    }

    void IEditableTextControl.SetUserSelection(int selectionStart, int selectionEnd, string direction) => TextControlSelection.SetUserSelection(Selection, selectionStart, selectionEnd, direction);

    private TextSelection Selection => new(this);

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-select</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-select"/>
    [IdlMember("select", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-select")]
    public void Select()
    {
        if (!SelectApplies)
            return;

        if (SelectionApplies(_type))
        {
            Selection.SetRange(0, _value.Length, "none");
        }
    }

    /// <summary>
    /// Gets or sets the index of the start of the text selection, or null when the selection does not apply to the
    /// input type.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-selectionstart"/>
    [IdlMember("selectionStart", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-selectionstart")]
    public uint? SelectionStart
    {
        get => SelectionApplies(_type) ? (uint)_selectionStart : null;
        set
        {
            if (!SelectionApplies(_type))
                throw DomException.InvalidState("The selection does not apply to this input type");

            Selection.SetStart(value ?? 0);
        }
    }

    /// <summary>
    /// Gets or sets the index of the end of the text selection, or null when the selection does not apply to the input
    /// type.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-selectionend"/>
    [IdlMember("selectionEnd", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-selectionend")]
    public uint? SelectionEnd
    {
        get => SelectionApplies(_type) ? (uint)_selectionEnd : null;
        set
        {
            if (!SelectionApplies(_type))
                throw DomException.InvalidState("The selection does not apply to this input type");

            Selection.SetEnd(value ?? 0);
        }
    }

    /// <summary>
    /// Gets or sets the direction of the text selection (<c>forward</c>, <c>backward</c> or <c>none</c>), or null when
    /// the selection does not apply to the input type.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-selectiondirection"/>
    [IdlMember("selectionDirection", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-selectiondirection")]
    public string? SelectionDirection
    {
        get => SelectionApplies(_type) ? _selectionDirection : null;
        set
        {
            if (!SelectionApplies(_type))
                throw DomException.InvalidState("The selection does not apply to this input type");

            Selection.SetRange(_selectionStart, _selectionEnd, value);
        }
    }

    /// <summary>Replaces the selected text with the given text.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-setrangetext"/>
    [IdlMember("setRangeText", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-setrangetext")]
    public void SetRangeText(string replacement)
    {
        if (!SelectionApplies(_type))
            throw DomException.InvalidState("The selection does not apply to this input type");

        Selection.SetRangeText(replacement, null, null, "preserve");
    }

    /// <summary>
    /// Replaces the text between the start and end indexes with the given text and updates the selection according to
    /// the selection mode.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-setrangetext"/>
    [IdlMember("setRangeText", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-setrangetext")]
    public void SetRangeText(string replacement, uint start, uint end, string selectionMode = "preserve")
    {
        if (!SelectionApplies(_type))
            throw DomException.InvalidState("The selection does not apply to this input type");

        Selection.SetRangeText(replacement, start, end, selectionMode);
    }

    /// <summary>Selects the text between the start and end indexes, with the given direction.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-setselectionrange"/>
    [IdlMember("setSelectionRange", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-setselectionrange")]
    public void SetSelectionRange(uint start, uint end, string? direction = null)
    {
        if (!SelectionApplies(_type))
            throw DomException.InvalidState("The selection does not apply to this input type");

        Selection.SetRange(start, end, direction);
    }

    /// <summary>The selection state of the text control, shared with the textarea element.</summary>
    private readonly struct TextSelection(HtmlInputElement input) : ITextControl
    {
        public string Text
        {
            get => input._value;
            set
            {
                input._value = value;
                input._dirtyValue = true;
                input.SanitizeValue();
                TextControlEditing.ClearHistory(input);
            }
        }

        public HtmlElement Element => input;

        public int Start { get => input._selectionStart; set => input._selectionStart = value; }

        public int End { get => input._selectionEnd; set => input._selectionEnd = value; }

        public string Direction { get => input._selectionDirection; set => input._selectionDirection = value; }

        public void SetRange(long start, long end, string? direction) => TextControlSelection.SetSelectionRange(this, start, end, direction);

        public void SetStart(long start) => TextControlSelection.SetSelectionRange(this, start, Math.Max(start, End), Direction);

        public void SetEnd(long end) => TextControlSelection.SetSelectionRange(this, Start, end, Direction);

        public void SetRangeText(string replacement, long? start, long? end, string mode) => TextControlSelection.SetRangeText(this, replacement, start, end, mode);
    }
}

/// <summary>A text control whose selection can be changed (input and textarea elements).</summary>
internal interface ITextControl
{
    string Text { get; set; }

    HtmlElement Element { get; }

    int Start { get; set; }

    int End { get; set; }

    string Direction { get; set; }
}

/// <summary>The selection APIs of text controls (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#textFieldSelection).</summary>
internal static class TextControlSelection
{
    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#set-the-selection-range</summary>
    public static void SetSelectionRange<T>(T control, long start, long end, string? direction)
        where T : ITextControl
    {
        var length = control.Text.Length;
        var newEnd = (int)Math.Clamp(end, 0, length);
        var newStart = (int)Math.Clamp(start, 0, newEnd);
        var newDirection = direction is "forward" or "backward" ? direction : "none";
        var changed = newStart != control.Start || newEnd != control.End || newDirection != control.Direction;
        control.Start = newStart;
        control.End = newEnd;
        control.Direction = newDirection;
        if (changed)
        {
            // The selection is highlighted in the inner editor of the control, where its caret is painted
            control.Element.NodeDocument.OnCaretChanged();
            QueueSelectEvent(control.Element);

            // https://w3c.github.io/selection-api/#selectionchange-event
            Selection.ScheduleSelectionChangeEvent(control.Element);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-textarea/input-setrangetext</summary>
    public static void SetRangeText<T>(T control, string replacement, long? start, long? end, string mode)
        where T : ITextControl
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var selectionStart = start ?? control.Start;
        var selectionEnd = end ?? control.End;
        if (selectionStart > selectionEnd)
            throw DomException.IndexSize("The start is greater than the end");

        var length = control.Text.Length;
        selectionStart = Math.Min(selectionStart, length);
        selectionEnd = Math.Min(selectionEnd, length);
        var oldStart = control.Start;
        var oldEnd = control.End;

        control.Text = control.Text[..(int)selectionStart] + replacement + control.Text[(int)selectionEnd..];

        var newLength = replacement.Length;
        var newEnd = selectionStart + newLength;
        long resultStart;
        long resultEnd;
        switch (mode)
        {
            case "select":
                resultStart = selectionStart;
                resultEnd = newEnd;
                break;
            case "start":
                resultStart = resultEnd = selectionStart;
                break;
            case "end":
                resultStart = resultEnd = newEnd;
                break;
            default:
            {
                // preserve
                var oldLength = selectionEnd - selectionStart;
                var delta = newLength - oldLength;
                resultStart = oldStart;
                resultEnd = oldEnd;
                if (oldStart > selectionEnd)
                {
                    resultStart += delta;
                }
                else if (oldStart > selectionStart)
                {
                    resultStart = selectionStart;
                }

                if (oldEnd > selectionEnd)
                {
                    resultEnd += delta;
                }
                else if (oldEnd > selectionStart)
                {
                    resultEnd = newEnd;
                }

                break;
            }
        }

        SetSelectionRange(control, resultStart, resultEnd, null);
    }

    /// <summary>
    /// Sets the selection of a control after an edit of the user: as in the browsers, the caret that an edit moves fires
    /// selectionchange but no select event (https://w3c.github.io/selection-api/#selectionchange-event).
    /// </summary>
    public static void SetUserSelection<T>(T control, int start, int end, string direction, bool changed = false)
        where T : ITextControl
    {
        changed |= start != control.Start || end != control.End || direction != control.Direction;
        control.Start = start;
        control.End = end;
        control.Direction = direction;
        if (changed)
        {
            // The selection is highlighted in the inner editor of the control, where its caret is painted
            control.Element.NodeDocument.OnCaretChanged();
            Selection.ScheduleSelectionChangeEvent(control.Element);
        }
    }

    private static void QueueSelectEvent(HtmlElement element)
    {
        element.NodeDocument.QueueElementTask(() => element.FireEvent(new Event("select", new EventInit { Bubbles = true })));
    }
}
