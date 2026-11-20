using System.Text;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// An action (ISO 32000-2, 12.6): what a viewer does when a link is clicked, a bookmark is chosen, a page is opened, a
/// form field gets the focus... Each type of action the specification defines has its class; the actions of other types
/// (threads, sounds, movies, renditions, transitions, 3D views, rich media) are <see cref="PdfUnknownAction"/>.
/// </summary>
/// <remarks>
/// Read the actions with the <c>Action</c> properties of the objects that have one (<see cref="PdfLinkAnnotation.Action"/>,
/// <see cref="PdfOutlineItem.Action"/>, <see cref="PdfDocument.OpenAction"/>, the additional actions), and create them
/// with the static methods of this class. An action is a dictionary written in the object it is set on.
/// </remarks>
public abstract class PdfAction
{
    // The /Next chains of actions (12.6.2): their depth and their number of actions are bounded
    internal const int MaxDepth = 32;
    internal const int MaxActions = 256;

    private protected PdfAction(PdfDictionary dictionary, PdfDocument? document)
    {
        Dictionary = dictionary;
        Document = document;
    }

    /// <summary>Gets the action dictionary.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets the type of the action (<c>/S</c>), such as <c>GoTo</c>, <c>URI</c> or <c>JavaScript</c>.</summary>
    public string Kind => Dictionary.GetName(PdfNames.S)?.Value ?? "";

    /// <summary>
    /// Gets the actions run after this one (<c>/Next</c>, 12.6.2), in order. The sequence is bounded: an action reached
    /// twice ends it.
    /// </summary>
    public IReadOnlyList<PdfAction> Next
    {
        get
        {
            var result = new List<PdfAction>();
            switch (Dictionary.GetResolved(NextName))
            {
                case PdfDictionary next when Read(Document, next) is { } action:
                    result.Add(action);
                    break;
                case PdfArray array:
                    for (var i = 0; i < array.Count && result.Count < MaxActions; i++)
                    {
                        if (Read(Document, array.GetResolved(i)) is { } item)
                        {
                            result.Add(item);
                        }
                    }

                    break;
            }

            return result;
        }
    }

    internal PdfDocument? Document { get; }

    private static PdfName NextName => PdfNames.Next;

    private static PdfName TName { get; } = PdfName.Create("T");

    /// <summary>Sets the actions run after this one (<c>/Next</c>); an empty sequence removes them.</summary>
    /// <param name="actions">The actions.</param>
    /// <exception cref="ArgumentException">An action is null, or the sequence would lead back to this action (a cycle).</exception>
    public void SetNext(params IEnumerable<PdfAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var list = actions.ToList();
        if (list.Any(action => action is null))
            throw new ArgumentException("An action cannot be followed by null", nameof(actions));

        // The actions that follow, and the actions they are followed by, must not lead back to this action
        foreach (var action in list)
        {
            if (GetChain(action.Dictionary).Any(item => ReferenceEquals(item, Dictionary)))
                throw new ArgumentException("An action cannot be followed by itself: the actions would be a cycle", nameof(actions));
        }

        Dictionary[NextName] = list.Count switch
        {
            0 => null,
            1 => list[0].Dictionary,
            _ => new PdfArray(list.Select(action => (PdfObject)action.Dictionary)),
        };
    }

    /// <summary>Creates an action that goes to a destination of the document (<c>GoTo</c>, 12.6.4.2).</summary>
    /// <param name="destination">The destination.</param>
    /// <returns>The action.</returns>
    public static PdfGoToAction GoTo(PdfDestination destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var dictionary = new PdfDictionary();
        destination.WriteTo(dictionary);
        var action = dictionary.Get<PdfDictionary>(PdfNames.A) ?? new PdfDictionary
        {
            [PdfNames.S] = PdfNames.GoTo,
            [PdfNames.D] = dictionary[PdfNames.Dest],
        };
        return new PdfGoToAction(action, destination.Page.Document);
    }

    /// <summary>Creates an action that opens a page of another PDF file (<c>GoToR</c>, 12.6.4.3).</summary>
    /// <param name="fileName">The path of the file, relative to the document or absolute.</param>
    /// <param name="pageIndex">The zero-based index of the page.</param>
    /// <param name="newWindow">Whether the file opens in a new window, or <see langword="null"/> to let the viewer choose.</param>
    /// <returns>The action.</returns>
    public static PdfGoToRemoteAction GoToRemote(string fileName, int pageIndex = 0, bool? newWindow = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        var dictionary = Create("GoToR");
        dictionary[PdfNames.F] = CreateFileSpecification(fileName);
        dictionary[PdfNames.D] = new PdfArray(PdfInteger.Get(pageIndex), PdfName.Create("Fit"));
        SetNewWindow(dictionary, newWindow);
        return new PdfGoToRemoteAction(dictionary, document: null);
    }

