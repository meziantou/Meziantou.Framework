using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// A field of the interactive form of a document (ISO 32000-2, 12.7.4): a node of the field hierarchy. A terminal field
/// has a value and widgets, the annotations that show it on the pages; a non-terminal field holds other fields, whose
/// names are qualified by its name.
/// </summary>
/// <remarks>
/// The derived classes represent the terminal fields of each type: <see cref="PdfTextField"/>, <see cref="PdfCheckBoxField"/>,
/// <see cref="PdfRadioButtonField"/>, <see cref="PdfPushButtonField"/>, <see cref="PdfChoiceField"/> and
/// <see cref="PdfSignatureField"/>. The other fields (non-terminal fields, unknown types) are of this class.
/// </remarks>
public class PdfFormField
{
    private const int MaxDepth = 32;

    internal static readonly PdfName FTName = PdfName.Create("FT");
    internal static readonly PdfName FfName = PdfName.Create("Ff");
    internal static readonly PdfName TName = PdfName.Create("T");
    internal static readonly PdfName TUName = PdfName.Create("TU");
    internal static readonly PdfName VName = PdfName.Create("V");
    internal static readonly PdfName DVName = PdfName.Create("DV");
    internal static readonly PdfName DAName = PdfName.Create("DA");
    internal static readonly PdfName QName = PdfName.Create("Q");
    internal static readonly PdfName ASName = PdfName.Create("AS");
    internal static readonly PdfName APName = PdfName.Create("AP");
    internal static readonly PdfName MKName = PdfName.Create("MK");
    internal static readonly PdfName WidgetName = PdfName.Create("Widget");
    internal static readonly PdfName OffName = PdfName.Create("Off");

    private protected PdfFormField(PdfInteractiveForm form, PdfDictionary dictionary, PdfReference? reference, PdfFormField? parent)
    {
        Form = form;
        Dictionary = dictionary;
        Reference = reference;
        Parent = parent;
    }

    /// <summary>Gets the form of the field.</summary>
    public PdfInteractiveForm Form { get; }

    /// <summary>Gets the field dictionary. For a field with one widget, it can also be the widget annotation (a merged dictionary).</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets the reference to the field dictionary, or <see langword="null"/> for a direct object.</summary>
    public PdfReference? Reference { get; }

    /// <summary>Gets the parent field, or <see langword="null"/> for a field at the top of the hierarchy.</summary>
    public PdfFormField? Parent { get; }

    /// <summary>Gets the partial name of the field (<c>/T</c>).</summary>
    public string Name => Dictionary.GetText(TName) ?? "";

    /// <summary>Gets the fully qualified name of the field: the partial names of the field and its ancestors, separated by periods (12.7.4.2).</summary>
    public string FullName
    {
        get
        {
            var names = new List<string>();
            for (var (node, depth) = (this, 0); node is not null && depth < MaxDepth; (node, depth) = (node.Parent, depth + 1))
            {
                if (node.Dictionary.GetText(TName) is { } name)
                {
                    names.Add(name);
                }
            }

            names.Reverse();
            return string.Join('.', names);
        }
    }

    /// <summary>Gets or sets the alternate name of the field (<c>/TU</c>): the text the user interface shows, and assistive technologies read.</summary>
    public string? AlternateName
    {
        get => Dictionary.GetText(TUName);
        set => Dictionary.SetText(TUName, value);
    }

    /// <summary>Gets the field type (<c>/FT</c>, inherited): <c>Tx</c>, <c>Btn</c>, <c>Ch</c> or <c>Sig</c>, or <see langword="null"/> for a non-terminal field without type.</summary>
    public string? FieldType => (GetInherited(FTName) as PdfName)?.Value;

    /// <summary>Gets or sets the flags of the field (<c>/Ff</c>, inherited, 12.7.4.1 and the tables of the field types).</summary>
    public int Flags
    {
        get => GetInherited(FfName) is PdfNumber number ? unchecked((int)(long)number.DoubleValue) : 0;
        set => Dictionary[FfName] = PdfInteger.Get(value);
    }

    /// <summary>Gets or sets a value indicating whether the user cannot change the value of the field (bit 1 of <see cref="Flags"/>).</summary>
    public bool IsReadOnly
    {
        get => HasFlag(1);
        set => SetFlag(1, value);
    }

    /// <summary>Gets or sets a value indicating whether the field must have a value when the form is submitted (bit 2 of <see cref="Flags"/>).</summary>
    public bool IsRequired
    {
        get => HasFlag(2);
        set => SetFlag(2, value);
    }

    /// <summary>Gets or sets the value of the field (<c>/V</c>, inherited) as a text: a text string or a name; <see langword="null"/> when it has none.</summary>
    /// <remarks>The value of a text field, a choice field, a check box (its on state or <c>Off</c>) and a group of radio buttons (its option) can be set; their appearances are updated.</remarks>
    /// <exception cref="NotSupportedException">The value of the field cannot be set: a push button, a signature field, a non-terminal field or a field of an unknown type.</exception>
    public virtual string? Value
    {
        get => ToText(GetInherited(VName));
        set => throw new NotSupportedException($"The value of the field '{FullName}' cannot be set");
    }

