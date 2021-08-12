using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public sealed class RegexExtensionsTests
{
    [Fact]
    public async Task ReplaceAsync()
    {
        var regex = new Regex("[0-9]", RegexOptions.None, TimeSpan.FromSeconds(1));
        var actual = await regex.ReplaceAsync("a123b", async match =>
        {
            await Task.Yield();
            return "_";
        });

        actual.Should().Be("a___b");
    }
}
