using System.Diagnostics;
using System.Runtime.InteropServices;
using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering;
using Meziantou.Framework.Pdf.Rendering.Images;
using Meziantou.Framework.Pdf.Text;
using Meziantou.Framework.Pdf.Writing;

namespace Meziantou.Framework.Pdf.Redaction;

/// <summary>
/// Rewrites a content stream without what it paints in the areas of a redaction: the glyphs whose box touches an area,
/// the subpaths that lie inside one (the paths that cross one are clipped), the pixels of the images under them, and the
/// same in the forms it paints, which are replaced by redacted copies. The replacement texts of the marked-content
/// sequences that lose content are removed with it.
/// </summary>
internal sealed class ContentRedactor : IDisposable
{
    private const int MaxFormDepth = 16;

    // The work of a page, as for the text extraction: four bytes of content, an operator or a glyph
    private const long MaxOperations = 200_000_000;

    private readonly PdfDocument _document;
    private readonly PdfRectangle[] _areas;
    private readonly PdfRectangle _world;
    private readonly PdfRedactionResult _result;
    private readonly HashSet<PdfStream> _activeForms = new(ReferenceEqualityComparer.Instance);
    private RenderCache? _renderCache;
    private long _operations;

    public ContentRedactor(PdfDocument document, PdfRectangle[] areas, PdfRectangle world, PdfRedactionResult result)
    {
        _document = document;
        _areas = areas;
        _world = world;
        _result = result;
    }

    private RenderCache RenderCache => _renderCache ??= new RenderCache(new PdfRenderOptions { UseSystemFonts = false });

    public void Dispose() => _renderCache?.Dispose();

    /// <summary>Redacts a content stream.</summary>
    /// <param name="content">The content.</param>
    /// <param name="resources">The resources of the content.</param>
    /// <param name="matrix">The transformation matrix at the start of the content, to the default user space of the page.</param>
    /// <param name="depth">The number of forms that paint the content.</param>
    /// <returns>The new content, or <see langword="null"/> when nothing of the content is in the areas.</returns>
    public RedactedContent? Redact(ReadOnlyMemory<byte> content, PdfDictionary? resources, PdfMatrix matrix, int depth) => Redact(content, resources, new GraphicsState { Ctm = matrix }, depth);

    private RedactedContent? Redact(ReadOnlyMemory<byte> content, PdfDictionary? resources, GraphicsState state, int depth)
    {
        var run = new ContentRun(this, resources, state, depth);
        run.Execute(content);
        return run.Changed ? run.Build() : null;
    }

    internal static bool Intersects(PdfRectangle box, PdfRectangle area) => box.Left < area.Right && box.Right > area.Left && box.Bottom < area.Top && box.Top > area.Bottom;

    private bool IntersectsAny(PdfRectangle box)
    {
        foreach (var area in _areas)
        {
            if (Intersects(box, area))
                return true;
        }

        return false;
    }

    private bool IsInsideOneArea(PdfRectangle box)
    {
        foreach (var area in _areas)
        {
            if (box.Left >= area.Left && box.Right <= area.Right && box.Bottom >= area.Bottom && box.Top <= area.Top)
                return true;
        }

        return false;
    }

    private void Charge(long count)
    {
        _operations += count;
        if (_operations > MaxOperations)
            throw new PdfResourceLimitException($"The redaction of the page runs more than {MaxOperations} operators and glyphs");
    }

    private static PdfRectangle Transform(PdfMatrix matrix, PdfRectangle rectangle)
    {
        var (x1, y1) = matrix.Transform(rectangle.Left, rectangle.Bottom);
        var (x2, y2) = matrix.Transform(rectangle.Right, rectangle.Bottom);
        var (x3, y3) = matrix.Transform(rectangle.Left, rectangle.Top);
        var (x4, y4) = matrix.Transform(rectangle.Right, rectangle.Top);
        return new PdfRectangle(Math.Min(Math.Min(x1, x2), Math.Min(x3, x4)), Math.Min(Math.Min(y1, y2), Math.Min(y3, y4)), Math.Max(Math.Max(x1, x2), Math.Max(x3, x4)), Math.Max(Math.Max(y1, y2), Math.Max(y3, y4)));
    }

    private static PdfMatrix ReadMatrix(PdfArray? array) => array is { Count: 6 }
        ? new PdfMatrix(array.GetNumber(0), array.GetNumber(1), array.GetNumber(2), array.GetNumber(3), array.GetNumber(4), array.GetNumber(5))
        : PdfMatrix.Identity;

    [StructLayout(LayoutKind.Auto)]
    private struct GraphicsState()
    {
        public PdfMatrix Ctm { get; set; } = PdfMatrix.Identity;

        public PdfMatrix TextMatrix { get; set; } = PdfMatrix.Identity;

