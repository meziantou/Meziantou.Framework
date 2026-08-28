<!-- Generated from the ADF fixtures in this corpus. See NOTICE.md. -->

<!--@ blocks/align_html.json @-->
Centered text

<!--@ blocks/decision_text.json @-->
- This is a decision

<!--@ blocks/heading_align_html.json @-->
## Aligned Heading

<!--@ blocks/heading_align_html_marks.json @-->
### **Bold Heading**

<!--@ blocks/heading_aligned_right_pandoc.json @-->
## Heading

<!--@ blocks/heading_offset1.json @-->
# H1

## H2

<!--@ blocks/multiple_paragraphs.json @-->
Para 1

Para 2

<!--@ blocks/panel_bold.json @-->
> ℹ️ content

<!--@ blocks/panel_github.json @-->
> ⚠️ watch out

<!--@ blocks/panel_title.json @-->
> ℹ️ content

<!--@ blocks/paragraph_aligned_center_pandoc.json @-->
Centered text

<!--@ codeblocks/basic.json @-->
```go
func main() {
    fmt.Println("Hello, World!")
}
```

<!--@ codeblocks/empty.json @-->
```javascript

```

<!--@ codeblocks/language_map_cpp.json @-->
```c++
int main() {}
```

<!--@ codeblocks/trailing_newline.json @-->
```python
print('hello')
```

<!--@ decisions/decision_decided.json @-->
- test decision

<!--@ decisions/decision_empty.json @-->

<!--@ decisions/decision_formatted.json @-->
- This is a **bold** decision

<!--@ decisions/decision_list_multiple.json @-->
- First decision
- Second decision

<!--@ decisions/decision_multiline.json @-->
- First paragraph

  Second paragraph

<!--@ decisions/decision_no_state.json @-->
- Generic decision

<!--@ decisions/decision_undecided.json @-->
- test undecided

<!--@ edge_cases/empty_document.json @-->

<!--@ edge_cases/empty_paragraph.json @-->

<!--@ edge_cases/literal_block_markers.json @-->
\# literal heading

\> literal quote

<!--@ edge_cases/non_text_node_boundary.json @-->
**Bold**Plain

<!--@ edge_cases/unknown_node.json @-->

<!--@ expanders/expand.json @-->
> **Click to see more**
>
> Hidden content here.

<!--@ expanders/expand_empty.json @-->
> **Title**

<!--@ expanders/expand_html.json @-->
> **Click to see more**
>
> Hidden content here.

<!--@ expanders/expand_in_list.json @-->
- Item 1

  > **Details**
  >
  > Detailed info

<!--@ expanders/expand_nested.json @-->
> **Outer**
>
> Outer content
>
> > **Inner**
> >
> > Inner content

<!--@ expanders/expand_no_title.json @-->
> Content

<!--@ expanders/expand_title_with_quotes_pandoc.json @-->
> **Click "now"**
>
> Body

<!--@ expanders/expand_with_title_pandoc.json @-->
> **Click to expand**
>
> body

<!--@ expanders/expand_without_title_pandoc.json @-->
> body

<!--@ expanders/nested_expand_pandoc.json @-->
> **Outer**
>
> Outer content
>
> > **Inner**
> >
> > Inner content

<!--@ extensions/bodied_ext_html.json @-->
- [ ] **Customise the overview page** - Click the pencil icon...
- [ ] **Create additional pages** - Click the + in the left sidebar...

<!--@ extensions/bodied_ext_json.json @-->
- [ ] **Customise the overview page** - Click the pencil icon...
- [ ] **Create additional pages** - Click the + in the left sidebar...

<!--@ extensions/bodied_ext_pandoc.json @-->
- [ ] **Customise the overview page** - Click the pencil icon...
- [ ] **Create additional pages** - Click the + in the left sidebar...

<!--@ extensions/bodied_ext_pandoc_no_params.json @-->
Hello world

<!--@ extensions/bodied_ext_standard.json @-->
- [ ] **Customise the overview page** - Click the pencil icon...
- [ ] **Create additional pages** - Click the + in the left sidebar...

<!--@ extensions/ext_json.json @-->
package main

<!--@ extensions/ext_strip.json @-->

<!--@ extensions/ext_text.json @-->
@username

<!--@ extensions/inline_extension_with_text.json @-->
Beforeafter

<!--@ inline/block_card.json @-->
<https://example.com>

<!--@ inline/block_card_pandoc.json @-->
<https://example.com>

<!--@ inline/date.json @-->
Due by 2020-02-19

<!--@ inline/date_invalid.json @-->
invalid

<!--@ inline/embed_card.json @-->
<https://embedded.example.com>

