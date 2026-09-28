# Extensions

Adds support for outputting bootstrap ready tags:

## Bootstrap
 
Adds bootstrap `.table` class to `<table>`:

```````````````````````````````` example
Name | Value
-----| -----
Abc  | 16
.
<table class="table">
<thead>
<tr>
<th>Name</th>
<th>Value</th>
</tr>
</thead>
<tbody>
<tr>
<td>Abc</td>
<td>16</td>
</tr>
</tbody>
</table>
````````````````````````````````

Adds bootstrap `.blockquote` class to `<blockquote>`:

```````````````````````````````` example
> This is a blockquote
.
<blockquote class="blockquote">
<p>This is a blockquote</p>
</blockquote>
````````````````````````````````

Adds bootstrap `.figure` class to `<figure>` and `.figure-caption` to `<figcaption>`

```````````````````````````````` example
^^^
This is a text in a caption
^^^ This is the caption
.
<figure class="figure">
<p>This is a text in a caption</p>
<figcaption class="figure-caption">This is the caption</figcaption>
</figure>
````````````````````````````````

Adds the `.img-fluid` class to all image links `<img>`

```````````````````````````````` example
![Image Link](/url)
.
<p><img src="/url" class="img-fluid" alt="Image Link" /></p>
````````````````````````````````

Adds the `.alert` class, the `.alert-*` class of its kind and the `alert` role to alert blocks, and the `.mb-0` class to their last paragraph:

```````````````````````````````` example
> [!WARNING]
> Some text
>
> More text
.
<div class="markdown-alert markdown-alert-warning alert alert-warning" role="alert">
<p class="markdown-alert-title"><svg viewBox="0 0 16 16" version="1.1" width="16" height="16" aria-hidden="true"><path d="M6.457 1.047c.659-1.234 2.427-1.234 3.086 0l6.082 11.378A1.75 1.75 0 0 1 14.082 15H1.918a1.75 1.75 0 0 1-1.543-2.575Zm1.763.707a.25.25 0 0 0-.44 0L1.698 13.132a.25.25 0 0 0 .22.368h12.164a.25.25 0 0 0 .22-.368Zm.53 3.996v2.5a.75.75 0 0 1-1.5 0v-2.5a.75.75 0 0 1 1.5 0ZM9 11a1 1 0 1 1-2 0 1 1 0 0 1 2 0Z"></path></svg>Warning</p>
<p>Some text</p>
<p class="mb-0">More text</p>
</div>
````````````````````````````````
