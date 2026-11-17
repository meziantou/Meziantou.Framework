using System.Linq.Expressions;
using Meziantou.AspNetCore.Components.Tests.TestComponents;
using Meziantou.AspNetCore.Testing;
using Meziantou.AspNetCore.Testing.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Meziantou.AspNetCore.Components.Tests;

public sealed class DataGridTests
{
    private static RenderFragment SingleColumn(string title) => builder =>
    {
        builder.OpenComponent<DataGridColumn<int>>(0);
        builder.AddComponentParameter(1, nameof(DataGridColumn<int>.Title), title);
        builder.AddComponentParameter(2, nameof(DataGridColumn<int>.Expression), (Expression<Func<int, object>>)(value => value));
        builder.CloseComponent();
    };

    [Fact]
    public async Task ColumnsAreRenderedDuringStaticServerSideRendering()
    {
        // Static server-side rendering never calls OnAfterRender, so the grid must not depend on it to
        // discover the columns declared in its ChildContent
        var html = await BlazorTestRenderer.RenderAsync<DataGrid<int>>(
            (nameof(DataGrid<int>.Items), new[] { 1, 2, 3 }),
            (nameof(DataGrid<int>.ChildContent), SingleColumn("Value")));

        Assert.Contains("<th>Value</th>", html);
        Assert.Contains("<td>1</td>", html);
        Assert.Contains("<td>2</td>", html);
        Assert.Contains("<td>3</td>", html);
    }