        public PdfMatrix LineMatrix { get; set; } = PdfMatrix.Identity;

        public double CharacterSpacing { get; set; }

        public double WordSpacing { get; set; }

        public double HorizontalScaling { get; set; } = 1;

        public double Leading { get; set; }

        public double Rise { get; set; }

        public double FontSize { get; set; } = 1;

        public PdfFontDecoder? Font { get; set; }

        // The name of the font resource, when the font was set by Tf
        public PdfName? FontName { get; set; }

        public double LineWidth { get; set; } = 1;

        public double MiterLimit { get; set; } = 10;
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ContentOp(string Name, PdfObject[] Operands, ReadOnlyMemory<byte> InlineData = default);

    private sealed class Subpath
    {
        public List<ContentOp> Ops { get; } = [];

        public double StartX { get; init; }

        public double StartY { get; init; }

        public double Left { get; private set; } = double.PositiveInfinity;

        public double Bottom { get; private set; } = double.PositiveInfinity;

        public double Right { get; private set; } = double.NegativeInfinity;

        public double Top { get; private set; } = double.NegativeInfinity;

        public bool HasPoints => Left <= Right;

        public void AddPoint((double X, double Y) point)
        {
            Left = Math.Min(Left, point.X);
            Right = Math.Max(Right, point.X);
            Bottom = Math.Min(Bottom, point.Y);
            Top = Math.Max(Top, point.Y);
        }

        public PdfRectangle GetBounds(double expansion) => new(Left - expansion, Bottom - expansion, Right + expansion, Top + expansion);
    }

    /// <summary>The redaction of one content stream.</summary>
    private sealed class ContentRun
    {
        private static readonly PdfName Lw = PdfName.Create("LW");
        private static readonly PdfName Ml = PdfName.Create("ML");
        private static readonly PdfName FontKey = PdfName.Create("Font");
        private static readonly PdfName E = PdfName.Create("E");

        private readonly ContentRedactor _owner;
        private readonly PdfDictionary? _resources;
        private readonly int _depth;
        private readonly List<ContentOp> _ops = [];
        private readonly Stack<GraphicsState> _states = new();
        private readonly List<Subpath> _subpaths = [];
        private readonly List<int> _markedContent = [];
        private readonly HashSet<int> _redactedMarkedContent = [];
        private readonly List<(PdfName Category, PdfName Name, PdfObject Value)> _added = [];
        private readonly HashSet<(PdfName Category, PdfName Name)> _replaced = [];
        private GraphicsState _state;
        private Subpath? _current;
        private double _currentX;
        private double _currentY;
        private string? _pendingClip;
        private int _nextName = 1;

        // A form kept as it is that has no resources uses those of the content: the resources replaced stay
        private bool _keepReplaced;

        public ContentRun(ContentRedactor owner, PdfDictionary? resources, GraphicsState state, int depth)
        {
            _owner = owner;
            _resources = resources;
            _depth = depth;
            _state = state;
        }

        public bool Changed { get; private set; }

        public void Execute(ReadOnlyMemory<byte> content)
        {
            _owner.Charge((content.Length / 4) + 1);
            var reader = new ContentStreamReader(content);
            var operands = new List<PdfObject>();
            while (reader.TryRead(operands, out var name))
            {
                _owner.Charge(1);
                Process(name, operands);
            }

            // A path the content does not paint is kept as it is
            FlushUnpaintedPath();
        }

