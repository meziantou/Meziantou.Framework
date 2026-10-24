using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf;

/// <summary>
/// The structure tree of a tagged document (ISO 32000-2, 14.7 and 14.8): the logical structure of its content, which
/// assistive technologies read, and the parent tree that maps the marked content of the pages back to its elements.
/// </summary>
/// <remarks>
/// The tree is created in the document when the first element is added. Each page whose content is tagged gets a
/// <c>/StructParents</c> key, each tagged annotation a <c>/StructParent</c> key; the parent tree, the
/// <c>/MarkInfo</c> entry of the catalog and the tab order of the pages are written when the document is saved.
/// </remarks>
public sealed class PdfStructureTree
{
    private static readonly PdfName StructTreeRootName = PdfName.Create("StructTreeRoot");
    private static readonly PdfName StructElemName = PdfName.Create("StructElem");
    private static readonly PdfName ParentTreeName = PdfName.Create("ParentTree");
    private static readonly PdfName ParentTreeNextKeyName = PdfName.Create("ParentTreeNextKey");
    private static readonly PdfName StructParentsName = PdfName.Create("StructParents");
    private static readonly PdfName StructParentName = PdfName.Create("StructParent");
    private static readonly PdfName RoleMapName = PdfName.Create("RoleMap");
    private static readonly PdfName McrName = PdfName.Create("MCR");
    private static readonly PdfName ObjrName = PdfName.Create("OBJR");
    private static readonly PdfName ObjName = PdfName.Create("Obj");
    private static readonly PdfName PgName = PdfName.Create("Pg");
    private static readonly PdfName McidName = PdfName.Create("MCID");
    private static readonly PdfName NumsName = PdfName.Create("Nums");

    private readonly PdfDocument _document;
    private readonly Dictionary<int, PdfObject> _parentTree = [];
    private PdfDictionary? _root;
    private int _nextKey;

    internal PdfStructureTree(PdfDocument document)
    {
        _document = document;
        if (document.Catalog.GetResolved(StructTreeRootName) is PdfDictionary root)
        {
            _root = root;
            ReadParentTree(root);
        }
    }

    /// <summary>Gets a value indicating whether the document has a structure tree.</summary>
    public bool Exists => _root is not null;

    /// <summary>Gets the elements at the top of the tree. PDF/UA expects one <c>Document</c> element that holds the others.</summary>
    public IReadOnlyList<PdfStructureElement> Elements => _root is null ? [] : [.. GetChildren(_root)];

    /// <summary>
    /// Gets the role map (<c>/RoleMap</c>, 14.7.3): the custom structure types of the document mapped to the types they
    /// are, such as <c>Heading1</c> to <c>H1</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> RoleMap
    {
        get
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_root?.GetResolved(RoleMapName) is PdfDictionary roleMap)
            {
                foreach (var (key, value) in roleMap)
                {
                    if (PdfReference.ResolveIfReference(value) is PdfName name)
                    {
                        result[key.Value] = name.Value;
                    }
                }
            }

