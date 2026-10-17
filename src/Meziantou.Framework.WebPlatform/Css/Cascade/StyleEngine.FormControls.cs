using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

// The styles of the form controls other than the select element that depend on their appearance
// (https://drafts.csswg.org/css-forms-1/#appearance): "appearance: base" puts a control in the basic appearance state, in
// which "the user agent applies styles from the Basic Appearance User Agent Stylesheet to that control" and enables its
// pseudo-elements. The rules of that style sheet are conditional rules of the user agent style sheet, as the ones of the
// select element (see StyleEngine.Select.cs): the cascade applies them when the control has base appearance.
internal sealed partial class StyleEngine
{
    /// <summary>The name of the <c>::color-swatch</c> pseudo-element (https://drafts.csswg.org/css-forms-1/#color-swatch-pseudo).</summary>
    internal const string ColorSwatch = "color-swatch";

    /// <summary>
    /// The Basic Appearance User Agent Stylesheet of CSS Form Control Styling Level 1
    /// (https://drafts.csswg.org/css-forms-1/#basic-appearance-stylesheet), as of the editor's draft of 2026-09-17 (the
    /// revision of the pinned specification data), without the rules of the select element (they are in HTML) and of the
    /// pseudo-elements that the library does not generate (the parts of the text fields, of the number fields and of the date
    /// and time fields). The attribute values are matched ASCII case-insensitively, as in the user agent style sheet of HTML.
    /// The rule of the text inputs "must apply to textareas and textual inputs only". <c>control-value()</c> is not
    /// supported: the background of <c>::color-swatch</c> is set by the box tree builder. The selector of the hidden
    /// checkmarks misses a comma in the specification.
    /// </summary>
    private const string BaseAppearanceStyleSheet = """
        input,
        textarea,
        button,
        ::file-selector-button,
        ::color-swatch,
        meter,
        progress,
        fieldset,
        legend {
          color: inherit;
          font: inherit;
          box-sizing: border-box;
          background-color: transparent;
        }

        input:not([type=file i], [type=range i], [type=checkbox i][switch]),
        textarea,
        button,
        ::file-selector-button,
        ::slider-track {
          border: 1px solid currentColor;
          background-color: light-dark(transparent, alpha(from white / 4%));
        }

        fieldset {
          border: 1px solid currentColor;
        }

        input {
          align-content: unsafe center;
          align-items: unsafe center;
        }

        input[type=checkbox i][switch],
        input[type=range i] {
          display: inline-flex;
          position: relative;
          align-items: center;
          inline-size: 100%;
        }

        input[type=checkbox i][switch]::slider-track,
        input[type=range i]::slider-track,
        meter::slider-track,
        progress::slider-track {
          block-size: 1em;
          inline-size: 100%;
        }

        input[type=range i]::slider-track {
          block-size: 0.5em;
        }

        input[type=checkbox i][switch]::slider-track {
          border-radius: 1em;
        }

        input[type=checkbox i][switch]::slider-fill,
        input[type=range i]::slider-fill,
        meter::slider-fill,
        progress::slider-fill {
          block-size: 100%;
          background-color: currentColor;
        }

        progress:indeterminate::slider-fill {
          inline-size: 0;
        }

        input[type=range i]::slider-fill,
        input[type=checkbox i][switch]::slider-fill {
          background-color: color-mix(currentColor 25%, transparent);
          border-radius: inherit;
          block-size: 100%;
        }

        input[type=checkbox i][switch]::slider-fill {
          inline-size: 0%;
        }

        input[type=checkbox i][switch]:checked::slider-fill {
          inline-size: 100%;
        }

        input[type=checkbox i][switch]::slider-thumb,
        input[type=range i]::slider-thumb {
          border-radius: 0;
          border: none;
          background-color: currentColor;
          appearance: none;
          inline-size: 1em;
          block-size: 1em;
          border-radius: 100%;
          position: absolute;
        }

        input[type=checkbox i][switch]:checked::slider-thumb {
          inset-inline-end: 0;
        }

        input[type=checkbox i]:not([switch]),
        input[type=radio i] {
          block-size: 1em;
          inline-size: 1em;
          font-size: max(1em, 24px);
          display: inline-flex;
          align-items: center;
          justify-content: center;
        }

        input[type=checkbox i]:not([switch])::checkmark {
          content: '\2713' / '';
        }

        input[type=radio i]::checkmark {
          content: '';
        }

        input[type=checkbox i]:not([switch], :checked, :indeterminate)::checkmark,
        input[type=radio i]:not(:checked)::checkmark {
          visibility: hidden;
        }

        input[type=checkbox i]:indeterminate::checkmark {
          content: '\2500' / '';
        }

        input[type=radio i] {
          border-radius: 50%;
        }

        input[type=radio i]:checked::checkmark {
          background-color: currentColor;
          display: inline-block;
          border-radius: inherit;
          height: 100%;
          width: 100%;
        }

        button,
        ::file-selector-button,
        input:is([type=color i], [type=button i], [type=reset i], [type=submit i]) {
          border: 1px solid currentColor;
          background-color: transparent;
          color: inherit;
          padding-block: 0.25em;
          padding-inline: 0.5em;
          border-radius: 4px;
          min-block-size: calc-size(auto, max(size, 24px, 1lh));
          min-inline-size: calc-size(auto, max(size, 24px));
          box-sizing: border-box;
          display: inline-flex;
          gap: 0.5em;
          user-select: none;
          text-transform: initial;
          text-align: initial;
          text-indent: initial;
        }

        button:enabled:hover,
        input:is([type=color i], [type=button i], [type=reset i], [type=submit i]):enabled:hover,
        input[type=file i]:enabled::file-selector-button:hover {
          background-color: color-mix(currentColor 10%, transparent);
        }

        button:enabled:active,
        input:is([type=color i], [type=button i], [type=reset i], [type=submit i]):enabled:active,
        input[type=file i]:enabled::file-selector-button:active {
          background-color: color-mix(currentColor 20%, transparent);
        }

        button:disabled,
        input:is([type=color i], [type=button i], [type=reset i], [type=submit i]):disabled,
        input[type=file i]:disabled::file-selector-button {
          color: color-mix(currentColor 50%, transparent);
        }

        input[type=color i] {
          padding: 0;
          overflow: clip;
        }

        ::color-swatch {
          inline-size: stretch;
          block-size: stretch;
          color-scheme: light only;
          forced-color-adjust: none;
        }

        textarea,
        input:not([type=checkbox i], [type=radio i], [type=range i], [type=color i], [type=file i], [type=button i], [type=reset i], [type=submit i], [type=image i], [type=hidden i]) {
          border-radius: 4px;
        }
        """;