        private void Process(string name, List<PdfObject> operands)
        {
            switch (name)
            {
                case "m" or "l" or "c" or "v" or "y" or "h" or "re":
                    AddPathOperator(name, operands);
                    return;
                case "W" or "W*":
                    _pendingClip = name;
                    return;
                case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*" or "n":
                    PaintPath(name);
                    return;
            }

            // The operators of the graphics state and of the colors do not end a path: readers paint it with the state
            // the painting operator finds (and with the transformation matrix of each point). They do not change the
            // points of the path, so they are written before it. The other operators end a path that is not painted.
            if (!IsStateOperator(name))
            {
                FlushUnpaintedPath();
            }

            switch (name)
            {
                case "q":
                    _states.Push(_state);
                    break;
                case "Q":
                    if (_states.Count > 0)
                    {
                        _state = _states.Pop();
                    }

                    break;
                case "cm" when operands.Count >= 6:
                    _state.Ctm = ReadOperandMatrix(operands) * _state.Ctm;
                    break;
                case "w" when operands.Count >= 1:
                    _state.LineWidth = Number(operands, 0);
                    break;
                case "M" when operands.Count >= 1:
                    _state.MiterLimit = Number(operands, 0);
                    break;
                case "gs" when operands.Count >= 1 && operands[^1] is PdfName stateName:
                    ApplyExtGState(stateName);
                    break;
                case "BT":
                    _state.TextMatrix = PdfMatrix.Identity;
                    _state.LineMatrix = PdfMatrix.Identity;
                    break;
                case "Tc" when operands.Count >= 1:
                    _state.CharacterSpacing = Number(operands, 0);
                    break;
                case "Tw" when operands.Count >= 1:
                    _state.WordSpacing = Number(operands, 0);
                    break;
                case "Tz" when operands.Count >= 1:
                    _state.HorizontalScaling = Number(operands, 0) / 100;
                    break;
                case "TL" when operands.Count >= 1:
                    _state.Leading = Number(operands, 0);
                    break;
                case "Ts" when operands.Count >= 1:
                    _state.Rise = Number(operands, 0);
                    break;
                case "Tf" when operands.Count >= 2:
                    _state.FontSize = Number(operands, 1);
                    _state.FontName = operands[0] as PdfName;
                    _state.Font = _state.FontName is { } fontName && _resources?.Get<PdfDictionary>(PdfNames.Font)?.Get<PdfDictionary>(fontName) is { } font ? _owner._document.TextDecoders.Get(font) : null;
                    break;
                case "Td" when operands.Count >= 2:
                    MoveText(Number(operands, 0), Number(operands, 1));
                    break;
                case "TD" when operands.Count >= 2:
                    _state.Leading = -Number(operands, 1);
                    MoveText(Number(operands, 0), Number(operands, 1));
                    break;
                case "Tm" when operands.Count >= 6:
                    _state.TextMatrix = ReadOperandMatrix(operands);
                    _state.LineMatrix = _state.TextMatrix;
                    break;
                case "T*":
                    MoveText(0, -_state.Leading);
                    break;
                case "Tj" or "'" when operands.Count >= 1 && operands[^1] is PdfString:
                case "\"" when operands.Count >= 3 && operands[^1] is PdfString:
                case "TJ" when operands.Count >= 1 && operands[^1] is PdfArray:
                    ShowText(name, operands);
                    return;
                case "BMC" or "BDC":
                    _markedContent.Add(_ops.Count);
                    break;
                case "EMC":
                    if (_markedContent.Count > 0)
                    {
                        _markedContent.RemoveAt(_markedContent.Count - 1);
                    }

                    break;
                case "Do" when operands.Count >= 1 && operands[^1] is PdfName xobjectName:
                    PaintXObject(xobjectName, operands);
                    return;
                case "BI" when operands.Count == 2 && operands[0] is PdfDictionary inline && operands[1] is PdfString data:
                    PaintInlineImage(inline, data);
                    return;
            }

            Emit(name, operands);
        }

        private static bool IsStateOperator(string name) => name is "w" or "J" or "j" or "M" or "d" or "ri" or "i" or "gs" or "CS" or "cs" or "SC" or "SCN" or "sc" or "scn" or "G" or "g" or "RG" or "rg" or "K" or "k";

        private void Emit(string name, List<PdfObject> operands) => _ops.Add(new ContentOp(name, [.. operands]));

        private void Emit(string name, params PdfObject[] operands) => _ops.Add(new ContentOp(name, operands));

        /// <summary>Marks the marked-content sequences that are open as having lost content: their replacement texts are removed.</summary>
        private void MarkRemoval()
        {
            Changed = true;
            foreach (var index in _markedContent)
            {
                _redactedMarkedContent.Add(index);
            }
        }

        private void ApplyExtGState(PdfName name)
        {
            if (_resources?.Get<PdfDictionary>(PdfNames.ExtGState)?.Get<PdfDictionary>(name) is not { } state)
                return;

            if (state.GetResolved(Lw) is PdfNumber lineWidth)
            {
                _state.LineWidth = lineWidth.DoubleValue;
            }

            if (state.GetResolved(Ml) is PdfNumber miterLimit)
            {
                _state.MiterLimit = miterLimit.DoubleValue;
            }

            if (state.Get<PdfArray>(FontKey) is { Count: 2 } font && font.GetResolved(0) is PdfDictionary fontDictionary && font.GetResolved(1) is PdfNumber size)
            {
                _state.Font = _owner._document.TextDecoders.Get(fontDictionary);
                _state.FontName = null;
                _state.FontSize = size.DoubleValue;
            }
        }

        private void MoveText(double x, double y)
        {
            _state.LineMatrix = PdfMatrix.CreateTranslation(x, y) * _state.LineMatrix;
            _state.TextMatrix = _state.LineMatrix;
        }

        // Paths

