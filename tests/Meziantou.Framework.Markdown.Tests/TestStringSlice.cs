// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;

namespace Meziantou.Framework.Markdown.Tests;

public class TestStringSlice
{
    [Fact]
    public void TestRuneBmp()
    {
        var slice = new StringSlice("01234");

        Assert.Equal('0', slice.CurrentRune.Value);
        Assert.Equal(0, slice.Start);
        Assert.Equal('1', slice.NextRune().Value);
        Assert.Equal(1, slice.Start);
        Assert.Equal('2', slice.NextRune().Value);
        Assert.Equal(2, slice.Start);
        Assert.Equal('2', slice.CurrentRune.Value);
        Assert.Equal("234", slice.ToString());
        Assert.Equal('3', slice.PeekRuneExtra(1).Value);
        Assert.Equal('4', slice.PeekRuneExtra(2).Value);
        Assert.Equal(0, slice.PeekRuneExtra(3).Value);
        Assert.Equal('1', slice.PeekRuneExtra(-1).Value);
        Assert.Equal('0', slice.PeekRuneExtra(-2).Value);
        Assert.Equal(0, slice.PeekRuneExtra(-3).Value);
        Assert.Equal('0', slice.RuneAt(0).Value);
        Assert.Equal('1', slice.RuneAt(1).Value);
        Assert.Equal('2', slice.RuneAt(2).Value);
        Assert.Equal('3', slice.RuneAt(3).Value);
        Assert.Equal('4', slice.RuneAt(4).Value);
        Assert.Equal(2, slice.Start);
    }

    [Fact]
    public void TestRuneSupplementaryOnly()
    {
        var slice = new StringSlice("𝟎𝟏𝟐𝟑𝟒");
        Assert.Equal(10, slice.Length);

        // 𝟎 = U+1D7CE, 𝟐 = U+1D7D0
        Assert.Equal(0x1D7CE, slice.CurrentRune.Value); // 𝟎
        Assert.Equal(0, slice.Start);
        Assert.Equal(0x1D7CF, slice.NextRune().Value); // 𝟏
        Assert.Equal(2, slice.Start);
        Assert.Equal(0x1D7D0, slice.NextRune().Value); // 𝟐
        Assert.Equal(4, slice.Start);
        Assert.Equal(0x1D7D0, slice.CurrentRune.Value); // 𝟐
        Assert.Equal("𝟐𝟑𝟒", slice.ToString());
        // CurrentRune occupies 2 `char`s, so next Rune starts at index 2
        Assert.Equal(0x1D7D1, slice.PeekRuneExtra(2).Value); // 𝟑
        Assert.Equal(0x1D7D2, slice.PeekRuneExtra(4).Value); // 𝟒
        Assert.Equal(0, slice.PeekRuneExtra(6).Value);
        Assert.Equal(0x1D7CF, slice.PeekRuneExtra(-1).Value); // 𝟏
        Assert.Equal(0x1D7CE, slice.PeekRuneExtra(-3).Value); // 𝟎
        Assert.Equal(0, slice.PeekRuneExtra(-5).Value);
        Assert.Equal(0x1D7CE, slice.RuneAt(0).Value); // 𝟎
        Assert.Equal(0x1D7CF, slice.RuneAt(2).Value); // 𝟏
        Assert.Equal(0x1D7D0, slice.RuneAt(4).Value); // 𝟐
        Assert.Equal(0x1D7D1, slice.RuneAt(6).Value); // 𝟑
        Assert.Equal(0x1D7D2, slice.RuneAt(8).Value); // 𝟒
        // The following usages are not expected. You should take into consideration the `char`s that the Rune you just acquired occupies.
        Assert.Equal(0, slice.PeekRuneExtra(-4).Value);
        Assert.Equal(0, slice.PeekRuneExtra(-2).Value);
        Assert.Equal(0, slice.PeekRuneExtra(1).Value);
        Assert.Equal(0, slice.PeekRuneExtra(3).Value);
        Assert.Equal(0, slice.PeekRuneExtra(5).Value);
        Assert.Equal(0, slice.RuneAt(1).Value);
        Assert.Equal(0, slice.RuneAt(3).Value);
        Assert.Equal(0, slice.RuneAt(5).Value);
        Assert.Equal(0, slice.RuneAt(7).Value);
        Assert.Equal(0, slice.RuneAt(9).Value);
        Assert.Equal(4, slice.Start);
    }