    /// <summary>Creates an action that opens a file with the application of the system (<c>Launch</c>, 12.6.4.6).</summary>
    /// <param name="fileName">The path of the file, relative to the document or absolute.</param>
    /// <param name="newWindow">Whether a PDF file opens in a new window, or <see langword="null"/> to let the viewer choose.</param>
    /// <returns>The action. Viewers ask the user before they open the file, or refuse to.</returns>
    public static PdfLaunchAction Launch(string fileName, bool? newWindow = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        var dictionary = Create("Launch");
        dictionary[PdfNames.F] = CreateFileSpecification(fileName);
        SetNewWindow(dictionary, newWindow);
        return new PdfLaunchAction(dictionary, document: null);
    }

    /// <summary>Creates an action that opens a URI (<c>URI</c>, 12.6.4.8).</summary>
    /// <param name="uri">The URI: an absolute URI, written as 7-bit ASCII (an internationalized host in its IDNA ASCII form, the other non-ASCII characters percent-encoded as UTF-8).</param>
    /// <returns>The action.</returns>
    /// <exception cref="ArgumentException">The URI is not absolute.</exception>
    public static PdfUriAction OpenUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("The URI must be absolute", nameof(uri));

        var dictionary = Create("URI");
        dictionary[PdfNames.URI] = PdfString.FromBytes(Encoding.ASCII.GetBytes(PdfUriEncoder.ToAscii(uri)));
        return new PdfUriAction(dictionary, document: null);
    }

    /// <summary>Creates a named action (<c>Named</c>, 12.6.4.12): <c>NextPage</c>, <c>PrevPage</c>, <c>FirstPage</c>, <c>LastPage</c>, or a name a viewer knows.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The action.</returns>
    public static PdfNamedAction Named(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var dictionary = Create("Named");
        dictionary[PdfNames.N] = PdfName.Create(name);
        return new PdfNamedAction(dictionary, document: null);
    }

    /// <summary>Creates an action that sends the values of the form fields to a URL (<c>SubmitForm</c>, 12.7.6.2).</summary>
    /// <param name="url">The URL.</param>
    /// <param name="fieldNames">The fully qualified names of the fields sent (or not sent, with <see cref="PdfSubmitFormFlags.Exclude"/>), or <see langword="null"/> for all the fields.</param>
    /// <param name="flags">The flags: the format of the data and the fields sent.</param>
    /// <returns>The action.</returns>
    public static PdfSubmitFormAction SubmitForm(Uri url, IEnumerable<string>? fieldNames = null, PdfSubmitFormFlags flags = PdfSubmitFormFlags.None)
    {
        ArgumentNullException.ThrowIfNull(url);
        var dictionary = Create("SubmitForm");
        dictionary[PdfNames.F] = new PdfDictionary
        {
            [PdfName.Create("FS")] = PdfName.Create("URL"),
            [PdfNames.F] = PdfString.FromBytes(Encoding.ASCII.GetBytes(url.IsAbsoluteUri ? PdfUriEncoder.ToAscii(url) : url.OriginalString)),
        };
        SetFields(dictionary, fieldNames);
        dictionary[PdfName.Create("Flags")] = flags == PdfSubmitFormFlags.None ? null : PdfInteger.Get((int)flags);
        return new PdfSubmitFormAction(dictionary, document: null);
    }

    /// <summary>Creates an action that resets the form fields to their default values (<c>ResetForm</c>, 12.7.6.3).</summary>
    /// <param name="fieldNames">The fully qualified names of the fields reset (or not reset when <paramref name="exclude"/> is <see langword="true"/>), or <see langword="null"/> for all the fields.</param>
    /// <param name="exclude">Whether the fields named are the fields not reset.</param>
    /// <returns>The action.</returns>
    public static PdfResetFormAction ResetForm(IEnumerable<string>? fieldNames = null, bool exclude = false)
    {
        var dictionary = Create("ResetForm");
        SetFields(dictionary, fieldNames);
        dictionary[PdfName.Create("Flags")] = exclude ? PdfInteger.Get(1) : null;
        return new PdfResetFormAction(dictionary, document: null);
    }

    /// <summary>Creates an action that imports the values of the form fields from an FDF file (<c>ImportData</c>, 12.7.6.4).</summary>
    /// <param name="fileName">The path of the FDF file.</param>
    /// <returns>The action.</returns>
    public static PdfImportDataAction ImportData(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        var dictionary = Create("ImportData");
        dictionary[PdfNames.F] = CreateFileSpecification(fileName);
        return new PdfImportDataAction(dictionary, document: null);
    }

    /// <summary>Creates an action that hides or shows form fields (<c>Hide</c>, 12.6.4.11).</summary>
    /// <param name="fieldNames">The fully qualified names of the fields.</param>
    /// <param name="hide">Whether the fields are hidden (<see langword="true"/>) or shown.</param>
    /// <returns>The action.</returns>
    /// <exception cref="ArgumentException">There is no field name.</exception>
    public static PdfHideAction Hide(IEnumerable<string> fieldNames, bool hide = true)
    {
        ArgumentNullException.ThrowIfNull(fieldNames);
        var names = fieldNames.ToList();
        if (names.Count == 0)
            throw new ArgumentException("A hide action needs a field", nameof(fieldNames));

        var dictionary = Create("Hide");
        dictionary[TName] = names.Count == 1 ? PdfString.FromText(names[0]) : new PdfArray(names.Select(name => (PdfObject)PdfString.FromText(name)));
        dictionary[PdfName.Create("H")] = hide ? null : PdfBoolean.False;
        return new PdfHideAction(dictionary, document: null);
    }

    /// <summary>Creates an action that turns optional content groups (layers) on or off (<c>SetOCGState</c>, 12.6.4.13).</summary>
    /// <param name="state">The new state of the groups.</param>
    /// <param name="groups">The groups.</param>
    /// <returns>The action.</returns>
    /// <exception cref="ArgumentException">There is no group, or the groups belong to several documents.</exception>
    public static PdfSetOptionalContentStateAction SetOptionalContentState(PdfOptionalContentStateChange state, params IEnumerable<PdfOptionalContentGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var list = groups.ToList();
        if (list.Count == 0 || list.Any(group => group is null) || list.Select(group => group.Document).Distinct().Count() != 1)
            throw new ArgumentException("The action needs groups of one document", nameof(groups));

        var name = state switch
        {
            PdfOptionalContentStateChange.On => "ON",
            PdfOptionalContentStateChange.Off => "OFF",
            PdfOptionalContentStateChange.Toggle => "Toggle",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "The state is not valid"),
        };
        var dictionary = Create("SetOCGState");
        var array = new PdfArray(PdfName.Create(name));
        foreach (var group in list)
        {
            array.Add(group.Reference);
        }

        dictionary[PdfName.Create("State")] = array;
        return new PdfSetOptionalContentStateAction(dictionary, list[0].Document);
    }

    /// <summary>Creates an action that runs a script (<c>JavaScript</c>, 12.6.4.17).</summary>
    /// <param name="script">The script.</param>
    /// <returns>The action.</returns>
    public static PdfJavaScriptAction JavaScript(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var dictionary = Create("JavaScript");
        dictionary[PdfJavaScriptAction.JSName] = PdfString.FromText(script);
        return new PdfJavaScriptAction(dictionary, script);
    }

    /// <summary>Reads an action dictionary; <see langword="null"/> for a value that is not a dictionary.</summary>
    internal static PdfAction? Read(PdfDocument? document, PdfObject? value)
    {
        if ((value is null ? null : PdfReference.ResolveIfReference(value)) is not PdfDictionary dictionary)
            return null;

        return dictionary.GetName(PdfNames.S)?.Value switch
        {
            "GoTo" => new PdfGoToAction(dictionary, document),
            "GoToR" => new PdfGoToRemoteAction(dictionary, document),
            "GoToE" => new PdfGoToEmbeddedAction(dictionary, document),
            "Launch" => new PdfLaunchAction(dictionary, document),
            "URI" => new PdfUriAction(dictionary, document),
            "Named" => new PdfNamedAction(dictionary, document),
            "SubmitForm" => new PdfSubmitFormAction(dictionary, document),
            "ResetForm" => new PdfResetFormAction(dictionary, document),
            "ImportData" => new PdfImportDataAction(dictionary, document),
            "Hide" => new PdfHideAction(dictionary, document),
            "SetOCGState" => new PdfSetOptionalContentStateAction(dictionary, document),
            "JavaScript" => new PdfJavaScriptAction(dictionary, PdfJavaScriptAction.ReadScript(dictionary.GetResolved(PdfJavaScriptAction.JSName)) ?? "", document),
            _ => new PdfUnknownAction(dictionary, document),
        };
    }

    /// <summary>Checks that an action can be set on an object of a document: the objects it refers to (pages, groups) belong to it.</summary>
    internal static void CheckDocument(PdfAction action, PdfDocument document, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(action, parameterName);
        if (action.Document is not null && action.Document != document)
            throw new ArgumentException("The action refers to objects of another document", parameterName);

        // The actions that follow it too: their destinations and groups are references of the document
        foreach (var item in GetChain(action.Dictionary))
        {
            if (HasForeignReference(item, document.Objects, depth: 0))
                throw new ArgumentException("An action of the sequence refers to objects of another document", parameterName);
        }
    }

    /// <summary>Gets an action dictionary and the actions that follow it (<c>/Next</c>), each once, in a bounded number.</summary>
    internal static List<PdfDictionary> GetChain(PdfDictionary start)
    {
        var result = new List<PdfDictionary>();
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<PdfDictionary>();
        pending.Push(start);
        while (pending.Count > 0 && result.Count < MaxActions)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
                continue;

            result.Add(current);
            switch (current.GetResolved(PdfNames.Next))
            {
                case PdfDictionary next:
                    pending.Push(next);
                    break;
                case PdfArray array:
                    for (var i = array.Count - 1; i >= 0; i--)
                    {
                        if (array.GetResolved(i) is PdfDictionary item)
                        {
                            pending.Push(item);
                        }
                    }

                    break;
            }
        }

        return result;
    }

    /// <summary>Indicates whether an action refers to an object of another document: a page of a destination, a group of a set-OCG-state action.</summary>
    private static bool HasForeignReference(PdfObject value, PdfIndirectObjectCollection objects, int depth)
    {
        switch (value)
        {
            case PdfReference reference:
                return !ReferenceEquals(reference.Owner, objects);
            case PdfArray array when depth < 8:
                foreach (var item in array.Take(4096))
                {
                    if (HasForeignReference(item, objects, depth + 1))
                        return true;
                }

                return false;
            case PdfDictionary dictionary when depth < 8:
                foreach (var (key, item) in dictionary)
                {
                    if (key != PdfNames.Next && item is not null && HasForeignReference(item, objects, depth + 1))
                        return true;
                }

                return false;
            default:
                return false;
        }
    }

    /// <summary>Reads the path of a file specification (7.11): a string, or the <c>/UF</c> or <c>/F</c> of a dictionary.</summary>
    internal static string? ReadFileName(PdfObject? value) => (value is null ? null : PdfReference.ResolveIfReference(value)) switch
    {
        PdfString text => text.ToText(),
        PdfDictionary specification => specification.GetText(PdfEmbeddedFile.UFName) ?? specification.GetText(PdfNames.F),
        _ => null,
    };

    /// <summary>Reads the names of the fields of a <c>/Fields</c> or <c>/T</c> entry: the strings, and the fully qualified names of the field dictionaries.</summary>
    internal static List<string> ReadFieldNames(PdfObject? value)
    {
        var result = new List<string>();
        void Add(PdfObject? item)
        {
            switch (item is null ? null : PdfReference.ResolveIfReference(item))
            {
                case PdfString name:
                    result.Add(name.ToText());
                    break;
                case PdfDictionary field when GetFullName(field) is { } name:
                    result.Add(name);
                    break;
            }
        }

        if ((value is null ? null : PdfReference.ResolveIfReference(value)) is PdfArray array)
        {
            for (var i = 0; i < array.Count && i < 4096; i++)
            {
                Add(array[i]);
            }
        }
        else
        {
            Add(value);
        }

        return result;
    }

    private static string? GetFullName(PdfDictionary field)
    {
        var parts = new List<string>();
        var current = field;
        for (var depth = 0; current is not null && depth < MaxDepth; depth++)
        {
            if (current.GetText(TName) is { } part)
            {
                parts.Add(part);
            }

            current = current.Get<PdfDictionary>(PdfNames.Parent);
        }

        parts.Reverse();
        return parts.Count == 0 ? null : string.Join('.', parts);
    }

    private static PdfDictionary Create(string kind) => new()
    {
        [PdfNames.Type] = PdfName.Create("Action"),
        [PdfNames.S] = PdfName.Create(kind),
    };

    private static PdfDictionary CreateFileSpecification(string fileName) => new()
    {
        [PdfNames.Type] = PdfEmbeddedFile.FilespecName,
        [PdfNames.F] = PdfString.FromText(fileName),
        [PdfEmbeddedFile.UFName] = PdfString.FromText(fileName),
    };

    private static void SetNewWindow(PdfDictionary dictionary, bool? newWindow) => dictionary[PdfName.Create("NewWindow")] = newWindow is { } value ? PdfBoolean.Get(value) : null;

    private static void SetFields(PdfDictionary dictionary, IEnumerable<string>? fieldNames)
    {
        if (fieldNames is null)
            return;

        dictionary[PdfName.Create("Fields")] = new PdfArray(fieldNames.Select(name => (PdfObject)PdfString.FromText(name)));
    }
}