<!--@ inline/embed_card_pandoc.json @-->
<https://embedded.example.com>

<!--@ inline/emoji.json @-->
Hello :smile: world

<!--@ inline/emoji_fallback.json @-->
😊

<!--@ inline/emoji_missing_both.json @-->
Empty:

<!--@ inline/emoji_unicode.json @-->
😄

<!--@ inline/inline_card.json @-->
<https://example.com>

<!--@ inline/inline_card_empty.json @-->

<!--@ inline/inline_card_url_only_pandoc.json @-->
<https://example.com>

<!--@ inline/inline_card_with_data.json @-->
[My Link](https://example.com)

<!--@ inline/inline_card_with_title_pandoc.json @-->
[My Page](https://example.atlassian.net/wiki/spaces/ABC/pages/1)

<!--@ inline/inline_combined.json @-->
Here is a :smile: for @User Name who is `IN PROGRESS` as of 2020-02-19

<!--@ inline/inlinecard_embed.json @-->
[jira-adf-converter](https://github.com/rgonek/jira-adf-converter)

<!--@ inline/inlinecard_embed_with_text.json @-->
Before[Example](https://example.com)after

<!--@ inline/mention.json @-->
Hey @User Name

<!--@ inline/mention_display_text_only_pandoc.json @-->
@Bob

<!--@ inline/mention_html.json @-->
@User Name

<!--@ inline/mention_html_escape.json @-->
@User \<Admin\> & "Ops"

<!--@ inline/mention_html_missing_id.json @-->
@User Name

<!--@ inline/mention_link.json @-->
@User Name

<!--@ inline/mention_link_missing_id.json @-->
@User Name

<!--@ inline/mention_no_id.json @-->
@User Name

<!--@ inline/mention_no_text.json @-->
@12345

<!--@ inline/mention_text.json @-->
@User Name

<!--@ inline/mention_with_account_id_pandoc.json @-->
@Alice

<!--@ inline/status.json @-->
`In Progress`

<!--@ inline/status_text.json @-->
`IN PROGRESS`

<!--@ inline/status_with_color.json @-->
`In Progress`

<!--@ layout/layout_section_html.json @-->
Column 1 content

Column 2 content

<!--@ layout/layout_section_pandoc.json @-->
Column 1 content

Column 2 content

<!--@ layout/layout_section_standard.json @-->
Column 1 content

Column 2 content

<!--@ lists/bullet.json @-->
- First item
- Second item
- Third item with nested list

  - Nested item 1
  - Nested item 2

<!--@ lists/bullet_star.json @-->
- item

<!--@ lists/mixed.json @-->
- Bullet item with nested ordered list

  1. First ordered item
  2. Second ordered item
- Another bullet item with code block

  ```python
  print('Hello')
  ```

<!--@ lists/ordered.json @-->
1. First step
2. Second step
3. Third step

<!--@ lists/ordered_lazy.json @-->
1. Item 1
2. Item 2

<!--@ lists/task.json @-->
- [ ] Incomplete task
- [x] Completed task
- [ ] Another incomplete task

<!--@ lists/task_canonical_unchecked.json @-->
- [ ] first
- [ ] second

<!--@ lists/task_mixed_states.json @-->
- [ ] todo
- [x] done

<!--@ lists/task_nested.json @-->
- [ ] parent
  - [x] child done

<!--@ lists/task_nested_bug.json @-->
- [ ] Implement authentication
  - [ ] Add login page
- [ ] Update user documentation

<!--@ lists/task_rich.json @-->
- [ ] Task with **bold** text
- [x] Task with *italic* text
- [ ] Task with `code`

<!--@ marks/annotation.json @-->
Annotated text normal text

<!--@ marks/annotation_pandoc.json @-->
Annotated text normal text

<!--@ marks/background_color_pandoc.json @-->
highlighted

<!--@ marks/bgcolor_html.json @-->
highlighted text

<!--@ marks/bgcolor_html_invalid_injection.json @-->
bad

<!--@ marks/bold.json @-->
**bold**

<!--@ marks/color_html.json @-->
red text

<!--@ marks/color_html_invalid_injection.json @-->
bad

<!--@ marks/color_ignore.json @-->
red text

<!--@ marks/formatting_html.json @-->
This is <sub>subscript</sub> and <sup>superscript</sup> and underline

<!--@ marks/formatting_plain.json @-->
This is <sub>subscript</sub> and <sup>superscript</sup> and underline

<!--@ marks/inline_code.json @-->
`code`

<!--@ marks/italic.json @-->
*italic*

<!--@ marks/leading_marked_space.json @-->
Design **is good**

<!--@ marks/link.json @-->
This is a [link](https://example.com) in text

<!--@ marks/link_empty_text.json @-->

<!--@ marks/link_literal_escape.json @-->
[literal \[label\]](<https://example.com/docs_(v1)> "He said \"go\" \\ now")

<!--@ marks/link_missing_href.json @-->
[not a link]()

<!--@ marks/link_title_with_quotes.json @-->
[link](https://example.com "He said \"hello\"")

<!--@ marks/link_with_title.json @-->
[link with title](https://example.com "Example Site")

<!--@ marks/literal_chars.json @-->
Literal \* \_ \[ \] ( ) \` and \\

<!--@ marks/mixed_bold_underline_html.json @-->
**bold and underlined**

<!--@ marks/mixed_known_unknown.json @-->
**bold and underlined**

<!--@ marks/mixed_marks.json @-->
***bold italic***

<!--@ marks/mixed_marks_pandoc.json @-->
colored underline

<!--@ marks/multiple_unknown_marks.json @-->
colorful text

<!--@ marks/nested_marks.json @-->
**bold** ***bold+italic*** **end**

<!--@ marks/strike.json @-->
~~strike~~

<!--@ marks/strong_whitespace_boundaries.json @-->
**name and surname match** problem

prefix **problem**

<!--@ marks/subscript_pandoc.json @-->
<sub>H2O</sub>

<!--@ marks/subsup_html.json @-->
<sub>sub</sub> and <sup>sup</sup>

<!--@ marks/subsup_latex.json @-->
H<sub>2</sub>O

<!--@ marks/superscript_pandoc.json @-->
x<sup>2</sup>

<!--@ marks/text_color_pandoc.json @-->
red text

<!--@ marks/underline_and_bold_pandoc.json @-->
**bold underline**

<!--@ marks/underline_bold.json @-->
underlined text

<!--@ marks/underline_html.json @-->
underlined text

<!--@ marks/underline_ignore.json @-->
underlined

<!--@ marks/underline_pandoc.json @-->
underlined text

<!--@ marks/unknown_mark.json @-->
underlined text

<!--@ marks/unknown_placeholder_mixed.json @-->
Hello **bold** and *italic*!

<!--@ marks/unknown_placeholder_multiple_output.json @-->
text

<!--@ marks/unknown_placeholder_single.json @-->
colored

<!--@ marks/unknown_placeholder_whitespace_continuity_output.json @-->
**bold** **text**

<!--@ marks/unknown_placeholder_with_known_mark.json @-->
**bold**

<!--@ marks/whitespace_continuity.json @-->
**Bold still bold** *Italic*

|  |
| --- |
| ` Cell  ` |

<!--@ media/media_baseurl.json @-->

<!--@ media/media_caption.json @-->
*A photo caption*

<!--@ media/media_caption_pandoc.json @-->
*A photo caption*

<!--@ media/media_file.json @-->

<!--@ media/media_group.json @-->

<!--@ media/media_group_empty.json @-->

<!--@ media/media_image_id.json @-->

<!--@ media/media_image_no_alt.json @-->
![](http://example.com/image.png)

<!--@ media/media_image_url.json @-->
![Alt Text](http://example.com/image.png)

<!--@ media/media_in_table.json @-->
|  |
| --- |
| ![](img.png) |

<!--@ media/media_inline.json @-->
Before  after

<!--@ media/media_inline_fileid.json @-->
Before  after

<!--@ media/media_inline_pandoc.json @-->
Before  after

<!--@ media/media_single.json @-->

<!--@ media/media_unknown_type.json @-->

<!--@ nodes/blockquote.json @-->
> This is a blockquote

<!--@ nodes/blockquote_empty.json @-->

<!--@ nodes/blockquote_multiline.json @-->
> First paragraph in blockquote
>
> Second paragraph in blockquote

<!--@ nodes/blockquote_with_marks.json @-->
> This has **bold**, *italic*, `code`, and [a link](https://example.com)

<!--@ nodes/hard_break.json @-->
Line 1\
Line 2

<!--@ nodes/heading.json @-->
# Heading 1

## Heading 2

### Heading 3

#### Heading 4

##### Heading 5

###### Heading 6

<!--@ nodes/heading_empty.json @-->
###

<!--@ nodes/heading_trailing_space.json @-->
### Design

<!--@ nodes/heading_with_marks.json @-->
## This is **bold** heading with *italic* and `code`

<!--@ nodes/nested_blockquote.json @-->
> > Nested blockquote

<!--@ nodes/nested_blockquote_with_marks.json @-->
> Outer blockquote with **bold** text
>
> > Nested with *italic* and `code`

<!--@ nodes/rule.json @-->
Before rule

---

After rule

<!--@ panels/panel_empty.json @-->
> ℹ️
>
>

<!--@ panels/panel_error.json @-->
> ❌ Test error panel

<!--@ panels/panel_info.json @-->
> ℹ️ Test info panel

<!--@ panels/panel_multiline.json @-->
> ℹ️ First paragraph
>
> Second paragraph

<!--@ panels/panel_nested_content.json @-->
> 📝 Text with **bold** and *italic*
>
> - List item 1
> - List item 2
>
> ```
> code block
> ```

<!--@ panels/panel_no_type.json @-->
> Plain blockquote

<!--@ panels/panel_note.json @-->
> 📝 Test note panel

<!--@ panels/panel_success.json @-->
> ✅ Test success panel

<!--@ panels/panel_warning.json @-->
> ⚠️ Test warning panel

<!--@ simple/basic_text.json @-->
Hello World

<!--@ simple/placeholder.json @-->
Here is some text with a  node in between.

<!--@ tables/complex_table_autopandoc_fallback.json @-->
|  |  |
| --- | --- |
| complex cell |  |
| cell 1 | cell 2 |

<!--@ tables/simple_table_autopandoc.json @-->
| a | b | c |
| --- | --- | --- |
| 1 | 2 | 3 |

<!--@ tables/simple_table_pandoc.json @-->
| a | b | c |
| --- | --- | --- |
| 1 | 2 | 3 |
| 4 | 5 | 6 |

<!--@ tables/table_auto.json @-->
|  |
| --- |
| cell |

<!--@ tables/table_auto_complex.json @-->
|  |  |
| --- | --- |
| complex cell |  |
| cell 1 | cell 2 |

<!--@ tables/table_codeblock_auto.json @-->
|  |
| --- |
| ```<br>fmt.Println("Hello")<br>return<br>``` |

<!--@ tables/table_colspan_pandoc.json @-->
| Name | Scores |  |
| --- | --- | --- |
| Alice | 10 | 20 |

<!--@ tables/table_complex_content.json @-->
| Header | Content |
| --- | --- |
| List | - Item 1<br>- Item 2 |
| Link | [Example](https://example.com) |

<!--@ tables/table_empty_cells.json @-->
| A |  | C |
| --- | --- | --- |
|  | B |  |

<!--@ tables/table_formatted_cells.json @-->
| **Bold** | *Italic* | `Code` |
| --- | --- | --- |
| ~~Strike~~ | Plain | ***Both*** |

<!--@ tables/table_html.json @-->
|  |
| --- |
| cell |

<!--@ tables/table_html_escape_injection.json @-->
|  |
| --- |
| \<img src=x onerror=1\> & text |

<!--@ tables/table_multiline_cells.json @-->
| Cell 1 | Cell 2 |
| --- | --- |
| Line 1<br>Line 2 | Single line |

<!--@ tables/table_nested_list.json @-->
| List |
| --- |
| - Item 1<br>- Nested Item |

<!--@ tables/table_nested_list_html.json @-->
| List |
| --- |
| - Item 1<br>- Nested Item |

<!--@ tables/table_no_headers.json @-->
|  |  |
| --- | --- |
| Data 1 | Data 2 |
| Data 3 | Data 4 |

<!--@ tables/table_pipe_codeblock_hardbreakbackslash.json @-->
|  |
| --- |
| ```<br>fmt.Println("Hello")<br>return<br>``` |

<!--@ tables/table_pipe_codeblock_hardbreakhtml.json @-->
|  |
| --- |
| ```<br>fmt.Println("Hello")<br>return<br>``` |

<!--@ tables/table_pipe_escape.json @-->
| Header \| with pipe |
| --- |
| Cell \| with pipe |

<!--@ tables/table_rowspan_pandoc.json @-->
| Span | A | B |
| --- | --- | --- |
| 1 | 2 |  |

<!--@ tables/table_simple.json @-->
| a | b | c |
| --- | --- | --- |
| 1 | 2 | 3 |
| 4 | 5 | 6 |

<!--@ tables/table_single_column.json @-->
| Column |
| --- |
| Row 1 |

<!--@ tables/table_single_row.json @-->
| A | B | C |
| --- | --- | --- |

<!--@ tables/table_with_headers.json @-->
| Name | Age | City |
| --- | --- | --- |
| Alice | 30 | NYC |
| Bob | 25 | LA |

<!--@ tables/table_with_wide_cells_pandoc.json @-->
| Name | Value |
| --- | --- |
| foobar | 12345 |