        private void AddPathOperator(string name, List<PdfObject> operands)
        {
            var op = new ContentOp(name, [.. operands]);
            var ctm = _state.Ctm;
            var valid = AllNumbers(operands);
            switch (name)
            {
                case "m":
                    _current = new Subpath { StartX = Number(operands, 0), StartY = Number(operands, 1) };
                    _subpaths.Add(_current);
                    _current.Ops.Add(op);
                    if (valid && operands.Count >= 2)
                    {
                        _currentX = Number(operands, 0);
                        _currentY = Number(operands, 1);
                        _current.AddPoint(ctm.Transform(_currentX, _currentY));
                    }

                    return;
                case "re":
                    var rectangle = new Subpath { StartX = Number(operands, 0), StartY = Number(operands, 1) };
                    _subpaths.Add(rectangle);
                    rectangle.Ops.Add(op);
                    if (valid && operands.Count >= 4)
                    {
                        var (x, y, width, height) = (Number(operands, 0), Number(operands, 1), Number(operands, 2), Number(operands, 3));
                        rectangle.AddPoint(ctm.Transform(x, y));
                        rectangle.AddPoint(ctm.Transform(x + width, y));
                        rectangle.AddPoint(ctm.Transform(x, y + height));
                        rectangle.AddPoint(ctm.Transform(x + width, y + height));
                        _currentX = x;
                        _currentY = y;
                    }

                    // The rectangle is a closed subpath: a segment after it starts a new one at its first corner
                    _current = null;
                    return;
            }

            if (_current is null)
            {
                _current = new Subpath { StartX = _currentX, StartY = _currentY };
                _current.AddPoint(ctm.Transform(_currentX, _currentY));
                _subpaths.Add(_current);
            }

            _current.Ops.Add(op);
            if (name == "h")
            {
                _currentX = _current.StartX;
                _currentY = _current.StartY;
                _current = null;
                return;
            }

            if (!valid)
                return;

            // The control points of a curve contain it (its convex hull)
            for (var i = 0; i + 1 < operands.Count; i += 2)
            {
                _current.AddPoint(ctm.Transform(Number(operands, i), Number(operands, i + 1)));
            }

            if (operands.Count >= 2)
            {
                _currentX = Number(operands, operands.Count - 2);
                _currentY = Number(operands, operands.Count - 1);
            }
        }

        private void FlushUnpaintedPath()
        {
            if (_subpaths.Count == 0 && _pendingClip is null)
                return;

            foreach (var subpath in _subpaths)
            {
                _ops.AddRange(subpath.Ops);
            }

            if (_pendingClip is not null)
            {
                Emit(_pendingClip);
            }

            ClearPath();
        }

        private void ClearPath()
        {
            _subpaths.Clear();
            _current = null;
            _pendingClip = null;
        }

        private void PaintPath(string paint)
        {
            var stroke = paint is "S" or "s" or "B" or "B*" or "b" or "b*";
            var expansion = stroke ? GetStrokeExpansion() : 0;
            var removed = new bool[_subpaths.Count];
            var removedCount = 0;
            var crosses = false;
            for (var i = 0; i < _subpaths.Count; i++)
            {
                var subpath = _subpaths[i];
                if (!subpath.HasPoints)
                    continue;

                var bounds = subpath.GetBounds(expansion);
                if (_owner.IsInsideOneArea(bounds))
                {
                    // The subpath only paints inside the area: removing it changes nothing outside the area, whatever the
                    // fill rule, since the winding number of a point outside its bounding box is zero
                    removed[i] = true;
                    removedCount++;
                }
                else if (_owner.IntersectsAny(bounds))
                {
                    crosses = true;
                }
            }

            var clip = crosses && paint != "n";
            if (removedCount == 0 && !clip)
            {
                FlushUnpaintedPath();
                Emit(paint);
                return;
            }

            // A path that loses subpaths or is clipped loses content: the replacement text of its sequence could tell what
            _owner._result.RemovedPathCount += removedCount;
            MarkRemoval();

            Changed = true;
            var kept = new List<Subpath>(_subpaths.Count - removedCount);
            for (var i = 0; i < _subpaths.Count; i++)
            {
                if (!removed[i])
                {
                    kept.Add(_subpaths[i]);
                }
            }

            // A closing operator closes the last subpath: it is closed explicitly when it is kept, so that the operator
            // does not close another one
            var lastKept = !removed[^1];
            if (paint is "s" or "b" or "b*")
            {
                paint = paint switch { "s" => "S", "b" => "B", _ => "B*" };
                if (lastKept)
                {
                    kept[^1].Ops.Add(new ContentOp("h", []));
                }
            }

            var pendingClip = _pendingClip;
            if (clip && kept.Count > 0)
            {
                _owner._result.ClippedPathCount++;
                Emit("q");
                EmitExclusionClip();
                EmitSubpaths(kept);
                Emit(paint);
                Emit("Q");
                if (pendingClip is not null)
                {
                    EmitClip(kept, pendingClip);
                }
            }
            else if (pendingClip is not null)
            {
                EmitClip(kept, pendingClip);
            }
            else if (kept.Count > 0 && paint != "n")
            {
                EmitSubpaths(kept);
                Emit(paint);
            }

            ClearPath();
        }

