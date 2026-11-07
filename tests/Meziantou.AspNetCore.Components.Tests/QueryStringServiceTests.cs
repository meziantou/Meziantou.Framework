using Microsoft.AspNetCore.Components;

namespace Meziantou.AspNetCore.Components.Tests;

public sealed class QueryStringServiceTests
{
    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string uri) => Initialize("https://example.com/", uri);

        public string? NavigatedTo { get; private set; }

        protected override void NavigateToCore(string uri, NavigationOptions options) => NavigatedTo = uri;
    }

    private sealed class TestComponent : ComponentBase
    {
        [Parameter, SupplyParameterFromQuery] public string? Search { get; set; }

        [Parameter, SupplyParameterFromQuery] public int Page { get; set; }

        [Parameter, SupplyParameterFromQuery] public DayOfWeek Day { get; set; }

        [Parameter, SupplyParameterFromQuery] public DateTime? When { get; set; }

        [Parameter, SupplyParameterFromQuery] public bool Enabled { get; set; }

        // Assigning parameters from inside the component keeps BL0005 happy. Populating them from the outside is
        // exactly what QueryStringService does, which is what these tests exercise.
        public static TestComponent Create(string? search = null, int page = 0, DayOfWeek day = default, DateTime? when = null, bool enabled = false)
            => new() { Search = search, Page = page, Day = day, When = when, Enabled = enabled };
    }

    private static (QueryStringService Service, TestNavigationManager Navigation) Create(string uri)
    {
        var navigation = new TestNavigationManager(uri);
        return (new QueryStringService(navigation, jsRuntime: null!), navigation);
    }

    [Fact]
    public void ReadsPlainValuesWithoutJsonQuoting()
    {
        var (service, _) = Create("https://example.com/?Search=hello&Page=42&Day=Monday&When=2024-01-02&Enabled=true");
        var component = new TestComponent();

        service.SetParametersFromQueryString(component);

        Assert.Equal("hello", component.Search);
        Assert.Equal(42, component.Page);
        Assert.Equal(DayOfWeek.Monday, component.Day);
        Assert.Equal(new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Unspecified), component.When);
        Assert.True(component.Enabled);
    }

    [Theory]
    [InlineData("https://example.com/?Page=abc")]
    [InlineData("https://example.com/?Day=NotADay")]
    [InlineData("https://example.com/?When=not-a-date")]
    public void UnparsableValuesLeaveThePropertyUntouchedInsteadOfThrowing(string uri)
    {
        // The query string is user-controlled and this runs from OnInitialized, so throwing here tears down the circuit
        var (service, _) = Create(uri);
        var component = TestComponent.Create(page: 7, day: DayOfWeek.Friday);

        service.SetParametersFromQueryString(component);

        Assert.Equal(7, component.Page);
        Assert.Equal(DayOfWeek.Friday, component.Day);
        Assert.Null(component.When);
    }

    [Fact]
    public void MissingParametersLeaveThePropertiesUntouched()
    {
        var (service, _) = Create("https://example.com/");
        var component = TestComponent.Create(page: 7);

        service.SetParametersFromQueryString(component);

        Assert.Equal(7, component.Page);
    }

    [Fact]
    public void EmptyValueResetsToTheDefault()
    {
        var (service, _) = Create("https://example.com/?Page=&When=");
        var component = TestComponent.Create(page: 7, when: DateTime.UtcNow);

        service.SetParametersFromQueryString(component);

        Assert.Equal(0, component.Page);
        Assert.Null(component.When);
    }

    [Fact]
    public async Task WritesPlainValuesAndRoundTrips()
    {
        var (service, navigation) = Create("https://example.com/page");
        var component = TestComponent.Create(search: "hello", page: 42, day: DayOfWeek.Monday, enabled: true);

        await service.UpdateQueryString(component);

        Assert.NotNull(navigation.NavigatedTo);
        Assert.Contains("Day=Monday", navigation.NavigatedTo);
        Assert.Contains("Page=42", navigation.NavigatedTo);
        Assert.DoesNotContain("%22", navigation.NavigatedTo);

        var (readBack, _) = Create(navigation.NavigatedTo);
        var target = new TestComponent();
        readBack.SetParametersFromQueryString(target);

        Assert.Equal("hello", target.Search);
        Assert.Equal(42, target.Page);
        Assert.Equal(DayOfWeek.Monday, target.Day);
        Assert.True(target.Enabled);
    }

    [Fact]
    public async Task TheFragmentIsPreserved()
    {
        var (service, navigation) = Create("https://example.com/page#section");
        var component = TestComponent.Create(page: 3);

        await service.UpdateQueryString(component);

        Assert.Contains("#section", navigation.NavigatedTo);
        Assert.Contains("Page=3", navigation.NavigatedTo);
    }

    [Fact]
    public void UpdateUrlUsingParametersSupportsTypesTheFrameworkRejects()
    {
        // GetUriWithQueryParameters throws for values it does not know, such as enums
        var navigation = new TestNavigationManager("https://example.com/page");
        var component = TestComponent.Create(day: DayOfWeek.Monday, page: 5);

        navigation.UpdateUrlUsingParameters(component);

        Assert.Contains("Day=Monday", navigation.NavigatedTo);
        Assert.Contains("Page=5", navigation.NavigatedTo);
    }
}
