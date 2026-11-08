using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering;
using Meziantou.Framework.Pdf.TestHarness;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfInteractiveFormTests
{
    private static readonly PdfName AP = PdfName.Create("AP");
    private static readonly PdfName AS = PdfName.Create("AS");
    private static readonly PdfName DA = PdfName.Create("DA");
    private static readonly PdfName DR = PdfName.Create("DR");
    private static readonly PdfName MK = PdfName.Create("MK");

    /// <summary>
    /// A form of each field type, written by hand: a text field merged with its widget, a hierarchy (address.city), a
    /// check box and a group of radio buttons with their appearance states, a combo box, a list box, a read-only field
    /// and a signed signature field. The second page has a field of its own.
    /// </summary>
    private static byte[] CreateForm()
    {
        const string Check = "<< /N << /Yes 30 0 R /Off 31 0 R >> >>";
        return new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [10 0 R 11 0 R 13 0 R 14 0 R 17 0 R 18 0 R 19 0 R 20 0 R 21 0 R] /DA (/Helv 0 Tf 0 g) /DR << /Font << /Helv 40 0 R /TiRo 41 0 R >> >> /SigFlags 3 >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 /MediaBox [0 0 300 300] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [10 0 R 12 0 R 13 0 R 15 0 R 16 0 R 17 0 R 18 0 R 19 0 R 20 0 R] >>")
            .Add(4, "<< /Type /Page /Parent 2 0 R /Annots [21 0 R] >>")
            .Add(10, "<< /FT /Tx /T (name) /TU (Your name) /V (Old) /Type /Annot /Subtype /Widget /Rect [10 250 150 270] /P 3 0 R /DA (/TiRo 12 Tf 0 0 1 rg) /MK << /BC [0] >> >>")
            .Add(11, "<< /T (address) /Kids [12 0 R] >>")
            .Add(12, "<< /FT /Tx /T (city) /Parent 11 0 R /Type /Annot /Subtype /Widget /Rect [10 220 150 240] /P 3 0 R /Ff 4096 >>")
            .Add(13, "<< /FT /Btn /T (agree) /V /Off /AS /Off /Type /Annot /Subtype /Widget /Rect [10 190 25 205] /P 3 0 R /AP " + Check + " >>")
            .Add(14, "<< /FT /Btn /Ff 49152 /T (size) /V /S /Kids [15 0 R 16 0 R] >>")
            .Add(15, "<< /Type /Annot /Subtype /Widget /Parent 14 0 R /Rect [10 160 25 175] /P 3 0 R /AS /S /AP << /N << /S 30 0 R /Off 31 0 R >> >> >>")
            .Add(16, "<< /Type /Annot /Subtype /Widget /Parent 14 0 R /Rect [40 160 55 175] /P 3 0 R /AS /Off /AP << /N << /L 30 0 R /Off 31 0 R >> >> >>")
            .Add(17, "<< /FT /Ch /Ff 131072 /T (country) /Opt [[(fr) (France)] [(de) (Germany)] (Other)] /V (fr) /Type /Annot /Subtype /Widget /Rect [10 130 150 150] /P 3 0 R >>")
            .Add(18, "<< /FT /Ch /Ff 2097152 /T (colors) /Opt [(red) (green) (blue)] /Type /Annot /Subtype /Widget /Rect [160 100 290 150] /P 3 0 R /DA (/Helv 10 Tf 0 g) >>")
            .Add(19, "<< /FT /Tx /Ff 1 /T (id) /V (42) /Type /Annot /Subtype /Widget /Rect [10 70 150 90] /P 3 0 R >>")
            .Add(20, "<< /FT /Sig /T (signature) /V << /Type /Sig /Filter /Adobe.PPKLite >> /Type /Annot /Subtype /Widget /Rect [0 0 0 0] /P 3 0 R /F 132 >>")
            .Add(21, "<< /FT /Tx /T (other) /V (Second page) /Type /Annot /Subtype /Widget /Rect [10 10 150 30] /P 4 0 R >>")
            .Add(30, HandWrittenPdf.Stream("0 0 15 15 re f", " /Type /XObject /Subtype /Form /BBox [0 0 15 15]"))
            .Add(31, HandWrittenPdf.Stream(" ", " /Type /XObject /Subtype /Form /BBox [0 0 15 15]"))
            .Add(40, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")
            .Add(41, "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding /WinAnsiEncoding >>")
            .ToArray();
    }

    [Fact]
    public void AFontNameWithTheNullCharacterInTheDefaultAppearanceIsNotAResource()
    {
        // The /Ti#00Ro of the default appearance names no font of /DR: the value is shown in the default font
        var data = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [10 0 R] /DR << /Font << /TiRo 41 0 R >> >> >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 300 300] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [10 0 R] >>")
            .Add(10, "<< /FT /Tx /T (name) /Type /Annot /Subtype /Widget /Rect [10 250 150 270] /P 3 0 R /DA (/Ti#00Ro 12 Tf 0 g) >>")
            .Add(41, "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding /WinAnsiEncoding >>")
            .ToArray();
        var document = PdfDocument.Open(data);

        document.Form.FindField("name")!.Value = "Jane Doe";

        var appearance = PdfDocument.Open(document.ToByteArray()).Form.FindField("name")!.Dictionary.Get<PdfDictionary>(AP)!.Get<PdfStream>(PdfNames.N)!;
        Assert.Contains("(Jane Doe) Tj", Encoding.Latin1.GetString(appearance.GetDecodedData()), StringComparison.Ordinal);
    }

    [Fact]
    public void FieldsAreRead()
    {
        var form = PdfDocument.Open(CreateForm()).Form;

        Assert.True(form.Exists);
        Assert.HasCount(9, form.Fields);
        var fields = form.GetFields();
        Assert.Equal(["name", "address.city", "agree", "size", "country", "colors", "id", "signature", "other"], fields.Select(field => field.FullName));
        Assert.Equal([typeof(PdfTextField), typeof(PdfTextField), typeof(PdfCheckBoxField), typeof(PdfRadioButtonField), typeof(PdfChoiceField), typeof(PdfChoiceField), typeof(PdfTextField), typeof(PdfSignatureField), typeof(PdfTextField)], fields.Select(field => field.GetType()));

        var name = Assert.IsType<PdfTextField>(form.FindField("name"));
        Assert.Equal("Old", name.Value);
        Assert.Equal("Your name", name.AlternateName);
        Assert.Single(name.Widgets);
        Assert.True(Assert.IsType<PdfTextField>(form.FindField("address.city")).IsMultiline);
        var address = form.FindField("address")!;
        Assert.False(address.IsTerminal);
        Assert.Null(address.FieldType);
        Assert.Same(address.Dictionary, form.FindField("address.city")!.Parent!.Dictionary);

        var agree = Assert.IsType<PdfCheckBoxField>(form.FindField("agree"));
        Assert.False(agree.IsChecked);
        Assert.Equal("Yes", agree.OnValue);
        var size = Assert.IsType<PdfRadioButtonField>(form.FindField("size"));
        Assert.Equal(["S", "L"], size.Options);
        Assert.Equal("S", size.Value);
        Assert.HasCount(2, size.Widgets);

        var country = Assert.IsType<PdfChoiceField>(form.FindField("country"));
        Assert.True(country.IsComboBox);
        Assert.Equal([new PdfChoiceOption("fr", "France"), new PdfChoiceOption("de", "Germany"), new PdfChoiceOption("Other", "Other")], country.Options);
        Assert.Equal("fr", country.Value);
        var colors = Assert.IsType<PdfChoiceField>(form.FindField("colors"));
        Assert.True(colors.IsMultiSelect);
        Assert.Empty(colors.SelectedValues);

        Assert.True(form.FindField("id")!.IsReadOnly);
        Assert.True(Assert.IsType<PdfSignatureField>(form.FindField("signature")).IsSigned);
        Assert.Null(form.FindField("missing"));
        Assert.Null(form.FindField("address.missing"));
    }

    [Fact]
    public void Example_FillForm()
    {
        var data = CreateForm();

        #region Pdf_FillForm
        var document = PdfDocument.Open(data);
        var form = document.Form;
        form.FindField("name")!.Value = "Jane Doe";
        ((PdfCheckBoxField)form.FindField("agree")!).IsChecked = true;
        form.FindField("size")!.Value = "L";
        form.FindField("country")!.Value = "de";
        ((PdfChoiceField)form.FindField("colors")!).SetSelectedValues(["red", "blue"]);

        // The values and the appearances of the fields are appended: the signatures stay valid
        using var output = new MemoryStream();
        document.SaveIncremental(output);
        #endregion

        Assert.Equal(data, output.ToArray()[..data.Length]);
        var read = PdfDocument.Open(output.ToArray()).Form;
        Assert.Equal("Jane Doe", read.FindField("name")!.Value);
        Assert.True(((PdfCheckBoxField)read.FindField("agree")!).IsChecked);
        Assert.Equal(PdfName.Create("Yes"), read.FindField("agree")!.Dictionary.GetName(AS));
        Assert.Equal("L", read.FindField("size")!.Value);
        var sizeWidgets = read.FindField("size")!.Widgets;
        Assert.Equal([PdfName.Create("Off"), PdfName.Create("L")], sizeWidgets.Select(widget => widget.Dictionary.GetName(AS)));
        Assert.Equal("de", read.FindField("country")!.Value);
        Assert.Equal(["red", "blue"], ((PdfChoiceField)read.FindField("colors")!).SelectedValues);
        Assert.Equal([0, 2], read.FindField("colors")!.Dictionary.Get<PdfArray>(PdfName.Create("I"))!.Select(item => (int)((PdfInteger)item).Value));

        // The other fields keep their values
        Assert.Equal("42", read.FindField("id")!.Value);
        Assert.Equal("Second page", read.FindField("other")!.Value);

        // The appearance of the text field shows the value in the font, size and color of its default appearance, with its border
        var appearance = read.FindField("name")!.Dictionary.Get<PdfDictionary>(AP)!.Get<PdfStream>(PdfNames.N)!;
        var content = Encoding.Latin1.GetString(appearance.GetDecodedData());
        Assert.Contains("/Tx BMC", content, StringComparison.Ordinal);
        Assert.Contains("(Jane Doe) Tj", content, StringComparison.Ordinal);
        Assert.Contains("0 0 1 rg", content, StringComparison.Ordinal);
        Assert.Contains(" 12 Tf", content, StringComparison.Ordinal);
        Assert.Equal("Times-Roman", appearance.Dictionary.Get<PdfDictionary>(PdfNames.Resources)!.Get<PdfDictionary>(PdfNames.Font)!.Select(entry => PdfReference.ResolveIfReference(entry.Value)).OfType<PdfDictionary>().Single().GetName(PdfNames.BaseFont)!.Value);
        Assert.Equal([0, 0, 140, 20], appearance.Dictionary.Get<PdfArray>(PdfNames.BBox)!.Select(item => ((PdfNumber)item).DoubleValue));
        var combo = Encoding.Latin1.GetString(read.FindField("country")!.Dictionary.Get<PdfDictionary>(AP)!.Get<PdfStream>(PdfNames.N)!.GetDecodedData());
        Assert.Contains("(Germany) Tj", combo, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidValuesAreRefused()
    {
        var form = PdfDocument.Open(CreateForm()).Form;

        Assert.Throws<InvalidOperationException>(() => form.FindField("id")!.Value = "43");
        Assert.Throws<ArgumentException>(() => form.FindField("size")!.Value = "XL");
        Assert.Throws<ArgumentException>(() => form.FindField("country")!.Value = "it");
        Assert.Throws<ArgumentException>(() => ((PdfChoiceField)form.FindField("country")!).SetSelectedValues(["fr", "de"]));
        Assert.Throws<ArgumentException>(() => form.FindField("agree")!.Value = "Maybe");
        Assert.Throws<NotSupportedException>(() => form.FindField("signature")!.Value = "x");
        Assert.Throws<NotSupportedException>(() => form.FindField("address")!.Value = "x");

        form.FindField("agree")!.Value = "Yes";
        Assert.True(((PdfCheckBoxField)form.FindField("agree")!).IsChecked);
        form.FindField("size")!.Value = null;
        Assert.Null(form.FindField("size")!.Value);
        Assert.All(form.FindField("size")!.Widgets, widget => Assert.Equal(PdfName.Create("Off"), widget.Dictionary.GetName(AS)));
    }

    [Fact]
    public void Example_FlattenForm()
    {
        var document = PdfDocument.Open(CreateForm());
        document.Form.FindField("name")!.Value = "Jane Doe";
        document.Form.FindField("address.city")!.Value = "Paris";

        #region Pdf_FlattenForm
        // The fields are drawn on their pages and removed: the values cannot be changed anymore
        document.Form.Flatten();
        #endregion

        var read = PdfDocument.Open(document.ToByteArray());
        var text = read.Pages[0].ExtractText();
        Assert.Contains("Jane Doe", text, StringComparison.Ordinal);
        Assert.Contains("Paris", text, StringComparison.Ordinal);
        Assert.Contains("France", text, StringComparison.Ordinal);
        Assert.Contains("42", text, StringComparison.Ordinal);
        Assert.Equal("Second page", read.Pages[1].ExtractText());

        // The signed signature field is kept, with its widget; the other widgets are gone
        var field = Assert.Single(read.Form.GetFields());
        Assert.Equal("signature", field.FullName);
        Assert.Single(read.Pages[0].Annotations);
        Assert.Empty(read.Pages[1].Annotations);

        // The check box was drawn with the appearance of its state: off, an empty form
        using var image = read.Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false });
        Assert.Equal(255, image.Frames[0][17, 300 - 197].R);
        Assert.Equal(0, image.Frames[0][17, 300 - 167].R);
    }

    [Fact]
    public void AFormWithoutSignatureIsRemovedWhenFlattened()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(200, 100));
        var widget = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Annot,
            [PdfNames.Subtype] = PdfName.Create("Widget"),
            [PdfName.Create("FT")] = PdfName.Create("Tx"),
            [PdfName.Create("T")] = PdfString.FromText("field"),
            [PdfNames.Rect] = PdfArray.FromNumbers(10, 10, 190, 40),
            [PdfNames.P] = page.Reference,
            [PdfName.Create("Ff")] = PdfInteger.Get(1 << 24), // Comb
            [PdfName.Create("MaxLen")] = PdfInteger.Get(6),
        };
        var reference = document.Objects.Add(widget);
        page.Dictionary[PdfNames.Annots] = new PdfArray(reference);
        document.Catalog[PdfName.Create("AcroForm")] = new PdfDictionary { [PdfName.Create("Fields")] = new PdfArray(reference), [PdfName.Create("NeedAppearances")] = PdfBoolean.True };

        var field = Assert.IsType<PdfTextField>(document.Form.FindField("field"));
        Assert.True(field.IsComb);
        Assert.True(document.Form.NeedAppearances);
        field.Value = "ABCDEFGH";
        Assert.Equal("ABCDEF", field.Value);
        document.Form.Flatten();

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.False(read.Form.Exists);
        Assert.Empty(read.Pages[0].Annotations);
        Assert.Equal("ABCDEF", read.Pages[0].ExtractText().Replace("\n", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal));
    }

    [Fact]
    public void RemovingAPageRemovesItsFields()
    {
        var document = PdfDocument.Open(CreateForm());

        document.Pages.RemoveAt(1);

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Null(read.Form.FindField("other"));
        Assert.HasCount(8, read.Form.Fields);

        document.Pages.RemoveAt(0);
        read = PdfDocument.Open(document.ToByteArray());
        Assert.Empty(read.Form.Fields);
    }

    [Fact]
    public void AnInheritedValueIsRead()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [10 0 R] >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 100 100] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [11 0 R] >>")
            .Add(10, "<< /T (parent) /FT /Tx /V (inherited) /Ff 2 /Kids [11 0 R] >>")
            .Add(11, "<< /T (child) /Parent 10 0 R /Type /Annot /Subtype /Widget /Rect [0 0 10 10] >>");
        var field = PdfDocument.Open(file.ToArray()).Form.FindField("parent.child")!;

        Assert.IsType<PdfTextField>(field);
        Assert.Equal("inherited", field.Value);
        Assert.True(field.IsRequired);
    }

    /// <summary>A form created with the API: a field of each type on one page, the text fields in Helvetica.</summary>
    internal static PdfDocument CreateFields()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(300, 400));
        var form = document.Form;
        form.AddTextField(page, "name", new PdfRectangle(10, 360, 190, 380), new PdfTextFieldOptions { Value = "Jane", AlternateName = "Your name", IsRequired = true });
        form.AddTextField(page, "address.street", new PdfRectangle(10, 300, 190, 350), new PdfTextFieldOptions { IsMultiline = true, Value = "1 Main Street\nSpringfield", BackgroundColor = PdfColor.FromRgb(0.9, 0.9, 1) });
        form.AddTextField(page, "address.zip", new PdfRectangle(200, 330, 290, 350), new PdfTextFieldOptions { MaxLength = 5, IsComb = true });
        form.AddCheckBox(page, "agree", new PdfRectangle(10, 270, 25, 285), new PdfCheckBoxOptions { IsChecked = true });
        form.AddRadioButtons("size", [new PdfRadioButton(page, new PdfRectangle(10, 240, 25, 255), "S"), new PdfRadioButton(page, new PdfRectangle(40, 240, 55, 255), "M"), new PdfRadioButton(page, new PdfRectangle(70, 240, 85, 255), "L")], new PdfRadioButtonOptions { Value = "M" });
        form.AddComboBox(page, "country", new PdfRectangle(10, 200, 150, 220), [new PdfChoiceOption("fr", "France"), new PdfChoiceOption("de", "Germany")], new PdfChoiceFieldOptions { SelectedValues = ["fr"] });
        form.AddListBox(page, "colors", new PdfRectangle(160, 150, 290, 220), [new PdfChoiceOption("red", "Red"), new PdfChoiceOption("green", "Green"), new PdfChoiceOption("blue", "Blue")], new PdfChoiceFieldOptions { IsMultiSelect = true, SelectedValues = ["red", "blue"] });
        form.AddTextField(page, "id", new PdfRectangle(10, 120, 150, 140), new PdfTextFieldOptions { Value = "42", IsReadOnly = true, Alignment = PdfTextAlignment.Right });
        form.AddSignatureField(page, "signature", new PdfRectangle(10, 10, 190, 60));
        return document;
    }

    [Fact]
    public void Example_CreateForm()
    {
        #region Pdf_CreateForm
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);
        var form = document.Form;
        form.AddTextField(page, "name", new PdfRectangle(50, 780, 300, 800), new PdfTextFieldOptions { Value = "Jane Doe", IsRequired = true });
        form.AddTextField(page, "address.city", new PdfRectangle(50, 750, 300, 770));
        form.AddCheckBox(page, "agree", new PdfRectangle(50, 720, 65, 735), new PdfCheckBoxOptions { IsChecked = true });
        form.AddRadioButtons("size",
        [
            new PdfRadioButton(page, new PdfRectangle(50, 690, 65, 705), "S"),
            new PdfRadioButton(page, new PdfRectangle(80, 690, 95, 705), "L"),
        ], new PdfRadioButtonOptions { Value = "S" });
        form.AddComboBox(page, "country", new PdfRectangle(50, 650, 200, 670), [new PdfChoiceOption("fr", "France"), new PdfChoiceOption("de", "Germany")], new PdfChoiceFieldOptions { SelectedValues = ["fr"] });
        form.AddSignatureField(page, "signature", new PdfRectangle(50, 550, 250, 620));

        var data = document.ToByteArray();
        #endregion

        var read = PdfDocument.Open(data).Form;
        Assert.Equal(["name", "address.city", "agree", "size", "country", "signature"], read.GetFields().Select(field => field.FullName));
        Assert.Equal("Jane Doe", read.FindField("name")!.Value);
        Assert.True(read.FindField("name")!.IsRequired);
        Assert.Null(read.FindField("address.city")!.Value);
        Assert.True(((PdfCheckBoxField)read.FindField("agree")!).IsChecked);
        Assert.Equal("S", read.FindField("size")!.Value);
        Assert.Equal("fr", read.FindField("country")!.Value);
        Assert.False(((PdfSignatureField)read.FindField("signature")!).IsSigned);
    }

    [Fact]
    public void CreatedFieldsAreRead()
    {
        var read = PdfDocument.Open(CreateFields().ToByteArray()).Form;

        var fields = read.GetFields();
        Assert.Equal(["name", "address.street", "address.zip", "agree", "size", "country", "colors", "id", "signature"], fields.Select(field => field.FullName));
        Assert.Equal([typeof(PdfTextField), typeof(PdfTextField), typeof(PdfTextField), typeof(PdfCheckBoxField), typeof(PdfRadioButtonField), typeof(PdfChoiceField), typeof(PdfChoiceField), typeof(PdfTextField), typeof(PdfSignatureField)], fields.Select(field => field.GetType()));
        Assert.HasCount(11, read.Document.Pages[0].Annotations);

        var name = (PdfTextField)read.FindField("name")!;
        Assert.Equal("Jane", name.Value);
        Assert.Equal("Your name", name.AlternateName);
        Assert.True(name.IsRequired);
        Assert.False(name.IsReadOnly);
        Assert.Equal("/Helv 12 Tf 0 g", name.Dictionary.Get<PdfString>(DA)!.ToText());
        var content = Encoding.Latin1.GetString(name.Dictionary.Get<PdfDictionary>(AP)!.Get<PdfStream>(PdfNames.N)!.GetDecodedData());
        Assert.Contains("/Tx BMC", content, StringComparison.Ordinal);
        Assert.Contains("(Jane) Tj", content, StringComparison.Ordinal);

        var street = (PdfTextField)read.FindField("address.street")!;
        Assert.True(street.IsMultiline);
        Assert.Equal("1 Main Street\nSpringfield", street.Value);
        Assert.False(read.FindField("address")!.IsTerminal);
        var zip = (PdfTextField)read.FindField("address.zip")!;
        Assert.True(zip.IsComb);
        Assert.Equal(5, zip.MaxLength);
        Assert.Null(zip.Value);

        var agree = (PdfCheckBoxField)read.FindField("agree")!;
        Assert.True(agree.IsChecked);
        Assert.Equal("Yes", agree.OnValue);
        Assert.Equal(PdfName.Create("Yes"), agree.Dictionary.GetName(AS));
        Assert.Equal("4", agree.Dictionary.Get<PdfDictionary>(MK)!.Get<PdfString>(PdfName.Create("CA"))!.ToText());
        Assert.Equal("/ZaDb 0 Tf 0 g", agree.Dictionary.Get<PdfString>(DA)!.ToText());

        var size = (PdfRadioButtonField)read.FindField("size")!;
        Assert.Equal(["S", "M", "L"], size.Options);
        Assert.Equal("M", size.Value);
        Assert.Equal([PdfName.Create("Off"), PdfName.Create("M"), PdfName.Create("Off")], size.Widgets.Select(widget => widget.Dictionary.GetName(AS)));
        Assert.Equal(1 << 14, size.Flags & (1 << 14)); // NoToggleToOff

        var country = (PdfChoiceField)read.FindField("country")!;
        Assert.True(country.IsComboBox);
        Assert.False(country.IsEditable);
        Assert.Equal([new PdfChoiceOption("fr", "France"), new PdfChoiceOption("de", "Germany")], country.Options);
        Assert.Equal("fr", country.Value);
        Assert.Contains("(France) Tj", Encoding.Latin1.GetString(country.Dictionary.Get<PdfDictionary>(AP)!.Get<PdfStream>(PdfNames.N)!.GetDecodedData()), StringComparison.Ordinal);
        var colors = (PdfChoiceField)read.FindField("colors")!;
        Assert.False(colors.IsComboBox);
        Assert.True(colors.IsMultiSelect);
        Assert.Equal(["red", "blue"], colors.SelectedValues);

        var id = (PdfTextField)read.FindField("id")!;
        Assert.True(id.IsReadOnly);
        Assert.Equal(2, id.Dictionary.GetInteger(PdfName.Create("Q"), 0));

        var signature = (PdfSignatureField)read.FindField("signature")!;
        Assert.False(signature.IsSigned);
        Assert.NotNull(signature.Dictionary.Get<PdfDictionary>(AP)!.Get<PdfStream>(PdfNames.N));

        var fonts = read.Dictionary!.Get<PdfDictionary>(DR)!.Get<PdfDictionary>(PdfNames.Font)!;
        Assert.Equal(["Helv", "ZaDb"], fonts.Select(entry => entry.Key.Value));
        Assert.Equal(["Helvetica", "ZapfDingbats"], fonts.Select(entry => ((PdfDictionary)PdfReference.ResolveIfReference(entry.Value)).GetName(PdfNames.BaseFont)!.Value));
    }

    [Fact]
    public void CreatedFieldsAreFilledAndFlattened()
    {
        var data = CreateFields().ToByteArray();
        var document = PdfDocument.Open(data);
        var form = document.Form;
        form.FindField("name")!.Value = "John";
        form.FindField("address.zip")!.Value = "75001";
        ((PdfCheckBoxField)form.FindField("agree")!).IsChecked = false;
        form.FindField("size")!.Value = "L";
        form.FindField("country")!.Value = "de";
        ((PdfChoiceField)form.FindField("colors")!).SetSelectedValues(["green"]);
        Assert.Throws<InvalidOperationException>(() => form.FindField("id")!.Value = "43");
        using var output = new MemoryStream();
        document.SaveIncremental(output);

        var read = PdfDocument.Open(output.ToArray());
        Assert.Equal("John", read.Form.FindField("name")!.Value);
        Assert.Equal("75001", read.Form.FindField("address.zip")!.Value);
        Assert.False(((PdfCheckBoxField)read.Form.FindField("agree")!).IsChecked);
        Assert.Equal(PdfName.Create("Off"), read.Form.FindField("agree")!.Dictionary.GetName(AS));
        Assert.Equal("L", read.Form.FindField("size")!.Value);
        Assert.Equal("de", read.Form.FindField("country")!.Value);
        Assert.Equal(["green"], ((PdfChoiceField)read.Form.FindField("colors")!).SelectedValues);

        read.Form.Flatten();
        var flattened = PdfDocument.Open(read.ToByteArray());
        var text = flattened.Pages[0].ExtractText();
        Assert.Contains("John", text, StringComparison.Ordinal);
        Assert.Contains("Springfield", text, StringComparison.Ordinal);
        Assert.Contains("Germany", text, StringComparison.Ordinal);
        Assert.Contains("Green", text, StringComparison.Ordinal);
        Assert.Contains("42", text, StringComparison.Ordinal);
        Assert.Contains("75001", text.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);

        // The signature field is not signed: it is flattened with the others
        Assert.False(flattened.Form.Exists);
        Assert.Empty(flattened.Pages[0].Annotations);

        // The radio button L is on, M is off; the check box is off
        using var image = flattened.Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false });
        Assert.True(image.Frames[0][77, 400 - 248].R < 128);
        Assert.Equal(255, image.Frames[0][47, 400 - 248].R);
        Assert.Equal(255, image.Frames[0][17, 400 - 278].R);
    }

    [Fact]
    public void CheckedButtonsAreDrawn()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(new PdfSize(100, 40));
        var styles = Enum.GetValues<PdfCheckBoxStyle>();
        for (var i = 0; i < styles.Length; i++)
        {
            document.Form.AddCheckBox(page, "box" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), PdfRectangle.FromSize(5 + (i * 15), 5, 12, 12), new PdfCheckBoxOptions { IsChecked = true, Style = styles[i], BorderColor = null, TextColor = PdfColor.FromRgb(1, 0, 0) });
        }

        using var image = document.Pages[0].Render(new PdfRenderOptions { UseSystemFonts = false });

        // The symbol of each box is red, inside the box
        var frame = image.Frames[0];
        for (var i = 0; i < styles.Length; i++)
        {
            var red = 0;
            for (var y = 40 - 17; y < 40 - 5; y++)
            {
                for (var x = 5 + (i * 15); x < 17 + (i * 15); x++)
                {
                    red += frame[x, y] is { R: > 200, G: < 100 } ? 1 : 0;
                }
            }

            Assert.True(red > 4, $"{styles[i]}: {red.ToString(System.Globalization.CultureInfo.InvariantCulture)} red pixels");
        }

        Assert.Equal(255, frame[3, 20].G);
    }

    [Fact]
    public void Example_CreateFieldWithEmbeddedFont()
    {
        var fontPath = (RepositoryPaths.Fonts / "noto-sans/NotoSans-Regular.subset.ttf").Value;

        #region Pdf_CreateFieldWithEmbeddedFont
        var font = PdfFont.Load(fontPath);
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);

        // The font is added to the default resources of the form, embedded whole so that viewers can show what users type
        document.Form.AddTextField(page, "comment", new PdfRectangle(50, 780, 300, 800), new PdfTextFieldOptions { Font = font, Value = "Été “fine” — 1€" });
        var data = document.ToByteArray();

        // The fonts of a saved document cannot show new text: give the font again to fill the fields that use it
        var filled = PdfDocument.Open(data);
        filled.Form.AddFont(font);
        filled.Form.FindField("comment")!.Value = "AVATAR Office";
        #endregion

        var read = PdfDocument.Open(data);
        var field = read.Form.FindField("comment")!;
        Assert.Equal("/F1 12 Tf 0 g", field.Dictionary.Get<PdfString>(DA)!.ToText());
        var resource = read.Form.Dictionary!.Get<PdfDictionary>(DR)!.Get<PdfDictionary>(PdfNames.Font)!.Get<PdfDictionary>(PdfName.Create("F1"))!;
        Assert.Equal(PdfNames.Type0, resource.GetName(PdfNames.Subtype));

        // Embedded whole: the name has no subset tag, and every glyph has a width
        Assert.Equal(font.PostScriptName, resource.GetName(PdfNames.BaseFont)!.Value);
        var descendant = (PdfDictionary)resource.Get<PdfArray>(PdfNames.DescendantFonts)!.GetResolved(0);
        var widths = descendant.Get<PdfArray>(PdfNames.W)!;
        Assert.Equal(0, ((PdfInteger)widths[0]).Value);
        var glyphCount = ((Meziantou.Framework.Pdf.Fonts.EmbeddedPdfFont)font).Face.GlyphCount;
        Assert.HasCount(glyphCount, (PdfArray)widths[1]);

        // The appearance uses the same font object
        var appearance = field.Dictionary.Get<PdfDictionary>(AP)!.Get<PdfStream>(PdfNames.N)!;
        Assert.Same(resource, appearance.Dictionary.Get<PdfDictionary>(PdfNames.Resources)!.Get<PdfDictionary>(PdfNames.Font)!.Select(entry => PdfReference.ResolveIfReference(entry.Value)).Single());

        // The value stays readable when the form is flattened
        read.Form.Flatten();
        Assert.Equal("Été “fine” — 1€", PdfDocument.Open(read.ToByteArray()).Pages[0].ExtractText());

        // The filled document draws the new value with the font given again, and keeps the font of the file for viewers
        var refilled = PdfDocument.Open(filled.ToByteArray());
        var filledField = refilled.Form.FindField("comment")!;
        Assert.Equal("/F1 12 Tf 0 g", filledField.Dictionary.Get<PdfString>(DA)!.ToText());
        var filledFont = (PdfDictionary)PdfReference.ResolveIfReference(filledField.Dictionary.Get<PdfDictionary>(AP)!.Get<PdfStream>(PdfNames.N)!.Dictionary.Get<PdfDictionary>(PdfNames.Resources)!.Get<PdfDictionary>(PdfNames.Font)!.Single().Value);
        Assert.Equal(PdfNames.Type0, filledFont.GetName(PdfNames.Subtype));
        Assert.EndsWith("+" + font.PostScriptName, filledFont.GetName(PdfNames.BaseFont)!.Value, StringComparison.Ordinal);
        Assert.Equal(font.PostScriptName, refilled.Form.Dictionary!.Get<PdfDictionary>(DR)!.Get<PdfDictionary>(PdfNames.Font)!.Get<PdfDictionary>(PdfName.Create("F1"))!.GetName(PdfNames.BaseFont)!.Value);
        refilled.Form.Flatten();
        Assert.Equal("AVATAR Office", PdfDocument.Open(refilled.ToByteArray()).Pages[0].ExtractText());
    }

    [Fact]
    public void AReadOnlyFieldEmbedsASubsetOfItsFont()
    {
        var font = PdfFont.Load(RepositoryPaths.Fonts / "noto-sans/NotoSans-Regular.subset.ttf");
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A5);
        Assert.Equal("F1", document.Form.AddTextField(page, "a", new PdfRectangle(10, 10, 200, 30), new PdfTextFieldOptions { Font = font, Value = "Office", IsReadOnly = true }).Dictionary.Get<PdfString>(DA)!.ToText()[1..3]);
        document.Form.AddTextField(page, "b", new PdfRectangle(10, 40, 200, 60), new PdfTextFieldOptions { Font = font, Value = "AVATAR", SubsetFont = true });

        // The second field uses the same font resource
        Assert.Equal("/F1 12 Tf 0 g", document.Form.FindField("b")!.Dictionary.Get<PdfString>(DA)!.ToText());
        Assert.Equal("Helv", document.Form.AddFont(PdfFont.GetStandardFont(PdfStandardFont.Helvetica)));
        Assert.Equal("Helv", document.Form.AddFont(PdfFont.GetStandardFont(PdfStandardFont.Helvetica)));

        var read = PdfDocument.Open(document.ToByteArray());
        var resource = read.Form.Dictionary!.Get<PdfDictionary>(DR)!.Get<PdfDictionary>(PdfNames.Font)!.Get<PdfDictionary>(PdfName.Create("F1"))!;
        Assert.EndsWith("+" + font.PostScriptName, resource.GetName(PdfNames.BaseFont)!.Value, StringComparison.Ordinal);
        read.Form.Flatten();
        Assert.Equal("Office\nAVATAR", PdfDocument.Open(read.ToByteArray()).Pages[0].ExtractText());
    }

    [Fact]
    public void FieldsAreCreatedInAnExistingForm()
    {
        var document = PdfDocument.Open(CreateForm());
        var page = document.Pages[0];

        // A field in an existing group, and a text that its default font cannot show
        var zip = document.Form.AddTextField(page, "address.zip", new PdfRectangle(160, 220, 290, 240), new PdfTextFieldOptions { Value = "75001" });
        Assert.Equal("address.zip", zip.FullName);
        Assert.Same(document.Form.FindField("address")!.Dictionary, zip.Parent!.Dictionary);
        Assert.Equal(["city", "zip"], document.Form.FindField("address")!.Children.Select(field => field.Name));

        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal("75001", read.Form.FindField("address.zip")!.Value);
        Assert.HasCount(10, read.Form.GetFields());
        Assert.Equal(["Helv", "TiRo"], read.Form.Dictionary!.Get<PdfDictionary>(DR)!.Get<PdfDictionary>(PdfNames.Font)!.Select(entry => entry.Key.Value));
    }

    [Fact]
    public void InvalidFieldsAreRefused()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A5);
        var other = new PdfDocument().Pages.Add(PdfSize.A5);
        var form = document.Form;
        var rectangle = new PdfRectangle(10, 10, 100, 30);
        form.AddTextField(page, "name", rectangle);
        form.AddTextField(page, "group.child", rectangle);

        Assert.Throws<ArgumentException>(() => form.AddTextField(page, "name", rectangle));
        Assert.Throws<ArgumentException>(() => form.AddTextField(page, "group.child", rectangle));
        Assert.Throws<ArgumentException>(() => form.AddTextField(page, "name.child", rectangle));
        Assert.Throws<ArgumentException>(() => form.AddTextField(page, "a..b", rectangle));
        Assert.Throws<ArgumentException>(() => form.AddTextField(page, "", rectangle));
        Assert.Throws<ArgumentException>(() => form.AddTextField(other, "other", rectangle));
        Assert.Throws<ArgumentException>(() => form.AddTextField(page, "comb", rectangle, new PdfTextFieldOptions { IsComb = true }));
        Assert.Throws<ArgumentException>(() => form.AddTextField(page, "long", rectangle, new PdfTextFieldOptions { MaxLength = 2, Value = "abc" }));
        Assert.Throws<ArgumentException>(() => form.AddTextField(page, "justified", rectangle, new PdfTextFieldOptions { Alignment = PdfTextAlignment.Justify }));
        Assert.Throws<ArgumentException>(() => form.AddCheckBox(page, "off", rectangle, new PdfCheckBoxOptions { OnValue = "Off" }));
        Assert.Throws<ArgumentException>(() => form.AddRadioButtons("none", []));
        Assert.Throws<ArgumentException>(() => form.AddRadioButtons("missing", [new PdfRadioButton(page, rectangle, "A")], new PdfRadioButtonOptions { Value = "B" }));
        Assert.Throws<ArgumentException>(() => form.AddComboBox(page, "combo", rectangle, [new PdfChoiceOption("a", "A")], new PdfChoiceFieldOptions { IsMultiSelect = true }));
        Assert.Throws<ArgumentException>(() => form.AddComboBox(page, "combo", rectangle, [new PdfChoiceOption("a", "A")], new PdfChoiceFieldOptions { SelectedValues = ["b"] }));
        Assert.Throws<ArgumentException>(() => form.AddListBox(page, "list", rectangle, [new PdfChoiceOption("a", "A")], new PdfChoiceFieldOptions { IsEditable = true }));
        Assert.Throws<ArgumentException>(() => form.AddListBox(page, "list", rectangle, [new PdfChoiceOption("a", "A"), new PdfChoiceOption("b", "B")], new PdfChoiceFieldOptions { SelectedValues = ["a", "b"] }));

        // An editable combo box accepts a text that is not an option
        var combo = form.AddComboBox(page, "combo", rectangle, [new PdfChoiceOption("a", "A")], new PdfChoiceFieldOptions { IsEditable = true, SelectedValues = ["other"] });
        Assert.True(combo.IsEditable);
        Assert.Equal("other", combo.Value);

        // The refused fields left nothing behind
        Assert.Equal(["name", "group.child", "combo"], form.GetFields().Select(field => field.FullName));
        Assert.HasCount(3, page.Annotations);
    }
}
