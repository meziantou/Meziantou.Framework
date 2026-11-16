using Meziantou.Framework.JavaScriptEngine;

namespace Meziantou.Framework.Pdf.JavaScript;

/// <summary>
/// Runs the scripts of an interactive form (ISO 32000-2, 12.6.4.17, 12.7.6) in a sandbox, when the host sets values:
/// the keystroke and validation scripts of the field set, then the calculation scripts of the form in their order
/// (<see cref="PdfInteractiveForm.CalculationOrder"/>), then the format scripts of the fields that changed.
/// </summary>
/// <remarks>
/// <para>
/// The scripts never run implicitly: opening a document and setting <see cref="PdfFormField.Value"/> run none. The
/// document-level scripts (<see cref="PdfDocument.JavaScripts"/>) run once, when the session is created; the open action
/// never runs.
/// </para>
/// <para>
/// The scripts run in a realm of their own, with the limits of the options. They see the <c>event</c> object, the fields
/// (<c>this.getField</c>), the AF functions of the viewers (<c>AFSimple_Calculate</c>, <c>AFNumber_Format</c>...),
/// <c>util.printf</c>, <c>util.printd</c>, <c>util.scand</c> and <c>app.alert</c>. The functions that reach outside of
/// the form (submit, mail, print, open a URL or a document, menu items) do nothing, and are reported in the diagnostics.
/// The numbers and the dates are formatted with fixed rules, not with the culture of the process.
/// </para>
/// </remarks>
public sealed class PdfFormScriptSession : IDisposable
{
    private readonly PdfInteractiveForm _form;
    private readonly PdfFormScriptOptions _options;
    private readonly JsEngine _engine;
    private readonly JsExecutionLimits _limits;
    private readonly JsRealm _realm;
    private readonly JsValue _document;
    private readonly JsValue _makeEvent;
    private readonly Dictionary<string, JsValue> _compiled = new(StringComparer.Ordinal);
    private List<PdfFormScriptDiagnostic> _diagnostics = [];
    private Dictionary<PdfFormField, PdfFormFieldChange>? _changes;
    private List<PdfFormFieldChange>? _changeOrder;
    private PdfFormScriptEvent _currentEvent;
    private string? _currentField;
    private bool _isDisposed;

    internal PdfFormScriptSession(PdfInteractiveForm form, PdfFormScriptOptions options)
    {
        _form = form;
        _options = options;
        // The limits apply to each script of the form (an execution scope), not to the functions of the viewers the
        // session defines first
        _limits = new JsExecutionLimits
        {
            Timeout = options.Timeout,
            MaxStatements = options.MaxStatements,
            MemoryLimit = options.MemoryLimit,
            CancellationToken = options.CancellationToken,
        };
        _engine = new JsEngine(new JsEngineOptions
        {
            RecursionLimit = options.RecursionLimit,
            RegExpTimeout = options.Timeout,
            TimeZone = "UTC",
            DefaultLocale = "en-US",
            AllowStringCompilation = false,
        });
        _realm = _engine.CreateRealm();
        using (_engine.Enter())
        {
            var host = _realm.CreateObject();
            host.SetProperty("exists", _realm.CreateFunction("exists", 1, (_, arguments) => FindField(Argument(arguments, 0)) is not null));
            host.SetProperty("getValue", _realm.CreateFunction("getValue", 1, (_, arguments) => FindField(Argument(arguments, 0)) is { } field ? JsValue.FromString(GetValue(field) ?? "") : JsValue.Undefined));
            host.SetProperty("setValue", _realm.CreateFunction("setValue", 2, (_, arguments) =>
            {
                if (FindField(Argument(arguments, 0)) is { } field)
                {
                    Commit(field, Argument(arguments, 1), displayText: null, fromScript: true);
                }

                return JsValue.Undefined;
            }));
            host.SetProperty("info", _realm.CreateFunction("info", 2, (_, arguments) => GetInfo(Argument(arguments, 0), Argument(arguments, 1))));
            host.SetProperty("alert", _realm.CreateFunction("alert", 1, (_, arguments) =>
            {
                var message = Argument(arguments, 0);
                Report(PdfFormScriptDiagnosticKind.Alert, message);
                _options.Alert?.Invoke(message);
                return 1;
            }));
            host.SetProperty("blocked", _realm.CreateFunction("blocked", 1, (_, arguments) =>
            {
                Report(PdfFormScriptDiagnosticKind.BlockedCall, $"The call to {Argument(arguments, 0)} was blocked");
                return JsValue.Undefined;
            }));
            _realm.GlobalObject.SetProperty("__pdf", host);
        }

        _realm.Execute(ReadResource("Prelude.js"));
        var api = _realm.Execute(ReadResource("FormApi.js"));
        using (_engine.Enter())
        {
            var apiObject = api.AsObject();
            _document = apiObject.GetProperty("doc");
            _makeEvent = apiObject.GetProperty("makeEvent");
        }

        // The document-level scripts, once (12.6.4.17): they declare the functions the field scripts call
        _currentEvent = PdfFormScriptEvent.Document;
        foreach (var (name, script) in form.Document.JavaScripts)
        {
            _currentField = null;
            Run(() => _realm.Execute(script), name);
        }

        InitializationDiagnostics = _diagnostics;
    }