        /// <summary>
        /// Writes a clipping path. Without subpaths, the clip is empty: the subpaths removed were inside the areas, so the
        /// clipping path only let content inside the areas through, which the redaction removes.
        /// </summary>
        private void EmitClip(List<Subpath> kept, string clip)
        {
            if (kept.Count == 0)
            {
                Emit("re", PdfInteger.Get(0), PdfInteger.Get(0), PdfInteger.Get(0), PdfInteger.Get(0));
            }
            else
            {
                EmitSubpaths(kept);
            }

            Emit(clip);
            Emit("n");
        }

        private void EmitSubpaths(List<Subpath> subpaths)
        {
            foreach (var subpath in subpaths)
            {
                // A subpath that started from the current point after a closed subpath starts with its point
                if (subpath.Ops.Count == 0 || subpath.Ops[0].Name is not ("m" or "re"))
                {
                    Emit("m", PdfReal.Get(subpath.StartX), PdfReal.Get(subpath.StartY));
                }

                _ops.AddRange(subpath.Ops);
            }
        }

        /// <summary>
        /// Clips out the areas: one clipping path per area, the rectangle of the page with the area as a hole (even-odd
        /// rule), in the current user space.
        /// </summary>
        private void EmitExclusionClip()
        {
            if (!_state.Ctm.TryInvert(out var inverse))
                return;

            foreach (var area in _owner._areas)
            {
                EmitPolygon(inverse, _owner._world);
                EmitPolygon(inverse, area);
                Emit("W*");
                Emit("n");
            }
        }

        private void EmitPolygon(PdfMatrix inverse, PdfRectangle rectangle)
        {
            var (x1, y1) = inverse.Transform(rectangle.Left, rectangle.Bottom);
            var (x2, y2) = inverse.Transform(rectangle.Right, rectangle.Bottom);
            var (x3, y3) = inverse.Transform(rectangle.Right, rectangle.Top);
            var (x4, y4) = inverse.Transform(rectangle.Left, rectangle.Top);
            Emit("m", PdfReal.Get(x1), PdfReal.Get(y1));
            Emit("l", PdfReal.Get(x2), PdfReal.Get(y2));
            Emit("l", PdfReal.Get(x3), PdfReal.Get(y3));
            Emit("l", PdfReal.Get(x4), PdfReal.Get(y4));
            Emit("h");
        }

        /// <summary>
        /// Gets how far a stroke can paint from its path, in default user space: half the line width, times the miter
        /// limit for the joins, scaled by the transformation matrix (an upper bound of its norm), and a point more.
        /// </summary>
        private double GetStrokeExpansion()
        {
            var ctm = _state.Ctm;
            var scale = Math.Sqrt((ctm.A * ctm.A) + (ctm.B * ctm.B)) + Math.Sqrt((ctm.C * ctm.C) + (ctm.D * ctm.D));
            var halfWidth = Math.Abs(_state.LineWidth) / 2 * Math.Max(_state.MiterLimit, 1.5);
            var expansion = (halfWidth * scale) + 1;
            return double.IsFinite(expansion) ? expansion : double.MaxValue / 4;
        }

        // Text

        private void ShowText(string name, List<PdfObject> operands)
        {
            var original = new ContentOp(name, [.. operands]);
            var before = _state;
            if (name == "\"")
            {
                _state.WordSpacing = Number(operands, operands.Count - 3);
                _state.CharacterSpacing = Number(operands, operands.Count - 2);
            }

            if (name is "'" or "\"")
            {
                MoveText(0, -_state.Leading);
            }

            var font = _state.Font;
            if (font is null)
            {
                // Without a font, the position of the glyphs is unknown: the text is removed, and the operator still
                // moves to the next line
                MarkRemoval();
                EmitLineMove(name, operands);
                return;
            }

            var pieces = new List<TextPiece>();
            var removedAny = false;
            if (operands[^1] is PdfArray array)
            {
                foreach (var item in array)
                {
                    _owner.Charge(1);
                    switch (item)
                    {
                        case PdfString text:
                            removedAny |= ShowString(font, text, pieces);
                            break;
                        case PdfNumber adjustment:
                            pieces.Add(TextPiece.Adjustment(adjustment.DoubleValue * _state.FontSize));
                            var offset = -adjustment.DoubleValue / 1000 * _state.FontSize;
                            _state.TextMatrix = (font.IsVertical ? PdfMatrix.CreateTranslation(0, offset) : PdfMatrix.CreateTranslation(offset * _state.HorizontalScaling, 0)) * _state.TextMatrix;
                            break;
                    }
                }
            }
            else
            {
                removedAny = ShowString(font, (PdfString)operands[^1], pieces);
            }

            if (!removedAny)
            {
                _ops.Add(original);
                return;
            }

            MarkRemoval();
            EmitLineMove(name, operands);
            EmitPieces(pieces, before);
        }