    /// <summary>Gets the child fields, for a non-terminal field.</summary>
    public IReadOnlyList<PdfFormField> Children
    {
        get
        {
            var result = new List<PdfFormField>();
            if (Dictionary.Get<PdfArray>(PdfNames.Kids) is { } kids && Depth < MaxDepth)
            {
                foreach (var kid in kids)
                {
                    // A kid without a partial name that is a widget is a widget of this field, not a field
                    if (PdfReference.ResolveIfReference(kid) is PdfDictionary child && !IsWidgetOnly(child))
                    {
                        result.Add(Form.CreateField(child, kid as PdfReference, this));
                    }
                }
            }

            return result;
        }
    }

    /// <summary>Gets a value indicating whether the field is a terminal field: it has no child fields.</summary>
    public bool IsTerminal => Children.Count == 0;

    /// <summary>Gets the widgets of the field: the annotations that show it on the pages. A widget that is on no page is left out.</summary>
    public IReadOnlyList<PdfAnnotation> Widgets
    {
        get
        {
            var result = new List<PdfAnnotation>();
            foreach (var (dictionary, reference) in GetWidgetDictionaries())
            {
                if (Form.FindPage(dictionary, reference) is { } page)
                {
                    result.Add(page.Annotations.FirstOrDefault(annotation => ReferenceEquals(annotation.Dictionary, dictionary)) ?? PdfAnnotation.Create(page, dictionary, reference));
                }
            }

            return result;
        }
    }

    internal int Depth
    {
        get
        {
            var depth = 0;
            for (var ancestor = Parent; ancestor is not null && depth < MaxDepth; ancestor = ancestor.Parent)
            {
                depth++;
            }

            return depth;
        }
    }

    /// <summary>Gets the widget dictionaries of the field: the field itself when it is merged with its widget, and its kids that are widgets.</summary>
    internal List<(PdfDictionary Dictionary, PdfReference? Reference)> GetWidgetDictionaries()
    {
        var result = new List<(PdfDictionary, PdfReference?)>();
        if (Dictionary.GetName(PdfNames.Subtype) == WidgetName)
        {
            result.Add((Dictionary, Reference));
        }

        if (Dictionary.Get<PdfArray>(PdfNames.Kids) is { } kids)
        {
            foreach (var kid in kids)
            {
                if (PdfReference.ResolveIfReference(kid) is PdfDictionary child && IsWidgetOnly(child))
                {
                    result.Add((child, kid as PdfReference));
                }
            }
        }

        return result;
    }

    /// <summary>Gets an inheritable entry of the field (12.7.4.1): the value of the field or of its first ancestor that has it.</summary>
    internal PdfObject? GetInherited(PdfName key)
    {
        for (var (node, depth) = (this, 0); node is not null && depth < MaxDepth; (node, depth) = (node.Parent, depth + 1))
        {
            if (node.Dictionary.GetResolved(key) is { } value and not PdfNull)
                return value;
        }

        // The dictionary of a field read without its parents: its /Parent chain
        var ancestor = Dictionary.Get<PdfDictionary>(PdfNames.Parent);
        for (var depth = 0; ancestor is not null && depth < MaxDepth && Parent is null; depth++)
        {
            if (ancestor.GetResolved(key) is { } value and not PdfNull)
                return value;

            ancestor = ancestor.Get<PdfDictionary>(PdfNames.Parent);
        }

        return null;
    }

    internal bool HasFlag(int bit) => (Flags & (1 << (bit - 1))) != 0;

    internal void SetFlag(int bit, bool value) => Flags = value ? Flags | (1 << (bit - 1)) : Flags & ~(1 << (bit - 1));

    internal static string? ToText(PdfObject? value) => value switch
    {
        PdfString text => text.ToText(),
        PdfName name => name.Value,
        _ => null,
    };

    /// <summary>Indicates whether a kid of a field is a widget annotation only: a widget without partial name (12.7.4.1).</summary>
    internal static bool IsWidgetOnly(PdfDictionary kid) => !kid.ContainsKey(TName) && (kid.GetName(PdfNames.Subtype) == WidgetName || kid.ContainsKey(PdfNames.Rect));

    internal static PdfFormField Create(PdfInteractiveForm form, PdfDictionary dictionary, PdfReference? reference, PdfFormField? parent)
    {
        var field = new PdfFormField(form, dictionary, reference, parent);
        if (!field.IsTerminal)
            return field;

        const int RadioFlag = 1 << 15;
        const int PushButtonFlag = 1 << 16;
        return field.FieldType switch
        {
            "Tx" => new PdfTextField(form, dictionary, reference, parent),
            "Btn" when (field.Flags & PushButtonFlag) != 0 => new PdfPushButtonField(form, dictionary, reference, parent),
            "Btn" when (field.Flags & RadioFlag) != 0 => new PdfRadioButtonField(form, dictionary, reference, parent),
            "Btn" => new PdfCheckBoxField(form, dictionary, reference, parent),
            "Ch" => new PdfChoiceField(form, dictionary, reference, parent),
            "Sig" => new PdfSignatureField(form, dictionary, reference, parent),
            _ => field,
        };
    }
}
