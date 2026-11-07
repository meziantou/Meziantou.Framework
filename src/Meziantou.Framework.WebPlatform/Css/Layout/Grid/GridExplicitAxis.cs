namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The two axes of a grid (https://drafts.csswg.org/css-grid-2/#grid-concepts): the columns (inline axis) and the rows (block axis).</summary>
internal enum GridAxis : byte
{
    /// <summary>The columns: the tracks of the inline axis of the grid container.</summary>
    Columns,

    /// <summary>The rows: the tracks of the block axis of the grid container.</summary>
    Rows,
}

/// <summary>
/// The explicit grid in one axis (https://drafts.csswg.org/css-grid-2/#explicit-grids): the track sizing functions of the
/// explicit tracks (the repetitions of <c>repeat()</c> expanded), the names of the explicit grid lines (the explicitly-assigned
/// names and the implicitly-assigned names of the named grid areas), and the track sizing functions of the implicit tracks
/// (<c>grid-auto-rows</c>, <c>grid-auto-columns</c>). The lines are numbered from 0 (the first explicit line) to
/// <see cref="TrackCount"/> (the last explicit line).
/// </summary>
internal sealed class GridExplicitAxis
{
    /// <summary>
    /// The largest number of tracks of a grid in one axis: "UAs may clamp the possible size of the implicit grid to be within a
    /// UA-defined limit (which should accommodate lines in the range [-10000, 10000])" (https://drafts.csswg.org/css-grid-2/#overlarge-grids).
    /// </summary>
    public const int MaxLine = 10000;

    private readonly Dictionary<string, List<int>>? _lineNames;

    private GridExplicitAxis(GridTrackSize[] templateTracks, int trackCount, GridTrackSize[] autoTracks, Dictionary<string, List<int>>? lineNames, string[]?[]? explicitLineNames, int autoRepeatStart, int autoRepeatCount, bool autoFit)
    {
        TemplateTracks = templateTracks;
        TrackCount = trackCount;
        AutoTracks = autoTracks;
        _lineNames = lineNames;
        ExplicitLineNames = explicitLineNames;
        AutoRepeatStart = autoRepeatStart;
        AutoRepeatCount = autoRepeatCount;
        IsAutoFit = autoFit;
    }

    /// <summary>The track sizing functions of the tracks of the track list (<c>grid-template-rows</c>, <c>grid-template-columns</c>), the repetitions expanded.</summary>
    public GridTrackSize[] TemplateTracks { get; }

    /// <summary>The number of explicit tracks: the tracks of the track list, or the rows/columns of <c>grid-template-areas</c> when there are more.</summary>
    public int TrackCount { get; }

    /// <summary>The track sizing functions of the implicit tracks (and of the explicit tracks beyond the track list).</summary>
    public GridTrackSize[] AutoTracks { get; }

    /// <summary>The explicitly-assigned names of each explicit line (<see cref="TrackCount"/> + 1 entries; null when no line has a name), for the resolved value.</summary>
    public string[]?[]? ExplicitLineNames { get; }

    /// <summary>The index of the first track of the automatic repetition (<c>auto-fill</c>, <c>auto-fit</c>).</summary>
    public int AutoRepeatStart { get; }

    /// <summary>The number of tracks created by the automatic repetition (0 when there is none).</summary>
    public int AutoRepeatCount { get; }

    /// <summary>Whether the automatic repetition is <c>auto-fit</c>: its empty tracks are collapsed (https://drafts.csswg.org/css-grid-2/#auto-repeat).</summary>
    public bool IsAutoFit { get; }

    /// <summary>
    /// Creates the stacking axis of a grid lanes container (https://drafts.csswg.org/css-grid-3/#abspos): one track, the stacking
    /// range, whose lines 1 (-2) and 2 (-1) are the start and end edges of the stacking range; the track lists are ignored.
    /// </summary>
    public static GridExplicitAxis CreateStackingAxis() => new([GridTrackSize.Auto], 1, [GridTrackSize.Auto], null, null, 0, 0, autoFit: false);

    /// <summary>Returns whether an explicit track is one of the repetitions of <c>auto-fit</c>.</summary>
    public bool IsAutoFitTrack(int index) => IsAutoFit && index >= AutoRepeatStart && index < AutoRepeatStart + AutoRepeatCount;

    /// <summary>
    /// Returns the track sizing function of a track, from its index relative to the first explicit line (negative for the
    /// implicit tracks before the explicit grid): "The first implicit grid track after the explicit grid receives the first
    /// specified size, and so on forwards; and the last implicit grid track before the explicit grid receives the last
    /// specified size, and so on backwards" (https://drafts.csswg.org/css-grid-2/#auto-tracks).
    /// </summary>
    public GridTrackSize GetTrackSize(int index)
    {
        if (index >= 0 && index < TemplateTracks.Length)
            return TemplateTracks[index];

        var count = AutoTracks.Length;
        if (index >= 0)
            return AutoTracks[(index - TemplateTracks.Length) % count];

        return AutoTracks[count - 1 - ((-index - 1) % count)];
    }

    /// <summary>Returns the explicit lines (in increasing order) that have a name, or null.</summary>
    public List<int>? GetLines(string name) => _lineNames is not null && _lineNames.TryGetValue(name, out var lines) ? lines : null;

    /// <summary>Returns whether a line has a name.</summary>
    public bool HasLineName(string name) => _lineNames is not null && _lineNames.ContainsKey(name);

    /// <summary>
    /// Creates the explicit grid of an axis of a grid container whose content box has the given size in that axis (null when it
    /// is indefinite): the automatic repetitions (https://drafts.csswg.org/css-grid-2/#auto-repeat) are repeated as many times as
    /// fit in the content box (or the maximum size, or to fill the minimum size), taking the gaps into account.
    /// </summary>
    /// <param name="grid">The grid properties of the container.</param>
    /// <param name="axis">The axis.</param>
    /// <param name="size">The definite size of the content box in the axis, or null.</param>
    /// <param name="maxSize">The definite maximum size of the content box in the axis, or null.</param>
    /// <param name="minSize">The definite minimum size of the content box in the axis, or null.</param>
    /// <param name="gap">The used gap of the axis.</param>
    /// <param name="subgridSpan">The number of tracks of a subgridded axis (its explicit grid is the tracks it spans in its parent grid), or null for a standalone axis.</param>
    /// <param name="inheritedLineNames">The names of the lines of a subgridded axis given by the parent grid (one entry per line), or null.</param>
    /// <param name="intrinsicSizes">The hypothetical sizes of the tracks with intrinsic sizing functions of a grid lanes container (https://drafts.csswg.org/css-grid-3/#repeat-intrinsic), or null.</param>
    /// <param name="repetitions">The number of repetitions of the automatic repetition, when it is imposed (the hypothetical layout of the intrinsic repetitions), or null.</param>
    public static GridExplicitAxis Create(GridLayoutStyle grid, GridAxis axis, double? size, double? maxSize, double? minSize, double gap, int? subgridSpan = null, string[]?[]? inheritedLineNames = null, GridIntrinsicRepeatSizes? intrinsicSizes = null, int? repetitions = null)
    {
        var trackList = axis == GridAxis.Columns ? grid.TemplateColumns : grid.TemplateRows;
        var autoTracks = axis == GridAxis.Columns ? grid.AutoColumns : grid.AutoRows;
        var areas = grid.TemplateAreas;
        var areaCount = subgridSpan is null ? (axis == GridAxis.Columns ? areas.ColumnCount : areas.RowCount) : 0;

        var tracks = new List<GridTrackSize>();
        var lineNames = new List<List<string>?>();
        var autoRepeatStart = 0;
        var autoRepeatCount = 0;
        var autoFit = false;

        if (subgridSpan is { } span)
        {
            // https://drafts.csswg.org/css-grid-2/#subgrid-listing: the line names of the subgrid are assigned to its lines, one per
            // line (the excess names are ignored); repeat(auto-fill, ...) repeats enough times to match the span
            if (trackList.IsSubgrid)
            {
                AddSubgridLineNames(trackList, span, lineNames);
            }

            for (var i = 0; i < span; i++)
            {
                tracks.Add(GridTrackSize.Auto);
            }
        }
        else if (!trackList.IsSubgrid)
        {
            // (subgrid on an axis that is not subgridded: "If there is no parent grid, or the grid container is otherwise forced to
            // establish an independent formatting context [...] the used value is the initial value, none": its line names are not used)
            foreach (var segment in trackList.Segments)
            {
                switch (segment.Repeat)
                {
                    case GridRepeatKind.None:
                        AddTracks(segment, 1);
                        break;

                    case GridRepeatKind.Count:
                        AddTracks(segment, Math.Min(segment.Count, MaxLine / Math.Max(1, segment.Tracks.Length)));
                        break;

                    default:
                        autoRepeatStart = tracks.Count;
                        autoFit = segment.Repeat == GridRepeatKind.AutoFit;
                        AddTracks(segment, repetitions ?? GetAutoRepeatCount(trackList, segment, size, maxSize, minSize, gap, intrinsicSizes));
                        autoRepeatCount = tracks.Count - autoRepeatStart;
                        break;
                }
            }
        }

        if (tracks.Count > MaxLine)
        {
            tracks.RemoveRange(MaxLine, tracks.Count - MaxLine);
        }

        var trackCount = subgridSpan ?? Math.Min(MaxLine, Math.Max(tracks.Count, areaCount));
        var explicitNames = new string[]?[trackCount + 1];
        var hasExplicitNames = false;
        Dictionary<string, List<int>>? names = null;
        for (var line = 0; line < lineNames.Count && line <= trackCount; line++)
        {
            if (lineNames[line] is not { Count: > 0 } lineNameList)
                continue;

            explicitNames[line] = [.. lineNameList];
            hasExplicitNames = true;
            foreach (var name in lineNameList)
            {
                AddName(ref names, name, line);
            }
        }

        // https://drafts.csswg.org/css-grid-2/#subgrid-line-name-inheritance: the subgridded lines receive the explicitly-assigned
        // line names of the parent grid (and the names of its areas, see below), in addition to their own
        if (inheritedLineNames is not null)
        {
            for (var line = 0; line < inheritedLineNames.Length && line <= trackCount; line++)
            {
                if (inheritedLineNames[line] is not { } inherited)
                    continue;

                foreach (var name in inherited)
                {
                    AddName(ref names, name, line);
                }
            }
        }

        // https://drafts.csswg.org/css-grid-2/#implicit-named-lines: the named grid areas create implicitly-assigned line names
        // (foo-start and foo-end) on the lines of their edges (also in a subgridded axis, whose explicit grid they do not extend)
        if (!areas.IsNone)
        {
            foreach (var areaName in areas.AreaNames)
            {
                if (!areas.TryGetArea(areaName, out var area))
                    continue;

                // (the lines of an area that is outside of a subgridded axis are clamped to its last line, as in the browsers)
                var (start, end) = axis == GridAxis.Columns ? (area.ColumnStart - 1, area.ColumnEnd - 1) : (area.RowStart - 1, area.RowEnd - 1);
                if (subgridSpan is not null)
                {
                    start = Math.Min(start, trackCount);
                    end = Math.Min(end, trackCount);
                }

                if (start <= trackCount)
                {
                    AddName(ref names, areaName + "-start", start);
                }

                if (end <= trackCount)
                {
                    AddName(ref names, areaName + "-end", end);
                }
            }
        }

        if (names is not null)
        {
            foreach (var list in names.Values)
            {
                list.Sort();
            }
        }

        return new GridExplicitAxis(
            [.. tracks.Count > trackCount ? tracks.GetRange(0, trackCount) : tracks],
            trackCount,
            autoTracks.Length == 0 ? [GridTrackSize.Auto] : autoTracks,
            names,
            hasExplicitNames ? explicitNames : null,
            autoRepeatStart,
            autoRepeatCount,
            autoFit);

        void AddTracks(GridTrackSegment segment, int count)
        {
            for (var repetition = 0; repetition < count; repetition++)
            {
                for (var i = 0; i < segment.Tracks.Length; i++)
                {
                    AddLineNames(tracks.Count, segment.LineNames[i]);
                    tracks.Add(segment.Tracks[i]);
                }

                // The names of the last line of a repetition merge with the names of the first line of the next one
                AddLineNames(tracks.Count, segment.LineNames[^1]);
            }
        }

        void AddLineNames(int line, string[] namesOfLine)
        {
            while (lineNames.Count <= line)
            {
                lineNames.Add(null);
            }

            if (namesOfLine.Length == 0)
                return;

            var list = lineNames[line] ??= [];
            foreach (var name in namesOfLine)
            {
                if (!list.Contains(name, StringComparer.Ordinal))
                {
                    list.Add(name);
                }
            }
        }

        void AddSubgridLineNames(GridTrackList list, int spanCount, List<List<string>?> result)
        {
            // The <line-name-list> of a subgrid: each segment is a list of line names (one per line); repeat(auto-fill, ...)
            // repeats to fill the lines that are not named by the other segments (https://drafts.csswg.org/css-grid-2/#auto-repeat)
            var fixedCount = 0;
            foreach (var segment in list.Segments)
            {
                if (segment.Repeat != GridRepeatKind.AutoFill)
                {
                    fixedCount += segment.LineNames.Length * segment.Count;
                }
            }

            var lines = spanCount + 1;
            foreach (var segment in list.Segments)
            {
                var count = segment.Repeat switch
                {
                    GridRepeatKind.AutoFill => segment.LineNames.Length == 0 ? 0 : Math.Max(0, (lines - fixedCount) / segment.LineNames.Length),
                    GridRepeatKind.Count => Math.Min(segment.Count, lines),
                    _ => 1,
                };

                for (var repetition = 0; repetition < count && result.Count < lines; repetition++)
                {
                    foreach (var namesOfLine in segment.LineNames)
                    {
                        if (result.Count >= lines)
                            break;

                        result.Add(namesOfLine.Length == 0 ? null : [.. namesOfLine]);
                    }
                }
            }
        }

        static void AddName(ref Dictionary<string, List<int>>? names, string name, int line)
        {
            names ??= new Dictionary<string, List<int>>(StringComparer.Ordinal);
            if (!names.TryGetValue(name, out var list))
            {
                names[name] = list = [];
            }

            if (!list.Contains(line))
            {
                list.Add(line);
            }
        }
    }

    /// <summary>
    /// Returns the number of repetitions of <c>auto-fill</c>/<c>auto-fit</c> (https://drafts.csswg.org/css-grid-2/#auto-repeat):
    /// "if the grid container has a definite preferred size or maximum size in the relevant axis, then the number of repetitions
    /// is the largest possible positive integer that does not cause the grid to overflow the content box of its grid container
    /// taking gap into account; if any number of repetitions would overflow, then 1 repetition. Otherwise, if the grid container
    /// has a definite minimum size in the relevant axis, the number of repetitions is the smallest possible positive integer that
    /// fulfills that minimum requirement. Otherwise, the specified track list repeats only once."
    /// </summary>
    private static int GetAutoRepeatCount(GridTrackList trackList, GridTrackSegment repeat, double? size, double? maxSize, double? minSize, double gap, GridIntrinsicRepeatSizes? intrinsicSizes)
    {
        // (a maximum size smaller than the minimum size is the minimum size: the minimum wins)
        var limit = size ?? (maxSize is { } max ? Math.Max(max, minSize ?? 0) : null);
        var isMinimum = false;
        if (limit is null)
        {
            if (minSize is null)
                return 1;

            limit = minSize;
            isMinimum = true;
        }

        var basis = limit.Value;

        // "each track is treated as its max track sizing function if that is definite or else its min track sizing function if
        // that is definite. If both are definite, floor the max track sizing function by the min track sizing function. If neither
        // are definite, the number of repetitions is one." The size is floored at 1px to avoid a division by zero
        // (the tracks with intrinsic sizing functions of a grid lanes container have a hypothetical size, see GridIntrinsicRepeatSizes)
        var fixedSum = 0d;
        var fixedCount = 0;
        foreach (var segment in trackList.Segments)
        {
            if (segment.Repeat is GridRepeatKind.AutoFill or GridRepeatKind.AutoFit)
                continue;

            for (var repetition = 0; repetition < segment.Count; repetition++)
            {
                foreach (var track in segment.Tracks)
                {
                    if ((GetFixedSize(track, basis) ?? intrinsicSizes?.GetFixedTrackSize(fixedCount)) is not { } trackSize)
                        return 1;

                    fixedSum += trackSize;
                    fixedCount++;
                }
            }
        }

        var repeatSum = 0d;
        for (var i = 0; i < repeat.Tracks.Length; i++)
        {
            if ((GetFixedSize(repeat.Tracks[i], basis) ?? intrinsicSizes?.GetRepeatTrackSize(i)) is not { } trackSize)
                return 1;

            repeatSum += Math.Max(1, trackSize);
        }

        var repeatCount = repeat.Tracks.Length;
        if (repeatCount == 0)
            return 1;

        // The size of the grid with n repetitions: fixedSum + n * repeatSum + gap * (fixedCount + n * repeatCount - 1)
        var step = repeatSum + (gap * repeatCount);
        var free = basis - fixedSum - (gap * (fixedCount - 1));
        int repetitions;
        if (isMinimum)
        {
            repetitions = (int)Math.Max(1, Math.Ceiling((free / step) - 1e-9));
        }
        else
        {
            repetitions = (int)Math.Max(1, Math.Floor((free / step) + 1e-9));
        }

        return Math.Min(repetitions, Math.Max(1, MaxLine / repeatCount));

        static double? GetFixedSize(GridTrackSize track, double percentageBasis)
        {
            double? max = track.Max.Kind == GridBreadthKind.LengthPercentage && !track.IsFitContent ? track.Max.Length.Resolve(percentageBasis) : null;
            double? min = track.Min.Kind == GridBreadthKind.LengthPercentage ? track.Min.Length.Resolve(percentageBasis) : null;
            return (max, min) switch
            {
                ({ } a, { } b) => Math.Max(a, b),
                ({ } a, null) => a,
                (null, { } b) => b,
                _ => null,
            };
        }
    }
}

/// <summary>
/// The hypothetical sizes of the tracks of a grid lanes container whose sizing functions are not definite, which resolve the number
/// of repetitions of <c>repeat(auto-fill | auto-fit)</c> (https://drafts.csswg.org/css-grid-3/#repeat-intrinsic): the tracks of the
/// track list outside of the automatic repetition (in order, the repetitions of <c>repeat(&lt;integer&gt;)</c> expanded), and the
/// tracks of the automatic repetition ("given by the largest track corresponding to that entry (by index) in the listing").
/// </summary>
internal sealed class GridIntrinsicRepeatSizes(double[] fixedTracks, double[] repeatTracks)
{
    public double? GetFixedTrackSize(int index) => index < fixedTracks.Length ? fixedTracks[index] : null;

    public double? GetRepeatTrackSize(int index) => index < repeatTracks.Length ? repeatTracks[index] : null;
}
