using FluentAssertions;
using Meziantou.Framework.IO;
using Xunit;

namespace Meziantou.Framework.Tests.IO;

public class TeeTextWriterTests
{
    [Fact]
    public void WriteTest01()
    {
        using var sw1 = new StringWriter();
        using var sw2 = new StringWriter();
        using var tee = new TeeTextWriter(sw1, sw2);
        tee.Write("abc");
        tee.Flush();

        sw1.ToString().Should().Be("abc");
        sw2.ToString().Should().Be("abc");
    }
}
