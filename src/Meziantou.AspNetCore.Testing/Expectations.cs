namespace Meziantou.AspNetCore.Testing;

/// <summary>
/// The entry point of the web-first assertions, as <c>Assertions.Expect</c> in Playwright. Import it with
/// <c>using static Meziantou.AspNetCore.Testing.Expectations;</c>, then write
/// <c>await Expect(page.GetByRole("status")).ToHaveTextAsync("Saved");</c>.
/// </summary>
public static class Expectations
{
    /// <summary>Gets the assertions of a locator.</summary>
    /// <param name="locator">The locator.</param>
    /// <returns>The assertions.</returns>
    public static LocatorAssertions Expect(Locator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        return new LocatorAssertions(locator, isNot: false);
    }

    /// <summary>Gets the assertions of a page.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The assertions.</returns>
    public static PageAssertions Expect(TestPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new PageAssertions(page, isNot: false);
    }
}
