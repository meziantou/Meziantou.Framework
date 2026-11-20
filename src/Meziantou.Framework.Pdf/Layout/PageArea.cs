namespace Meziantou.Framework.Pdf.Layout;

/// <summary>The pages of a flow document: their content area, in one or several columns.</summary>
internal sealed class PageArea : FlowArea
{
    private readonly PdfFlowOptions _options;
    private int _columnCount = 1;
    private int _columnIndex;
    private double _columnGap;
    private double _columnTop;
    private double _lowestColumnY;

    public PageArea(PdfFlowOptions options)
    {
        _options = options;
        NewPage();
    }

    public List<List<DrawCommand>> Pages { get; } = [];

    public override double Bottom => _options.MarginBottom;

    public override bool CanBreak => true;

    public override int PageIndex => Pages.Count - 1;

    public override double FullHeight => Math.Max(1, _options.PageSize.Height - _options.MarginTop - _options.MarginBottom);

    private double ContentLeft => _options.MarginLeft;

    private double ContentWidth => Math.Max(1, _options.PageSize.Width - _options.MarginLeft - _options.MarginRight);

    public override void Add(DrawCommand command) => Pages[^1].Add(command);

    public override void Break()
    {
        _lowestColumnY = Math.Min(_lowestColumnY, Y);
        if (_columnIndex + 1 < _columnCount)
        {
            _columnIndex++;
            SetColumn(_columnTop);
            return;
        }

        NewPage();
        _columnIndex = 0;
        if (_columnCount > 1)
        {
            _columnTop = Y;
            _lowestColumnY = Y;
            SetColumn(_columnTop);
        }
    }

    public void NewPage()
    {
        Pages.Add([]);
        ColumnLeft = ContentLeft;
        ColumnWidth = ContentWidth;
        Y = _options.PageSize.Height - _options.MarginTop;
        AtTop = true;
    }

    public void BeginColumns(int count, double gap)
    {
        _columnCount = count;
        _columnGap = gap;
        _columnIndex = 0;
        _columnTop = Y;
        _lowestColumnY = Y;
        SetColumn(_columnTop);
        AtTop = true;
    }

    /// <summary>Ends the columns: the blocks that follow start below the lowest column of the page.</summary>
    public void EndColumns()
    {
        var y = Math.Min(_lowestColumnY, Y);
        _columnCount = 1;
        _columnIndex = 0;
        ColumnLeft = ContentLeft;
        ColumnWidth = ContentWidth;
        Y = y;
        AtTop = Y >= _options.PageSize.Height - _options.MarginTop;
    }

    private void SetColumn(double top)
    {
        var width = Math.Max(1, (ContentWidth - (_columnGap * (_columnCount - 1))) / _columnCount);
        ColumnLeft = ContentLeft + (_columnIndex * (width + _columnGap));
        ColumnWidth = width;
        Y = top;
        AtTop = true;
    }
}
