using System.Globalization;
using Meziantou.Framework.Pdf.Fonts;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <content>The creation of the fields: the field dictionaries, their widgets and their appearances (ISO 32000-2, 12.7).</content>
public sealed partial class PdfInteractiveForm
{
    // The flags of the fields (12.7.4.1 and the tables of the field types): the bit n is 1 << (n - 1)
    private const int RequiredFlag = 1 << 1;
    private const int MultilineFlag = 1 << 12;
    private const int PasswordFlag = 1 << 13;
    private const int NoToggleToOffFlag = 1 << 14;
    private const int RadioFlag = 1 << 15;
    private const int ComboFlag = 1 << 17;
    private const int EditFlag = 1 << 18;
    private const int MultiSelectFlag = 1 << 21;
    private const int DoNotSpellCheckFlag = 1 << 22;
    private const int DoNotScrollFlag = 1 << 23;
    private const int CombFlag = 1 << 24;

    // The Print flag of the annotations (12.5.3)
    private const int PrintFlag = 4;

    private static readonly PdfName DRName = PdfName.Create("DR");
    private static readonly PdfName MaxLenName = PdfName.Create("MaxLen");
    private static readonly PdfName OptName = PdfName.Create("Opt");
    private static readonly PdfName BCName = PdfName.Create("BC");
    private static readonly PdfName BGName = PdfName.Create("BG");
    private static readonly PdfName BSName = PdfName.Create("BS");
    private static readonly PdfName WName = PdfName.Create("W");

    // The resource names Acrobat gives the standard fonts in the default resources
    private static readonly Dictionary<string, string> StandardFontResourceNames = new(StringComparer.Ordinal)
    {
        ["Times-Roman"] = "TiRo",
        ["Times-Bold"] = "TiBo",
        ["Times-Italic"] = "TiIt",
        ["Times-BoldItalic"] = "TiBI",
        ["Helvetica"] = "Helv",
        ["Helvetica-Bold"] = "HeBo",
        ["Helvetica-Oblique"] = "HeOb",
        ["Helvetica-BoldOblique"] = "HeBO",
        ["Courier"] = "Cour",
        ["Courier-Bold"] = "CoBo",
        ["Courier-Oblique"] = "CoOb",
        ["Courier-BoldOblique"] = "CoBO",
        ["Symbol"] = "Symb",
        ["ZapfDingbats"] = "ZaDb",
    };

    // The fonts given to AddFont: the fonts of the default resources of a file are found by their names
    private readonly List<PdfFont> _fonts = [];

    /// <summary>Adds a single-line or multiline text field (12.7.5.3), with one widget, and writes its appearance.</summary>
    /// <param name="page">The page of the widget.</param>
    /// <param name="name">The fully qualified name of the field, such as <c>address.city</c>: the fields named by the parts before the last period are created when the form has none.</param>
    /// <param name="rectangle">The rectangle of the widget, in default user space.</param>
    /// <param name="options">The attributes of the field, or <see langword="null"/> for the defaults.</param>
    /// <returns>The field.</returns>
    /// <exception cref="ArgumentException">The name is not valid or is the name of a field of the form, the page is a page of another document, the options are not consistent, or the value is longer than the maximum length.</exception>
    public PdfTextField AddTextField(PdfPage page, string name, PdfRectangle rectangle, PdfTextFieldOptions? options = null)
    {
        options ??= new PdfTextFieldOptions();
        ValidateOptions(page, options);
        if (options.MaxLength is < 0)
            throw new ArgumentException("The maximum length cannot be negative", nameof(options));

        if (options.IsComb && (options.MaxLength is null || options.IsMultiline || options.IsPassword))
            throw new ArgumentException("A comb field needs a maximum length, and cannot be multiline nor a password", nameof(options));

        if (options.Value is { } value && options.MaxLength is { } maxLength && value.Length > maxLength)
            throw new ArgumentException($"The value is longer than the maximum length of the field ({maxLength.ToString(CultureInfo.InvariantCulture)})", nameof(options));

        var alignment = GetQuadding(options.Alignment);
        var flags = GetFlags(options);
        flags |= options.IsMultiline ? MultilineFlag : 0;
        flags |= options.IsPassword ? PasswordFlag : 0;
        flags |= options.DoNotSpellCheck ? DoNotSpellCheckFlag : 0;
        flags |= options.DoNotScroll ? DoNotScrollFlag : 0;
        flags |= options.IsComb ? CombFlag : 0;

        var (container, parent, partialName) = PrepareName(name);
        var widget = CreateWidget(page, rectangle, options);
        SetFieldEntries(widget, "Tx", partialName, parent, flags, options);
        widget[PdfFormField.DAName] = CreateTextAppearance(options, defaultFontSize: 12);
        if (alignment != 0)
        {
            widget[PdfFormField.QName] = PdfInteger.Get(alignment);
        }

        if (options.MaxLength is { } length)
        {
            widget[MaxLenName] = PdfInteger.Get(length);
        }

        if (options.Value is not null)
        {
            widget[PdfFormField.VName] = PdfString.FromText(options.Value);
        }

        var field = (PdfTextField)AddField(page, container, widget, name);
        PdfFieldAppearance.Update(field);
        SetReadOnly(field, options);
        return field;
    }

