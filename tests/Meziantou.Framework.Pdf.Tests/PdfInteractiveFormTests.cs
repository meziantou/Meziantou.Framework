using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfInteractiveFormTests
{
    private static readonly PdfName AP = PdfName.Create("AP");
    private static readonly PdfName AS = PdfName.Create("AS");

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
}
