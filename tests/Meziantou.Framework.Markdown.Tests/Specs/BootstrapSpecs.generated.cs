
// --------------------------------
//             Bootstrap
// --------------------------------

namespace Meziantou.Framework.Markdown.Tests.Specs.Bootstrap;

public class TestExtensionsBootstrap
{
    // # Extensions
    //
    // Adds support for outputting bootstrap ready tags:
    //
    // ## Bootstrap
    //
    // Adds bootstrap `.table` class to `<table>`:
    [Fact]
    public void ExtensionsBootstrap_Example001()
    {
        // Example 1
        // Section: Extensions / Bootstrap
        //
        // The following Markdown:
        //     Name | Value
        //     -----| -----
        //     Abc  | 16
        //
        // Should be rendered as:
        //     <table class="table">
        //     <thead>
        //     <tr>
        //     <th>Name</th>
        //     <th>Value</th>
        //     </tr>
        //     </thead>
        //     <tbody>
        //     <tr>
        //     <td>Abc</td>
        //     <td>16</td>
        //     </tr>
        //     </tbody>
        //     </table>

        TestParser.TestSpec("Name | Value\n-----| -----\nAbc  | 16", "<table class=\"table\">\n<thead>\n<tr>\n<th>Name</th>\n<th>Value</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>Abc</td>\n<td>16</td>\n</tr>\n</tbody>\n</table>", "bootstrap+pipetables+figures+attributes+alerts", context: "Example 1\nSection Extensions / Bootstrap\n");
    }

    // Adds bootstrap `.blockquote` class to `<blockquote>`:
    [Fact]
    public void ExtensionsBootstrap_Example002()
    {
        // Example 2
        // Section: Extensions / Bootstrap
        //
        // The following Markdown:
        //     > This is a blockquote
        //
        // Should be rendered as:
        //     <blockquote class="blockquote">
        //     <p>This is a blockquote</p>
        //     </blockquote>

        TestParser.TestSpec("> This is a blockquote", "<blockquote class=\"blockquote\">\n<p>This is a blockquote</p>\n</blockquote>", "bootstrap+pipetables+figures+attributes+alerts", context: "Example 2\nSection Extensions / Bootstrap\n");
    }

    // Adds bootstrap `.figure` class to `<figure>` and `.figure-caption` to `<figcaption>`
    [Fact]
    public void ExtensionsBootstrap_Example003()
    {
        // Example 3
        // Section: Extensions / Bootstrap
        //
        // The following Markdown:
        //     ^^^
        //     This is a text in a caption
        //     ^^^ This is the caption
        //
        // Should be rendered as:
        //     <figure class="figure">
        //     <p>This is a text in a caption</p>
        //     <figcaption class="figure-caption">This is the caption</figcaption>
        //     </figure>

        TestParser.TestSpec("^^^\nThis is a text in a caption\n^^^ This is the caption", "<figure class=\"figure\">\n<p>This is a text in a caption</p>\n<figcaption class=\"figure-caption\">This is the caption</figcaption>\n</figure>", "bootstrap+pipetables+figures+attributes+alerts", context: "Example 3\nSection Extensions / Bootstrap\n");
    }

    // Adds the `.img-fluid` class to all image links `<img>`
    [Fact]
    public void ExtensionsBootstrap_Example004()
    {
        // Example 4
        // Section: Extensions / Bootstrap
        //
        // The following Markdown:
        //     ![Image Link](/url)
        //
        // Should be rendered as:
        //     <p><img src="/url" class="img-fluid" alt="Image Link" /></p>

        TestParser.TestSpec("![Image Link](/url)", "<p><img src=\"/url\" class=\"img-fluid\" alt=\"Image Link\" /></p>", "bootstrap+pipetables+figures+attributes+alerts", context: "Example 4\nSection Extensions / Bootstrap\n");
    }

    // Adds the `.alert` class, the `.alert-*` class of its kind and the `alert` role to alert blocks, and the `.mb-0` class to their last paragraph:
    [Fact]
    public void ExtensionsBootstrap_Example005()
    {
        // Example 5
        // Section: Extensions / Bootstrap
        //
        // The following Markdown:
        //     > [!WARNING]
        //     > Some text
        //     >
        //     > More text
        //
        // Should be rendered as:
        //     <div class="markdown-alert markdown-alert-warning alert alert-warning" role="alert">
        //     <p class="markdown-alert-title"><svg viewBox="0 0 16 16" version="1.1" width="16" height="16" aria-hidden="true"><path d="M6.457 1.047c.659-1.234 2.427-1.234 3.086 0l6.082 11.378A1.75 1.75 0 0 1 14.082 15H1.918a1.75 1.75 0 0 1-1.543-2.575Zm1.763.707a.25.25 0 0 0-.44 0L1.698 13.132a.25.25 0 0 0 .22.368h12.164a.25.25 0 0 0 .22-.368Zm.53 3.996v2.5a.75.75 0 0 1-1.5 0v-2.5a.75.75 0 0 1 1.5 0ZM9 11a1 1 0 1 1-2 0 1 1 0 0 1 2 0Z"></path></svg>Warning</p>
        //     <p>Some text</p>
        //     <p class="mb-0">More text</p>
        //     </div>

        TestParser.TestSpec("> [!WARNING]\n> Some text\n>\n> More text", "<div class=\"markdown-alert markdown-alert-warning alert alert-warning\" role=\"alert\">\n<p class=\"markdown-alert-title\"><svg viewBox=\"0 0 16 16\" version=\"1.1\" width=\"16\" height=\"16\" aria-hidden=\"true\"><path d=\"M6.457 1.047c.659-1.234 2.427-1.234 3.086 0l6.082 11.378A1.75 1.75 0 0 1 14.082 15H1.918a1.75 1.75 0 0 1-1.543-2.575Zm1.763.707a.25.25 0 0 0-.44 0L1.698 13.132a.25.25 0 0 0 .22.368h12.164a.25.25 0 0 0 .22-.368Zm.53 3.996v2.5a.75.75 0 0 1-1.5 0v-2.5a.75.75 0 0 1 1.5 0ZM9 11a1 1 0 1 1-2 0 1 1 0 0 1 2 0Z\"></path></svg>Warning</p>\n<p>Some text</p>\n<p class=\"mb-0\">More text</p>\n</div>", "bootstrap+pipetables+figures+attributes+alerts", context: "Example 5\nSection Extensions / Bootstrap\n");
    }
}