    /// <summary>Adds a check box (12.7.5.2.3), with one widget and the appearances of its two states.</summary>
    /// <param name="page">The page of the widget.</param>
    /// <param name="name">The fully qualified name of the field: see <see cref="AddTextField"/>.</param>
    /// <param name="rectangle">The rectangle of the widget, in default user space.</param>
    /// <param name="options">The attributes of the field, or <see langword="null"/> for the defaults.</param>
    /// <returns>The field.</returns>
    /// <exception cref="ArgumentException">The name is not valid or is the name of a field of the form, the page is a page of another document, or the on state is empty or <c>Off</c>.</exception>
    public PdfCheckBoxField AddCheckBox(PdfPage page, string name, PdfRectangle rectangle, PdfCheckBoxOptions? options = null)
    {
        options ??= new PdfCheckBoxOptions();
        ValidateOptions(page, options);
        var state = CreateState(options.OnValue, nameof(options));

        var (container, parent, partialName) = PrepareName(name);
        var widget = CreateWidget(page, rectangle, options);
        SetFieldEntries(widget, "Btn", partialName, parent, GetFlags(options), options);
        widget[PdfFormField.DAName] = CreateSymbolAppearance(options);
        PdfFieldAppearance.WriteButtonAppearances(Document, widget, state, options.Style, round: false, options.FontSize ?? 0, options.TextColor ?? PdfColor.FromGray(0));
        widget[PdfFormField.VName] = options.IsChecked ? state : PdfFormField.OffName;
        widget[PdfFormField.ASName] = options.IsChecked ? state : PdfFormField.OffName;

        var field = (PdfCheckBoxField)AddField(page, container, widget, name);
        SetReadOnly(field, options);
        return field;
    }

