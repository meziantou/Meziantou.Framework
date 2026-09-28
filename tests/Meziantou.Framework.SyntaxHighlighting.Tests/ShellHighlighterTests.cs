namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class ShellHighlighterTests
{
    [Fact]
    public void DollarPrompt()
    {
        AssertHighlighter("shell",
"""
$ echo hello
hello
""",
"""
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">echo</span> hello</span>
hello
""");
    }

    [Fact]
    public void CommandWithOutput()
    {
        AssertHighlighter("shell",
"""
$ ls -la /tmp
total 8
drwxr-xr-x  2 root root 4096 Jan  1 00:00 .
drwxr-xr-x 20 root root 4096 Jan  1 00:00 ..
""",
"""
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">ls</span> -la /tmp</span>
total 8
drwxr-xr-x  2 root root 4096 Jan  1 00:00 .
drwxr-xr-x 20 root root 4096 Jan  1 00:00 ..
""");
    }

    [Fact]
    public void RootPrompt()
    {
        AssertHighlighter("shell",
"""
# apt-get install -y curl
Reading package lists... Done
""",
"""
<span class="hljs-meta prompt_"># </span><span class="language-bash">apt-get install -y curl</span>
Reading package lists... Done
""");
    }

    [Fact]
    public void LineContinuation()
    {
        AssertHighlighter("shell",
"""
$ docker run \
    --rm \
    -it ubuntu:22.04 bash
root@abc:/#

""",
"""
<span class="hljs-meta prompt_">$ </span><span class="language-bash">docker run \
    --<span class="hljs-built_in">rm</span> \
    -it ubuntu:22.04 bash</span>
<span class="hljs-meta prompt_">root@abc:/#</span><span class="language-bash">
</span>
""");
    }

    [Fact]
    public void ContinuationPrompt()
    {
        AssertHighlighter("shell",
"""
$ for i in 1 2 3; do
> echo "$i"
> done
1
2
3
""",
"""
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-keyword">for</span> i <span class="hljs-keyword">in</span> 1 2 3; <span class="hljs-keyword">do</span></span>
<span class="hljs-meta prompt_">&gt; </span><span class="language-bash"><span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">$i</span>&quot;</span></span>
<span class="hljs-meta prompt_">&gt; </span><span class="language-bash"><span class="hljs-keyword">done</span></span>
1
2
3
""");
    }

    [Fact]
    public void UserAtHostPrompt()
    {
        AssertHighlighter("shell",
"""
user@host:~$ git status
On branch main
user@host:~/src/project$ make build
""",
"""
<span class="hljs-meta prompt_">user@host:~$ </span><span class="language-bash">git status</span>
On branch main
<span class="hljs-meta prompt_">user@host:~/src/project$ </span><span class="language-bash">make build</span>
""");
    }

    [Fact]
    public void UserAtHostRootPrompt()
    {
        AssertHighlighter("shell",
"""
root@server:/var/log# tail -f syslog
""",
"""
<span class="hljs-meta prompt_">root@server:/var/log# </span><span class="language-bash"><span class="hljs-built_in">tail</span> -f syslog</span>
""");
    }

    [Fact]
    public void DottedHostName()
    {
        AssertHighlighter("shell",
"""
me@my-host.local:~/work$ ls
""",
"""
<span class="hljs-meta prompt_">me@my-host.local:~/work$ </span><span class="language-bash"><span class="hljs-built_in">ls</span></span>
""");
    }

    [Fact]
    public void DirectoryPrompt()
    {
        AssertHighlighter("shell",
"""
~/src$ npm install
added 1 package
""",
"""
<span class="hljs-meta prompt_">~/src$ </span><span class="language-bash">npm install</span>
added 1 package
""");
    }

    [Fact]
    public void PercentPrompt()
    {
        AssertHighlighter("shell",
"""
% ls
file.txt
""",
"""
<span class="hljs-meta prompt_">% </span><span class="language-bash"><span class="hljs-built_in">ls</span></span>
file.txt
""");
    }

    [Fact]
    public void PromptWithSpacesIsOutput()
    {
        AssertHighlighter("shell",
"""
(venv) $ pip install requests
Collecting requests
""",
"""
(venv) $ pip install requests
Collecting requests
""");
    }

    [Fact]
    public void BracketedPromptWithSpaceIsOutput()
    {
        AssertHighlighter("shell",
"""
[user@host ~]$ whoami
user
""",
"""
[user@host ~]$ whoami
user
""");
    }

    [Fact]
    public void PromptWithoutSpace()
    {
        AssertHighlighter("shell",
"""
$echo x
$

""",
"""
<span class="hljs-meta prompt_">$</span><span class="language-bash"><span class="hljs-built_in">echo</span> x</span>
<span class="hljs-meta prompt_">$</span><span class="language-bash">
</span>
""");
    }

    [Fact]
    public void IndentedPrompt()
    {
        AssertHighlighter("shell",
"""
  $ echo indented
    $ echo four
""",
"""
<span class="hljs-meta prompt_">  $ </span><span class="language-bash"><span class="hljs-built_in">echo</span> indented</span>
    $ echo four
""");
    }

    [Fact]
    public void Variables()
    {
        AssertHighlighter("shell",
"""
$ export PATH="$HOME/bin:$PATH"
$ echo ${HOME} $USER
/home/user user
""",
"""
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">export</span> PATH=<span class="hljs-string">&quot;<span class="hljs-variable">$HOME</span>/bin:<span class="hljs-variable">$PATH</span>&quot;</span></span>
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">echo</span> <span class="hljs-variable">${HOME}</span> <span class="hljs-variable">$USER</span></span>
/home/user user
""");
    }

    [Fact]
    public void Quotes()
    {
        AssertHighlighter("shell",
"""
$ echo 'single' "double $x" `date`
single double
""",
"""
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">echo</span> <span class="hljs-string">&#x27;single&#x27;</span> <span class="hljs-string">&quot;double <span class="hljs-variable">$x</span>&quot;</span> `<span class="hljs-built_in">date</span>`</span>
single double
""");
    }

    [Fact]
    public void CommentAfterCommand()
    {
        AssertHighlighter("shell",
"""
$ ls # list files
# this is a root prompt, not a comment
""",
"""
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">ls</span> <span class="hljs-comment"># list files</span></span>
<span class="hljs-meta prompt_"># </span><span class="language-bash">this is a root prompt, not a comment</span>
""");
    }

    [Fact]
    public void Pipes()
    {
        AssertHighlighter("shell",
"""
$ cat file.txt | grep -i error | wc -l
42
$ ls > out.txt 2>&1
""",
"""
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">cat</span> file.txt | grep -i error | <span class="hljs-built_in">wc</span> -l</span>
42
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">ls</span> &gt; out.txt 2&gt;&amp;1</span>
""");
    }

    [Fact]
    public void RedirectionIsNotAPrompt()
    {
        AssertHighlighter("shell",
"""
echo /path/to/home > t.exe
""",
"""
echo /path/to/home &gt; t.exe
""");
    }

    [Fact]
    public void Substitutions()
    {
        AssertHighlighter("shell",
"""
$ echo $(whoami) $((1 + 2))
user 3
""",
"""
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">echo</span> $(<span class="hljs-built_in">whoami</span>) $((<span class="hljs-number">1</span> + <span class="hljs-number">2</span>))</span>
user 3
""");
    }

    [Fact]
    public void Heredoc()
    {
        AssertHighlighter("shell",
"""
$ cat <<EOF
> hello
> EOF
hello
""",
"""
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">cat</span> &lt;&lt;<span class="hljs-string">EOF</span></span>
<span class="hljs-meta prompt_">&gt; </span><span class="language-bash"><span class="hljs-string">hello</span></span>
<span class="hljs-meta prompt_">&gt; </span><span class="language-bash"><span class="hljs-string">EOF</span></span>
hello
""");
    }

    [Fact]
    public void EmptyPrompt()
    {
        AssertHighlighter("shell",
"$ \n$\n",
"""
<span class="hljs-meta prompt_">$ </span>
<span class="hljs-meta prompt_">$</span><span class="language-bash">
</span>
""");
    }

    [Fact]
    public void EmptyPromptFollowedByPrompt()
    {
        AssertHighlighter("shell",
"$ \n$ ls\nfile\n$\n$ pwd",
"""
<span class="hljs-meta prompt_">$ </span>
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">ls</span></span>
file
<span class="hljs-meta prompt_">$</span>
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">pwd</span></span>
""");
    }

    [Fact]
    public void TrailingSpaces()
    {
        AssertHighlighter("shell",
"$ echo hi   \nhi",
"<span class=\"hljs-meta prompt_\">$ </span><span class=\"language-bash\"><span class=\"hljs-built_in\">echo</span> hi</span>   \nhi");
    }

    [Fact]
    public void TrailingBackslash()
    {
        AssertHighlighter("shell",
"""
$ echo \

""",
"""
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">echo</span> \
</span>
""");
    }

    [Fact]
    public void BackslashFollowedBySpaceIsNotAContinuation()
    {
        AssertHighlighter("shell",
"$ echo a \\ \nb",
"""
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">echo</span> a \ </span>
b
""");
    }

    [Fact]
    public void OutputContainingDollar()
    {
        AssertHighlighter("shell",
"""
Total: $5
price is 10$
""",
"""
Total: $5
price is 10$
""");
    }

    [Fact]
    public void Session()
    {
        AssertHighlighter("shell",
"""
$ cd project
$ git log --oneline -3
abc1234 Fix bug
def5678 Add feature
$ git push origin main
""",
"""
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">cd</span> project</span>
<span class="hljs-meta prompt_">$ </span><span class="language-bash">git <span class="hljs-built_in">log</span> --oneline -3</span>
abc1234 Fix bug
def5678 Add feature
<span class="hljs-meta prompt_">$ </span><span class="language-bash">git push origin main</span>
""");
    }

    [Fact]
    public void OutputLineStartingWithGreaterThan()
    {
        AssertHighlighter("shell",
"""
> hello
>

""",
"""
<span class="hljs-meta prompt_">&gt; </span><span class="language-bash">hello</span>
<span class="hljs-meta prompt_">&gt;</span><span class="language-bash">
</span>
""");
    }

    [Fact]
    public void WindowsPromptIsNotSupported()
    {
        AssertHighlighter("shell",
"""
C:\Users\me> dir
""",
"""
C:\Users\me&gt; dir
""");
    }

    [Fact]
    public void BlankLinesBetweenPrompts()
    {
        AssertHighlighter("shell",
"""
$ ls


$ pwd
/home
""",
"""
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">ls</span></span>


<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">pwd</span></span>
/home
""");
    }

    [Fact]
    public void Keywords()
    {
        AssertHighlighter("shell",
"""
$ if [ -f file ]; then echo yes; fi
$ sudo systemctl restart nginx
""",
"""
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-keyword">if</span> [ -f file ]; <span class="hljs-keyword">then</span> <span class="hljs-built_in">echo</span> <span class="hljs-built_in">yes</span>; <span class="hljs-keyword">fi</span></span>
<span class="hljs-meta prompt_">$ </span><span class="language-bash"><span class="hljs-built_in">sudo</span> systemctl restart nginx</span>
""");
    }

    [Fact]
    public void TabAfterPrompt()
    {
        AssertHighlighter("shell",
"""
$	echo tab
""",
"""
<span class="hljs-meta prompt_">$</span><span class="language-bash">	<span class="hljs-built_in">echo</span> tab</span>
""");
    }

    [Fact]
    public void CrLf()
    {
        AssertHighlighter("shell",
"$ echo a\r\nb\r\n$ echo c\r",
"<span class=\"hljs-meta prompt_\">$ </span><span class=\"language-bash\"><span class=\"hljs-built_in\">echo</span> a</span>\r\nb\r\n<span class=\"hljs-meta prompt_\">$ </span><span class=\"language-bash\"><span class=\"hljs-built_in\">echo</span> c</span>\r");
    }

    [Fact]
    public void DollarInsideLine()
    {
        AssertHighlighter("shell",
"""
text $ not a prompt
""",
"""
text $ not a prompt
""");
    }

    [Fact]
    public void MultiLineOutput()
    {
        AssertHighlighter("shell",
"""
$ dotnet --info
.NET SDK:
 Version:   10.0.100
 Commit:    abc

Runtime Environment:
 OS Name:     Mac OS X
""",
"""
<span class="hljs-meta prompt_">$ </span><span class="language-bash">dotnet --info</span>
.NET SDK:
 Version:   10.0.100
 Commit:    abc

Runtime Environment:
 OS Name:     Mac OS X
""");
    }
}