    /// <summary>
    /// The rules that the library adds for the controls with base appearance, which the specification does not have: the
    /// boxes of the parts are blocks, and a meter and a progress bar keep the inline size that HTML gives them
    /// (https://html.spec.whatwg.org/multipage/rendering.html#the-meter-element-2: 5em and 10em) as their intrinsic inline
    /// size (their track is as wide as they are: its percentage would otherwise give them no inline size), so that they
    /// stretch as the other boxes.
    /// </summary>
    private const string BaseAppearanceSupplementalStyleSheet = """
        meter, progress { contain: inline-size; }
        meter { contain-intrinsic-inline-size: 5em; }
        progress { contain-intrinsic-inline-size: 10em; }
        """;

    /// <summary>
    /// The rules of the parts of the controls with primitive appearance (<c>appearance: none</c>, see
    /// <see cref="Layout.FormControlContents"/>), which the specifications leave to the user agent: the track and the fill of
    /// a slider-like control fill its content box; the thumb of a slider is a 16px disc centered in the block axis, with its
    /// own native appearance (as in the browsers); a progress bar keeps the gray track and the green fill of the unstyled
    /// <c>::-webkit-progress-bar</c> and <c>::-webkit-progress-value</c> of Blink and WebKit.
    /// </summary>
    private const string PrimitiveAppearanceStyleSheet = """
        ::slider-track, ::slider-fill { block-size: 100%; }
        input[type=range i]::slider-thumb {
          appearance: auto;
          position: absolute;
          inset-block: 0;
          margin-block: auto;
          inline-size: 16px;
          block-size: 16px;
          border-radius: 50%;
        }
        progress::slider-track { background-color: #808080; }
        progress::slider-fill { background-color: #008000; }
        """;

    private static void AddControlRules(CascadeRuleCollector collector)
    {
        collector.SelectCondition = SelectRuleCondition.ControlNativeAppearance;
        collector.AddRules(ParseUserAgentStyleSheet(PrimitiveAppearanceStyleSheet), baseUrl: null);
        collector.SelectCondition = SelectRuleCondition.ControlBaseAppearance;
        collector.AddRules(ParseUserAgentStyleSheet(BaseAppearanceStyleSheet), baseUrl: null);
        collector.AddRules(ParseUserAgentStyleSheet(BaseAppearanceSupplementalStyleSheet), baseUrl: null);
        collector.SelectCondition = SelectRuleCondition.None;
    }

    /// <summary>
    /// Whether the form control whose style is being computed has base appearance: as for the select element, its style is
    /// first computed with the rules of the native appearance, which gives the computed value of its appearance property,
    /// then with the rules of the base appearance.
    /// </summary>
    private Element? _controlWithBaseAppearance;

    /// <summary>
    /// Whether the element is a form control that the Basic Appearance User Agent Stylesheet styles (the select element has
    /// its own rules, see <see cref="HasBaseAppearance"/>): input, textarea, button, meter, progress, fieldset and legend.
    /// </summary>
    internal static bool IsBaseAppearanceControl(Element element)
        => element.IsHtmlNamespace && element.TagId is TagId.Input or TagId.Textarea or TagId.Button or TagId.Meter or TagId.Progress or TagId.Fieldset or TagId.Legend;

    /// <summary>
    /// Whether the style of a form control other than a select element gives it base appearance
    /// (https://drafts.csswg.org/css-forms-1/#appearance): <c>appearance: base</c>.
    /// </summary>
    internal static bool HasControlBaseAppearance(ComputedStyle style)
        => style.PseudoElement is null && IsBaseAppearanceControl(style.Element) && style.GetComputedValue("appearance") == "base";

    /// <summary>Whether the subject of the cascade that is running is a form control with base appearance, or one of its pseudo-elements.</summary>
    private bool IsCascadeSubjectControlWithBaseAppearance()
    {
        if (_cascadeElement is not { } element || !IsBaseAppearanceControl(element))
            return false;

        if (_cascadePseudoElement is null)
            return ReferenceEquals(_controlWithBaseAppearance, element);

        // "These pseudo-elements [...] always inherit appearance from their originating element": the style of the
        // originating element is computed before the ones of its pseudo-elements
        return _styles.TryGetValue((element, null), out var style) && HasControlBaseAppearance(style);
    }
}