    /// <summary>Adds a group of radio buttons (12.7.5.2.4): a field with one widget per button, of which one at most is on.</summary>
    /// <param name="name">The fully qualified name of the field: see <see cref="AddTextField"/>.</param>
    /// <param name="buttons">The buttons: their pages, their rectangles and their options. Buttons that share an option are turned on and off together.</param>
    /// <param name="options">The attributes of the field, or <see langword="null"/> for the defaults.</param>
    /// <returns>The field.</returns>
    /// <exception cref="ArgumentException">The name is not valid or is the name of a field of the form, there is no button, a page is a page of another document, an option is empty or <c>Off</c>, or the value is not an option.</exception>
    public PdfRadioButtonField AddRadioButtons(string name, IEnumerable<PdfRadioButton> buttons, PdfRadioButtonOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(buttons);
        options ??= new PdfRadioButtonOptions();
        var list = buttons.ToList();
        if (list.Count == 0)
            throw new ArgumentException("A group of radio buttons needs at least one button", nameof(buttons));

        var states = new List<PdfName>(list.Count);
        foreach (var button in list)
        {
            ArgumentNullException.ThrowIfNull(button, nameof(buttons));
            ValidateOptions(button.Page, options);
            states.Add(CreateState(button.Value, nameof(buttons)));
        }

        if (options.Value is not null && !list.Exists(button => button.Value == options.Value))
            throw new ArgumentException($"'{options.Value}' is not an option of the buttons", nameof(options));

        var (container, parent, partialName) = PrepareName(name);
        var flags = GetFlags(options) | RadioFlag | (options.AllowToggleOff ? 0 : NoToggleToOffFlag);
        var dictionary = new PdfDictionary();
        SetFieldEntries(dictionary, "Btn", partialName, parent, flags, options);
        dictionary[PdfFormField.DAName] = CreateSymbolAppearance(options);
        var selected = options.Value is null ? PdfFormField.OffName : PdfName.Create(options.Value);
        dictionary[PdfFormField.VName] = selected;
        var reference = Document.Objects.Add(dictionary);
        var kids = new PdfArray();
        dictionary[PdfNames.Kids] = kids;
        for (var i = 0; i < list.Count; i++)
        {
            var widget = CreateWidget(list[i].Page, list[i].Rectangle, options);
            widget[PdfNames.Parent] = reference;
            PdfFieldAppearance.WriteButtonAppearances(Document, widget, states[i], options.Style, round: true, options.FontSize ?? 0, options.TextColor ?? PdfColor.FromGray(0));
            widget[PdfFormField.ASName] = states[i] == selected ? selected : PdfFormField.OffName;
            var widgetReference = Document.Objects.Add(widget);
            kids.Add(widgetReference);
            AddAnnotation(list[i].Page, widgetReference);
        }

        container.Add(reference);
        var field = (PdfRadioButtonField)FindField(name)!;
        SetReadOnly(field, options);
        return field;
    }

    /// <summary>Adds a combo box (12.7.5.4): a drop-down list of options, editable or not, with one widget, and writes its appearance.</summary>
    /// <param name="page">The page of the widget.</param>
    /// <param name="name">The fully qualified name of the field: see <see cref="AddTextField"/>.</param>
    /// <param name="rectangle">The rectangle of the widget, in default user space.</param>
    /// <param name="choices">The options: the export values and the texts the field shows.</param>
    /// <param name="options">The attributes of the field, or <see langword="null"/> for the defaults.</param>
    /// <returns>The field.</returns>
    /// <exception cref="ArgumentException">The name is not valid or is the name of a field of the form, the page is a page of another document, the options are not consistent, or a chosen value is not an option.</exception>
    public PdfChoiceField AddComboBox(PdfPage page, string name, PdfRectangle rectangle, IEnumerable<PdfChoiceOption> choices, PdfChoiceFieldOptions? options = null)
    {
        options ??= new PdfChoiceFieldOptions();
        if (options.IsMultiSelect)
            throw new ArgumentException("A combo box has one value: only a list box can be multi-select", nameof(options));

        return AddChoiceField(page, name, rectangle, choices, options, ComboFlag | (options.IsEditable ? EditFlag : 0));
    }

    /// <summary>Adds a list box (12.7.5.4): a scrollable list of options, with one widget, and writes its appearance.</summary>
    /// <param name="page">The page of the widget.</param>
    /// <param name="name">The fully qualified name of the field: see <see cref="AddTextField"/>.</param>
    /// <param name="rectangle">The rectangle of the widget, in default user space.</param>
    /// <param name="choices">The options: the export values and the texts the field shows.</param>
    /// <param name="options">The attributes of the field, or <see langword="null"/> for the defaults.</param>
    /// <returns>The field.</returns>
    /// <exception cref="ArgumentException">The name is not valid or is the name of a field of the form, the page is a page of another document, the options are not consistent, or a chosen value is not an option.</exception>
    public PdfChoiceField AddListBox(PdfPage page, string name, PdfRectangle rectangle, IEnumerable<PdfChoiceOption> choices, PdfChoiceFieldOptions? options = null)
    {
        options ??= new PdfChoiceFieldOptions();
        if (options.IsEditable)
            throw new ArgumentException("A list box cannot be editable: only a combo box can", nameof(options));

        return AddChoiceField(page, name, rectangle, choices, options, options.IsMultiSelect ? MultiSelectFlag : 0);
    }