        /// <summary>Writes the part of <c>'</c> and <c>"</c> that moves to the next line, without their text.</summary>
        private void EmitLineMove(string name, List<PdfObject> operands)
        {
            if (name == "\"")
            {
                Emit("Tw", operands[^3]);
                Emit("Tc", operands[^2]);
            }

            if (name is "'" or "\"")
            {
                Emit("T*");
            }
        }

        /// <summary>
        /// Writes the glyphs kept, with the displacement of the glyphs removed as the numbers of a <c>TJ</c> array (9.4.3),
        /// so that the glyphs kept and the text after them stay in place.
        /// </summary>
        private void EmitPieces(List<TextPiece> pieces, GraphicsState before)
        {
            if (before.FontSize != 0)
            {
                var items = new List<PdfObject>();
                var adjustment = 0d;
                foreach (var piece in pieces)
                {
                    if (piece.Text is { } text)
                    {
                        if (adjustment != 0)
                        {
                            items.Add(PdfReal.Get(adjustment));
                            adjustment = 0;
                        }

                        items.Add(text);
                    }
                    else
                    {
                        adjustment += piece.Value / before.FontSize;
                    }
                }

                if (adjustment != 0)
                {
                    items.Add(PdfReal.Get(adjustment));
                }

                Emit("TJ", new PdfArray(items));
                return;
            }

            // A font size of 0: the numbers of a TJ array move nothing, so the displacements are written with a font size of 1
            foreach (var piece in pieces)
            {
                if (piece.Text is { } text)
                {
                    Emit("Tj", text);
                }
                else if (before.FontName is { } fontName && piece.Value != 0)
                {
                    Emit("Tf", fontName, PdfInteger.Get(1));
                    Emit("TJ", new PdfArray(PdfReal.Get(piece.Value)));
                    Emit("Tf", fontName, PdfInteger.Get(0));
                }
            }
        }

        /// <summary>
        /// Shows the glyphs of a string as the text extraction does (9.4.4), and splits it into the codes kept and the
        /// displacements of the codes removed: a displacement is in thousandths of a unit of text space, times the font
        /// size (it is divided by the font size when written).
        /// </summary>
        /// <returns>Whether a glyph was removed.</returns>
        private bool ShowString(PdfFontDecoder font, PdfString text, List<TextPiece> pieces)
        {
            var bytes = text.Bytes.Span;
            var matrix = _state.TextMatrix * _state.Ctm;
            var vertical = font.IsVertical;
            var (upX, upY) = matrix.TransformVector(0, _state.FontSize);
            var (acrossX, acrossY) = vertical ? matrix.TransformVector(_state.FontSize * _state.HorizontalScaling, 0) : (upX, upY);
            var x = 0d;
            var y = 0d;
            var removedAny = false;
            var keptStart = 0;
            var position = 0;
            while (position < bytes.Length)
            {
                _owner.Charge(1);
                var (code, length) = font.ReadCode(bytes[position..]);
                length = Math.Clamp(length, 1, bytes.Length - position);
                var spacing = _state.CharacterSpacing + (length == 1 && code == 32 ? _state.WordSpacing : 0);
                double advanceX;
                double advanceY;
                double low;
                double high;
                if (vertical)
                {
                    var (verticalAdvance, originX, _) = font.GetVerticalMetrics(code);
                    advanceX = 0;
                    advanceY = (verticalAdvance * _state.FontSize) + spacing;
                    low = -originX;
                    high = font.GetWidth(code) - originX;
                }
                else
                {
                    advanceX = ((font.GetWidth(code) * _state.FontSize) + spacing) * _state.HorizontalScaling;
                    advanceY = 0;
                    low = font.Descent;
                    high = font.Ascent;
                }

                var (startX, startY) = matrix.Transform(x, y + _state.Rise);
                var (advanceVectorX, advanceVectorY) = matrix.TransformVector(advanceX, advanceY);
                var bounds = TextExtractor.GetBounds(startX, startY, advanceVectorX, advanceVectorY, acrossX, acrossY, low, high);
                if (_owner.IntersectsAny(bounds))
                {
                    if (position > keptStart)
                    {
                        pieces.Add(TextPiece.Codes(PdfString.FromBytes(bytes[keptStart..position], text.IsHexadecimal)));
                    }

                    // The number that moves by the displacement of the glyph: -advance * 1000 / (font size * scaling) for
                    // horizontal writing, -advance * 1000 / font size for vertical writing; here times the font size
                    var displacement = vertical ? advanceY : (_state.HorizontalScaling != 0 ? advanceX / _state.HorizontalScaling : 0);
                    pieces.Add(TextPiece.Adjustment(-displacement * 1000));
                    keptStart = position + length;
                    removedAny = true;
                    _owner._result.RemovedGlyphCount++;
                }

                x += advanceX;
                y += advanceY;
                position += length;
            }

            if (keptStart < bytes.Length)
            {
                pieces.Add(TextPiece.Codes(keptStart == 0 ? text : PdfString.FromBytes(bytes[keptStart..], text.IsHexadecimal)));
            }

            _state.TextMatrix = PdfMatrix.CreateTranslation(x, y) * _state.TextMatrix;
            return removedAny;
        }

