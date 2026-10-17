using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

// https://html.spec.whatwg.org/multipage/tables.html

public partial class HtmlTableElement
{
    private HtmlCollection? _tBodies;
    private HtmlCollection? _rows;

    private Element? FirstChildOfType(TagId tagId)
    {
        for (var child = FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (child.IsHtml(tagId))
                return child;
        }

        return null;
    }

    private T CreateHtmlElement<T>(string localName)
        where T : Element => (T)ElementFactory.CreateElement(NodeDocument, localName, Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false);

    /// <summary>https://html.spec.whatwg.org/multipage/tables.html#dom-table-caption</summary>
    [IdlMember("caption", "https://html.spec.whatwg.org/multipage/tables.html#dom-table-caption")]
    public HtmlTableCaptionElement? Caption
    {
        get => FirstChildOfType(TagId.Caption) as HtmlTableCaptionElement;
        set
        {
            FirstChildOfType(TagId.Caption)?.RemoveFromParent();
            if (value is not null)
            {
                InsertBefore(value, FirstChild);
            }
        }
    }

    /// <summary>
    /// Returns the <c>caption</c> element of the table, creating and inserting it when there is none.
    /// </summary>
    [IdlMember("createCaption", "https://html.spec.whatwg.org/multipage/tables.html#dom-table-createcaption")]
    public HtmlTableCaptionElement CreateCaption()
    {
        if (Caption is { } caption)
            return caption;

        caption = CreateHtmlElement<HtmlTableCaptionElement>("caption");
        InsertBefore(caption, FirstChild);
        return caption;
    }

    /// <summary>Removes the first <c>caption</c> child of the table, if any.</summary>
    [IdlMember("deleteCaption", "https://html.spec.whatwg.org/multipage/tables.html#dom-table-deletecaption")]
    public void DeleteCaption() => FirstChildOfType(TagId.Caption)?.RemoveFromParent();

    /// <summary>https://html.spec.whatwg.org/multipage/tables.html#dom-table-thead</summary>
    [IdlMember("tHead", "https://html.spec.whatwg.org/multipage/tables.html#dom-table-thead")]
    public HtmlTableSectionElement? THead
    {
        get => FirstChildOfType(TagId.Thead) as HtmlTableSectionElement;
        set
        {
            if (value is not null && !value.IsHtml(TagId.Thead))
                throw DomException.HierarchyRequest("The value must be a thead element");

            FirstChildOfType(TagId.Thead)?.RemoveFromParent();
            if (value is not null)
            {
                InsertBefore(value, GetTHeadInsertionPoint());
            }
        }
    }

    // Immediately before the first element child that is neither a caption nor a colgroup
    private Element? GetTHeadInsertionPoint()
    {
        for (var child = FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (!child.IsHtml(TagId.Caption) && !child.IsHtml(TagId.Colgroup))
                return child;
        }

        return null;
    }

    /// <summary>Returns the <c>thead</c> element of the table, creating and inserting it when there is none.</summary>
    [IdlMember("createTHead", "https://html.spec.whatwg.org/multipage/tables.html#dom-table-createthead")]
    public HtmlTableSectionElement CreateTHead()
    {
        if (THead is { } head)
            return head;

        head = CreateHtmlElement<HtmlTableSectionElement>("thead");
        InsertBefore(head, GetTHeadInsertionPoint());
        return head;
    }

    /// <summary>Removes the first <c>thead</c> child of the table, if any.</summary>
    [IdlMember("deleteTHead", "https://html.spec.whatwg.org/multipage/tables.html#dom-table-deletethead")]
    public void DeleteTHead() => FirstChildOfType(TagId.Thead)?.RemoveFromParent();

    /// <summary>https://html.spec.whatwg.org/multipage/tables.html#dom-table-tfoot</summary>
    [IdlMember("tFoot", "https://html.spec.whatwg.org/multipage/tables.html#dom-table-tfoot")]
    public HtmlTableSectionElement? TFoot
    {
        get => FirstChildOfType(TagId.Tfoot) as HtmlTableSectionElement;
        set
        {
            if (value is not null && !value.IsHtml(TagId.Tfoot))
                throw DomException.HierarchyRequest("The value must be a tfoot element");

            FirstChildOfType(TagId.Tfoot)?.RemoveFromParent();
            if (value is not null)
            {
                AppendChild(value);
            }
        }
    }

    /// <summary>Returns the <c>tfoot</c> element of the table, creating and inserting it when there is none.</summary>
    [IdlMember("createTFoot", "https://html.spec.whatwg.org/multipage/tables.html#dom-table-createtfoot")]
    public HtmlTableSectionElement CreateTFoot()
    {
        if (TFoot is { } foot)
            return foot;

        foot = CreateHtmlElement<HtmlTableSectionElement>("tfoot");
        AppendChild(foot);
        return foot;
    }

