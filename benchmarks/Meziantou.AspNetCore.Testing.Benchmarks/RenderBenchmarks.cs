using System.Globalization;
using BenchmarkDotNet.Attributes;
using Meziantou.AspNetCore.Testing.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Meziantou.AspNetCore.Testing.Benchmarks;

/// <summary>Renders a list of child components, as a page of an application does, then renders it again with new values.</summary>
[MemoryDiagnoser]
public class RenderBenchmarks : IDisposable
{
    private ComponentTestHost _host = null!;
    private RenderedComponent<UserList> _list = null!;
    private int _generation;

    [Params(100, 1000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _host = new ComponentTestHost();
        _list = _host.RenderAsync<UserList>(parameters => parameters.Add(list => list.Count, Rows)).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Dispose() => _host.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [Benchmark]
    public async Task<int> Render()
    {
        await using var host = new ComponentTestHost();
        var list = await host.RenderAsync<UserList>(parameters => parameters.Add(list => list.Count, Rows));
        return list.Instance.Count;
    }

    [Benchmark]
    public async Task Rerender()
    {
        _generation++;
        await _list.SetParametersAsync(parameters => parameters.Add(list => list.Generation, _generation));
    }

    public sealed class UserList : ComponentBase
    {
        [Parameter]
        public int Count { get; set; }

        [Parameter]
        public int Generation { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "ul");
            for (var i = 0; i < Count; i++)
            {
                builder.OpenComponent<UserRow>(1);
                builder.SetKey(i);
                builder.AddComponentParameter(2, nameof(UserRow.Index), i);
                builder.AddComponentParameter(3, nameof(UserRow.Generation), Generation);
                builder.CloseComponent();
            }

            builder.CloseElement();
        }
    }

    public sealed class UserRow : ComponentBase
    {
        [Parameter]
        public int Index { get; set; }

        [Parameter]
        public int Generation { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "li");
            builder.AddAttribute(1, "class", "user");
            builder.OpenElement(2, "span");
            builder.AddContent(3, "User ");
            builder.AddContent(4, Index.ToString(CultureInfo.InvariantCulture));
            builder.CloseElement();
            builder.AddContent(5, " ");
            builder.OpenElement(6, "button");
            builder.AddAttribute(7, "onclick", EventCallback.Factory.Create(this, () => Generation++));
            builder.AddContent(8, "Generation ");
            builder.AddContent(9, Generation.ToString(CultureInfo.InvariantCulture));
            builder.CloseElement();
            builder.CloseElement();
        }
    }
}
