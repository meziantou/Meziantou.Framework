namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class DiffHighlighterTests
{
    [Fact]
    public void GitDiff()
    {
        AssertHighlighter("diff",
"""
diff --git a/src/app.py b/src/app.py
index 3b18e51..a9c8f2d 100644
--- a/src/app.py
+++ b/src/app.py
@@ -1,5 +1,6 @@
 import os
+import sys

 def main():
-    print("hello")
+    print("hello, world")
     return 0
""",
"""
<span class="hljs-comment">diff --git a/src/app.py b/src/app.py</span>
<span class="hljs-comment">index 3b18e51..a9c8f2d 100644</span>
<span class="hljs-comment">--- a/src/app.py</span>
<span class="hljs-comment">+++ b/src/app.py</span>
<span class="hljs-meta">@@ -1,5 +1,6 @@</span>
 import os
<span class="hljs-addition">+import sys</span>

 def main():
<span class="hljs-deletion">-    print(&quot;hello&quot;)</span>
<span class="hljs-addition">+    print(&quot;hello, world&quot;)</span>
     return 0
""");
    }

    [Fact]
    public void HunkWithSection()
    {
        AssertHighlighter("diff",
"""
@@ -10,6 +10,8 @@ public class Program
     {
-        Console.WriteLine("a");
+        Console.WriteLine("b");
     }
""",
"""
<span class="hljs-meta">@@ -10,6 +10,8 @@</span> public class Program
     {
<span class="hljs-deletion">-        Console.WriteLine(&quot;a&quot;);</span>
<span class="hljs-addition">+        Console.WriteLine(&quot;b&quot;);</span>
     }
""");
    }

    [Fact]
    public void HunkSingleLine()
    {
        AssertHighlighter("diff",
"""
@@ -1 +1 @@
-old
+new
@@ -0,0 +1,2 @@
+a
+b
@@ -5 +5,2 @@
""",
"""
<span class="hljs-meta">@@ -1 +1 @@</span>
<span class="hljs-deletion">-old</span>
<span class="hljs-addition">+new</span>
<span class="hljs-meta">@@ -0,0 +1,2 @@</span>
<span class="hljs-addition">+a</span>
<span class="hljs-addition">+b</span>
<span class="hljs-meta">@@ -5 +5,2 @@</span>
""");
    }

    [Fact]
    public void ContextDiff()
    {
        AssertHighlighter("diff",
"""
*** a/file.txt	2024-01-01 10:00:00
--- b/file.txt	2024-01-02 10:00:00
***************
*** 1,3 ****
  line one
! line two
  line three
--- 1,3 ----
  line one
! line 2
  line three
""",
"""
<span class="hljs-comment">*** a/file.txt	2024-01-01 10:00:00</span>
<span class="hljs-comment">--- b/file.txt	2024-01-02 10:00:00</span>
<span class="hljs-comment">***************</span>
<span class="hljs-meta">*** 1,3 ****</span>
  line one
<span class="hljs-addition">! line two</span>
  line three
<span class="hljs-meta">--- 1,3 ----</span>
  line one
<span class="hljs-addition">! line 2</span>
  line three
""");
    }

    [Fact]
    public void SvnIndex()
    {
        AssertHighlighter("diff",
"""
Index: trunk/file.c
===================================================================
--- trunk/file.c	(revision 1)
+++ trunk/file.c	(working copy)
@@ -1,2 +1,2 @@
-int a;
+int b;
""",
"""
<span class="hljs-comment">Index: trunk/file.c</span>
<span class="hljs-comment">===================================================================</span>
<span class="hljs-comment">--- trunk/file.c	(revision 1)</span>
<span class="hljs-comment">+++ trunk/file.c	(working copy)</span>
<span class="hljs-meta">@@ -1,2 +1,2 @@</span>
<span class="hljs-deletion">-int a;</span>
<span class="hljs-addition">+int b;</span>
""");
    }

    [Fact]
    public void NormalDiff()
    {
        AssertHighlighter("diff",
"""
2c2
< old line
---
> new line
5a6,7
> added
""",
"""
2c2
&lt; old line
<span class="hljs-comment">---</span>
&gt; new line
5a6,7
&gt; added
""");
    }

    [Fact]
    public void NewFile()
    {
        AssertHighlighter("diff",
"""
diff --git a/new.txt b/new.txt
new file mode 100644
index 0000000..e69de29
--- /dev/null
+++ b/new.txt
@@ -0,0 +1 @@
+content
""",
"""
<span class="hljs-comment">diff --git a/new.txt b/new.txt</span>
new file mode 100644
<span class="hljs-comment">index 0000000..e69de29</span>
<span class="hljs-comment">--- /dev/null</span>
<span class="hljs-comment">+++ b/new.txt</span>
<span class="hljs-meta">@@ -0,0 +1 @@</span>
<span class="hljs-addition">+content</span>
""");
    }

    [Fact]
    public void BinaryAndRename()
    {
        AssertHighlighter("diff",
"""
diff --git a/old.png b/new.png
similarity index 100%
rename from old.png
rename to new.png
Binary files a/x.bin and b/x.bin differ
""",
"""
<span class="hljs-comment">diff --git a/old.png b/new.png</span>
similarity index 100%
rename from old.png
rename to new.png
Binary files a/x.bin and b/x.bin differ
""");
    }

    [Fact]
    public void NoNewline()
    {
        AssertHighlighter("diff",
"""
-last
\ No newline at end of file
+last
\ No newline at end of file
""",
"""
<span class="hljs-deletion">-last</span>
\ No newline at end of file
<span class="hljs-addition">+last</span>
\ No newline at end of file
""");
    }

    [Fact]
    public void MarkersInsideLine()
    {
        AssertHighlighter("diff",
"""
context with --- dashes
context with +++ plus
context === equals ===
 Index: not at start
 index lowercase
""",
"""
context with --- dashes
context with +++ plus
context <span class="hljs-comment">=== equals ===</span>
 <span class="hljs-comment">Index: not at start</span>
 index lowercase
""");
    }

    [Fact]
    public void EmptyChanges()
    {
        AssertHighlighter("diff",
"""
+
-
!
""",
"""
<span class="hljs-addition">+</span>
<span class="hljs-deletion">-</span>
<span class="hljs-addition">!</span>
""");
    }

    [Fact]
    public void HtmlEscaping()
    {
        AssertHighlighter("diff",
"""
-<div class="a">&amp;</div>
+<div class='b'>&lt;</div>
""",
"""
<span class="hljs-deletion">-&lt;div class=&quot;a&quot;&gt;&amp;amp;&lt;/div&gt;</span>
<span class="hljs-addition">+&lt;div class=&#x27;b&#x27;&gt;&amp;lt;&lt;/div&gt;</span>
""");
    }
}
