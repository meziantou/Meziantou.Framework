using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using Meziantou.AspNetCore.Testing.Components;

namespace Meziantou.AspNetCore.Testing.Benchmarks;

/// <summary>The locators evaluated on a page of a typical application: a table of users, with a button and a field per row.</summary>
[MemoryDiagnoser]
public class LocatorBenchmarks : IDisposable
{
    private ComponentTestHost _host = null!;
    private RenderedComponent<FragmentContainer> _page = null!;

    [Params(200)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var markup = CreateMarkup(Rows);
        _host = new ComponentTestHost();
        _page = _host.RenderAsync(builder => builder.AddMarkupContent(0, markup)).GetAwaiter().GetResult();
    }

    [GlobalCleanup]
    public void Dispose() => _host.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [Benchmark]
    public int GetByText() => _page.GetByText("user150@example.com").Count;

    [Benchmark]
    public int GetByTextExact() => _page.GetByText("User 150", exact: true).Count;

    [Benchmark]
    public int GetByRole() => _page.GetByRole("button", "Delete User 150").Count;

    [Benchmark]
    public int GetByLabel() => _page.GetByLabel("Notes of User 150").Count;

    [Benchmark]
    public int GetByTestId() => _page.GetByTestId("user-150").Count;

    [Benchmark]
    public int CssLocator() => _page.Locator("tr.active td").Count;

    [Benchmark]
    public int FilterHasText() => _page.Locator("tr").Filter(new() { HasText = "user150@" }).Count;

    [Benchmark]
    public string Markup() => _page.Markup;

    private static string CreateMarkup(int rows)
    {
        var builder = new StringBuilder();
        builder.Append("""
            <header>
              <nav>
                <a href="/">Home</a>
                <a href="/users">Users</a>
                <a href="/settings">Settings</a>
              </nav>
            </header>
            <main>
              <h1>Users</h1>
              <table>
                <thead>
                  <tr><th>Name</th><th>Email</th><th>Notes</th><th>Actions</th></tr>
                </thead>
                <tbody>

            """);
        for (var i = 0; i < rows; i++)
        {
            var id = i.ToString(CultureInfo.InvariantCulture);
            builder.Append(CultureInfo.InvariantCulture, $"""
                      <tr data-testid="user-{id}" class="{(i % 3 == 0 ? "active" : "inactive")}">
                        <td>
                          <span class="name">User {id}</span>
                        </td>
                        <td><a href="mailto:user{id}@example.com">user{id}@example.com</a></td>
                        <td><label for="notes-{id}">Notes of User {id}</label> <input id="notes-{id}" placeholder="Notes"></td>
                        <td>
                          <button type="button">Edit</button>
                          <button type="button" aria-label="Delete User {id}">x</button>
                        </td>
                      </tr>

                """);
        }

        builder.Append("""
                </tbody>
              </table>
            </main>
            <footer><p>Copyright</p></footer>
            """);
        return builder.ToString();
    }
}
