
// --------------------------------
//              Sample
// --------------------------------

namespace Meziantou.Framework.Markdown.Tests.Specs.PlainText.Sample;

public class TestSamplePlainTextSpec
{
    // # Sample plain text spec
    //
    // Emphasis and anchors are stripped. A newline is ensured.
    [Fact]
    public void SamplePlainTextSpec_Example001()
    {
        // Example 1
        // Section: Sample plain text spec
        //
        // The following Markdown:
        //     *Hello*, [world](http://example.com)!
        //
        // Should be rendered as:
        //     Hello, world!
        //

        TestPlainText.TestSpec("*Hello*, [world](http://example.com)!", "Hello, world!\n", "", context: "Example 1\nSection Sample plain text spec\n");
    }
}
