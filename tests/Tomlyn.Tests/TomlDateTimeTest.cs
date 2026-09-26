// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Globalization;
using Meziantou.Xunit;

namespace Tomlyn.Tests;

public class TomlDateTimeTest
{
    [Fact]
    public void TestIConvertibleToString_NoCulture()
    {
        AssertConvertsToIsoDate(cultureInfo: null);
    }

    [Theory]
    [RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    [InlineData("IV")]
    [InlineData("fr-FR")]
    [InlineData("es-ES")]
    public void TestIConvertibleToString(string cultureName)
    {
        AssertConvertsToIsoDate(CultureInfo.GetCultureInfo(cultureName));
    }

    private static void AssertConvertsToIsoDate(CultureInfo? cultureInfo)
    {
        var dateTime = new TomlDateTime(new DateTimeOffset(new DateTime(2022, 1, 27)), 0, TomlDateTimeKind.LocalDate);

        var converted = Convert.ToString(dateTime, cultureInfo);

        Assert.Equal("2022-01-27", converted);
    }
}