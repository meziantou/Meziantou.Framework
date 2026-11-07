<!-- Generated from the ADF fixtures in this corpus. See NOTICE.md. -->

<!--@ mark_background_color.json @-->
this is backgroundColor text.

<!--@ mark_code.json @-->
this is a `code` text.

<!--@ mark_em.json @-->
this is a *italic* text.

<!--@ mark_link.json @-->
this is a [link](https://example.com) text.

<!--@ mark_strike.json @-->
this is a ~~strike~~ text.

<!--@ mark_strong.json @-->
this is a **strong** text.

<!--@ mark_subsup.json @-->
this is a <sub>sub</sub> and <sup>sup</sup> text.

<!--@ mark_text_color.json @-->
this is a colored text.

<!--@ mark_underline.json @-->
this is an underline text.

<!--@ node_block_card.json @-->
<https://example.com>

<!--@ node_blockquote.json @-->
> Alice says:
>
> Just do it!

<!--@ node_bullet_list.json @-->
- item **1**

  - item **1.1**

    - item **1.1.1**
- item 2

  - item 2.1

    - item 2.1.1
- item 3

  - item 3.1

    - item 3.1.1

<!--@ node_caption.json @-->
![](https://www.python.org/static/img/python-logo.png)

*this is caption text*

python-logo.png

*this is caption text*

<!--@ node_code_block.json @-->
```python
def add_two(a, b):
    return a + b
```

<!--@ node_date.json @-->
this is a date: 2026-01-01

<!--@ node_decision_list.json @-->
- decision **1**
- decision 2\
  \- first\
  \- second\
  \
  some text
- decision 3\
  \
  1. [https://example.com](https://example.com) \
  2. <https://example.com> \
  \
  some text

<!--@ node_doc.json @-->
This document is purposely built for creating a software to parse Atlassian Document Format JSON.

This is a table of content

# Text and Paragraph

## Text and Paragraph 28d8c0

This is a simple sentence 761ec2.

This is a simple sentence 17adc1.

This is a simple sentence 9c9c4f, there is a empty line above this.

This is a simple paragraph c3f610, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph.

This is a simple paragraph 9af9a0, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph.

This is a simple paragraph ff2fa6, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, This is a simple paragraph, there is a empty line above this.

## Text Style and Format e0dfeb

This is a **bolded text**, do you see that?

This is a *italic text*, do you see that?

This is a underline, do you see that?

This is a ~~strike through~~, do you see that?

This is a ***~~bolded itlic strike through and underline~~***, do you see that?

This is a <sub>subscript</sub>, do you see that?

This is a <sup>superscript</sup>, do you see that?

This text has color, do you see that?

This text has background, do you see that?

Note that you can not do Text color and Background color at the same time.

This line has code `a = 1 + 2`**.**

## Hyper Link

This line has titled hyperlink [Atlas Doc Format](https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/).

This line has url hyperlink <https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/>

This line has inline hyperlink <https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/>

This line has card hyperlink

<https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/>

This line has emoji 📝 .

This line at @machu for something.

# Bullet List b2abc0

## Simple Bullet List

bullet list 1 a50f00

- item 1
- item 2
- item 3

- bullet list 2 item 1 (there is an empty line above this between this and the previous bullet list)
- bullet list 2 item 2
- bullet list 2 item 3

## Bullet List with Format

bullet list 1 944782

- this is **Alice**, *Bob*, Cathy, ~~David~~, `Edward`, <sub>Frank</sub>, <sup>George</sup>.
- This line has titled hyperlink [Atlas Doc Format](https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/).
- This line has url hyperlink <https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/>
- This line has inline hyperlink <https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/>

## Nested Bullet List

bullet list 1 c5e045

- **item** 1
- item 2
- `item` 3

  - [item](https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/) 3.1

    - item 3.1.1
    - item 3.1.2
  - item 3.2

    - item 3.2.1
    - item 3.2.2

# Ordered List 639d5a

## Simple Ordered List

Ordered list 1 e7bd58

1. Alice
2. Bob
3. Cathy

1. Ordered list 2 item 1 (there is an empty line above this between this and the previous bullet list)
2. Ordered list 2 item 2
3. Ordered list 2 item 3

## Ordered List With Format

ordered list 1 fd02cb

11. this is **Alice**, *Bob*, Cathy, ~~David~~, `Edward`, <sub>Frank</sub>, <sup>George</sup>.
12. This line has titled hyperlink [Atlas Doc Format](https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/).
13. This line has url hyperlink <https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/>
14. This line has inline hyperlink <https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/>

## Nested Ordered List

ordered list 1 eb58ef

1. Alice
2. Bob
3. Cathy

   1. Cathy 1

      1. Cathy 1.1
      2. Cathy 1.2
   2. Cathy 2

      1. Cathy 2.1
      2. Cathy 2.2

# Task List 4141f8

## Nested Task List

task list 1 e5461d

- [x] Do this
- [ ] And do **this**
  - [ ] sub `task` 1
    - [x] sub task 1.1
    - [ ] sub ~~task~~ 1.2
  - [ ] sub **task** 2
    - [ ] sub task 2.1
    - [x] sub task 2.2

# Code Block 3d7d7e

This is a code block

```none
> Hello world
```

This is a Python code block

```python
def add_two(a, b):
    return a + b
```

# Image 75c648

This is an image

![](https://www.python.org/static/img/python-logo.png)

This is an image with alt text

![](https://www.python.org/static/img/python-logo.png)

This is an image with clickable link

![](https://www.python.org/static/img/python-logo.png)

This is an image with capital and clickable link

![](https://www.python.org/static/img/python-logo.png)

# Table 8cb9f1

## Simple Table 69b8c4

simple table 1

| **name** | **age** |
| --- | --- |
| Alice | 20 |
| Bob | 35 |

## Multiline Content Table 51e9b3

multiline content table 1

| **Col 1** | **Col 2** |
| --- | --- |
| key 1<br>special character \| is not markdown friendly | value 1<br>- this is **Alice**, *Bob*, Cathy, ~~David~~, `Edward`, <sub>Frank</sub>, <sup>George</sup>.<br>- This line has titled hyperlink [Atlas Doc Format](https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/).<br>- This line has url hyperlink <https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/><br>- This line has inline hyperlink <https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/> |
| key 2<br>special character \| is not markdown friendly | value 2<br>1. Alice<br>2. Bob<br>3. Cathy<br>1. Cathy 1<br>1. Cathy 1.1<br>2. Cathy 1.2<br>2. Cathy 2<br>1. Cathy 2.1<br>2. Cathy 2.2 |
| key 3<br>special character \| is not markdown friendly | value 3<br>- [x] Do this<br>- [ ] And do **this**<br>- [ ] sub `task` 1<br>- [x] sub task 1.1<br>- [ ] sub ~~task~~ 1.2<br>- [ ] sub **task** 2<br>- [ ] sub task 2.1<br>- [x] sub task 2.2 |

# Expand 0479c0

This is a expandable container

> **This is an expand title**
>
> this is expand content
>
> - bullet 1 in expand content
> - bullet 2 in expand content
> - bullet 3 in expand content
>
> > Quote something here in expand content
>
> end of expand content

# Quote 08e36f

## Simple Quote 84378f

simple quote 1

> Alice says:
>
> Just do it!

## Quote as Container b644da

quote as container 1

> This is a one line paragraph. Text may have **bold**, *italic*, underscore, ~~strike through~~, [hyperlink](https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/) and more.
>
> This is a bullet list
>
> - bullet 1 in quote
> - bullet 2 in quote
>
> Code block in quote
>
> Start
>
> ```python
> def mul_two(a, b):
>     return a * b
> ```
>
> End

# Panel 8c4bed

This is an info

> ℹ️ Info here
>
> This is a one line paragraph. Text may have **bold**, *italic*, underscore, ~~strike through~~, [hyperlink](https://developer.atlassian.com/cloud/jira/platform/apis/document/structure/) and more.
>
> This is a bullet list
>
> - bullet 1 in panel
> - bullet 2 in panel
>
> Code block in quote
>
> Start
>
> ```python
> def mul_two(a, b):
>     return a * b
> ```
>
> End

This is a note

> 📝 Note here

This is a success

> ✅ Success here

This is a warning

> ⚠️ Warning here

This is a error

> ❌ Error here

This is a custom emoji

> Custom emoji info here

This is an decision

- Decision title\
  \
  decision body here\
  \- alice\
  \- bob\
  \- cathy\
  \
  This is pure text only

This is an Date

2025-01-01

This is a status

`In Progress`

This is a devider

---

## 1.1 Header 2

### 1.1.1 Header 3

<!--@ node_doc_with_unimplemented_model.json @-->
|  |  |
| --- | --- |
| owner | alice |
| **email** | alice@example.com |

Hello World

<!--@ node_embed_card.json @-->
<https://www.youtube.com/watch?v=XqZsoesa55w>

<!--@ node_emoji.json @-->
this is an emoji: ⚙️

<!--@ node_expand.json @-->
> **level 1 expand title**
>
> level 1 expand content
>
> - bullet list
>
> > some quote
>
> > **level 2 expand title**
> >
> > level 2 expand content (level 2 is the max)
> >
> > <https://example.com>
> >
> > > ℹ️ some info

<!--@ node_extension_draw_io_diagram.json @-->
draw.io Diagram

<!--@ node_hard_break.json @-->
below is a regular 'enter'

below is a hard break\
above is a hard break

<!--@ node_heading.json @-->
# header 1

# header 1

## header 2

### **header** 3

#### ~~header~~ 4

##### header 5

###### [header](https://example.com) 6

<!--@ node_inline_card.json @-->
<https://example.com>

<!--@ node_media.json @-->
This is an image

![](https://www.python.org/static/img/python-logo.png)

This is an image with caption text

![](https://www.python.org/static/img/python-logo.png)

*this is caption text*

This is an image with alt text

![python org](https://www.python.org/static/img/python-logo.png)

This is an image with clickable link

[![](https://www.python.org/static/img/python-logo.png)](https://python.org)

This is an image with caption and alt text and clickable link

[![this is alt text](https://www.python.org/static/img/python-logo.png)](https://python.org)

*this is caption text*

This is an uploaded image

python-logo.png

<!--@ node_mention.json @-->
@machu

<!--@ node_ordered_list.json @-->
1. item **1**

   1. item **1.1**

      1. item **1.1.1**
2. item 2

   1. item 2.1

      1. item 2.1.1
3. item 3

   1. item 3.1

      1. item 3.1.1

<!--@ node_panel.json @-->
> ℹ️ This is info

<!--@ node_paragraph.json @-->
This is a **bolded text**, do you see that? This is a *italic text*, do you see that? This is a underline, do you see that? This is a ~~strike through~~, do you see that? This is a ***~~bolded itlic strike through and underline~~***, do you see that? This is a <sub>subscript</sub>, do you see that? This is a <sup>superscript</sup>, do you see that? This text has color, do you see that? This text has background, do you see that? Note that you can not do Text color and Background color at the same time. This line has code `a = 1 + 2`**.**

<!--@ node_rule.json @-->
---

<!--@ node_status.json @-->
`TODO`

<!--@ node_table.json @-->
| **id** | **name** |
| --- | --- |
| 1 | alice |
| 2 | bob |

| **col 1**<br>second line | **col 2**<br>second line |
| --- | --- |
| > abc<br>><br>> - 1<br>> - 2<br>another line | > ℹ️ efg<br>><br>> 1. alice<br>> 2. bob<br>another line |
| ```<br>def add_two(a, b):<br>return a + b<br>``` |  |

<!--@ node_task_list.json @-->
- [x] item **1**
  - [x] item **1.1**
    - [x] item **1.1.1**
- [ ] item 2
  - [x] item 2.1
    - [ ] item 2.1.1
- [ ] item 3
  - [ ] item 3.1
    - [x] item 3.1.1

<!--@ node_text.json @-->
this is a text.
