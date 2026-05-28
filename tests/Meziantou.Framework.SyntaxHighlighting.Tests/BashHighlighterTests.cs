namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class BashHighlighterTests
{

    [Fact]
    public void Command_Echo()
    {
        AssertHighlighter("bash",
"""
echo hello
""",
"""
<span class="hljs-built_in">echo</span> hello
""");
    }

    [Fact]
    public void Command_EchoQuoted()
    {
        AssertHighlighter("bash",
"""
echo "hello world"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;hello world&quot;</span>
""");
    }

    [Fact]
    public void Command_EchoSingle()
    {
        AssertHighlighter("bash",
"""
echo 'literal $var'
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&#x27;literal $var&#x27;</span>
""");
    }

    [Fact]
    public void Command_Cd()
    {
        AssertHighlighter("bash",
"""
cd /tmp
""",
"""
<span class="hljs-built_in">cd</span> /tmp
""");
    }

    [Fact]
    public void Command_Ls()
    {
        AssertHighlighter("bash",
"""
ls -la /var/log
""",
"""
<span class="hljs-built_in">ls</span> -la /var/log
""");
    }

    [Fact]
    public void Command_Pwd()
    {
        AssertHighlighter("bash",
"""
pwd
""",
"""
<span class="hljs-built_in">pwd</span>
""");
    }

    [Fact]
    public void Command_Mkdir()
    {
        AssertHighlighter("bash",
"""
mkdir -p /tmp/data/cache
""",
"""
<span class="hljs-built_in">mkdir</span> -p /tmp/data/cache
""");
    }

    [Fact]
    public void Command_Rm()
    {
        AssertHighlighter("bash",
"""
rm -rf /tmp/work
""",
"""
<span class="hljs-built_in">rm</span> -rf /tmp/work
""");
    }

    [Fact]
    public void Command_Cp()
    {
        AssertHighlighter("bash",
"""
cp -R src/ dest/
""",
"""
<span class="hljs-built_in">cp</span> -R src/ dest/
""");
    }

    [Fact]
    public void Command_Mv()
    {
        AssertHighlighter("bash",
"""
mv old.txt new.txt
""",
"""
<span class="hljs-built_in">mv</span> old.txt new.txt
""");
    }

    [Fact]
    public void Command_Grep()
    {
        AssertHighlighter("bash",
"""
grep -i "error" /var/log/syslog
""",
"""
grep -i <span class="hljs-string">&quot;error&quot;</span> /var/log/syslog
""");
    }

    [Fact]
    public void Command_Find()
    {
        AssertHighlighter("bash",
"""
find . -name "*.log" -mtime +7
""",
"""
find . -name <span class="hljs-string">&quot;*.log&quot;</span> -mtime +7
""");
    }

    [Fact]
    public void Command_Sed()
    {
        AssertHighlighter("bash",
"""
sed -i 's/foo/bar/g' file.txt
""",
"""
sed -i <span class="hljs-string">&#x27;s/foo/bar/g&#x27;</span> file.txt
""");
    }

    [Fact]
    public void Command_Awk()
    {
        AssertHighlighter("bash",
"""
awk '{ print $2 }' data.txt
""",
"""
awk <span class="hljs-string">&#x27;{ print $2 }&#x27;</span> data.txt
""");
    }

    [Fact]
    public void Command_Curl()
    {
        AssertHighlighter("bash",
"""
curl -fsSL https://example.com/install.sh
""",
"""
curl -fsSL https://example.com/install.sh
""");
    }

    [Fact]
    public void Command_Wget()
    {
        AssertHighlighter("bash",
"""
wget -O out.tar.gz https://example.com/file.tar.gz
""",
"""
wget -O out.tar.gz https://example.com/file.tar.gz
""");
    }

    [Fact]
    public void Command_Tar()
    {
        AssertHighlighter("bash",
"""
tar -xzvf archive.tar.gz -C /tmp
""",
"""
tar -xzvf archive.tar.gz -C /tmp
""");
    }

    [Fact]
    public void Command_Chmod()
    {
        AssertHighlighter("bash",
"""
chmod +x install.sh
""",
"""
<span class="hljs-built_in">chmod</span> +x install.sh
""");
    }

    [Fact]
    public void Command_Chown()
    {
        AssertHighlighter("bash",
"""
chown -R user:group /var/app
""",
"""
<span class="hljs-built_in">chown</span> -R user:group /var/app
""");
    }

    [Fact]
    public void Builtin_Read()
    {
        AssertHighlighter("bash",
"""
read -r line
""",
"""
<span class="hljs-built_in">read</span> -r line
""");
    }

    [Fact]
    public void Builtin_Source()
    {
        AssertHighlighter("bash",
"""
source ~/.bashrc
""",
"""
<span class="hljs-built_in">source</span> ~/.bashrc
""");
    }

    [Fact]
    public void Builtin_Dot()
    {
        AssertHighlighter("bash",
"""
. ./common.sh
""",
"""
. ./common.sh
""");
    }

    [Fact]
    public void Builtin_Export()
    {
        AssertHighlighter("bash",
"""
export PATH="$HOME/bin:$PATH"
""",
"""
<span class="hljs-built_in">export</span> PATH=<span class="hljs-string">&quot;<span class="hljs-variable">$HOME</span>/bin:<span class="hljs-variable">$PATH</span>&quot;</span>
""");
    }

    [Fact]
    public void Builtin_Local()
    {
        AssertHighlighter("bash",
"""
local result
""",
"""
<span class="hljs-built_in">local</span> result
""");
    }

    [Fact]
    public void Builtin_Declare()
    {
        AssertHighlighter("bash",
"""
declare -i counter=0
""",
"""
<span class="hljs-built_in">declare</span> -i counter=0
""");
    }

    [Fact]
    public void Builtin_Readonly()
    {
        AssertHighlighter("bash",
"""
readonly CONFIG_PATH=/etc/app
""",
"""
<span class="hljs-built_in">readonly</span> CONFIG_PATH=/etc/app
""");
    }

    [Fact]
    public void Builtin_Unset()
    {
        AssertHighlighter("bash",
"""
unset MY_VAR
""",
"""
<span class="hljs-built_in">unset</span> MY_VAR
""");
    }

    [Fact]
    public void Builtin_Shift()
    {
        AssertHighlighter("bash",
"""
shift 2
""",
"""
<span class="hljs-built_in">shift</span> 2
""");
    }

    [Fact]
    public void Builtin_Set()
    {
        AssertHighlighter("bash",
"""
set -euo pipefail
""",
"""
<span class="hljs-built_in">set</span> -euo pipefail
""");
    }

    [Fact]
    public void Builtin_Shopt()
    {
        AssertHighlighter("bash",
"""
shopt -s nullglob
""",
"""
<span class="hljs-built_in">shopt</span> -s nullglob
""");
    }

    [Fact]
    public void Builtin_Trap()
    {
        AssertHighlighter("bash",
"""
trap cleanup EXIT INT TERM
""",
"""
<span class="hljs-built_in">trap</span> cleanup EXIT INT TERM
""");
    }

    [Fact]
    public void Builtin_Exec()
    {
        AssertHighlighter("bash",
"""
exec > /tmp/log.txt 2>&1
""",
"""
<span class="hljs-built_in">exec</span> &gt; /tmp/log.txt 2&gt;&amp;1
""");
    }

    [Fact]
    public void Builtin_Wait()
    {
        AssertHighlighter("bash",
"""
wait $pid
""",
"""
<span class="hljs-built_in">wait</span> <span class="hljs-variable">$pid</span>
""");
    }

    [Fact]
    public void Builtin_Kill()
    {
        AssertHighlighter("bash",
"""
kill -TERM $pid
""",
"""
<span class="hljs-built_in">kill</span> -TERM <span class="hljs-variable">$pid</span>
""");
    }

    [Fact]
    public void Builtin_Printf()
    {
        AssertHighlighter("bash",
"""
printf "%-10s %d\n" "items" 42
""",
"""
<span class="hljs-built_in">printf</span> <span class="hljs-string">&quot;%-10s %d\n&quot;</span> <span class="hljs-string">&quot;items&quot;</span> 42
""");
    }

    [Fact]
    public void Builtin_Type()
    {
        AssertHighlighter("bash",
"""
type -a python
""",
"""
<span class="hljs-built_in">type</span> -a python
""");
    }

    [Fact]
    public void Builtin_Command()
    {
        AssertHighlighter("bash",
"""
command -v git
""",
"""
<span class="hljs-built_in">command</span> -v git
""");
    }

    [Fact]
    public void Variable_Assign()
    {
        AssertHighlighter("bash",
"""
name="alice"
""",
"""
name=<span class="hljs-string">&quot;alice&quot;</span>
""");
    }

    [Fact]
    public void Variable_Numeric()
    {
        AssertHighlighter("bash",
"""
count=42
""",
"""
count=42
""");
    }

    [Fact]
    public void Variable_NoQuotes()
    {
        AssertHighlighter("bash",
"""
name=alice
""",
"""
name=alice
""");
    }

    [Fact]
    public void Variable_Expansion()
    {
        AssertHighlighter("bash",
"""
echo "$name"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">$name</span>&quot;</span>
""");
    }

    [Fact]
    public void Variable_BracedExpansion()
    {
        AssertHighlighter("bash",
"""
echo "${name}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${name}</span>&quot;</span>
""");
    }

    [Fact]
    public void Variable_Special()
    {
        AssertHighlighter("bash",
"""
echo "args: $@ count: $#"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;args: <span class="hljs-variable">$@</span> count: <span class="hljs-variable">$#</span>&quot;</span>
""");
    }

    [Fact]
    public void Variable_ExitStatus()
    {
        AssertHighlighter("bash",
"""
echo "last status: $?"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;last status: $?&quot;</span>
""");
    }

    [Fact]
    public void Variable_Pid()
    {
        AssertHighlighter("bash",
"""
echo "pid: $$"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;pid: $$&quot;</span>
""");
    }

    [Fact]
    public void Variable_BgPid()
    {
        AssertHighlighter("bash",
"""
echo "bg pid: $!"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;bg pid: $!&quot;</span>
""");
    }

    [Fact]
    public void Variable_PositionalArgs()
    {
        AssertHighlighter("bash",
"""
echo "first: $1, second: $2"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;first: <span class="hljs-variable">$1</span>, second: <span class="hljs-variable">$2</span>&quot;</span>
""");
    }

    [Fact]
    public void Variable_EnvHome()
    {
        AssertHighlighter("bash",
"""
cd "$HOME/projects"
""",
"""
<span class="hljs-built_in">cd</span> <span class="hljs-string">&quot;<span class="hljs-variable">$HOME</span>/projects&quot;</span>
""");
    }

    [Fact]
    public void Variable_EnvPath()
    {
        AssertHighlighter("bash",
"""
export PATH="/usr/local/bin:$PATH"
""",
"""
<span class="hljs-built_in">export</span> PATH=<span class="hljs-string">&quot;/usr/local/bin:<span class="hljs-variable">$PATH</span>&quot;</span>
""");
    }

    [Fact]
    public void ParameterExpansion_Default()
    {
        AssertHighlighter("bash",
"""
echo "${name:-anonymous}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${name:-anonymous}</span>&quot;</span>
""");
    }

    [Fact]
    public void ParameterExpansion_AssignDefault()
    {
        AssertHighlighter("bash",
"""
echo "${name:=anonymous}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${name:=anonymous}</span>&quot;</span>
""");
    }

    [Fact]
    public void ParameterExpansion_ErrorIfUnset()
    {
        AssertHighlighter("bash",
"""
echo "${name:?must be set}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${name:?must be set}</span>&quot;</span>
""");
    }

    [Fact]
    public void ParameterExpansion_AlternateValue()
    {
        AssertHighlighter("bash",
"""
echo "${flag:+enabled}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${flag:+enabled}</span>&quot;</span>
""");
    }

    [Fact]
    public void ParameterExpansion_Length()
    {
        AssertHighlighter("bash",
"""
echo "${#name}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${#name}</span>&quot;</span>
""");
    }

    [Fact]
    public void ParameterExpansion_Substring()
    {
        AssertHighlighter("bash",
"""
echo "${name:0:3}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${name:0:3}</span>&quot;</span>
""");
    }

    [Fact]
    public void ParameterExpansion_RemovePrefix()
    {
        AssertHighlighter("bash",
"""
echo "${path#*/}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${path#*/}</span>&quot;</span>
""");
    }

    [Fact]
    public void ParameterExpansion_RemoveSuffix()
    {
        AssertHighlighter("bash",
"""
echo "${file%.txt}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${file%.txt}</span>&quot;</span>
""");
    }

    [Fact]
    public void ParameterExpansion_Replace()
    {
        AssertHighlighter("bash",
"""
echo "${name/alice/bob}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${name/alice/bob}</span>&quot;</span>
""");
    }

    [Fact]
    public void ParameterExpansion_ReplaceAll()
    {
        AssertHighlighter("bash",
"""
echo "${path//\//_}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${path//\//_}</span>&quot;</span>
""");
    }

    [Fact]
    public void ParameterExpansion_Uppercase()
    {
        AssertHighlighter("bash",
"""
echo "${name^^}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${name^^}</span>&quot;</span>
""");
    }

    [Fact]
    public void ParameterExpansion_Lowercase()
    {
        AssertHighlighter("bash",
"""
echo "${NAME,,}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${NAME,,}</span>&quot;</span>
""");
    }

    [Fact]
    public void ParameterExpansion_Indirection()
    {
        AssertHighlighter("bash",
"""
echo "${!varname}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${!varname}</span>&quot;</span>
""");
    }

    [Fact]
    public void CommandSubstitution_Dollar()
    {
        AssertHighlighter("bash",
"""
now=$(date +%Y-%m-%d)
""",
"""
now=$(<span class="hljs-built_in">date</span> +%Y-%m-%d)
""");
    }

    [Fact]
    public void CommandSubstitution_Backticks()
    {
        AssertHighlighter("bash",
"""
now=`date +%Y-%m-%d`
""",
"""
now=`<span class="hljs-built_in">date</span> +%Y-%m-%d`
""");
    }

    [Fact]
    public void CommandSubstitution_Nested()
    {
        AssertHighlighter("bash",
"""
count=$(ls -1 $(pwd) | wc -l)
""",
"""
count=$(<span class="hljs-built_in">ls</span> -1 $(<span class="hljs-built_in">pwd</span>) | <span class="hljs-built_in">wc</span> -l)
""");
    }

    [Fact]
    public void CommandSubstitution_InEcho()
    {
        AssertHighlighter("bash",
"""
echo "Today is $(date)"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;Today is <span class="hljs-subst">$(date)</span>&quot;</span>
""");
    }

    [Fact]
    public void Arithmetic_Expansion()
    {
        AssertHighlighter("bash",
"""
echo $((1 + 2))
""",
"""
<span class="hljs-built_in">echo</span> $((<span class="hljs-number">1</span> + <span class="hljs-number">2</span>))
""");
    }

    [Fact]
    public void Arithmetic_AssignInside()
    {
        AssertHighlighter("bash",
"""
echo $((x = 5, x * 2))
""",
"""
<span class="hljs-built_in">echo</span> $((x = <span class="hljs-number">5</span>, x * <span class="hljs-number">2</span>))
""");
    }

    [Fact]
    public void Arithmetic_LetCommand()
    {
        AssertHighlighter("bash",
"""
let "counter = counter + 1"
""",
"""
<span class="hljs-built_in">let</span> <span class="hljs-string">&quot;counter = counter + 1&quot;</span>
""");
    }

    [Fact]
    public void Arithmetic_DoubleParen()
    {
        AssertHighlighter("bash",
"""
(( counter++ ))
""",
"""
(( counter++ ))
""");
    }

    [Fact]
    public void Arithmetic_Comparison()
    {
        AssertHighlighter("bash",
"""
(( a > b ))
""",
"""
(( a &gt; b ))
""");
    }

    [Fact]
    public void Arithmetic_PowerOp()
    {
        AssertHighlighter("bash",
"""
echo $((2 ** 10))
""",
"""
<span class="hljs-built_in">echo</span> $((<span class="hljs-number">2</span> ** <span class="hljs-number">10</span>))
""");
    }

    [Fact]
    public void Redirection_Stdout()
    {
        AssertHighlighter("bash",
"""
echo hello > out.txt
""",
"""
<span class="hljs-built_in">echo</span> hello &gt; out.txt
""");
    }

    [Fact]
    public void Redirection_Append()
    {
        AssertHighlighter("bash",
"""
echo hello >> out.txt
""",
"""
<span class="hljs-built_in">echo</span> hello &gt;&gt; out.txt
""");
    }

    [Fact]
    public void Redirection_Stderr()
    {
        AssertHighlighter("bash",
"""
somecmd 2> err.txt
""",
"""
somecmd 2&gt; err.txt
""");
    }

    [Fact]
    public void Redirection_MergeBoth()
    {
        AssertHighlighter("bash",
"""
somecmd > out.txt 2>&1
""",
"""
somecmd &gt; out.txt 2&gt;&amp;1
""");
    }

    [Fact]
    public void Redirection_AllInOne()
    {
        AssertHighlighter("bash",
"""
somecmd &> out.txt
""",
"""
somecmd &amp;&gt; out.txt
""");
    }

    [Fact]
    public void Redirection_StdinFromFile()
    {
        AssertHighlighter("bash",
"""
sort < input.txt
""",
"""
<span class="hljs-built_in">sort</span> &lt; input.txt
""");
    }

    [Fact]
    public void Redirection_CloseStdin()
    {
        AssertHighlighter("bash",
"""
somecmd <&-
""",
"""
somecmd &lt;&amp;-
""");
    }

    [Fact]
    public void Redirection_NullDevice()
    {
        AssertHighlighter("bash",
"""
somecmd > /dev/null 2>&1
""",
"""
somecmd &gt; /dev/null 2&gt;&amp;1
""");
    }

    [Fact]
    public void Pipeline_Simple()
    {
        AssertHighlighter("bash",
"""
ps aux | grep nginx
""",
"""
ps aux | grep nginx
""");
    }

    [Fact]
    public void Pipeline_Multi()
    {
        AssertHighlighter("bash",
"""
find . -name "*.log" | xargs wc -l | sort -n
""",
"""
find . -name <span class="hljs-string">&quot;*.log&quot;</span> | xargs <span class="hljs-built_in">wc</span> -l | <span class="hljs-built_in">sort</span> -n
""");
    }

    [Fact]
    public void Pipeline_PipeStderr()
    {
        AssertHighlighter("bash",
"""
somecmd |& tee log.txt
""",
"""
somecmd |&amp; <span class="hljs-built_in">tee</span> log.txt
""");
    }

    [Fact]
    public void ProcessSubstitution_Read()
    {
        AssertHighlighter("bash",
"""
diff <(sort a.txt) <(sort b.txt)
""",
"""
diff &lt;(<span class="hljs-built_in">sort</span> a.txt) &lt;(<span class="hljs-built_in">sort</span> b.txt)
""");
    }

    [Fact]
    public void ProcessSubstitution_Write()
    {
        AssertHighlighter("bash",
"""
somecmd > >(gzip > out.gz)
""",
"""
somecmd &gt; &gt;(gzip &gt; out.gz)
""");
    }

    [Fact]
    public void String_DoubleQuoted()
    {
        AssertHighlighter("bash",
"""
msg="hello world"
""",
"""
msg=<span class="hljs-string">&quot;hello world&quot;</span>
""");
    }

    [Fact]
    public void String_SingleQuoted()
    {
        AssertHighlighter("bash",
"""
msg='no \$expansion here'
""",
"""
msg=<span class="hljs-string">&#x27;no \$expansion here&#x27;</span>
""");
    }

    [Fact]
    public void String_AnsiCEscape()
    {
        AssertHighlighter("bash",
"""
msg=$'line1\nline2'
""",
"""
msg=$<span class="hljs-string">&#x27;line1\nline2&#x27;</span>
""");
    }

    [Fact]
    public void String_I18nString()
    {
        AssertHighlighter("bash",
"""
msg=$"Localized text"
""",
"""
msg=$<span class="hljs-string">&quot;Localized text&quot;</span>
""");
    }

    [Fact]
    public void String_Concat()
    {
        AssertHighlighter("bash",
"""
msg="hello $name and $other"
""",
"""
msg=<span class="hljs-string">&quot;hello <span class="hljs-variable">$name</span> and <span class="hljs-variable">$other</span>&quot;</span>
""");
    }

    [Fact]
    public void String_EscapedDouble()
    {
        AssertHighlighter("bash",
"""
msg="She said \"hi\""
""",
"""
msg=<span class="hljs-string">&quot;She said \&quot;hi\&quot;&quot;</span>
""");
    }

    [Fact]
    public void HeredocHerestring_HereDoc()
    {
        AssertHighlighter("bash",
"""
cat << EOF
first line
second line
EOF
""",
"""
<span class="hljs-built_in">cat</span> &lt;&lt; <span class="hljs-string">EOF
first line
second line
EOF</span>
""");
    }

    [Fact]
    public void HeredocHerestring_HereDocIndent()
    {
        AssertHighlighter("bash",
"""
	cat <<- EOF
		indented heredoc
	EOF
""",
"""
	<span class="hljs-built_in">cat</span> &lt;&lt;- <span class="hljs-string">EOF
		indented heredoc
	EOF</span>
""");
    }

    [Fact]
    public void HeredocHerestring_HereDocQuoted()
    {
        AssertHighlighter("bash",
"""
cat << 'EOF'
no $expansion
EOF
""",
"""
<span class="hljs-built_in">cat</span> &lt;&lt; <span class="hljs-string">&#x27;EOF&#x27;</span>
no <span class="hljs-variable">$expansion</span>
EOF
""");
    }

    [Fact]
    public void HeredocHerestring_HereString()
    {
        AssertHighlighter("bash",
"""
grep alice <<< "$users"
""",
"""
grep alice &lt;&lt;&lt; <span class="hljs-string">&quot;<span class="hljs-variable">$users</span>&quot;</span>
""");
    }

    [Fact]
    public void HeredocHerestring_HereDocStdin()
    {
        AssertHighlighter("bash",
"""
mysql <<EOF
SELECT * FROM users;
EOF
""",
"""
mysql &lt;&lt;<span class="hljs-string">EOF
SELECT * FROM users;
EOF</span>
""");
    }

    [Fact]
    public void Array_Declare()
    {
        AssertHighlighter("bash",
"""
arr=(one two three)
""",
"""
arr=(one two three)
""");
    }

    [Fact]
    public void Array_Indexed()
    {
        AssertHighlighter("bash",
"""
echo "${arr[0]}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${arr[0]}</span>&quot;</span>
""");
    }

    [Fact]
    public void Array_AllElements()
    {
        AssertHighlighter("bash",
"""
echo "${arr[@]}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${arr[@]}</span>&quot;</span>
""");
    }

    [Fact]
    public void Array_AllAsString()
    {
        AssertHighlighter("bash",
"""
echo "${arr[*]}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${arr[*]}</span>&quot;</span>
""");
    }

    [Fact]
    public void Array_Length()
    {
        AssertHighlighter("bash",
"""
echo "${#arr[@]}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${#arr[@]}</span>&quot;</span>
""");
    }

    [Fact]
    public void Array_Append()
    {
        AssertHighlighter("bash",
"""
arr+=(four)
""",
"""
arr+=(four)
""");
    }

    [Fact]
    public void Array_Slice()
    {
        AssertHighlighter("bash",
"""
echo "${arr[@]:1:2}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${arr[@]:1:2}</span>&quot;</span>
""");
    }

    [Fact]
    public void Array_Associative()
    {
        AssertHighlighter("bash",
"""
declare -A user=([name]=alice [age]=30)
""",
"""
<span class="hljs-built_in">declare</span> -A user=([name]=alice [age]=30)
""");
    }

    [Fact]
    public void Array_AssocAccess()
    {
        AssertHighlighter("bash",
"""
echo "${user[name]}"
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">${user[name]}</span>&quot;</span>
""");
    }

    [Fact]
    public void Array_AssocKeys()
    {
        AssertHighlighter("bash",
"""
for k in "${!user[@]}"; do echo "$k"; done
""",
"""
<span class="hljs-keyword">for</span> k <span class="hljs-keyword">in</span> <span class="hljs-string">&quot;<span class="hljs-variable">${!user[@]}</span>&quot;</span>; <span class="hljs-keyword">do</span> <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">$k</span>&quot;</span>; <span class="hljs-keyword">done</span>
""");
    }

    [Fact]
    public void ControlFlow_If()
    {
        AssertHighlighter("bash",
"""
if [ "$x" -gt 0 ]; then
  echo positive
fi
""",
"""
<span class="hljs-keyword">if</span> [ <span class="hljs-string">&quot;<span class="hljs-variable">$x</span>&quot;</span> -gt 0 ]; <span class="hljs-keyword">then</span>
  <span class="hljs-built_in">echo</span> positive
<span class="hljs-keyword">fi</span>
""");
    }

    [Fact]
    public void ControlFlow_IfElse()
    {
        AssertHighlighter("bash",
"""
if [ -f "$file" ]; then
  echo exists
else
  echo missing
fi
""",
"""
<span class="hljs-keyword">if</span> [ -f <span class="hljs-string">&quot;<span class="hljs-variable">$file</span>&quot;</span> ]; <span class="hljs-keyword">then</span>
  <span class="hljs-built_in">echo</span> exists
<span class="hljs-keyword">else</span>
  <span class="hljs-built_in">echo</span> missing
<span class="hljs-keyword">fi</span>
""");
    }

    [Fact]
    public void ControlFlow_IfElif()
    {
        AssertHighlighter("bash",
"""
if [ "$x" -lt 0 ]; then
  echo negative
elif [ "$x" -eq 0 ]; then
  echo zero
else
  echo positive
fi
""",
"""
<span class="hljs-keyword">if</span> [ <span class="hljs-string">&quot;<span class="hljs-variable">$x</span>&quot;</span> -lt 0 ]; <span class="hljs-keyword">then</span>
  <span class="hljs-built_in">echo</span> negative
<span class="hljs-keyword">elif</span> [ <span class="hljs-string">&quot;<span class="hljs-variable">$x</span>&quot;</span> -eq 0 ]; <span class="hljs-keyword">then</span>
  <span class="hljs-built_in">echo</span> zero
<span class="hljs-keyword">else</span>
  <span class="hljs-built_in">echo</span> positive
<span class="hljs-keyword">fi</span>
""");
    }

    [Fact]
    public void ControlFlow_TestBracketsDouble()
    {
        AssertHighlighter("bash",
"""
if [[ "$name" == "alice" ]]; then echo hi; fi
""",
"""
<span class="hljs-keyword">if</span> [[ <span class="hljs-string">&quot;<span class="hljs-variable">$name</span>&quot;</span> == <span class="hljs-string">&quot;alice&quot;</span> ]]; <span class="hljs-keyword">then</span> <span class="hljs-built_in">echo</span> hi; <span class="hljs-keyword">fi</span>
""");
    }

    [Fact]
    public void ControlFlow_TestRegex()
    {
        AssertHighlighter("bash",
"""
if [[ "$value" =~ ^[0-9]+$ ]]; then echo number; fi
""",
"""
<span class="hljs-keyword">if</span> [[ <span class="hljs-string">&quot;<span class="hljs-variable">$value</span>&quot;</span> =~ ^[0-9]+$ ]]; <span class="hljs-keyword">then</span> <span class="hljs-built_in">echo</span> number; <span class="hljs-keyword">fi</span>
""");
    }

    [Fact]
    public void ControlFlow_TestAnd()
    {
        AssertHighlighter("bash",
"""
if [ -f "$f" ] && [ -r "$f" ]; then cat "$f"; fi
""",
"""
<span class="hljs-keyword">if</span> [ -f <span class="hljs-string">&quot;<span class="hljs-variable">$f</span>&quot;</span> ] &amp;&amp; [ -r <span class="hljs-string">&quot;<span class="hljs-variable">$f</span>&quot;</span> ]; <span class="hljs-keyword">then</span> <span class="hljs-built_in">cat</span> <span class="hljs-string">&quot;<span class="hljs-variable">$f</span>&quot;</span>; <span class="hljs-keyword">fi</span>
""");
    }

    [Fact]
    public void ControlFlow_Case()
    {
        AssertHighlighter("bash",
"""
case "$x" in
  start) echo starting ;;
  stop)  echo stopping ;;
  *)     echo unknown ;;
esac
""",
"""
<span class="hljs-keyword">case</span> <span class="hljs-string">&quot;<span class="hljs-variable">$x</span>&quot;</span> <span class="hljs-keyword">in</span>
  start) <span class="hljs-built_in">echo</span> starting ;;
  stop)  <span class="hljs-built_in">echo</span> stopping ;;
  *)     <span class="hljs-built_in">echo</span> unknown ;;
<span class="hljs-keyword">esac</span>
""");
    }

    [Fact]
    public void ControlFlow_CaseFallthrough()
    {
        AssertHighlighter("bash",
"""
case "$x" in
  a|b) echo "a or b" ;;
  c)   echo "c" ;;
esac
""",
"""
<span class="hljs-keyword">case</span> <span class="hljs-string">&quot;<span class="hljs-variable">$x</span>&quot;</span> <span class="hljs-keyword">in</span>
  a|b) <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;a or b&quot;</span> ;;
  c)   <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;c&quot;</span> ;;
<span class="hljs-keyword">esac</span>
""");
    }

    [Fact]
    public void ControlFlow_For()
    {
        AssertHighlighter("bash",
"""
for f in *.txt; do
  echo "$f"
done
""",
"""
<span class="hljs-keyword">for</span> f <span class="hljs-keyword">in</span> *.txt; <span class="hljs-keyword">do</span>
  <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">$f</span>&quot;</span>
<span class="hljs-keyword">done</span>
""");
    }

    [Fact]
    public void ControlFlow_ForCStyle()
    {
        AssertHighlighter("bash",
"""
for ((i = 0; i < 10; i++)); do
  echo "$i"
done
""",
"""
<span class="hljs-keyword">for</span> ((i = <span class="hljs-number">0</span>; i &lt; <span class="hljs-number">10</span>; i++)); <span class="hljs-keyword">do</span>
  <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">$i</span>&quot;</span>
<span class="hljs-keyword">done</span>
""");
    }

    [Fact]
    public void ControlFlow_ForRange()
    {
        AssertHighlighter("bash",
"""
for i in {1..5}; do
  echo "$i"
done
""",
"""
<span class="hljs-keyword">for</span> i <span class="hljs-keyword">in</span> {1..5}; <span class="hljs-keyword">do</span>
  <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">$i</span>&quot;</span>
<span class="hljs-keyword">done</span>
""");
    }

    [Fact]
    public void ControlFlow_While()
    {
        AssertHighlighter("bash",
"""
while read -r line; do
  echo "$line"
done < input.txt
""",
"""
<span class="hljs-keyword">while</span> <span class="hljs-built_in">read</span> -r line; <span class="hljs-keyword">do</span>
  <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">$line</span>&quot;</span>
<span class="hljs-keyword">done</span> &lt; input.txt
""");
    }

    [Fact]
    public void ControlFlow_Until()
    {
        AssertHighlighter("bash",
"""
until [ "$count" -ge 10 ]; do
  ((count++))
done
""",
"""
<span class="hljs-keyword">until</span> [ <span class="hljs-string">&quot;<span class="hljs-variable">$count</span>&quot;</span> -ge 10 ]; <span class="hljs-keyword">do</span>
  ((count++))
<span class="hljs-keyword">done</span>
""");
    }

    [Fact]
    public void ControlFlow_Select()
    {
        AssertHighlighter("bash",
"""
select choice in "Add" "Remove" "Quit"; do
  case "$choice" in
    Add) echo adding ;;
    Remove) echo removing ;;
    Quit) break ;;
  esac
done
""",
"""
<span class="hljs-keyword">select</span> choice <span class="hljs-keyword">in</span> <span class="hljs-string">&quot;Add&quot;</span> <span class="hljs-string">&quot;Remove&quot;</span> <span class="hljs-string">&quot;Quit&quot;</span>; <span class="hljs-keyword">do</span>
  <span class="hljs-keyword">case</span> <span class="hljs-string">&quot;<span class="hljs-variable">$choice</span>&quot;</span> <span class="hljs-keyword">in</span>
    Add) <span class="hljs-built_in">echo</span> adding ;;
    Remove) <span class="hljs-built_in">echo</span> removing ;;
    Quit) <span class="hljs-built_in">break</span> ;;
  <span class="hljs-keyword">esac</span>
<span class="hljs-keyword">done</span>
""");
    }

    [Fact]
    public void ControlFlow_Break()
    {
        AssertHighlighter("bash",
"""
for i in 1 2 3 4 5; do
  [ "$i" -eq 3 ] && break
  echo "$i"
done
""",
"""
<span class="hljs-keyword">for</span> i <span class="hljs-keyword">in</span> 1 2 3 4 5; <span class="hljs-keyword">do</span>
  [ <span class="hljs-string">&quot;<span class="hljs-variable">$i</span>&quot;</span> -eq 3 ] &amp;&amp; <span class="hljs-built_in">break</span>
  <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">$i</span>&quot;</span>
<span class="hljs-keyword">done</span>
""");
    }

    [Fact]
    public void ControlFlow_Continue()
    {
        AssertHighlighter("bash",
"""
for i in 1 2 3 4 5; do
  [ $((i % 2)) -eq 0 ] && continue
  echo "$i"
done
""",
"""
<span class="hljs-keyword">for</span> i <span class="hljs-keyword">in</span> 1 2 3 4 5; <span class="hljs-keyword">do</span>
  [ $((i % <span class="hljs-number">2</span>)) -eq 0 ] &amp;&amp; <span class="hljs-built_in">continue</span>
  <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-variable">$i</span>&quot;</span>
<span class="hljs-keyword">done</span>
""");
    }

    [Fact]
    public void LogicalChain_And()
    {
        AssertHighlighter("bash",
"""
mkdir -p /tmp/work && cd /tmp/work
""",
"""
<span class="hljs-built_in">mkdir</span> -p /tmp/work &amp;&amp; <span class="hljs-built_in">cd</span> /tmp/work
""");
    }

    [Fact]
    public void LogicalChain_Or()
    {
        AssertHighlighter("bash",
"""
test -d /var/log || mkdir /var/log
""",
"""
<span class="hljs-built_in">test</span> -d /var/log || <span class="hljs-built_in">mkdir</span> /var/log
""");
    }

    [Fact]
    public void LogicalChain_Semicolon()
    {
        AssertHighlighter("bash",
"""
echo first; echo second; echo third
""",
"""
<span class="hljs-built_in">echo</span> first; <span class="hljs-built_in">echo</span> second; <span class="hljs-built_in">echo</span> third
""");
    }

    [Fact]
    public void LogicalChain_GroupBraces()
    {
        AssertHighlighter("bash",
"""
{ echo a; echo b; } > out.txt
""",
"""
{ <span class="hljs-built_in">echo</span> a; <span class="hljs-built_in">echo</span> b; } &gt; out.txt
""");
    }

    [Fact]
    public void LogicalChain_Subshell()
    {
        AssertHighlighter("bash",
"""
(cd /tmp && tar -czf - data) | gzip > out.gz
""",
"""
(<span class="hljs-built_in">cd</span> /tmp &amp;&amp; tar -czf - data) | gzip &gt; out.gz
""");
    }

    [Fact]
    public void Function_Simple()
    {
        AssertHighlighter("bash",
"""
greet() { echo "Hello $1"; }
""",
"""
<span class="hljs-function"><span class="hljs-title">greet</span></span>() { <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;Hello <span class="hljs-variable">$1</span>&quot;</span>; }
""");
    }

    [Fact]
    public void Function_KeywordForm()
    {
        AssertHighlighter("bash",
"""
function greet {
  echo "Hello $1"
}
""",
"""
<span class="hljs-keyword">function</span> greet {
  <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;Hello <span class="hljs-variable">$1</span>&quot;</span>
}
""");
    }

    [Fact]
    public void Function_WithLocal()
    {
        AssertHighlighter("bash",
"""
add() {
  local a=$1
  local b=$2
  echo $((a + b))
}
""",
"""
<span class="hljs-function"><span class="hljs-title">add</span></span>() {
  <span class="hljs-built_in">local</span> a=<span class="hljs-variable">$1</span>
  <span class="hljs-built_in">local</span> b=<span class="hljs-variable">$2</span>
  <span class="hljs-built_in">echo</span> $((a + b))
}
""");
    }

    [Fact]
    public void Function_ReturnStatus()
    {
        AssertHighlighter("bash",
"""
is_root() {
  [ "$(id -u)" -eq 0 ]
}
""",
"""
<span class="hljs-function"><span class="hljs-title">is_root</span></span>() {
  [ <span class="hljs-string">&quot;<span class="hljs-subst">$(id -u)</span>&quot;</span> -eq 0 ]
}
""");
    }

    [Fact]
    public void Function_Recursive()
    {
        AssertHighlighter("bash",
"""
factorial() {
  if [ "$1" -le 1 ]; then echo 1; else echo $(($1 * $(factorial $(($1 - 1))))); fi
}
""",
"""
<span class="hljs-function"><span class="hljs-title">factorial</span></span>() {
  <span class="hljs-keyword">if</span> [ <span class="hljs-string">&quot;<span class="hljs-variable">$1</span>&quot;</span> -le 1 ]; <span class="hljs-keyword">then</span> <span class="hljs-built_in">echo</span> 1; <span class="hljs-keyword">else</span> <span class="hljs-built_in">echo</span> $((<span class="hljs-variable">$1</span> * $(factorial $((<span class="hljs-variable">$1</span> - <span class="hljs-number">1</span>))))); <span class="hljs-keyword">fi</span>
}
""");
    }

    [Fact]
    public void Comment_Line()
    {
        AssertHighlighter("bash",
"""
# this is a comment
""",
"""
<span class="hljs-comment"># this is a comment</span>
""");
    }

    [Fact]
    public void Comment_Inline()
    {
        AssertHighlighter("bash",
"""
echo hi   # trailing comment
""",
"""
<span class="hljs-built_in">echo</span> hi   <span class="hljs-comment"># trailing comment</span>
""");
    }

    [Fact]
    public void Comment_Shebang()
    {
        AssertHighlighter("bash",
"""
#!/usr/bin/env bash
echo hi
""",
"""
<span class="hljs-meta">#!/usr/bin/env bash</span>
<span class="hljs-built_in">echo</span> hi
""");
    }

    [Fact]
    public void Comment_BashShebang()
    {
        AssertHighlighter("bash",
"""
#!/bin/bash
set -euo pipefail
""",
"""
<span class="hljs-meta">#!/bin/bash</span>
<span class="hljs-built_in">set</span> -euo pipefail
""");
    }

    [Fact]
    public void Composite_BackupScript()
    {
        AssertHighlighter("bash",
"""
#!/usr/bin/env bash
set -euo pipefail

SRC="${1:-./data}"
DEST="${2:-./backup}"

mkdir -p "$DEST"
timestamp=$(date +%Y%m%d-%H%M%S)
archive="${DEST}/backup-${timestamp}.tar.gz"

tar -czf "$archive" -C "$SRC" .

echo "Created: $archive"
""",
"""
<span class="hljs-meta">#!/usr/bin/env bash</span>
<span class="hljs-built_in">set</span> -euo pipefail

SRC=<span class="hljs-string">&quot;<span class="hljs-variable">${1:-./data}</span>&quot;</span>
DEST=<span class="hljs-string">&quot;<span class="hljs-variable">${2:-./backup}</span>&quot;</span>

<span class="hljs-built_in">mkdir</span> -p <span class="hljs-string">&quot;<span class="hljs-variable">$DEST</span>&quot;</span>
timestamp=$(<span class="hljs-built_in">date</span> +%Y%m%d-%H%M%S)
archive=<span class="hljs-string">&quot;<span class="hljs-variable">${DEST}</span>/backup-<span class="hljs-variable">${timestamp}</span>.tar.gz&quot;</span>

tar -czf <span class="hljs-string">&quot;<span class="hljs-variable">$archive</span>&quot;</span> -C <span class="hljs-string">&quot;<span class="hljs-variable">$SRC</span>&quot;</span> .

<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;Created: <span class="hljs-variable">$archive</span>&quot;</span>
""");
    }

    [Fact]
    public void Composite_WaitForService()
    {
        AssertHighlighter("bash",
"""
#!/bin/bash
set -e

for i in {1..30}; do
  if curl -sf "http://localhost:8080/health" > /dev/null; then
    echo "Service is up"
    exit 0
  fi
  echo "Waiting for service... ($i/30)"
  sleep 2
done

echo "Service failed to start" >&2
exit 1
""",
"""
<span class="hljs-meta">#!/bin/bash</span>
<span class="hljs-built_in">set</span> -e

<span class="hljs-keyword">for</span> i <span class="hljs-keyword">in</span> {1..30}; <span class="hljs-keyword">do</span>
  <span class="hljs-keyword">if</span> curl -sf <span class="hljs-string">&quot;http://localhost:8080/health&quot;</span> &gt; /dev/null; <span class="hljs-keyword">then</span>
    <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;Service is up&quot;</span>
    <span class="hljs-built_in">exit</span> 0
  <span class="hljs-keyword">fi</span>
  <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;Waiting for service... (<span class="hljs-variable">$i</span>/30)&quot;</span>
  <span class="hljs-built_in">sleep</span> 2
<span class="hljs-keyword">done</span>

<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;Service failed to start&quot;</span> &gt;&amp;2
<span class="hljs-built_in">exit</span> 1
""");
    }

    [Fact]
    public void Composite_DeployScript()
    {
        AssertHighlighter("bash",
"""
#!/usr/bin/env bash
set -euo pipefail

readonly APP_DIR="/var/app"
readonly LOG_FILE="/var/log/deploy.log"

log() {
  echo "$(date +%FT%T) $*" >> "$LOG_FILE"
}

deploy() {
  log "Starting deployment"
  cd "$APP_DIR"
  git pull --ff-only
  npm ci --production
  systemctl restart myapp
  log "Deployment complete"
}

deploy "$@"
""",
"""
<span class="hljs-meta">#!/usr/bin/env bash</span>
<span class="hljs-built_in">set</span> -euo pipefail

<span class="hljs-built_in">readonly</span> APP_DIR=<span class="hljs-string">&quot;/var/app&quot;</span>
<span class="hljs-built_in">readonly</span> LOG_FILE=<span class="hljs-string">&quot;/var/log/deploy.log&quot;</span>

<span class="hljs-function"><span class="hljs-title">log</span></span>() {
  <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;<span class="hljs-subst">$(date +%FT%T)</span> $*&quot;</span> &gt;&gt; <span class="hljs-string">&quot;<span class="hljs-variable">$LOG_FILE</span>&quot;</span>
}

<span class="hljs-function"><span class="hljs-title">deploy</span></span>() {
  <span class="hljs-built_in">log</span> <span class="hljs-string">&quot;Starting deployment&quot;</span>
  <span class="hljs-built_in">cd</span> <span class="hljs-string">&quot;<span class="hljs-variable">$APP_DIR</span>&quot;</span>
  git pull --ff-only
  npm ci --production
  systemctl restart myapp
  <span class="hljs-built_in">log</span> <span class="hljs-string">&quot;Deployment complete&quot;</span>
}

deploy <span class="hljs-string">&quot;<span class="hljs-variable">$@</span>&quot;</span>
""");
    }

    [Fact]
    public void Composite_ParseArgs()
    {
        AssertHighlighter("bash",
"""
#!/bin/bash

VERBOSE=0
NAME=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    -v|--verbose)
      VERBOSE=1
      shift
      ;;
    -n|--name)
      NAME="$2"
      shift 2
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 1
      ;;
  esac
done

echo "verbose=$VERBOSE name=$NAME"
""",
"""
<span class="hljs-meta">#!/bin/bash</span>

VERBOSE=0
NAME=<span class="hljs-string">&quot;&quot;</span>

<span class="hljs-keyword">while</span> [[ <span class="hljs-variable">$#</span> -gt 0 ]]; <span class="hljs-keyword">do</span>
  <span class="hljs-keyword">case</span> <span class="hljs-string">&quot;<span class="hljs-variable">$1</span>&quot;</span> <span class="hljs-keyword">in</span>
    -v|--verbose)
      VERBOSE=1
      <span class="hljs-built_in">shift</span>
      ;;
    -n|--name)
      NAME=<span class="hljs-string">&quot;<span class="hljs-variable">$2</span>&quot;</span>
      <span class="hljs-built_in">shift</span> 2
      ;;
    *)
      <span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;Unknown option: <span class="hljs-variable">$1</span>&quot;</span> &gt;&amp;2
      <span class="hljs-built_in">exit</span> 1
      ;;
  <span class="hljs-keyword">esac</span>
<span class="hljs-keyword">done</span>

<span class="hljs-built_in">echo</span> <span class="hljs-string">&quot;verbose=<span class="hljs-variable">$VERBOSE</span> name=<span class="hljs-variable">$NAME</span>&quot;</span>
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("bash",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("bash",
"""
# just a comment
""",
"""
<span class="hljs-comment"># just a comment</span>
""");
    }

    [Fact]
    public void SpecialEdge_OnlyShebang()
    {
        AssertHighlighter("bash",
"""
#!/bin/bash
""",
"""
<span class="hljs-meta">#!/bin/bash</span>
""");
    }

    [Fact]
    public void SpecialEdge_BackslashContinue()
    {
        AssertHighlighter("bash",
"""
echo hello \
  world
""",
"""
<span class="hljs-built_in">echo</span> hello \
  world
""");
    }

    [Fact]
    public void SpecialEdge_BraceExpansion()
    {
        AssertHighlighter("bash",
"""
echo {a,b,c}-{1,2,3}
""",
"""
<span class="hljs-built_in">echo</span> {a,b,c}-{1,2,3}
""");
    }

    [Fact]
    public void SpecialEdge_TildeExpansion()
    {
        AssertHighlighter("bash",
"""
cd ~/projects
""",
"""
<span class="hljs-built_in">cd</span> ~/projects
""");
    }

    [Fact]
    public void SpecialEdge_Glob()
    {
        AssertHighlighter("bash",
"""
ls *.txt
""",
"""
<span class="hljs-built_in">ls</span> *.txt
""");
    }

    [Fact]
    public void SpecialEdge_ExtendedGlob()
    {
        AssertHighlighter("bash",
"""
shopt -s extglob
ls !(README).md
""",
"""
<span class="hljs-built_in">shopt</span> -s extglob
<span class="hljs-built_in">ls</span> !(README).md
""");
    }

    [Fact]
    public void Export_AtStart()
    {
        AssertHighlighter("bash",
"""
export sample
""",
"""
<span class="hljs-built_in">export</span> sample
""");
    }

    [Fact]
    public void Export_NotHighlightedAfterEcho()
    {
        AssertHighlighter("bash",
"""
echo export
""",
"""
<span class="hljs-built_in">echo</span> export
""");
    }

    [Fact]
    public void Export_NotHighlightedAsArgument()
    {
        AssertHighlighter("bash",
"""
echo foo export bar
""",
"""
<span class="hljs-built_in">echo</span> foo export bar
""");
    }

    [Fact]
    public void Export_AfterSemicolon()
    {
        AssertHighlighter("bash",
"""
echo hi; export FOO=bar
""",
"""
<span class="hljs-built_in">echo</span> hi; <span class="hljs-built_in">export</span> FOO=bar
""");
    }

    [Fact]
    public void Export_AfterAndAnd()
    {
        AssertHighlighter("bash",
"""
true && export FOO=bar
""",
"""
<span class="hljs-literal">true</span> &amp;&amp; <span class="hljs-built_in">export</span> FOO=bar
""");
    }

    [Fact]
    public void Export_AfterPipe()
    {
        AssertHighlighter("bash",
"""
echo hi | export FOO=bar
""",
"""
<span class="hljs-built_in">echo</span> hi | <span class="hljs-built_in">export</span> FOO=bar
""");
    }

    [Fact]
    public void Export_InsideSubshell()
    {
        AssertHighlighter("bash",
"""
(export FOO=bar)
""",
"""
(<span class="hljs-built_in">export</span> FOO=bar)
""");
    }

    [Fact]
    public void Export_InsideBraceGroup()
    {
        AssertHighlighter("bash",
"""
{ export FOO=bar; }
""",
"""
{ <span class="hljs-built_in">export</span> FOO=bar; }
""");
    }

    [Fact]
    public void Export_OnNewLine()
    {
        AssertHighlighter("bash",
"""
echo hi
export FOO=bar
""",
"""
<span class="hljs-built_in">echo</span> hi
<span class="hljs-built_in">export</span> FOO=bar
""");
    }

    [Fact]
    public void Export_IndentedAtLineStart()
    {
        AssertHighlighter("bash",
"""
if true; then
    export FOO=bar
fi
""",
"""
<span class="hljs-keyword">if</span> <span class="hljs-literal">true</span>; <span class="hljs-keyword">then</span>
    <span class="hljs-built_in">export</span> FOO=bar
<span class="hljs-keyword">fi</span>
""");
    }
}