    /// <summary>Gets the diagnostics of the document-level scripts, run when the session was created.</summary>
    public IReadOnlyList<PdfFormScriptDiagnostic> InitializationDiagnostics { get; }

    /// <summary>
    /// Sets the value of a field as a user would commit it: the keystroke and validation scripts of the field may reject
    /// it, then the calculation scripts of the form run, then the format scripts of the fields that changed.
    /// </summary>
    /// <param name="fullName">The fully qualified name of the field.</param>
    /// <param name="value">The value, or <see langword="null"/> to clear it.</param>
    /// <returns>Whether the value was accepted, the fields that changed, and the diagnostics of the scripts.</returns>
    /// <exception cref="ArgumentException">The form has no field of this name.</exception>
    /// <exception cref="PdfFormScriptException">A script failed, and <see cref="PdfFormScriptOptions.ThrowOnScriptError"/> is set.</exception>
    public PdfFormScriptResult SetValue(string fullName, string? value) => SetValues(new Dictionary<string, string?>(StringComparer.Ordinal) { [fullName] = value });

    /// <summary>
    /// Sets the values of several fields: the keystroke and validation scripts of each, then the calculation scripts of the
    /// form once, at the end, then the format scripts of the fields that changed.
    /// </summary>
    /// <param name="values">The values, by fully qualified name of field.</param>
    /// <returns>Whether every value was accepted, the fields that changed, and the diagnostics of the scripts.</returns>
    /// <exception cref="ArgumentException">The form has no field of one of the names.</exception>
    /// <exception cref="PdfFormScriptException">A script failed, and <see cref="PdfFormScriptOptions.ThrowOnScriptError"/> is set.</exception>
    public PdfFormScriptResult SetValues(IEnumerable<KeyValuePair<string, string?>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var list = values.ToList();
        var fields = new List<(PdfFormField Field, string? Value)>();
        foreach (var (name, value) in list)
        {
            var field = _form.FindField(name) ?? throw new ArgumentException($"The form has no field '{name}'", nameof(values));
            fields.Add((field, value));
        }

        BeginOperation();
        var accepted = true;
        var committed = 0;
        foreach (var (field, proposed) in fields)
        {
            if (field.IsReadOnly || !field.IsTerminal)
            {
                accepted = false;
                Report(PdfFormScriptDiagnosticKind.Error, $"The field '{field.FullName}' cannot be set: it is read-only or not a terminal field", field.FullName);
                continue;
            }

            var value = proposed ?? "";
            var actions = field.Actions;
            if (actions.Keystroke is { } keystroke)
            {
                var (rc, result) = RunEvent(PdfFormScriptEvent.Keystroke, field, keystroke.Script, value, source: null);
                if (!rc)
                {
                    accepted = false;
                    continue;
                }

                value = result ?? value;
            }

            if (actions.Validate is { } validate)
            {
                var (rc, result) = RunEvent(PdfFormScriptEvent.Validate, field, validate.Script, value, source: null);
                if (!rc)
                {
                    accepted = false;
                    continue;
                }

                value = result ?? value;
            }

            Commit(field, proposed is null && value.Length == 0 ? null : value, displayText: null, fromScript: false);
            committed++;
        }

        // A value rejected changes nothing: the calculations run when a value was committed
        if (committed > 0)
        {
            Calculate(fields[^1].Field);
        }
        return EndOperation(accepted);
    }