            return result;
        }
    }

    /// <summary>Maps a custom structure type to the type it is, in the role map.</summary>
    /// <param name="type">The custom type, such as <c>Heading1</c>.</param>
    /// <param name="mappedType">The type it is: a standard type such as <c>H1</c>, or another custom type.</param>
    public void MapRole(string type, string mappedType)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentException.ThrowIfNullOrEmpty(mappedType);
        var root = EnsureRoot();
        if (root.GetResolved(RoleMapName) is not PdfDictionary roleMap)
        {
            roleMap = [];
            root[RoleMapName] = roleMap;
        }

        roleMap[PdfName.Create(type)] = PdfName.Create(mappedType);
    }

    /// <summary>Adds an element at the top of the tree.</summary>
    /// <param name="type">The structure type, such as <c>Document</c>.</param>
    /// <returns>The element.</returns>
    public PdfStructureElement AddElement(string type) => CreateElement(parent: null, type);

    internal PdfDictionary EnsureRoot()
    {
        if (_root is not null)
            return _root;

        _root = new PdfDictionary
        {
            [PdfNames.Type] = StructTreeRootName,
            [PdfNames.K] = new PdfArray(),
        };
        _document.Catalog[StructTreeRootName] = _document.Objects.Add(_root);
        return _root;
    }

    internal PdfStructureElement CreateElement(PdfStructureElement? parent, string type)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        var root = EnsureRoot();
        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = StructElemName,
            [PdfNames.S] = PdfName.Create(type),
        };
        var reference = _document.Objects.Add(dictionary);
        var element = new PdfStructureElement(this, dictionary, reference);
        if (parent is null)
        {
            dictionary[PdfNames.P] = _document.Catalog[StructTreeRootName];
            if (root[PdfNames.K] is not PdfArray kids)
            {
                kids = [];
                if (root[PdfNames.K] is { } existing)
                {
                    kids.Add(existing);
                }

                root[PdfNames.K] = kids;
            }

            kids.Add(reference);
        }
        else
        {
            dictionary[PdfNames.P] = parent.Reference;
            parent.AddKid(reference);
        }

        return element;
    }

    internal IEnumerable<PdfStructureElement> GetChildren(PdfDictionary parent)
    {
        IEnumerable<PdfObject> kids = parent[PdfNames.K] switch
        {
            PdfArray array => array,
            { } kid => [kid],
            _ => [],
        };
        foreach (var kid in kids)
        {
            if (kid is PdfReference reference && reference.Resolve() is PdfDictionary dictionary && dictionary.GetName(PdfNames.S) is not null)
                yield return new PdfStructureElement(this, dictionary, reference);
        }
    }

    /// <summary>Gets the next marked-content identifier of a page, and records the element its content belongs to (14.7.5.2).</summary>
    internal int AddMarkedContent(PdfStructureElement element, PdfPage page)
    {
        var contents = GetPageContents(page);
        var mcid = contents.Count;
        contents.Add(element.Reference);
        element.AddKid(new PdfDictionary
        {
            [PdfNames.Type] = McrName,
            [PgName] = page.Reference,
            [McidName] = PdfInteger.Get(mcid),
        });
        return mcid;
    }

    internal void AddAnnotation(PdfStructureElement element, PdfAnnotation annotation)
    {
        if (annotation.Page.Document != _document || annotation.Reference is not { } reference)
            throw new ArgumentException("The annotation is not an indirect object of a page of the document", nameof(annotation));

        var key = _nextKey++;
        annotation.Dictionary[StructParentName] = PdfInteger.Get(key);
        _parentTree[key] = element.Reference;
        element.AddKid(new PdfDictionary
        {
            [PdfNames.Type] = ObjrName,
            [PgName] = annotation.Page.Reference,
            [ObjName] = reference,
        });
    }

    /// <summary>Writes the parent tree and the entries of the catalog that a tagged document has (14.7.5.4, 14.8.2.1).</summary>
    internal void Write()
    {
        if (_root is null)
            return;

        var nums = new PdfArray();
        foreach (var (key, value) in _parentTree.OrderBy(entry => entry.Key))
        {
            nums.Add(PdfInteger.Get(key));
            nums.Add(value);
        }

        // The parent tree is written as one node: the node of the document, when it has one, is replaced
        if (_root[ParentTreeName] is PdfReference reference && reference.Resolve() is PdfDictionary tree)
        {
            tree.Remove(PdfNames.Kids);
            tree.Remove(PdfNames.Limits);
            tree[NumsName] = nums;
        }
        else
        {
            _root[ParentTreeName] = _document.Objects.Add(new PdfDictionary { [NumsName] = nums });
        }

        _root[ParentTreeNextKeyName] = PdfInteger.Get(_nextKey);
        _document.Catalog[PdfName.Create("MarkInfo")] = new PdfDictionary { [PdfName.Create("Marked")] = PdfBoolean.True };

        // The annotations of a tagged page are in the order of the structure (12.5.1)
        foreach (var page in _document.Pages)
        {
            if (page.Dictionary.ContainsKey(StructParentsName) && page.Annotations.Count > 0)
            {
                page.Dictionary[PdfName.Create("Tabs")] = PdfNames.S;
            }
        }
    }

    private PdfArray GetPageContents(PdfPage page)
    {
        // A page tagged before keeps its key: its new marked content follows the marked content it has
        if (page.Dictionary.GetResolved(StructParentsName) is PdfInteger existing && _parentTree.TryGetValue((int)existing.Value, out var value) && PdfReference.ResolveIfReference(value) is PdfArray array)
        {
            _parentTree[(int)existing.Value] = array;
            return array;
        }

        var key = _nextKey++;
        array = [];
        _parentTree[key] = array;
        page.Dictionary[StructParentsName] = PdfInteger.Get(key);
        return array;
    }

    /// <summary>Reads the number tree of the parent tree of a document (7.9.7), so that the elements added keep the keys it has.</summary>
    private void ReadParentTree(PdfDictionary root)
    {
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        if (root.GetResolved(ParentTreeName) is PdfDictionary tree)
        {
            ReadNumberTree(tree, visited, depth: 0);
        }

        var next = _parentTree.Count == 0 ? 0 : _parentTree.Keys.Max() + 1;
        _nextKey = (int)Math.Max(next, Math.Clamp(root.GetInteger(ParentTreeNextKeyName, 0), 0, int.MaxValue));
    }

    private void ReadNumberTree(PdfDictionary node, HashSet<PdfDictionary> visited, int depth)
    {
        if (depth > 32 || !visited.Add(node))
            return;

        if (node.Get<PdfArray>(NumsName) is { } nums)
        {
            for (var i = 0; i + 1 < nums.Count; i += 2)
            {
                if (nums.TryGetResolved<PdfInteger>(i, out var key) && key.Value is >= 0 and < int.MaxValue)
                {
                    _parentTree[(int)key.Value] = nums[i + 1];
                }
            }
        }

        if (node.Get<PdfArray>(PdfNames.Kids) is { } kids)
        {
            foreach (var kid in kids)
            {
                if (PdfReference.ResolveIfReference(kid) is PdfDictionary child)
                {
                    ReadNumberTree(child, visited, depth + 1);
                }
            }
        }
    }
}