        // XObjects and inline images

        private void PaintXObject(PdfName name, List<PdfObject> operands)
        {
            if (_resources?.Get<PdfDictionary>(PdfNames.XObject)?.GetResolved(name) is not PdfStream stream)
            {
                Emit("Do", operands);
                return;
            }

            var subtype = stream.Dictionary.GetName(PdfNames.Subtype);
            if (subtype == PdfNames.Image)
            {
                var bounds = Transform(_state.Ctm, new PdfRectangle(0, 0, 1, 1));
                if (!_owner.IntersectsAny(bounds))
                {
                    Emit("Do", operands);
                    return;
                }

                var decoded = PdfImageDecoder.Decode(stream, _resources, _owner.RenderCache);
                ReplaceImage(name, decoded, stream.Dictionary, operands);
            }
            else if (subtype == PdfNames.Form)
            {
                PaintForm(name, stream, operands);
            }
            else
            {
                Emit("Do", operands);
            }
        }

        private void PaintForm(PdfName name, PdfStream form, List<PdfObject> operands)
        {
            var matrix = ReadMatrix(form.Dictionary.Get<PdfArray>(PdfNames.Matrix)) * _state.Ctm;
            var ownResources = form.Dictionary.Get<PdfDictionary>(PdfNames.Resources);
            var intersects = !PdfRectangle.TryRead(form.Dictionary.Get<PdfArray>(PdfNames.BBox), out var bbox) || _owner.IntersectsAny(Transform(matrix, bbox));
            if (!intersects)
            {
                _keepReplaced |= ownResources is null;
                Emit("Do", operands);
                return;
            }

            if (_owner._activeForms.Contains(form))
            {
                // A form that paints itself: the readers stop the recursion, the redaction removes it
                MarkRemoval();
                return;
            }

            if (_depth >= MaxFormDepth)
                throw new PdfResourceLimitException($"The forms of the page are nested more than {MaxFormDepth} levels deep");

            var formResources = ownResources ?? _resources;
            RedactedContent? content;
            _owner._activeForms.Add(form);
            try
            {
                // A form starts with the graphics state of the content that paints it, its text state included (8.10.1)
                content = _owner.Redact(form.GetDecodedData(), formResources, _state with { Ctm = matrix }, _depth + 1);
            }
            finally
            {
                _owner._activeForms.Remove(form);
            }

            if (content is null)
            {
                _keepReplaced |= ownResources is null;
                Emit("Do", operands);
                return;
            }

            // A copy of the form: the form can be painted elsewhere, outside the areas
            var dictionary = form.Dictionary.CloneShallow();
            var resources = CloneResources(formResources);
            content.ApplyTo(resources);
            dictionary[PdfNames.Resources] = resources;
            var copy = new PdfStream(dictionary);
            copy.SetData(content.Data);
            var reference = _owner._document.Objects.Add(copy);
            ReplaceXObject(name, reference, operands);
            MarkRemoval();
        }

        private void PaintInlineImage(PdfDictionary inline, PdfString data)
        {
            var bounds = Transform(_state.Ctm, new PdfRectangle(0, 0, 1, 1));
            if (!_owner.IntersectsAny(bounds))
            {
                _ops.Add(new ContentOp("BI", [inline], data.Bytes));
                return;
            }

            var limits = _owner._document.Reader?.DecodeLimits ?? Filters.PdfDecodeLimits.Default;
            var decoded = PdfImageDecoder.DecodeInline(inline, data.Bytes, _resources, limits, _owner.RenderCache);
            ReplaceImage(name: null, decoded, PdfImageOccurrence.ExpandInlineDictionary(inline), operands: null);
        }

