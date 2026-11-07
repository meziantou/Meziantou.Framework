using System.Collections;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Infra;
using File = Meziantou.Framework.WebPlatform.Dom.FileApi.File;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The value of a form data entry: a string or a file (https://xhr.spec.whatwg.org/#formdataentryvalue).</summary>
/// <seealso href="https://xhr.spec.whatwg.org/#formdataentryvalue"/>
[IdlInterface("FormDataEntryValue", "https://xhr.spec.whatwg.org/#formdataentryvalue")]
public readonly struct FormDataEntryValue : IUnionValue, IEquatable<FormDataEntryValue>
{
    private FormDataEntryValue(object value) => Value = value;

    /// <summary>Gets the value: a <see cref="string"/> or a <see cref="FileApi.File"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value as a string, or null when the value is a file.</summary>
    [NotInIdl]
    public string? StringValue => Value as string;

    /// <summary>Gets the value as a file, or null when the value is a string.</summary>
    [NotInIdl]
    public File? File => Value as File;

    /// <summary>Converts a string to a form data entry value.</summary>
    [NotInIdl]
    public static implicit operator FormDataEntryValue(string value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a file to a form data entry value.</summary>
    [NotInIdl]
    public static implicit operator FormDataEntryValue(File value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a form data entry value from a string.</summary>
    [NotInIdl]
    public static FormDataEntryValue FromString(string value) => value;

    /// <summary>Creates a form data entry value from a file.</summary>
    [NotInIdl]
    public static FormDataEntryValue FromFile(File value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(FormDataEntryValue other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is FormDataEntryValue other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <inheritdoc/>
    [NotInIdl]
    public override string? ToString() => Value?.ToString();

    /// <summary>Determines whether two form data entry values are equal.</summary>
    [NotInIdl]
    public static bool operator ==(FormDataEntryValue left, FormDataEntryValue right) => left.Equals(right);

    /// <summary>Determines whether two form data entry values are different.</summary>
    [NotInIdl]
    public static bool operator !=(FormDataEntryValue left, FormDataEntryValue right) => !left.Equals(right);
}

/// <summary>https://xhr.spec.whatwg.org/#interface-formdata</summary>
/// <seealso href="https://xhr.spec.whatwg.org/#interface-formdata"/>
[IdlInterface("FormData", "https://xhr.spec.whatwg.org/#interface-formdata")]
public sealed class FormData : IEnumerable<KeyValuePair<string, FormDataEntryValue>>
{
    private readonly List<KeyValuePair<string, FormDataEntryValue>> _entries = [];

    /// <summary>https://xhr.spec.whatwg.org/#dom-formdata</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-formdata"/>
    [IdlMember("constructor", "https://xhr.spec.whatwg.org/#dom-formdata")]
    public FormData(HtmlFormElement? form = null, HtmlElement? submitter = null)
    {
        if (form is null)
            return;

        if (submitter is not null)
        {
            if (!submitter.IsSubmitButton())
                throw new TypeErrorException("The submitter is not a submit button");

            if (!ReferenceEquals(submitter.FormOwner, form))
                throw DomException.NotFound("The submitter is not owned by the form");
        }

        var list = form.ConstructEntryList(submitter) ?? throw DomException.InvalidState("The entry list of the form is being constructed");
        _entries.AddRange(list);
    }

    internal FormData(List<KeyValuePair<string, FormDataEntryValue>> entries)
    {
        _entries = entries;
    }

    internal List<KeyValuePair<string, FormDataEntryValue>> Entries => _entries;

    /// <summary>Appends a new entry with the given name and string value.</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-formdata-append"/>
    [IdlMember("append", "https://xhr.spec.whatwg.org/#dom-formdata-append")]
    public void Append(string name, string value) => _entries.Add(CreateEntry(name, value));

    /// <summary>
    /// Appends a new entry with the given name and blob value; a blob is converted to a file with the given file name.
    /// </summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-formdata-append-blob"/>
    [IdlMember("append", "https://xhr.spec.whatwg.org/#dom-formdata-append-blob")]
    public void Append(string name, Blob blobValue, string? filename = null) => _entries.Add(CreateEntry(name, blobValue, filename));

    /// <summary>Removes all the entries with the given name.</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-formdata-delete"/>
    [IdlMember("delete", "https://xhr.spec.whatwg.org/#dom-formdata-delete")]
    public void Delete(string name)
    {
        name = InfraString.ToScalarValueString(name);
        _entries.RemoveAll(e => e.Key == name);
    }

    /// <summary>Returns the value of the first entry with the given name, or null when there is none.</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-formdata-get"/>
    [IdlMember("get", "https://xhr.spec.whatwg.org/#dom-formdata-get")]
    public FormDataEntryValue? Get(string name)
    {
        name = InfraString.ToScalarValueString(name);
        foreach (var entry in _entries)
        {
            if (entry.Key == name)
                return entry.Value;
        }

        return null;
    }

    /// <summary>Returns the values of all the entries with the given name.</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-formdata-getall"/>
    [IdlMember("getAll", "https://xhr.spec.whatwg.org/#dom-formdata-getall")]
    public IReadOnlyList<FormDataEntryValue> GetAll(string name)
    {
        name = InfraString.ToScalarValueString(name);
        return _entries.Where(e => e.Key == name).Select(e => e.Value).ToArray();
    }

    /// <summary>Returns a value indicating whether an entry with the given name exists.</summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-formdata-has"/>
    [IdlMember("has", "https://xhr.spec.whatwg.org/#dom-formdata-has")]
    public bool Has(string name)
    {
        name = InfraString.ToScalarValueString(name);
        return _entries.Exists(e => e.Key == name);
    }

    /// <summary>
    /// Sets the value of the first entry with the given name and removes the other ones, or appends a new entry.
    /// </summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-formdata-set"/>
    [IdlMember("set", "https://xhr.spec.whatwg.org/#dom-formdata-set")]
    public void Set(string name, string value) => SetEntry(CreateEntry(name, value));

    /// <summary>
    /// Sets the blob value of the first entry with the given name and removes the other ones, or appends a new entry.
    /// </summary>
    /// <seealso href="https://xhr.spec.whatwg.org/#dom-formdata-set-blob"/>
    [IdlMember("set", "https://xhr.spec.whatwg.org/#dom-formdata-set-blob")]
    public void Set(string name, Blob blobValue, string? filename = null) => SetEntry(CreateEntry(name, blobValue, filename));

    // Replaces the first entry with the same name and removes the others, or appends the entry
    private void SetEntry(KeyValuePair<string, FormDataEntryValue> entry)
    {
        var index = _entries.FindIndex(e => e.Key == entry.Key);
        if (index < 0)
        {
            _entries.Add(entry);
            return;
        }

        _entries[index] = entry;
        for (var i = _entries.Count - 1; i > index; i--)
        {
            if (_entries[i].Key == entry.Key)
            {
                _entries.RemoveAt(i);
            }
        }
    }

    /// <summary>https://xhr.spec.whatwg.org/#create-an-entry</summary>
    internal static KeyValuePair<string, FormDataEntryValue> CreateEntry(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        return new(InfraString.ToScalarValueString(name), InfraString.ToScalarValueString(value));
    }

    internal static KeyValuePair<string, FormDataEntryValue> CreateEntry(string name, Blob value, string? filename)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        File file;
        if (value is not File existing)
        {
            file = new File(value, filename ?? "blob");
        }
        else if (filename is not null)
        {
            file = existing.WithName(filename);
        }
        else
        {
            file = existing;
        }

        return new(InfraString.ToScalarValueString(name), file);
    }

    /// <inheritdoc/>
    /// <seealso href="https://xhr.spec.whatwg.org/#interface-formdata"/>
    [IdlMember("iterable", "https://xhr.spec.whatwg.org/#interface-formdata")]
    public IEnumerator<KeyValuePair<string, FormDataEntryValue>> GetEnumerator()
    {
        // Pair iterators are live
        for (var i = 0; i < _entries.Count; i++)
        {
            yield return _entries[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#submitevent</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#submiteventinit"/>
[IdlInterface("SubmitEventInit", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#submiteventinit")]
public sealed class SubmitEventInit : EventInit
{
    /// <summary>Gets the submit button used to submit the form, if any.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-submiteventinit-submitter"/>
    [IdlMember("submitter", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-submiteventinit-submitter")]
    public HtmlElement? Submitter { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#submitevent</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#submitevent"/>
[IdlInterface("SubmitEvent", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#submitevent")]
public sealed class SubmitEvent : Event
{
    /// <summary>Creates a <c>submit</c> event with the given type and initialization dictionary.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-submitevent-constructor"/>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-submitevent-constructor")]
    public SubmitEvent(string type, SubmitEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        Submitter = eventInitDict?.Submitter;
    }

    /// <summary>
    /// Gets the element that was used to submit the form, or null when the form was submitted by other means.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-submitevent-submitter"/>
    [IdlMember("submitter", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-submitevent-submitter")]
    public HtmlElement? Submitter { get; }
}

/// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#formdataeventinit</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#formdataeventinit"/>
[IdlInterface("FormDataEventInit", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#formdataeventinit")]
public sealed class FormDataEventInit : EventInit
{
    /// <summary>Gets the entry list of the form to expose to the event.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-formdataeventinit-formdata"/>
    [IdlMember("formData", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-formdataeventinit-formdata")]
    public required FormData FormData { get; init; }
}

/// <summary>https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#formdataevent</summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#formdataevent"/>
[IdlInterface("FormDataEvent", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#formdataevent")]
public sealed class FormDataEvent : Event
{
    /// <summary>Creates a <c>formdata</c> event with the given type and initialization dictionary.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-formdataevent-constructor"/>
    [IdlMember("constructor", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-formdataevent-constructor")]
    public FormDataEvent(string type, FormDataEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        ArgumentNullException.ThrowIfNull(eventInitDict);
        FormData = eventInitDict.FormData;
    }

    /// <summary>Gets the form data containing the entries of the form being submitted.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-formdataevent-formdata"/>
    [IdlMember("formData", "https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#dom-formdataevent-formdata")]
    public FormData FormData { get; }
}
