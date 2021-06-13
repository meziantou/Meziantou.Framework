using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using FluentAssertions;

namespace Meziantou.Framework.Text.Tests
{
    public sealed class Utf8ExtensionsTests
    {
        [Fact]
        public void EnumerateRunesFromUtf8Test()
        {
            // 😊=√
            ReadOnlySpan<byte> bytes = new byte[] { 240, 159, 152, 138, 0x3C, 0xE2, 0x88, 0x9A };
            var runes = new List<Rune>();
            foreach (var rune in bytes.EnumerateRunesFromUtf8())
            {
                runes.Add(rune);
            }

            var expected = new[]
            {
                new Rune('\uD83D', '\uDE0A'),
                new Rune('\u003C'),
                new Rune('\u221A'),
            };
            runes.Should().Equal(expected);
        }
    }
}