        /// <summary>Replaces an image by a copy without its pixels in the areas, painted as an image XObject.</summary>
        private void ReplaceImage(PdfName? name, DecodedImage? decoded, PdfDictionary dictionary, List<PdfObject>? operands)
        {
            var (kind, reference) = ImageRedactor.Redact(_owner._document, decoded, dictionary, _state.Ctm, _owner._areas);
            switch (kind)
            {
                case ImageRedactionKind.Unchanged when operands is not null:
                    Emit("Do", operands);
                    return;
                case ImageRedactionKind.Unchanged:
                    throw new UnreachableException();
                case ImageRedactionKind.Removed:
                    _owner._result.RedactedImageCount++;
                    MarkRemoval();
                    if (name is not null)
                    {
                        _replaced.Add((PdfNames.XObject, name));
                    }

                    return;
                default:
                    _owner._result.RedactedImageCount++;
                    MarkRemoval();
                    ReplaceXObject(name, reference!, operands);
                    return;
            }
        }

        private void ReplaceXObject(PdfName? name, PdfReference reference, List<PdfObject>? operands)
        {
            var newName = AddResource(PdfNames.XObject, reference);
            if (name is not null)
            {
                _replaced.Add((PdfNames.XObject, name));
            }

            // The operands before the name, if any, are kept: they are not valid, and the readers ignore them as before
            var newOperands = operands is null ? [newName] : operands.Take(operands.Count - 1).Append(newName).ToArray();
            Emit("Do", newOperands);
        }

        private PdfName AddResource(PdfName category, PdfObject value)
        {
            var existing = _resources?.Get<PdfDictionary>(category);
            while (true)
            {
                var name = PdfName.Create("Rd" + _nextName.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _nextName++;
                if ((existing is null || !existing.ContainsKey(name)) && !_added.Exists(item => item.Category == category && item.Name == name))
                {
                    _added.Add((category, name, value));
                    return name;
                }
            }
        }

        private static PdfDictionary CloneResources(PdfDictionary? resources)
        {
            var result = new PdfDictionary();
            if (resources is null)
                return result;

            foreach (var (key, value) in resources)
            {
                result[key] = value;
            }

            return result;
        }

        // Output

        public RedactedContent Build()
        {
            RemoveReplacementTexts();
            var writer = new PdfByteWriter(4096);
            var used = new HashSet<(PdfName Category, PdfName Name)>();
            foreach (var op in _ops)
            {
                ContentStreamWriter.Write(writer, op.Name, op.Operands, op.InlineData.Span);
                if (op.Name == "Do" && op.Operands is [.., PdfName xobject])
                {
                    used.Add((PdfNames.XObject, xobject));
                }
                else if (op.Name == "BDC" && op.Operands is [_, PdfName properties, ..])
                {
                    used.Add((PdfNames.Properties, properties));
                }
            }

            return new RedactedContent(writer.ToArray(), _added, _keepReplaced ? [] : _replaced, used);
        }

        /// <summary>
        /// Removes the replacement texts, alternate descriptions and expansions (14.9) of the marked-content sequences that
        /// lost content: they could tell what was removed.
        /// </summary>
        private void RemoveReplacementTexts()
        {
            foreach (var index in _redactedMarkedContent)
            {
                var op = _ops[index];
                if (op.Name != "BDC" || op.Operands.Length < 2)
                    continue;

                var operand = op.Operands[1];
                var properties = operand switch
                {
                    PdfDictionary dictionary => dictionary,
                    PdfName name => _resources?.Get<PdfDictionary>(PdfNames.Properties)?.Get<PdfDictionary>(name),
                    _ => null,
                };
                if (properties is null || !(properties.ContainsKey(PdfNames.ActualText) || properties.ContainsKey(PdfNames.Alt) || properties.ContainsKey(E)))
                    continue;

                var copy = properties.CloneShallow();
                copy.Remove(PdfNames.ActualText);
                copy.Remove(PdfNames.Alt);
                copy.Remove(E);
                PdfObject replacement = copy;
                if (operand is PdfName propertyName)
                {
                    _replaced.Add((PdfNames.Properties, propertyName));
                    replacement = AddResource(PdfNames.Properties, copy);
                }

                var operands = (PdfObject[])op.Operands.Clone();
                operands[1] = replacement;
                _ops[index] = op with { Operands = operands };
            }
        }

        private static bool AllNumbers(List<PdfObject> operands) => operands.TrueForAll(operand => operand is PdfNumber);

        private static PdfMatrix ReadOperandMatrix(List<PdfObject> operands)
        {
            var start = operands.Count - 6;
            return new PdfMatrix(Number(operands, start), Number(operands, start + 1), Number(operands, start + 2), Number(operands, start + 3), Number(operands, start + 4), Number(operands, start + 5));
        }

        private static double Number(List<PdfObject> operands, int index) => index >= 0 && index < operands.Count && operands[index] is PdfNumber number ? number.DoubleValue : 0;
    }

    /// <summary>A part of a text: codes shown, or a displacement in thousandths of a unit of text space, times the font size.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct TextPiece(PdfString? Text, double Value)
    {
        public static TextPiece Codes(PdfString text) => new(text, 0);

        public static TextPiece Adjustment(double value) => new(null, value);
    }
}