    /// <summary>Removes the first <c>tfoot</c> child of the table, if any.</summary>
    [IdlMember("deleteTFoot", "https://html.spec.whatwg.org/multipage/tables.html#dom-table-deletetfoot")]
    public void DeleteTFoot() => FirstChildOfType(TagId.Tfoot)?.RemoveFromParent();

    /// <summary>Gets the live collection of the <c>tbody</c> children of the table.</summary>
    [IdlMember("tBodies", "https://html.spec.whatwg.org/multipage/tables.html#dom-table-tbodies")]
    public HtmlCollection TBodies => _tBodies ??= new HtmlCollection(this, static e => e.IsHtml(TagId.Tbody), childrenOnly: true, structural: true);

    /// <summary>https://html.spec.whatwg.org/multipage/tables.html#dom-table-createtbody</summary>
    [IdlMember("createTBody", "https://html.spec.whatwg.org/multipage/tables.html#dom-table-createtbody")]
    public HtmlTableSectionElement CreateTBody()
    {
        var body = CreateHtmlElement<HtmlTableSectionElement>("tbody");
        Element? lastBody = null;
        for (var child = FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (child.IsHtml(TagId.Tbody))
            {
                lastBody = child;
            }
        }

        InsertBefore(body, lastBody?.NextSibling);
        return body;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/tables.html#dom-table-rows: the rows of the thead elements, then the rows
    /// that are children of the table or of its tbody elements, then the rows of the tfoot elements.
    /// </summary>
    [IdlMember("rows", "https://html.spec.whatwg.org/multipage/tables.html#dom-table-rows")]
    public HtmlCollection Rows => _rows ??= new HtmlCollection(this, PopulateRows);

    private void PopulateRows(List<Element> result)
    {
        AddSectionRows(result, TagId.Thead);
        for (var child = FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (child.IsHtml(TagId.Tr))
            {
                result.Add(child);
            }
            else if (child.IsHtml(TagId.Tbody))
            {
                AddRows(result, child);
            }
        }

        AddSectionRows(result, TagId.Tfoot);
    }

    private void AddSectionRows(List<Element> result, TagId section)
    {
        for (var child = FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (child.IsHtml(section))
            {
                AddRows(result, child);
            }
        }
    }

    private static void AddRows(List<Element> result, Element section)
    {
        for (var row = section.FirstElementChild; row is not null; row = row.NextElementSibling)
        {
            if (row.IsHtml(TagId.Tr))
            {
                result.Add(row);
            }
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/tables.html#dom-table-insertrow</summary>
    [IdlMember("insertRow", "https://html.spec.whatwg.org/multipage/tables.html#dom-table-insertrow")]
    public HtmlTableRowElement InsertRow(int index = -1)
    {
        var rows = Rows;
        var count = rows.Count;
        if (index < -1 || index > count)
            throw DomException.IndexSize($"The index {index} is out of range");

        var row = CreateHtmlElement<HtmlTableRowElement>("tr");
        if (count == 0)
        {
            Element? lastBody = null;
            for (var child = FirstElementChild; child is not null; child = child.NextElementSibling)
            {
                if (child.IsHtml(TagId.Tbody))
                {
                    lastBody = child;
                }
            }

            if (lastBody is null)
            {
                var body = CreateHtmlElement<HtmlTableSectionElement>("tbody");
                body.AppendChild(row);
                AppendChild(body);
            }
            else
            {
                lastBody.AppendChild(row);
            }
        }
        else if (index == -1 || index == count)
        {
            rows[count - 1].ParentNode!.AppendChild(row);
        }
        else
        {
            var reference = rows[index];
            reference.ParentNode!.InsertBefore(row, reference);
        }

        return row;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/tables.html#dom-table-deleterow</summary>
    [IdlMember("deleteRow", "https://html.spec.whatwg.org/multipage/tables.html#dom-table-deleterow")]
    public void DeleteRow(int index)
    {
        var rows = Rows;
        var count = rows.Count;
        if (index == -1)
        {
            // The last row, if any
            if (count > 0)
            {
                rows[count - 1].RemoveFromParent();
            }

            return;
        }

        if (index < -1 || index >= count)
            throw DomException.IndexSize($"The index {index} is out of range");

        rows[index].RemoveFromParent();
    }
}

public partial class HtmlTableSectionElement
{
    private HtmlCollection? _rows;

    /// <summary>Gets the live collection of the <c>tr</c> children of the section.</summary>
    [IdlMember("rows", "https://html.spec.whatwg.org/multipage/tables.html#dom-tbody-rows")]
    public HtmlCollection Rows => _rows ??= new HtmlCollection(this, static e => e.IsHtml(TagId.Tr), childrenOnly: true, structural: true);

    /// <summary>https://html.spec.whatwg.org/multipage/tables.html#dom-tbody-insertrow</summary>
    [IdlMember("insertRow", "https://html.spec.whatwg.org/multipage/tables.html#dom-tbody-insertrow")]
    public HtmlTableRowElement InsertRow(int index = -1)
    {
        var rows = Rows;
        if (index < -1 || index > rows.Count)
            throw DomException.IndexSize($"The index {index} is out of range");

        var row = (HtmlTableRowElement)ElementFactory.CreateElement(NodeDocument, "tr", Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false);
        InsertBefore(row, index == -1 || index == rows.Count ? null : rows[index]);
        return row;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/tables.html#dom-tbody-deleterow</summary>
    [IdlMember("deleteRow", "https://html.spec.whatwg.org/multipage/tables.html#dom-tbody-deleterow")]
    public void DeleteRow(int index)
    {
        var rows = Rows;
        if (index == -1)
        {
            if (rows.Count > 0)
            {
                rows[rows.Count - 1].RemoveFromParent();
            }

            return;
        }

        if (index < -1 || index >= rows.Count)
            throw DomException.IndexSize($"The index {index} is out of range");

        rows[index].RemoveFromParent();
    }
}

public partial class HtmlTableRowElement
{
    private HtmlCollection? _cells;

    /// <summary>https://html.spec.whatwg.org/multipage/tables.html#dom-tr-rowindex</summary>
    [IdlMember("rowIndex", "https://html.spec.whatwg.org/multipage/tables.html#dom-tr-rowindex")]
    public int RowIndex
    {
        get
        {
            var table = ParentNode switch
            {
                HtmlTableElement parentTable => parentTable,
                Element { IsHtmlNamespace: true, TagId: TagId.Thead or TagId.Tbody or TagId.Tfoot } section => section.ParentNode as HtmlTableElement,
                _ => null,
            };

            return table is null ? -1 : IndexOf(table.Rows, this);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/tables.html#dom-tr-sectionrowindex</summary>
    [IdlMember("sectionRowIndex", "https://html.spec.whatwg.org/multipage/tables.html#dom-tr-sectionrowindex")]
    public int SectionRowIndex => ParentNode switch
    {
        HtmlTableElement table => IndexOf(table.Rows, this),
        HtmlTableSectionElement section => IndexOf(section.Rows, this),
        _ => -1,
    };

    internal static int IndexOf(HtmlCollection collection, Element element)
    {
        for (var i = 0; i < collection.Count; i++)
        {
            if (ReferenceEquals(collection[i], element))
                return i;
        }

        return -1;
    }

    /// <summary>Gets the live collection of the <c>td</c> and <c>th</c> children of the row.</summary>
    [IdlMember("cells", "https://html.spec.whatwg.org/multipage/tables.html#dom-tr-cells")]
    public HtmlCollection Cells => _cells ??= new HtmlCollection(this, static e => e.IsHtml(TagId.Td) || e.IsHtml(TagId.Th), childrenOnly: true, structural: true);

    /// <summary>https://html.spec.whatwg.org/multipage/tables.html#dom-tr-insertcell</summary>
    [IdlMember("insertCell", "https://html.spec.whatwg.org/multipage/tables.html#dom-tr-insertcell")]
    public HtmlTableCellElement InsertCell(int index = -1)
    {
        var cells = Cells;
        if (index < -1 || index > cells.Count)
            throw DomException.IndexSize($"The index {index} is out of range");

        var cell = (HtmlTableCellElement)ElementFactory.CreateElement(NodeDocument, "td", Namespaces.Html, prefix: null, isValue: null, synchronousCustomElements: false);
        InsertBefore(cell, index == -1 || index == cells.Count ? null : cells[index]);
        return cell;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/tables.html#dom-tr-deletecell</summary>
    [IdlMember("deleteCell", "https://html.spec.whatwg.org/multipage/tables.html#dom-tr-deletecell")]
    public void DeleteCell(int index)
    {
        var cells = Cells;
        if (index == -1)
        {
            if (cells.Count > 0)
            {
                cells[cells.Count - 1].RemoveFromParent();
            }

            return;
        }

        if (index < -1 || index >= cells.Count)
            throw DomException.IndexSize($"The index {index} is out of range");

        cells[index].RemoveFromParent();
    }
}

public partial class HtmlTableCellElement
{
    /// <summary>https://html.spec.whatwg.org/multipage/tables.html#dom-tdth-cellindex</summary>
    [IdlMember("cellIndex", "https://html.spec.whatwg.org/multipage/tables.html#dom-tdth-cellindex")]
    public int CellIndex => ParentNode is HtmlTableRowElement row ? HtmlTableRowElement.IndexOf(row.Cells, this) : -1;
}
