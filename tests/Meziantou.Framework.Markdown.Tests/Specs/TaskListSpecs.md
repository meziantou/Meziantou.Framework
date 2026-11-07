# Extensions

Adds support for task lists:

## TaskLists
 
A task list item consist of `[ ]` or `[x]` or `[X]` at the start of the first paragraph of a list item (ordered or unordered), followed by whitespace

```````````````````````````````` example
- [ ] Item1
- [x] Item2
- [ ] Item3
- Item4
.
<ul class="contains-task-list">
<li class="task-list-item"><input disabled="disabled" type="checkbox" /> Item1</li>
<li class="task-list-item"><input disabled="disabled" type="checkbox" checked="checked" /> Item2</li>
<li class="task-list-item"><input disabled="disabled" type="checkbox" /> Item3</li>
<li>Item4</li>
</ul>
````````````````````````````````

A task is not recognized outside a list item:

```````````````````````````````` example
[ ] This is not a task list
.
<p>[ ] This is not a task list</p>
````````````````````````````````

A task is only recognized at the start of the first paragraph of a list item, and must be followed by whitespace:

```````````````````````````````` example
- Press [x] to close
- see [x](http://example.com)
- [x]abc
- item

  [ ] second paragraph
.
<ul>
<li><p>Press [x] to close</p>
</li>
<li><p>see <a href="http://example.com">x</a></p>
</li>
<li><p>[x]abc</p>
</li>
<li><p>item</p>
<p>[ ] second paragraph</p>
</li>
</ul>
````````````````````````````````
