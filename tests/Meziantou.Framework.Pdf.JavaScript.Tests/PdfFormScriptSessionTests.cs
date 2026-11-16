using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.JavaScript.Tests;

public sealed class PdfFormScriptSessionTests
{
    /// <summary>
    /// An order form as form designers write it: the quantity and the price are numbers (AF keystroke and format), the
    /// total is calculated (AFSimple_Calculate) and formatted as currency, the tax is a percentage of the total computed by
    /// a custom script that calls a function of the document-level scripts, and the date is formatted.
    /// </summary>
    internal static PdfDocument CreateOrderForm()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);
        var form = document.Form;
        var quantity = form.AddTextField(page, "Quantity", new PdfRectangle(50, 780, 200, 800));
        var price = form.AddTextField(page, "Price", new PdfRectangle(50, 750, 200, 770));
        var total = form.AddTextField(page, "Total", new PdfRectangle(50, 720, 200, 740), new PdfTextFieldOptions { IsReadOnly = true });
        var tax = form.AddTextField(page, "Tax", new PdfRectangle(50, 690, 200, 710), new PdfTextFieldOptions { IsReadOnly = true });
        var date = form.AddTextField(page, "Date", new PdfRectangle(50, 660, 200, 680));
        form.AddTextField(page, "Notes", new PdfRectangle(50, 630, 200, 650));

        SetActions(quantity, keystroke: "AFNumber_Keystroke(0, 1, 0, 0, \"\", true);", validate: "AFRange_Validate(true, 1, true, 100);");
        SetActions(price, keystroke: "AFNumber_Keystroke(2, 0, 0, 0, \"\", true);", format: "AFNumber_Format(2, 0, 0, 0, \"$\", true);");
        SetActions(total, calculate: "AFSimple_Calculate(\"PRD\", new Array (\"Quantity\", \"Price\"));", format: "AFNumber_Format(2, 0, 0, 0, \"$\", true);");
        SetActions(tax, calculate: "event.value = taxOf(this.getField(\"Total\").value);", format: "AFPercent_Format(1, 0);");
        SetActions(date, keystroke: "AFDate_KeystrokeEx(\"yyyy-mm-dd\");", format: "AFDate_FormatEx(\"mmm d, yyyy\");");

        form.Dictionary![PdfName.Create("CO")] = new PdfArray(total.Reference!, tax.Reference!);
        document.Catalog[PdfName.Create("Names")] = new PdfDictionary
        {
            [PdfName.Create("JavaScript")] = new PdfDictionary
            {
                [PdfName.Create("Names")] = new PdfArray(PdfString.FromText("rates"), JavaScriptAction("var rate = 0.2; function taxOf(total) { return total > 0 ? rate : 0; }")),
            },
        };
        return PdfDocument.Open(document.ToByteArray());
    }

    private static void SetActions(PdfFormField field, string? keystroke = null, string? format = null, string? validate = null, string? calculate = null)
    {
        var actions = new PdfDictionary();
        void Add(string key, string? script)
        {
            if (script is not null)
            {
                actions[PdfName.Create(key)] = JavaScriptAction(script);
            }
        }

        Add("K", keystroke);
        Add("F", format);
        Add("V", validate);
        Add("C", calculate);
        field.Dictionary[PdfName.Create("AA")] = actions;
    }

    private static PdfDictionary JavaScriptAction(string script) => new()
    {
        [PdfName.Create("S")] = PdfName.Create("JavaScript"),
        [PdfName.Create("JS")] = PdfString.FromText(script),
    };

    [Fact]
    public void CalculationsRunWhenAValueIsSet()
    {
        var document = CreateOrderForm();

        #region PdfJavaScript_SetValue
        using var scripts = document.Form.CreateScriptSession(new PdfFormScriptOptions
        {
            Timeout = TimeSpan.FromSeconds(1),
            MaxStatements = 1_000_000,
            MemoryLimit = 64 * 1024 * 1024,
        });

        scripts.SetValue("Price", "12.5");
        var result = scripts.SetValue("Quantity", "3");
        #endregion

        Assert.True(result.IsAccepted);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(["Quantity", "Total", "Tax"], result.Changes.Select(change => change.Field.FullName));
        Assert.Equal("3", document.Form.FindField("Quantity")!.Value);
        Assert.Equal("37.5", document.Form.FindField("Total")!.Value);
        var total = result.Changes.Single(change => change.Field.FullName == "Total");
        Assert.Equal("$37.50", total.DisplayText);

        // The value keeps the number, the appearance shows the text of the format script
        var read = PdfDocument.Open(document.ToByteArray());
        Assert.Equal("37.5", read.Form.FindField("Total")!.Value);
        var appearance = read.Form.FindField("Total")!.Dictionary.Get<PdfDictionary>(PdfName.Create("AP"))!.Get<PdfStream>(PdfName.Create("N"))!;
        Assert.Contains("($37.50) Tj", System.Text.Encoding.Latin1.GetString(appearance.GetDecodedData()), StringComparison.Ordinal);
    }

    [Fact]
    public void KeystrokeAndValidationScriptsRejectValues()
    {
        var document = CreateOrderForm();
        using var scripts = document.Form.CreateScriptSession();

        var notANumber = scripts.SetValue("Quantity", "three");
        var malformed = scripts.SetValue("Quantity", "1.2.3");
        var outOfRange = scripts.SetValue("Quantity", "500");
        var badDate = scripts.SetValue("Date", "2026-02-30");

        Assert.False(notANumber.IsAccepted);
        Assert.Empty(notANumber.Changes);
        Assert.Equal(PdfFormScriptDiagnosticKind.Alert, Assert.Single(notANumber.Diagnostics).Kind);
        Assert.False(malformed.IsAccepted);
        Assert.False(outOfRange.IsAccepted);
        Assert.False(badDate.IsAccepted);
        Assert.Null(document.Form.FindField("Quantity")!.Value);

        var date = scripts.SetValue("Date", "2026-03-01");
        Assert.True(date.IsAccepted);
        Assert.Equal("Mar 1, 2026", date.Changes.Single(change => change.Field.FullName == "Date").DisplayText);
    }

    [Fact]
    public void SetValuesCalculatesOnce()
    {
        var document = CreateOrderForm();
        using var scripts = document.Form.CreateScriptSession();

        var result = scripts.SetValues(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Price"] = "10", ["Quantity"] = "2" });

        Assert.True(result.IsAccepted);
        Assert.Equal(["Price", "Quantity", "Total", "Tax"], result.Changes.Select(change => change.Field.FullName));
        Assert.Equal("20", document.Form.FindField("Total")!.Value);
        Assert.Equal("$10.00", result.Changes[0].DisplayText);
    }

    [Fact]
    public void SettingTheSameValueKeepsTheFormattedAppearance()
    {
        var document = CreateOrderForm();
        using var scripts = document.Form.CreateScriptSession();
        scripts.SetValue("Price", "12.5");

        var result = scripts.SetValue("Price", "12.5");

        Assert.True(result.IsAccepted);
        Assert.Empty(result.Changes);
        var appearance = document.Form.FindField("Price")!.Dictionary.Get<PdfDictionary>(PdfName.Create("AP"))!.Get<PdfStream>(PdfName.Create("N"))!;
        Assert.Contains("($12.50) Tj", System.Text.Encoding.Latin1.GetString(appearance.GetDecodedData()), StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentAndFieldScriptsShareTheDocument()
    {
        var document = CreateOrderForm();
        var names = (PdfArray)((PdfDictionary)((PdfDictionary)document.Catalog.GetResolved(PdfName.Create("Names"))!).GetResolved(PdfName.Create("JavaScript"))!).GetResolved(PdfName.Create("Names"))!;
        names.Add(PdfString.FromText("settings"));
        names.Add(JavaScriptAction("this.multiplier = 3;"));
        SetActions(document.Form.FindField("Tax")!, calculate: "event.value = this.multiplier * this.getField(\"Total\").value;");
        using var scripts = document.Form.CreateScriptSession();

        scripts.SetValues(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Price"] = "10", ["Quantity"] = "2" });

        Assert.Equal("60", document.Form.FindField("Tax")!.Value);
    }

    [Fact]
    public void ScriptsNeverRunImplicitly()
    {
        var document = CreateOrderForm();

        document.Form.FindField("Quantity")!.Value = "4";

        Assert.Null(document.Form.FindField("Total")!.Value);
    }

    [Fact]
    public void ErrorsLimitsAndBlockedCallsAreReported()
    {
        var document = CreateOrderForm();
        var notes = document.Form.FindField("Notes")!;
        SetActions(notes, validate: "this.submitForm('https://example.com'); app.launchURL('https://example.com'); app.alert('Saved');");
        SetActions(document.Form.FindField("Total")!, calculate: "while (true) { }");
        SetActions(document.Form.FindField("Tax")!, calculate: "undefinedFunction();");
        var alerts = new List<string>();
        using var scripts = document.Form.CreateScriptSession(new PdfFormScriptOptions { Timeout = TimeSpan.FromMilliseconds(200), MaxStatements = 100_000, Alert = alerts.Add });

        var result = scripts.SetValue("Notes", "hello");

        Assert.True(result.IsAccepted);
        Assert.Equal(
            [PdfFormScriptDiagnosticKind.BlockedCall, PdfFormScriptDiagnosticKind.BlockedCall, PdfFormScriptDiagnosticKind.Alert, PdfFormScriptDiagnosticKind.LimitExceeded, PdfFormScriptDiagnosticKind.Error],
            result.Diagnostics.Select(diagnostic => diagnostic.Kind));
        Assert.Equal(["Saved"], alerts);
        Assert.Equal("Total", result.Diagnostics[3].FieldName);
        Assert.Equal(PdfFormScriptEvent.Calculate, result.Diagnostics[3].Event);
        Assert.Equal("hello", document.Form.FindField("Notes")!.Value);

        using var strict = document.Form.CreateScriptSession(new PdfFormScriptOptions { MaxStatements = 100_000, ThrowOnScriptError = true });
        Assert.Throws<PdfFormScriptException>(() => strict.Recalculate());
    }

    [Theory]
    [InlineData("util.printf('%,0.2f', 1234567.891)", "1,234,567.89")]
    [InlineData("util.printf('%05d|%s|%x', 42, 'a', 255)", "00042|a|ff")]
    [InlineData("util.printd('yyyy-mm-dd HH:MM', new Date(2026, 2, 1, 14, 5))", "2026-03-01 14:05")]
    [InlineData("util.printd('dddd d mmmm yy, h tt', new Date(2026, 2, 1, 14, 5))", "Sunday 1 March 26, 2 pm")]
    [InlineData("util.printd('mm/dd/yyyy', util.scand('mm/dd/yyyy', '03/01/2026'))", "03/01/2026")]
    [InlineData("String(util.scand('yyyy-mm-dd', '2026-02-30'))", "null")]
    [InlineData("util.printd('HH:MM', util.scand('h:MM tt', '12:30 am'))", "00:30")]
    [InlineData("util.printd('HH:MM', util.scand('h:MM tt', '12:30 pm'))", "12:30")]
    [InlineData("util.printd('HH:MM', util.scand('h:MM tt', '1:30 pm'))", "13:30")]
    [InlineData("String(AFMakeNumber('1.2.3'))", "null")]
    [InlineData("String(AFMakeNumber('(1,234.5)'))", "-1234.5")]
    [InlineData("var event = { value: -1234.5 }; AFNumber_Format(1, 2, 2, 0, ' €', false); event.value", "(1.234,5 €)")]
    [InlineData("var event = { value: '5551234567' }; AFSpecial_Format(2); event.value", "(555) 123-4567")]
    [InlineData("var event = { value: 0.125 }; AFPercent_Format(2, 0); event.value", "12.50%")]
    public void ViewerFunctionsFormatWithFixedRules(string script, string expected)
    {
        var document = CreateOrderForm();
        using var scripts = document.Form.CreateScriptSession();

        Assert.Equal(expected, scripts.Evaluate(script));
    }
}