    /// <summary>
    /// Adds a signature field that is not signed (12.7.5.5), with one widget: <see cref="Signing.PdfSigner"/> signs it
    /// when <see cref="Signing.PdfSignatureOptions.FieldName"/> is its name. Its appearance shows the background and
    /// the border of the options.
    /// </summary>
    /// <param name="page">The page of the widget.</param>
    /// <param name="name">The fully qualified name of the field: see <see cref="AddTextField"/>.</param>
    /// <param name="rectangle">The rectangle of the widget, in default user space; an empty rectangle for an invisible signature.</param>
    /// <param name="options">The attributes of the field, or <see langword="null"/> for the defaults.</param>
    /// <returns>The field.</returns>
    /// <exception cref="ArgumentException">The name is not valid or is the name of a field of the form, or the page is a page of another document.</exception>
    public PdfSignatureField AddSignatureField(PdfPage page, string name, PdfRectangle rectangle, PdfFormFieldOptions? options = null)
    {
        options ??= new PdfFormFieldOptions();
        ValidateOptions(page, options);

        var (container, parent, partialName) = PrepareName(name);
        var widget = CreateWidget(page, rectangle, options);
        SetFieldEntries(widget, "Sig", partialName, parent, GetFlags(options), options);
        PdfFieldAppearance.WriteEmptyAppearance(Document, widget);

        var field = (PdfSignatureField)AddField(page, container, widget, name);
        SetReadOnly(field, options);
        return field;
    }

    /// <summary>
    /// Adds a font to the default resources of the form (<c>/DR</c>, 12.7.4.3), the fonts the default appearances of the
    /// fields (<c>/DA</c>) name, and returns its resource name. A font the resources already have keeps its name.
    /// </summary>
    /// <param name="font">The font. An embedded font is embedded whole, so that viewers can show the text typed in the fields.</param>
    /// <returns>The resource name of the font.</returns>
    /// <remarks>
    /// The appearances of the fields whose default appearance names the font are written with it. The fonts of a file
    /// cannot show new text: to fill the fields of a document that was saved, whose default appearances name an embedded
    /// font, call this method with the same font first. The font of the file is found by its PostScript name, and keeps
    /// its resource name.
    /// </remarks>
    public string AddFont(PdfFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        return AddFontCore(font, embedAllGlyphs: true);
    }

    /// <summary>Finds the font a default appearance names: an embedded font of the document, or a font given to <see cref="AddFont(PdfFont)"/> with the name of a font of the file.</summary>
    internal PdfFont? FindFont(PdfObject entry)
    {
        if (entry is PdfReference reference)
        {
            foreach (var (font, usage) in Document.EmbeddedFonts)
            {
                if (usage.Reference.Equals(reference))
                    return font;
            }
        }

        if (PdfReference.ResolveIfReference(entry) is PdfDictionary dictionary && dictionary.GetName(PdfNames.Subtype) == PdfNames.Type0 && dictionary.GetName(PdfNames.BaseFont) is { } baseFont)
        {
            var name = RemoveSubsetTag(baseFont.Value);
            return _fonts.Find(font => font is EmbeddedPdfFont && string.Equals(font.PostScriptName, name, StringComparison.Ordinal));
        }

        return null;
    }

