# Meziantou.Framework.Pdf.JavaScript

Runs the scripts of the interactive forms of [Meziantou.Framework.Pdf](https://www.nuget.org/packages/Meziantou.Framework.Pdf)
in a sandbox of [Meziantou.Framework.JavaScriptEngine](https://www.nuget.org/packages/Meziantou.Framework.JavaScriptEngine):
the keystroke, validation, calculation and format scripts of the fields, the AF functions of the viewers
(`AFSimple_Calculate`, `AFNumber_Format`, `AFDate_FormatEx`...) and `util.printf`, `util.printd` and `util.scand`, with
limits of time, statements and memory. The calls that reach outside of the form (submit, mail, print, URLs) do nothing
and are reported. The scripts never run implicitly.

## Usage

<!-- ref:../../tests/Meziantou.Framework.Pdf.JavaScript.Tests/PdfFormScriptSessionTests.cs;region=PdfJavaScript_SetValue;format=md-fenced-code-block;dedent=true -->
```csharp
using var scripts = document.Form.CreateScriptSession(new PdfFormScriptOptions
{
    Timeout = TimeSpan.FromSeconds(1),
    MaxStatements = 1_000_000,
    MemoryLimit = 64 * 1024 * 1024,
});

scripts.SetValue("Price", "12.5");
var result = scripts.SetValue("Quantity", "3");
```
<!-- endref -->

`SetValue` runs the keystroke and validation scripts of the field (which may reject the value), the calculation scripts
of the form in their order, then the format scripts of the fields that changed. The result lists the changes and the
diagnostics: script errors, limits exceeded, blocked calls and alerts.