    [Fact]
    public void TestRuneIsolatedHighSurrogate()
    {
        var slice = new StringSlice("\ud800\ud801\ud802\ud803\ud804");
        Assert.Equal(0, slice.CurrentRune.Value);
        Assert.Equal(0, slice.Start);
        Assert.Equal(0, slice.NextRune().Value);
        Assert.Equal(0, slice.CurrentRune.Value);
        Assert.Equal('\ud801', slice.CurrentChar);
        Assert.Equal(1, slice.Start);
        Assert.Equal(0, slice.NextRune().Value);
        Assert.Equal(2, slice.Start);
        Assert.Equal('\ud802', slice.CurrentChar);
        Assert.Equal(0, slice.CurrentRune.Value);
        Assert.Equal(0, slice.PeekRuneExtra(-3).Value);
        Assert.Equal(0, slice.PeekRuneExtra(-2).Value);
        Assert.Equal(0, slice.PeekRuneExtra(-1).Value);
        Assert.Equal(0, slice.PeekRuneExtra(1).Value);
        Assert.Equal(0, slice.PeekRuneExtra(2).Value);
        Assert.Equal(0, slice.PeekRuneExtra(3).Value);
        Assert.Equal(0, slice.RuneAt(0).Value);
        Assert.Equal(0, slice.RuneAt(1).Value);
        Assert.Equal(0, slice.RuneAt(2).Value);
        Assert.Equal(0, slice.RuneAt(3).Value);
        Assert.Equal(0, slice.RuneAt(4).Value);
        Assert.Equal(2, slice.Start);
    }

    [Fact]
    public void TestRuneIsolatedLowSurrogate()
    {
        var slice = new StringSlice("\udc00\udc01\udc02\udc03\udc04");
        Assert.Equal(0, slice.CurrentRune.Value);
        Assert.Equal(0, slice.NextRune().Value);
        Assert.Equal('\udc01', slice.CurrentChar);
        Assert.Equal(0, slice.NextRune().Value);
        Assert.Equal('\udc02', slice.CurrentChar);
        Assert.Equal(0, slice.CurrentRune.Value);
        Assert.Equal(0, slice.PeekRuneExtra(-3).Value);
        Assert.Equal(0, slice.PeekRuneExtra(-2).Value);
        Assert.Equal(0, slice.PeekRuneExtra(-1).Value);
        Assert.Equal(0, slice.PeekRuneExtra(1).Value);
        Assert.Equal(0, slice.PeekRuneExtra(2).Value);
        Assert.Equal(0, slice.PeekRuneExtra(3).Value);
        Assert.Equal(0, slice.RuneAt(0).Value);
        Assert.Equal(0, slice.RuneAt(1).Value);
        Assert.Equal(0, slice.RuneAt(2).Value);
        Assert.Equal(0, slice.RuneAt(3).Value);
        Assert.Equal(0, slice.RuneAt(4).Value);
    }

    [Fact]
    public void TestMixedInput()
    {
        var slice = new StringSlice("a\udc00bc𝟑d𝟒\udc00");
        Assert.Equal(10, slice.Length);
        Assert.Equal('a', slice.CurrentRune.Value);
        Assert.Equal(0, slice.Start);
        Assert.Equal(0, slice.NextRune().Value);
        Assert.Equal(1, slice.Start);
        Assert.Equal('b', slice.NextRune().Value);
        Assert.Equal(2, slice.Start);
        Assert.Equal('c', slice.NextRune().Value);
        Assert.Equal(3, slice.Start);
        Assert.Equal(0x1D7D1, slice.NextRune().Value);
        Assert.Equal(4, slice.Start);
        Assert.Equal('d', slice.NextRune().Value);
        Assert.Equal(6, slice.Start);
        Assert.Equal(0x1D7D2, slice.NextRune().Value);
        Assert.Equal(7, slice.Start);
        Assert.Equal(0, slice.NextRune().Value);
        Assert.Equal(9, slice.Start);
        Assert.False(slice.IsEmpty);
        Assert.Equal(0, slice.NextRune().Value);
        Assert.Equal(10, slice.Start);
        Assert.True(slice.IsEmpty);

        slice = new StringSlice(slice.Text + 'a', 7, 10);
        Assert.Equal(0x1D7D2, slice.CurrentRune.Value);
        Assert.Equal(0, slice.NextRune().Value);
        Assert.Equal(9, slice.Start);
        Assert.Equal('a', slice.NextRune().Value);
    }
}