    /// <summary>Runs the calculation scripts of the form in their order, then the format scripts of the fields that changed.</summary>
    /// <returns>The fields that changed, and the diagnostics of the scripts.</returns>
    /// <exception cref="PdfFormScriptException">A script failed, and <see cref="PdfFormScriptOptions.ThrowOnScriptError"/> is set.</exception>
    public PdfFormScriptResult Recalculate()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        BeginOperation();
        Calculate(source: null);
        return EndOperation(accepted: true);
    }

    /// <summary>Evaluates an expression in the realm of the scripts, for the tests of the functions of the viewers.</summary>
    internal string Evaluate(string script) => _engine.ToString(_realm.Execute(script));

    /// <summary>Releases the engine of the scripts.</summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _engine.Dispose();
    }

    private void BeginOperation()
    {
        _diagnostics = [];
        _changes = [];
        _changeOrder = [];
    }

    private PdfFormScriptResult EndOperation(bool accepted)
    {
        // The format scripts of the fields that changed give the text their appearances show
        foreach (var change in _changeOrder!.ToList())
        {
            if (change.Field.Actions.Format is { } format)
            {
                var (rc, text) = RunEvent(PdfFormScriptEvent.Format, change.Field, format.Script, GetValue(change.Field) ?? "", source: null);
                if (rc && text is not null)
                {
                    change.DisplayText = text;
                    if (change.Field is PdfTextField textField)
                    {
                        WithoutReadOnly(textField, () => textField.SetValue(GetValue(textField) ?? "", text));
                    }
                }
            }
        }

        var result = new PdfFormScriptResult(accepted, _changeOrder!, _diagnostics);
        _changes = null;
        _changeOrder = null;
        return result;
    }

    /// <summary>Runs the calculation scripts in the calculation order (<c>/CO</c>, 12.7.3): a calculated value is committed when the script accepts it.</summary>
    private void Calculate(PdfFormField? source)
    {
        foreach (var field in _form.CalculationOrder)
        {
            if (field.Actions.Calculate is not { } calculate)
                continue;

            var current = GetValue(field) ?? "";
            var (rc, value) = RunEvent(PdfFormScriptEvent.Calculate, field, calculate.Script, current, source);
            if (rc && value is not null && !string.Equals(value, current, StringComparison.Ordinal))
            {
                Commit(field, value, displayText: null, fromScript: true);
            }
        }
    }

    /// <summary>Runs the script of an event of a field: the result of <c>event.rc</c> and the value the script left in <c>event.value</c>.</summary>
    private (bool Accepted, string? Value) RunEvent(PdfFormScriptEvent scriptEvent, PdfFormField field, string script, string value, PdfFormField? source)
    {
        _currentEvent = scriptEvent;
        _currentField = field.FullName;
        (bool, string?) result = (true, null);
        Run(() =>
        {
            var function = Compile(script);
            var eventObject = _engine.Call(_makeEvent, JsValue.Undefined, [scriptEvent.ToString(), field.FullName, value, source?.FullName ?? ""]);
            using (_engine.Enter())
            {
                _realm.GlobalObject.SetProperty("event", eventObject);
            }

            _engine.Call(function, _document, [eventObject]);
            using (_engine.Enter())
            {
                var rc = eventObject.AsObject().GetProperty("rc");
                var newValue = eventObject.AsObject().GetProperty("value");
                result = (!rc.IsBoolean || rc.AsBoolean(), newValue.IsNullOrUndefined ? "" : _engine.ToString(newValue));
            }

            return JsValue.Undefined;
        }, field.FullName);
        return result;
    }

    /// <summary>Compiles a script as a function of <c>event</c>, called with the document as <c>this</c>.</summary>
    private JsValue Compile(string script)
    {
        if (!_compiled.TryGetValue(script, out var function))
        {
            function = _realm.Execute("(function (event) {\n" + script + "\n})");
            _compiled[script] = function;
        }

        return function;
    }

    private void Run(Func<JsValue> action, string? context)
    {
        try
        {
            using var scope = _engine.EnterExecutionScope(_limits);
            action();
        }
        catch (JsTerminationException ex)
        {
            ReportFailure(PdfFormScriptDiagnosticKind.LimitExceeded, ex.Message, ex);
        }
        catch (Exception ex) when (ex is JsException or JsSyntaxErrorException or PdfException)
        {
            // PdfException: a field the script reads is damaged
            ReportFailure(PdfFormScriptDiagnosticKind.Error, ex.Message, ex);
        }

        void ReportFailure(PdfFormScriptDiagnosticKind kind, string message, Exception exception)
        {
            Report(kind, message);
            if (_options.ThrowOnScriptError)
                throw new PdfFormScriptException($"The {_currentEvent} script of {context ?? "the document"} failed: {message}", exception);
        }
    }

    private void Report(PdfFormScriptDiagnosticKind kind, string message, string? fieldName = null)
    {
        _diagnostics.Add(new PdfFormScriptDiagnostic(kind, _currentEvent, fieldName ?? _currentField, message));
    }

    /// <summary>Sets the value of a field and records the change. A script may set a read-only field, as calculations do.</summary>
    private void Commit(PdfFormField field, string? value, string? displayText, bool fromScript)
    {
        var old = GetValue(field);
        var normalized = field switch
        {
            PdfCheckBoxField or PdfRadioButtonField => value is null or "" or "Off" ? "Off" : value,
            _ => value,
        };

        // The same value: the appearance, which the format script may have written, is kept
        if (displayText is null && string.Equals(old, normalized, StringComparison.Ordinal))
            return;

        try
        {
            void Apply()
            {
                switch (field)
                {
                    case PdfTextField text when displayText is not null:
                        text.SetValue(value ?? "", displayText);
                        break;
                    case PdfCheckBoxField or PdfRadioButtonField:
                        field.Value = value is null or "" or "Off" ? null : value;
                        break;
                    case PdfPushButtonField or PdfSignatureField:
                        return;
                    default:
                        field.Value = value;
                        break;
                }
            }

            if (fromScript)
            {
                WithoutReadOnly(field, Apply);
            }
            else
            {
                Apply();
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or PdfException)
        {
            Report(PdfFormScriptDiagnosticKind.Error, $"The value of the field '{field.FullName}' cannot be set: {ex.Message}", field.FullName);
            return;
        }

        var current = GetValue(field);
        if (_changes is null || string.Equals(old, current, StringComparison.Ordinal))
            return;

        if (_changes.TryGetValue(field, out var change))
        {
            change.NewValue = current;
        }
        else
        {
            change = new PdfFormFieldChange(field, old, current, displayText);
            _changes.Add(field, change);
            _changeOrder!.Add(change);
        }
    }

    private static void WithoutReadOnly(PdfFormField field, Action action)
    {
        var readOnly = field.IsReadOnly;
        if (readOnly)
        {
            field.IsReadOnly = false;
        }

        try
        {
            action();
        }
        finally
        {
            if (readOnly)
            {
                field.IsReadOnly = true;
            }
        }
    }

    private PdfFormField? FindField(string name)
    {
        try
        {
            return _form.FindField(name);
        }
        catch (PdfException)
        {
            return null;
        }
    }

    private static string? GetValue(PdfFormField field) => field switch
    {
        PdfChoiceField { IsMultiSelect: true } choice => string.Join(",", choice.SelectedValues),
        PdfCheckBoxField or PdfRadioButtonField => field.Value ?? "Off",
        _ => field.Value,
    };

    private JsValue GetInfo(string name, string property)
    {
        if (FindField(name) is not { } field)
            return JsValue.Undefined;

        return property switch
        {
            "type" => field switch
            {
                PdfTextField => "text",
                PdfChoiceField { IsComboBox: true } => "combobox",
                PdfChoiceField => "listbox",
                PdfCheckBoxField => "checkbox",
                PdfRadioButtonField => "radiobutton",
                PdfSignatureField => "signature",
                _ => "button",
            },
            "readonly" => field.IsReadOnly,
            "required" => field.IsRequired,
            "userName" => field.AlternateName ?? "",
            _ => JsValue.Undefined,
        };
    }

    private string Argument(ReadOnlySpan<JsValue> arguments, int index) => index < arguments.Length && !arguments[index].IsNullOrUndefined ? _engine.ToString(arguments[index]) : "";

    private static string ReadResource(string name)
    {
        using var stream = typeof(PdfFormScriptSession).Assembly.GetManifestResourceStream("Meziantou.Framework.Pdf.JavaScript." + name) ?? throw new InvalidOperationException($"The resource {name} is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
