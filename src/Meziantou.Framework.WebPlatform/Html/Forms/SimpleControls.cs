using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class HtmlLabelElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/forms.html#labeled-control</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-label-control"/>
    [IdlMember("control", "https://html.spec.whatwg.org/multipage/forms.html#dom-label-control")]
    public HtmlElement? Control
    {
        get
        {
            if (GetAttributeValue("for") is { } id)
            {
                var root = GetRoot();
                for (var node = root is Element ? root : root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
                {
                    if (node is Element element && element.IdValue == id)
                        return element is HtmlElement { IsLabelable: true } labelable ? labelable : null;
                }

                return null;
            }

            for (var node = FirstChild; node is not null; node = node.GetNextInTreeOrder(this))
            {
                if (node is HtmlElement { IsLabelable: true } labelable)
                    return labelable;
            }

            return null;
        }
    }

    // A label without labeled control does nothing when activated: it has no activation behavior, so that the activation
    // behavior of an ancestor runs (e.g. a label in a button with a popovertarget attribute), as in the browsers
    internal override bool HasActivationBehavior => Control is not null;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/forms.html#the-label-element:activation-behaviour: activating the label (or
    /// a descendant that is not interactive content) runs the synthetic click activation steps on the labeled control.
    /// </summary>
    internal override void ActivationBehavior(Event @event)
    {
        for (var node = @event.Target as Node; node is not null && !ReferenceEquals(node, this); node = node.ParentNode)
        {
            if (node is HtmlElement element && IsInteractiveContent(element))
                return;
        }

        if (Control is not { } control)
            return;

        // As in browsers, the click forwarded for a click of the user is trusted and keeps the properties of the pointer
        if (@event.IsTrusted && @event is MouseEvent mouseEvent)
        {
            // As in the browsers, a click of the user on the label focuses its labeled control
            if (!mouseEvent.IsPositionless && FocusModel.IsClickFocusable(control) && NodeDocument.BrowsingContext?.Top.Document is { } topDocument)
            {
                topDocument.LastFocusWasPointer = true;
                topDocument.LastPointerFocusDocument = control.NodeDocument;
                FocusModel.Focus(control, focusVisible: null, FocusTrigger.Click);
            }

            control.ForwardTrustedClick(mouseEvent);
        }
        else
        {
            control.Click();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#interactive-content-2</summary>
    internal static bool IsInteractiveContent(HtmlElement element) => element.TagId switch
    {
        TagId.A => element.HasAttributeWithLocalName("href"),
        TagId.Audio or TagId.Video => element.HasAttributeWithLocalName("controls"),
        TagId.Img => element.HasAttributeWithLocalName("usemap"),
        TagId.Input => element is not HtmlInputElement { InputType: InputType.Hidden },
        TagId.Button or TagId.Details or TagId.Embed or TagId.Iframe or TagId.Label or TagId.Select or TagId.Textarea => true,
        _ => false,
    };

    /// <summary>https://html.spec.whatwg.org/multipage/forms.html#dom-label-form</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/forms.html#dom-label-form"/>
    [IdlMember("form", "https://html.spec.whatwg.org/multipage/forms.html#dom-label-form")]
    public HtmlFormElement? Form => Control?.FormOwner;
}

public partial class HtmlLegendElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-legend-form</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-elements.html#dom-legend-form"/>
    [IdlMember("form", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-legend-form")]
    public HtmlFormElement? Form => ParentNode is HtmlFieldSetElement fieldset ? fieldset.FormOwner : null;
}

public partial class HtmlFieldSetElement
{
    private HtmlCollection? _elements;

    /// <summary>Gets the type of the control, always <c>fieldset</c>.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-elements.html#dom-fieldset-type"/>
    [IdlMember("type", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-fieldset-type")]
    public string Type => "fieldset";

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-fieldset-elements: the listed elements that are descendants.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-elements.html#dom-fieldset-elements"/>
    [IdlMember("elements", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-fieldset-elements")]
    public HtmlCollection Elements => _elements ??= new HtmlCollection(this, static e => e is HtmlElement { IsListed: true });
}

public partial class HtmlObjectElement
{
}

public partial class HtmlOutputElement
{
    private string? _defaultValueOverride;

    /// <summary>Gets the type of the control, always <c>output</c>.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-elements.html#dom-output-type"/>
    [IdlMember("type", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-output-type")]
    public string Type => "output";

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-output-defaultvalue</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-elements.html#dom-output-defaultvalue"/>
    [IdlMember("defaultValue", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-output-defaultvalue")]
    public string DefaultValue
    {
        get => _defaultValueOverride ?? GetDescendantTextContent();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_defaultValueOverride is null)
            {
                StringReplaceAll(value);
            }
            else
            {
                _defaultValueOverride = value;
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#dom-output-value</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-elements.html#dom-output-value"/>
    [IdlMember("value", "https://html.spec.whatwg.org/multipage/form-elements.html#dom-output-value")]
    public string Value
    {
        get => GetDescendantTextContent();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _defaultValueOverride = DefaultValue;
            StringReplaceAll(value);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#the-output-element:concept-form-reset-control</summary>
    internal override void ResetAlgorithm()
    {
        StringReplaceAll(DefaultValue);
        _defaultValueOverride = null;
    }
}

public partial class HtmlButtonElement
{
    private protected override bool IsBarredFromConstraintValidation => !IsButtonSubmitButton;

    internal override bool HasActivationBehavior => true;

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#the-button-element:activation-behaviour</summary>
    internal override void ActivationBehavior(Event @event)
    {
        if (Css.Selectors.HtmlElementStates.IsActuallyDisabled(this) || !NodeDocument.IsFullyActive)
            return;

        if (FormOwner is { } form)
        {
            if (IsButtonSubmitButton)
            {
                form.SubmitForm(this, submittedFromSubmitMethod: false);
                return;
            }

            var type = EnumeratedAttributes.ButtonType;
            var state = type.GetState(GetAttributeValue("type"));
            if (state == type.Keywords["reset"])
            {
                form.ResetForm();
                return;
            }

            // The Auto state
            if (state == type.MissingValueDefault)
                return;
        }

        var target = CommandForElement;
        if (target is null)
        {
            RunPopoverTargetAttributeActivationBehavior(this, @event.Target);
            return;
        }

        var command = CommandState;
        if (!IsValidCommand(command, target))
            return;

        var commandEvent = new CommandEvent("command", new CommandEventInit { Command = Command, Source = this, Cancelable = true });
        if (!target.FireEvent(commandEvent) || !target.IsConnected || command == ButtonCommand.Custom)
            return;

        var popover = (HtmlElement)target;
        switch (command)
        {
            case ButtonCommand.HidePopover:
                if (popover.CheckPopoverValidity(expectedToBeShowing: true, expectedDocument: null, forceHide: false, out _))
                {
                    popover.HidePopover(focusPreviousElement: true, fireEvents: true, throwExceptions: false, this);
                }

                break;

            case ButtonCommand.TogglePopover:
                if (popover.CheckPopoverValidity(expectedToBeShowing: false, expectedDocument: null, forceHide: false, out _))
                {
                    popover.ShowPopover(throwExceptions: false, this);
                }
                else if (popover.CheckPopoverValidity(expectedToBeShowing: true, expectedDocument: null, forceHide: false, out _))
                {
                    popover.HidePopover(focusPreviousElement: true, fireEvents: true, throwExceptions: false, this);
                }

                break;

            case ButtonCommand.ShowPopover:
                if (popover.CheckPopoverValidity(expectedToBeShowing: false, expectedDocument: null, forceHide: false, out _))
                {
                    popover.ShowPopover(throwExceptions: false, this);
                }

                break;

            default:
                // The command steps of the target element
                if (target is HtmlDialogElement dialog)
                {
                    dialog.RunCommandSteps(this, command);
                }

                break;
        }
    }

    /// <summary>The state of the command attribute (https://html.spec.whatwg.org/multipage/form-elements.html#attr-button-command).</summary>
    internal ButtonCommand CommandState
    {
        get
        {
            var value = GetAttributeValue("command");
            if (value is null)
                return ButtonCommand.Unknown;

            if (value.StartsWith("--", StringComparison.Ordinal))
                return ButtonCommand.Custom;

            var table = EnumeratedAttributes.Command;
            var state = table.GetState(value);
            return table.CanonicalKeywords[state] switch
            {
                "toggle-popover" => ButtonCommand.TogglePopover,
                "show-popover" => ButtonCommand.ShowPopover,
                "hide-popover" => ButtonCommand.HidePopover,
                "close" => ButtonCommand.Close,
                "request-close" => ButtonCommand.RequestClose,
                "show-modal" => ButtonCommand.ShowModal,
                _ => ButtonCommand.Unknown,
            };
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#determine-if-a-command-is-valid</summary>
    private static bool IsValidCommand(ButtonCommand command, Element target)
    {
        if (command == ButtonCommand.Unknown)
            return false;

        if (command == ButtonCommand.Custom)
            return true;

        if (target is not HtmlElement)
            return false;

        if (command is ButtonCommand.TogglePopover or ButtonCommand.ShowPopover or ButtonCommand.HidePopover)
            return true;

        // The is valid command steps of the dialog element
        return target is HtmlDialogElement && command is ButtonCommand.Close or ButtonCommand.RequestClose or ButtonCommand.ShowModal;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/form-elements.html#concept-button-optional-value</summary>
    internal string? OptionalValue => GetAttributeValue("value");
}

/// <summary>The states of the command attribute of button elements (https://html.spec.whatwg.org/multipage/form-elements.html#attr-button-command).</summary>
internal enum ButtonCommand
{
    Unknown,
    Custom,
    TogglePopover,
    ShowPopover,
    HidePopover,
    Close,
    RequestClose,
    ShowModal,
}
