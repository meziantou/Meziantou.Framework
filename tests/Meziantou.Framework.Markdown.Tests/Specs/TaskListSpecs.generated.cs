
// --------------------------------
//            Task Lists
// --------------------------------

namespace Meziantou.Framework.Markdown.Tests.Specs.TaskLists;

public class TestExtensionsTaskLists
{
    // # Extensions
    //
    // Adds support for task lists:
    //
    // ## TaskLists
    //
    // A task list item consist of `[ ]` or `[x]` or `[X]` at the start of the first paragraph of a list item (ordered or unordered), followed by whitespace
    [Fact]
    public void ExtensionsTaskLists_Example001()
    {
        // Example 1
        // Section: Extensions / TaskLists
        //
        // The following Markdown:
        //     - [ ] Item1
        //     - [x] Item2
        //     - [ ] Item3
        //     - Item4
        //
        // Should be rendered as:
        //     <ul class="contains-task-list">
        //     <li class="task-list-item"><input disabled="disabled" type="checkbox" /> Item1</li>
        //     <li class="task-list-item"><input disabled="disabled" type="checkbox" checked="checked" /> Item2</li>
        //     <li class="task-list-item"><input disabled="disabled" type="checkbox" /> Item3</li>
        //     <li>Item4</li>
        //     </ul>

        TestParser.TestSpec("- [ ] Item1\n- [x] Item2\n- [ ] Item3\n- Item4", "<ul class=\"contains-task-list\">\n<li class=\"task-list-item\"><input disabled=\"disabled\" type=\"checkbox\" /> Item1</li>\n<li class=\"task-list-item\"><input disabled=\"disabled\" type=\"checkbox\" checked=\"checked\" /> Item2</li>\n<li class=\"task-list-item\"><input disabled=\"disabled\" type=\"checkbox\" /> Item3</li>\n<li>Item4</li>\n</ul>", "tasklists|advanced", context: "Example 1\nSection Extensions / TaskLists\n");
    }

    // A task is not recognized outside a list item:
    [Fact]
    public void ExtensionsTaskLists_Example002()
    {
        // Example 2
        // Section: Extensions / TaskLists
        //
        // The following Markdown:
        //     [ ] This is not a task list
        //
        // Should be rendered as:
        //     <p>[ ] This is not a task list</p>

        TestParser.TestSpec("[ ] This is not a task list", "<p>[ ] This is not a task list</p>", "tasklists|advanced", context: "Example 2\nSection Extensions / TaskLists\n");
    }

    // A task is only recognized at the start of the first paragraph of a list item, and must be followed by whitespace:
    [Fact]
    public void ExtensionsTaskLists_Example003()
    {
        // Example 3
        // Section: Extensions / TaskLists
        //
        // The following Markdown:
        //     - Press [x] to close
        //     - see [x](http://example.com)
        //     - [x]abc
        //     - item
        //
        //       [ ] second paragraph
        //
        // Should be rendered as:
        //     <ul>
        //     <li><p>Press [x] to close</p>
        //     </li>
        //     <li><p>see <a href="http://example.com">x</a></p>
        //     </li>
        //     <li><p>[x]abc</p>
        //     </li>
        //     <li><p>item</p>
        //     <p>[ ] second paragraph</p>
        //     </li>
        //     </ul>

        TestParser.TestSpec("- Press [x] to close\n- see [x](http://example.com)\n- [x]abc\n- item\n\n  [ ] second paragraph", "<ul>\n<li><p>Press [x] to close</p>\n</li>\n<li><p>see <a href=\"http://example.com\">x</a></p>\n</li>\n<li><p>[x]abc</p>\n</li>\n<li><p>item</p>\n<p>[ ] second paragraph</p>\n</li>\n</ul>", "tasklists|advanced", context: "Example 3\nSection Extensions / TaskLists\n");
    }
}