    private PdfChoiceField AddChoiceField(PdfPage page, string name, PdfRectangle rectangle, IEnumerable<PdfChoiceOption> choices, PdfChoiceFieldOptions options, int typeFlags)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ValidateOptions(page, options);
        var list = choices.ToList();
        var values = options.SelectedValues ?? [];
        if (values.Count > 1 && !options.IsMultiSelect)
            throw new ArgumentException("Several values are chosen, and the field is not multi-select", nameof(options));

        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value, nameof(options));
            if (!options.IsEditable && !list.Exists(choice => choice.Value == value))
                throw new ArgumentException($"'{value}' is not an option of the field", nameof(options));
        }

        var alignment = GetQuadding(options.Alignment);
        var opt = new PdfArray();
        foreach (var choice in list)
        {
            ArgumentNullException.ThrowIfNull(choice, nameof(choices));
            opt.Add(choice.Value == choice.Text ? PdfString.FromText(choice.Value) : new PdfArray(PdfString.FromText(choice.Value), PdfString.FromText(choice.Text)));
        }

        var (container, parent, partialName) = PrepareName(name);
        var widget = CreateWidget(page, rectangle, options);
        SetFieldEntries(widget, "Ch", partialName, parent, GetFlags(options) | typeFlags, options);
        widget[PdfFormField.DAName] = CreateTextAppearance(options, defaultFontSize: 12);
        widget[OptName] = opt;
        if (alignment != 0)
        {
            widget[PdfFormField.QName] = PdfInteger.Get(alignment);
        }

        var field = (PdfChoiceField)AddField(page, container, widget, name);
        field.SetSelectedValues(values);
        SetReadOnly(field, options);
        return field;
    }

    private string AddFontCore(PdfFont font, bool embedAllGlyphs)
    {
        var form = GetOrCreateDictionary();
        var resources = form.Get<PdfDictionary>(DRName);
        if (resources is null)
        {
            resources = new PdfDictionary();
            form[DRName] = resources;
        }

        var fonts = resources.Get<PdfDictionary>(PdfNames.Font);
        if (fonts is null)
        {
            fonts = new PdfDictionary();
            resources[PdfNames.Font] = fonts;
        }

        if (!_fonts.Contains(font))
        {
            _fonts.Add(font);
        }

        var embedded = font as EmbeddedPdfFont;
        foreach (var (key, value) in fonts)
        {
            if (IsFont(value))
            {
                if (embedAllGlyphs && embedded is not null && Document.EmbeddedFonts.TryGetValue(embedded, out var existing) && existing.Reference.Equals(value))
                {
                    existing.EmbedAllGlyphs = true;
                }

                return key.Value;
            }
        }

        var reference = font.GetReference(Document);
        if (embedAllGlyphs && embedded is not null)
        {
            embedded.GetUsage(Document).EmbedAllGlyphs = true;
        }

        var name = embedded is null && StandardFontResourceNames.TryGetValue(font.PostScriptName, out var standardName) && !fonts.ContainsKey(PdfName.Create(standardName)) ? standardName : null;
        for (var number = 1; name is null; number++)
        {
            var candidate = "F" + number.ToString(CultureInfo.InvariantCulture);
            if (!fonts.ContainsKey(PdfName.Create(candidate)))
            {
                name = candidate;
            }
        }

        fonts[PdfName.Create(name)] = reference;
        return name;

        bool IsFont(PdfObject value)
        {
            if (embedded is not null && Document.EmbeddedFonts.TryGetValue(embedded, out var usage) && usage.Reference.Equals(value))
                return true;

            if (PdfReference.ResolveIfReference(value) is not PdfDictionary dictionary || dictionary.GetName(PdfNames.BaseFont) is not { } baseFont)
                return false;

            // A font of the file: an embedded font by its PostScript name, a standard font by its name, without embedded program
            return embedded is not null
                ? dictionary.GetName(PdfNames.Subtype) == PdfNames.Type0 && string.Equals(RemoveSubsetTag(baseFont.Value), font.PostScriptName, StringComparison.Ordinal)
                : dictionary.GetName(PdfNames.Subtype) == PdfNames.Type1 && string.Equals(baseFont.Value, font.PostScriptName, StringComparison.Ordinal) && dictionary.Get<PdfDictionary>(PdfNames.FontDescriptor) is null;
        }
    }

    /// <summary>Removes the tag of the name of a subset (9.6.4): six uppercase letters and a plus sign.</summary>
    private static string RemoveSubsetTag(string name)
    {
        if (name.Length > 7 && name[6] == '+')
        {
            for (var i = 0; i < 6; i++)
            {
                if (name[i] is < 'A' or > 'Z')
                    return name;
            }

            return name[7..];
        }

        return name;
    }

    private PdfDictionary GetOrCreateDictionary()
    {
        if (Dictionary is { } dictionary)
            return dictionary;

        dictionary = new PdfDictionary { [FieldsName] = new PdfArray() };
        Document.Catalog[AcroFormName] = dictionary;
        return dictionary;
    }

    private void ValidateOptions(PdfPage page, PdfFormFieldOptions options)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Document != Document)
            throw new ArgumentException("The page is a page of another document", nameof(page));

        if (!double.IsFinite(options.BorderWidth) || options.BorderWidth < 0)
            throw new ArgumentException("The width of the border must be a finite number that is not negative", nameof(options));

        if (options.FontSize is { } size && (!double.IsFinite(size) || size < 0))
            throw new ArgumentException("The font size must be a finite number that is not negative", nameof(options));
    }

    private static PdfName CreateState(string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) || value == "Off")
            throw new ArgumentException("The name of an on state cannot be empty nor 'Off'", parameterName);

        return PdfName.Create(value);
    }

    private static int GetQuadding(PdfTextAlignment alignment) => alignment switch
    {
        PdfTextAlignment.Left => 0,
        PdfTextAlignment.Center => 1,
        PdfTextAlignment.Right => 2,
        _ => throw new ArgumentException("The text of a field is aligned left, centered or right", nameof(alignment)),
    };

    // The read-only flag is set once the value and the appearance are written
    private static void SetReadOnly(PdfFormField field, PdfFormFieldOptions options)
    {
        if (options.IsReadOnly)
        {
            field.IsReadOnly = true;
        }
    }

    private static int GetFlags(PdfFormFieldOptions options) => options.IsRequired ? RequiredFlag : 0;

    /// <summary>Finds the container of a new field from its fully qualified name, creating the non-terminal fields of its parts (12.7.4.2).</summary>
    private (PdfArray Container, PdfReference? Parent, string PartialName) PrepareName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var parts = name.Split('.');
        if (Array.Exists(parts, part => part.Length == 0))
            throw new ArgumentException($"The field name '{name}' has an empty part", nameof(name));

        var form = GetOrCreateDictionary();
        var container = form.Get<PdfArray>(FieldsName);
        if (container is null)
        {
            container = new PdfArray();
            form[FieldsName] = container;
        }

        PdfReference? parent = null;
        for (var i = 0; i < parts.Length; i++)
        {
            var index = FindKid(container, parts[i]);
            if (i == parts.Length - 1)
            {
                if (index >= 0)
                    throw new ArgumentException($"The form already has a field named '{name}'", nameof(name));

                break;
            }

            PdfReference reference;
            if (index < 0)
            {
                var node = new PdfDictionary { [PdfFormField.TName] = PdfString.FromText(parts[i]), [PdfNames.Kids] = new PdfArray() };
                if (parent is not null)
                {
                    node[PdfNames.Parent] = parent;
                }

                reference = Document.Objects.Add(node);
                container.Add(reference);
            }
            else
            {
                var dictionary = (PdfDictionary)PdfReference.ResolveIfReference(container[index]);
                if (dictionary.ContainsKey(PdfFormField.FTName) || dictionary.GetName(PdfNames.Subtype) == PdfFormField.WidgetName || dictionary.Get<PdfArray>(PdfNames.Kids) is null)
                    throw new ArgumentException($"The field '{string.Join('.', parts[..(i + 1)])}' is a terminal field: it cannot have child fields", nameof(name));

                // The kids of the field refer to it: a field written as a direct object becomes an indirect one
                if (container[index] is PdfReference existing)
                {
                    reference = existing;
                }
                else
                {
                    reference = Document.Objects.Add(dictionary);
                    container[index] = reference;
                }
            }

            parent = reference;
            container = ((PdfDictionary)reference.Resolve()).Get<PdfArray>(PdfNames.Kids)!;
        }

        return (container, parent, parts[^1]);

        static int FindKid(PdfArray container, string partialName)
        {
            for (var i = 0; i < container.Count; i++)
            {
                if (container.GetResolved(i) is PdfDictionary kid && string.Equals(kid.GetText(PdfFormField.TName), partialName, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }
    }

    private static void SetFieldEntries(PdfDictionary dictionary, string fieldType, string partialName, PdfReference? parent, int flags, PdfFormFieldOptions options)
    {
        dictionary[PdfFormField.FTName] = PdfName.Create(fieldType);
        dictionary[PdfFormField.TName] = PdfString.FromText(partialName);
        if (parent is not null)
        {
            dictionary[PdfNames.Parent] = parent;
        }

        if (options.AlternateName is not null)
        {
            dictionary[PdfFormField.TUName] = PdfString.FromText(options.AlternateName);
        }

        if (flags != 0)
        {
            dictionary[PdfFormField.FfName] = PdfInteger.Get(flags);
        }
    }

    /// <summary>Creates a widget annotation (12.5.6.19) with the appearance characteristics of the options: the colors of the border and the background.</summary>
    private static PdfDictionary CreateWidget(PdfPage page, PdfRectangle rectangle, PdfFormFieldOptions options)
    {
        var widget = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Annot,
            [PdfNames.Subtype] = PdfFormField.WidgetName,
            [PdfNames.Rect] = rectangle.ToArray(),
            [PdfNames.P] = page.Reference,
            [PdfNames.F] = PdfInteger.Get(PrintFlag),
        };
        var characteristics = new PdfDictionary();
        var hasBorder = options.BorderColor is not null && options.BorderWidth > 0;
        if (hasBorder)
        {
            characteristics[BCName] = PdfArray.FromNumbers(options.BorderColor!.Value.GetComponents());
        }

        if (options.BackgroundColor is { } background)
        {
            characteristics[BGName] = PdfArray.FromNumbers(background.GetComponents());
        }

        if (characteristics.Count > 0)
        {
            widget[PdfFormField.MKName] = characteristics;
        }

        widget[BSName] = new PdfDictionary
        {
            [WName] = PdfObjectFactory.Number(hasBorder ? options.BorderWidth : 0),
            [PdfNames.S] = PdfNames.S,
        };
        return widget;
    }

    /// <summary>Adds a terminal field merged with its widget (12.7.4.1) to its container and its page.</summary>
    private PdfFormField AddField(PdfPage page, PdfArray container, PdfDictionary widget, string name)
    {
        var reference = Document.Objects.Add(widget);
        container.Add(reference);
        AddAnnotation(page, reference);
        return FindField(name)!;
    }

    private static void AddAnnotation(PdfPage page, PdfReference widget)
    {
        if (page.Dictionary.Get<PdfArray>(PdfNames.Annots) is not { } annotations)
        {
            annotations = new PdfArray();
            page.Dictionary[PdfNames.Annots] = annotations;
        }

        annotations.Add(widget);
    }

    /// <summary>Creates the default appearance (12.7.4.3) of a text: the font of the default resources, its size and the color of the text.</summary>
    private PdfString CreateTextAppearance(PdfFormFieldOptions options, double defaultFontSize)
    {
        var font = options.Font ?? PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        var name = AddFontCore(font, embedAllGlyphs: !options.IsReadOnly && !options.SubsetFont);
        return CreateDefaultAppearance(name, options.FontSize ?? defaultFontSize, options.TextColor ?? PdfColor.FromGray(0));
    }

    /// <summary>Creates the default appearance of a check box or a radio button: ZapfDingbats, the font of their symbols.</summary>
    private PdfString CreateSymbolAppearance(PdfFormFieldOptions options)
    {
        var name = AddFontCore(PdfFont.GetStandardFont(PdfStandardFont.ZapfDingbats), embedAllGlyphs: false);
        return CreateDefaultAppearance(name, options.FontSize ?? 0, options.TextColor ?? PdfColor.FromGray(0));
    }

    private static PdfString CreateDefaultAppearance(string fontName, double fontSize, PdfColor color)
    {
        var components = string.Join(' ', color.GetComponents().Select(Format));
        var colorOperator = color.ColorSpace switch
        {
            PdfDeviceColorSpace.Gray => "g",
            PdfDeviceColorSpace.Rgb => "rg",
            _ => "k",
        };
        return PdfString.FromText($"/{fontName} {Format(fontSize)} Tf {components} {colorOperator}");

        static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