    [Fact]
    public async Task DuplicateItemsDoNotProduceDuplicateKeys()
    {
        // Sibling keys must be unique. Keying on the item (or its hash code) makes equal rows collide,
        // which the renderer rejects on the next diff.
        var html = await BlazorTestRenderer.RenderAsync<Rerenderer>(
            (nameof(Rerenderer.First), new[] { 1, 2, 3 }),
            (nameof(Rerenderer.Second), new[] { 1, 2, 2 }));

        Assert.Contains("<td>1</td>", html);
        Assert.Equal(2, html.Split("<td>2</td>", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public async Task RowKeyIsUsedWhenProvided()
    {
        var html = await BlazorTestRenderer.RenderAsync<Rerenderer>(
            (nameof(Rerenderer.First), new[] { 1, 2, 3 }),
            (nameof(Rerenderer.Second), new[] { 3, 2, 1 }),
            (nameof(Rerenderer.RowKey), (Func<int, object>)(item => item)));

        Assert.Contains("<td>3</td>", html);
        Assert.Contains("<td>1</td>", html);
    }

    [Fact]
    public async Task RowClassReceivesTheRowIndex()
    {
        var html = await BlazorTestRenderer.RenderAsync<DataGrid<int>>(
            (nameof(DataGrid<int>.Items), new[] { 10, 20, 30 }),
            (nameof(DataGrid<int>.ChildContent), SingleColumn("Value")),
            (nameof(DataGrid<int>.RowClass), (Func<int, int, string>)((item, index) => "row-" + index.ToString(CultureInfo.InvariantCulture))));

        Assert.Contains("class=\"row-0\"", html);
        Assert.Contains("class=\"row-1\"", html);
        Assert.Contains("class=\"row-2\"", html);
    }

    [Fact]
    public async Task ColumnRemovedFromChildContentIsUnregistered()
    {
        var html = await BlazorTestRenderer.RenderAsync<ToggleColumnGrid>();

        Assert.Contains("<th>Kept</th>", html);
        Assert.DoesNotContain("Removed", html);
    }

    [Fact]
    public async Task InferringATitleFromAnUnsupportedExpressionReportsTheExpressionBody()
    {
        var exception = await Assert.ThrowsAsync<NotSupportedException>(() => BlazorTestRenderer.RenderAsync<DataGrid<int>>(
            (nameof(DataGrid<int>.Items), new[] { 1 }),
            (nameof(DataGrid<int>.ChildContent), (RenderFragment)(builder =>
            {
                builder.OpenComponent<DataGridColumn<int>>(0);
                builder.AddComponentParameter(1, nameof(DataGridColumn<int>.Expression), (Expression<Func<int, object>>)(value => value));
                builder.CloseComponent();
            }))));

        // The message must name the expression body, not the Expression<Func<...>> wrapper around it
        Assert.Contains("UnaryExpression", exception.Message);
        Assert.DoesNotContain("Func`2", exception.Message);
    }

    /// <summary>Renders the grid once, then swaps the items and renders again, so the keyed diff actually runs.</summary>
    internal sealed class Rerenderer : ComponentBase
    {
        private int[] _current = [];

        [Parameter] public int[] First { get; set; } = [];

        [Parameter] public int[] Second { get; set; } = [];

        [Parameter] public Func<int, object>? RowKey { get; set; }

        protected override async Task OnInitializedAsync()
        {
            _current = First;
            await Task.Yield();
            _current = Second;
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<DataGrid<int>>(0);
            builder.AddComponentParameter(1, nameof(DataGrid<int>.Items), _current);
            builder.AddComponentParameter(2, nameof(DataGrid<int>.RowKey), RowKey);
            builder.AddComponentParameter(3, nameof(DataGrid<int>.ChildContent), SingleColumn("Value"));
            builder.CloseComponent();
        }
    }

    /// <summary>Renders two columns, then drops one of them, so the column has to unregister itself.</summary>
    internal sealed class ToggleColumnGrid : ComponentBase
    {
        private bool _showRemoved = true;

        protected override async Task OnInitializedAsync()
        {
            await Task.Yield();
            _showRemoved = false;
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<DataGrid<int>>(0);
            builder.AddComponentParameter(1, nameof(DataGrid<int>.Items), new[] { 1 });
            builder.AddComponentParameter(2, nameof(DataGrid<int>.ChildContent), (RenderFragment)(inner =>
            {
                inner.OpenComponent<DataGridColumn<int>>(0);
                inner.AddComponentParameter(1, nameof(DataGridColumn<int>.Title), "Kept");
                inner.AddComponentParameter(2, nameof(DataGridColumn<int>.Expression), (Expression<Func<int, object>>)(value => value));
                inner.CloseComponent();

                if (_showRemoved)
                {
                    inner.OpenComponent<DataGridColumn<int>>(3);
                    inner.AddComponentParameter(4, nameof(DataGridColumn<int>.Title), "Removed");
                    inner.AddComponentParameter(5, nameof(DataGridColumn<int>.Expression), (Expression<Func<int, object>>)(value => value));
                    inner.CloseComponent();
                }
            }));
            builder.CloseComponent();
        }
    }

    [Fact]
    public async Task Interactive_RendersColumnsRowsAndCellTemplates()
    {
        await using var host = new ComponentTestHost();
        var deleted = new List<Person>();
        var grid = await host.RenderAsync<GridPage>(parameters => parameters
            .Add(c => c.Items, [new Person(1, "Ada", 36), new Person(2, "Alan", 41)])
            .Add(c => c.OnDelete, (Person person) => deleted.Add(person)));

        Assert.Equal(["Name", "Age", "Actions"], grid.FindAll("th").Select(th => th.TextContent));
        Assert.Equal(["even", "odd"], grid.FindAll("tbody tr").Select(row => row.GetAttribute("class")));
        Assert.Equal(["Ada", "036", "Delete Ada"], grid.FindAll("tbody tr:first-child td").Select(cell => cell.TextContent));
        Assert.Equal("grid", grid.Find("table").GetAttribute("class"));

        await grid.GetByRole("button", "Delete Alan").ClickAsync();
        Assert.Equal([new Person(2, "Alan", 41)], deleted);
    }

    [Fact]
    public async Task Interactive_RowKeyMovesTheExistingRows()
    {
        await using var host = new ComponentTestHost();
        var ada = new Person(1, "Ada", 36);
        var alan = new Person(2, "Alan", 41);
        var grid = await host.RenderAsync<GridPage>(parameters => parameters.Add(c => c.Items, [ada, alan]));
        var adaRow = grid.FindAll("tbody tr")[0];

        await grid.SetParametersAsync(parameters => parameters.Add(c => c.Items, [alan, ada]));

        Assert.Equal(["Alan", "Ada"], grid.FindAll("tbody tr td:first-child").Select(cell => cell.TextContent));
        Assert.Same(adaRow, grid.FindAll("tbody tr")[1]);
        Assert.Equal(["even", "odd"], grid.FindAll("tbody tr").Select(row => row.GetAttribute("class")));
    }

    [Fact]
    public async Task Interactive_ColumnsCanBeRemovedAndAddedAgain()
    {
        await using var host = new ComponentTestHost();
        var grid = await host.RenderAsync<GridPage>(parameters => parameters.Add(c => c.Items, [new Person(1, "Ada", 36)]));

        await grid.SetParametersAsync(parameters => parameters.Add(c => c.ShowAge, false));
        Assert.Equal(["Name", "Actions"], grid.FindAll("th").Select(th => th.TextContent));
        Assert.Equal(["Ada", "Delete Ada"], grid.FindAll("td").Select(cell => cell.TextContent));

        // The column is back at its position in ChildContent, once
        await grid.SetParametersAsync(parameters => parameters.Add(c => c.ShowAge, true));
        Assert.Equal(["Name", "Age", "Actions"], grid.FindAll("th").Select(th => th.TextContent));
        Assert.Equal(["Ada", "036", "Delete Ada"], grid.FindAll("td").Select(cell => cell.TextContent));
    }

    [Fact]
    public async Task Interactive_ColumnsWithUnchangedParametersKeepTheirPosition()
    {
        // A column with only a title does not render again when the grid renders again: it keeps its position
        await using var host = new ComponentTestHost();
        var grid = await host.RenderAsync<GridTitleOnlyColumnPage>(parameters => parameters.Add(c => c.Items, [new Person(1, "Ada", 36)]));
        Assert.Equal(["Notes", "Name"], grid.FindAll("th").Select(th => th.TextContent));

        await grid.SetParametersAsync(parameters => parameters.Add(c => c.Items, [new Person(2, "Alan", 41)]));

        Assert.Equal(["Notes", "Name"], grid.FindAll("th").Select(th => th.TextContent));
        Assert.Equal(["", "Alan"], grid.FindAll("td").Select(cell => cell.TextContent));
    }

    [Fact]
    public async Task Interactive_ColumnsAddedBeforeColumnsWithUnchangedParametersAreAtTheirPosition()
    {
        // The columns with only a title do not render again: the grid cannot place a new column among them from the
        // columns that render, so it renders the columns again
        await using var host = new ComponentTestHost();
        var grid = await host.RenderAsync<GridConditionalTitleColumnsPage>(parameters => parameters.Add(c => c.Items, [new Person(1, "Ada", 36)]));
        Assert.Equal(["A", "B"], grid.FindAll("th").Select(th => th.TextContent));

        // Only the columns are recreated: the table and its rows keep their elements (and the state of their components)
        var table = grid.Find("table");
        var row = grid.Find("tbody tr");
        await grid.SetParametersAsync(parameters => parameters.Add(c => c.ShowFirst, true));
        Assert.Equal(["First", "A", "B"], grid.FindAll("th").Select(th => th.TextContent));
        Assert.Same(table, grid.Find("table"));
        Assert.Same(row, grid.Find("tbody tr"));

        await grid.SetParametersAsync(parameters => parameters.Add(c => c.ShowMiddle, true));
        Assert.Equal(["First", "A", "Middle", "B"], grid.FindAll("th").Select(th => th.TextContent));

        await grid.SetParametersAsync(parameters => parameters.Add(c => c.ShowFirst, false));
        Assert.Equal(["A", "Middle", "B"], grid.FindAll("th").Select(th => th.TextContent));

        await grid.SetParametersAsync(parameters => parameters.Add(c => c.ShowFirst, true).Add(c => c.ShowMiddle, false));
        Assert.Equal(["First", "A", "B"], grid.FindAll("th").Select(th => th.TextContent));
        Assert.Equal(["", "", ""], grid.FindAll("td").Select(cell => cell.TextContent));
    }

    [Fact]
    public async Task Interactive_ExposesATableToAssistiveTechnologies()
    {
        await using var host = new ComponentTestHost();
        var grid = await host.RenderAsync<GridPage>(parameters => parameters.Add(c => c.Items, [new Person(1, "Ada", 36)]));

        Assert.Equal("table", grid.Find("table").ComputedRole);
        Assert.Equal("columnheader", grid.Find("th").ComputedRole);
        Assert.Equal("cell", grid.Find("td").ComputedRole);
        Assert.Equal("Delete Ada", grid.GetByRole("button").GetElement().ComputedName);
    }
}
