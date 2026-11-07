using System.Linq.Expressions;
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
}
