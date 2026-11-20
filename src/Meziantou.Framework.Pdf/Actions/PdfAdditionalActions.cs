using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// The additional actions of a document, a page or an annotation (<c>/AA</c>, ISO 32000-2, 12.6.3): the actions run on
/// events, such as opening a page or the mouse entering an annotation. The dictionary is created when an action is set.
/// </summary>
public sealed class PdfAdditionalActions
{
    private static readonly PdfName AAName = PdfName.Create("AA");

    private readonly PdfDictionary _owner;
    private readonly PdfDocument _document;
    private readonly PdfActionTrigger[] _triggers;

    internal PdfAdditionalActions(PdfDocument document, PdfDictionary owner, params PdfActionTrigger[] triggers)
    {
        _document = document;
        _owner = owner;
        _triggers = triggers;
    }

    /// <summary>Gets the events this object can have actions for: those of a document, a page, an annotation or a widget.</summary>
    public IReadOnlyList<PdfActionTrigger> SupportedTriggers => _triggers;

    /// <summary>Gets the events that have an action.</summary>
    public IReadOnlyList<PdfActionTrigger> Triggers => [.. _triggers.Where(trigger => this[trigger] is not null)];

    /// <summary>Gets or sets the action of an event, or <see langword="null"/>.</summary>
    /// <param name="trigger">The event.</param>
    /// <exception cref="ArgumentException">The object cannot have an action for this event, or the action refers to objects of another document.</exception>
    public PdfAction? this[PdfActionTrigger trigger]
    {
        get => Array.IndexOf(_triggers, trigger) >= 0 ? PdfAction.Read(_document, _owner.Get<PdfDictionary>(AAName)?[GetKey(trigger)]) : null;
        set
        {
            if (Array.IndexOf(_triggers, trigger) < 0)
                throw new ArgumentException($"The object cannot have an action for {trigger}", nameof(trigger));

            if (value is not null)
            {
                PdfAction.CheckDocument(value, _document, nameof(value));
            }

            var actions = _owner.Get<PdfDictionary>(AAName);
            if (value is null)
            {
                actions?.Remove(GetKey(trigger));
                if (actions is { Count: 0 })
                {
                    _owner.Remove(AAName);
                }

                return;
            }

            if (actions is null)
            {
                actions = new PdfDictionary();
                _owner[AAName] = actions;
            }

            actions[GetKey(trigger)] = value.Dictionary;
        }
    }

    internal static PdfName GetKey(PdfActionTrigger trigger) => PdfName.Create(trigger switch
    {
        PdfActionTrigger.MouseEnter => "E",
        PdfActionTrigger.MouseExit => "X",
        PdfActionTrigger.MouseDown => "D",
        PdfActionTrigger.MouseUp => "U",
        PdfActionTrigger.Focus => "Fo",
        PdfActionTrigger.Blur => "Bl",
        PdfActionTrigger.AnnotationPageOpen => "PO",
        PdfActionTrigger.AnnotationPageClose => "PC",
        PdfActionTrigger.AnnotationPageVisible => "PV",
        PdfActionTrigger.AnnotationPageInvisible => "PI",
        PdfActionTrigger.PageOpen => "O",
        PdfActionTrigger.PageClose => "C",
        PdfActionTrigger.Keystroke => "K",
        PdfActionTrigger.Format => "F",
        PdfActionTrigger.Validate => "V",
        PdfActionTrigger.Calculate => "C",
        PdfActionTrigger.DocumentClose => "WC",
        PdfActionTrigger.DocumentSave => "WS",
        PdfActionTrigger.DocumentSaved => "DS",
        PdfActionTrigger.DocumentPrint => "WP",
        PdfActionTrigger.DocumentPrinted => "DP",
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "The trigger is not valid"),
    });

    internal static PdfActionTrigger[] AnnotationTriggers { get; } =
    [
        PdfActionTrigger.MouseEnter, PdfActionTrigger.MouseExit, PdfActionTrigger.MouseDown, PdfActionTrigger.MouseUp, PdfActionTrigger.Focus, PdfActionTrigger.Blur,
        PdfActionTrigger.AnnotationPageOpen, PdfActionTrigger.AnnotationPageClose, PdfActionTrigger.AnnotationPageVisible, PdfActionTrigger.AnnotationPageInvisible,
    ];

    internal static PdfActionTrigger[] PageTriggers { get; } = [PdfActionTrigger.PageOpen, PdfActionTrigger.PageClose];

    internal static PdfActionTrigger[] DocumentTriggers { get; } = [PdfActionTrigger.DocumentClose, PdfActionTrigger.DocumentSave, PdfActionTrigger.DocumentSaved, PdfActionTrigger.DocumentPrint, PdfActionTrigger.DocumentPrinted];
}
