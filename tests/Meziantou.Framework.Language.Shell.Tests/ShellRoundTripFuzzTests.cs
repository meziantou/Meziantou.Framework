namespace Meziantou.Framework.Language.Shell.Tests;

/// <summary>
/// Round-tripping every input exactly is the invariant the whole library rests on, so it is checked against randomly
/// assembled fragments as well as the hand-written samples.
/// </summary>
public sealed class ShellRoundTripFuzzTests
{
    private static readonly string[] Fragments =
    [
        "echo", "ls", "-la", "foo", "$VAR", "${VAR}", "${VAR:-d}", "'sq'", "\"dq\"", "\"$VAR\"", "$(cmd)", "`cmd`",
        "$((1+2))", "|", "||", "&&", ";", "&", ">", ">>", "<", "2>", "2>&1", "<<EOF", "EOF", "\n", " ", "\t", "\\",
        "#c", "(", ")", "{", "}", "[[", "]]", "((", "))", "if", "then", "elif", "else", "fi", "for", "in", "do",
        "done", "while", "until", "case", "esac", ";;", "function", "a=b", "a=(1 2)", "<(x)", "*", "?", "!", "\"",
        "'", "$", "`", "select", "time", "coproc", "\r\n", "]", "[",
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RandomFragmentSequences_RoundTripExactly(int seed)
    {
        var random = new DeterministicRandom(seed);
        var dialects = new[] { ShellDialect.Sh, ShellDialect.Bash, ShellDialect.Zsh };

        for (var iteration = 0; iteration < 400; iteration++)
        {
            var builder = new StringBuilder();
            var length = 1 + random.Next(13);
            for (var index = 0; index < length; index++)
            {
                builder.Append(Fragments[random.Next(Fragments.Length)]);
                if (random.Next(3) == 0)
                {
                    builder.Append(' ');
                }
            }

            var text = builder.ToString();
            var dialect = dialects[random.Next(dialects.Length)];

            var tree = ShellSyntaxTree.ParseText(text, dialect);

            Assert.Equal(text, tree.Root.ToFullString());
        }
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    public void RandomCharacterSoup_RoundTripsExactly(int seed)
    {
        const string Alphabet = "abc \t\n\r'\"`$(){}[]<>|&;#\\*?!=-0123456789";
        var random = new DeterministicRandom(seed);

        for (var iteration = 0; iteration < 400; iteration++)
        {
            var text = string.Create(random.Next(40), random, (span, state) =>
            {
                for (var index = 0; index < span.Length; index++)
                {
                    span[index] = Alphabet[state.Next(Alphabet.Length)];
                }
            });

            var tree = ShellSyntaxTree.ParseText(text, ShellDialect.Bash);

            Assert.Equal(text, tree.Root.ToFullString());
        }
    }

    [Fact]
    public void EveryPrefixOfARealisticScript_RoundTripsExactly()
    {
        const string Script = """
            #!/usr/bin/env bash
            set -euo pipefail

            readonly ROOT="$(cd "$(dirname "$0")" && pwd)"
            declare -a targets=(build test pack)

            log() {
              printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*" >&2
            }

            for target in "${targets[@]}"; do
              if [[ -f "$ROOT/$target.sh" ]]; then
                log "running $target"
                bash "$ROOT/$target.sh" 2>&1 | tee "logs/$target.log" || {
                  log "FAILED: $target"
                  exit 1
                }
              else
                case "$target" in
                  pack) log "no pack step";;
                  *) log "skipping $target";;
                esac
              fi
            done

            cat <<-EOF
            	done: ${#targets[@]} targets
            EOF
            """;

        for (var length = 0; length <= Script.Length; length++)
        {
            var prefix = Script[..length];
            var tree = ShellSyntaxTree.ParseText(prefix, ShellDialect.Bash);

            Assert.Equal(prefix, tree.Root.ToFullString());
        }
    }

    [Fact]
    public void RealisticScript_ParsesWithoutDiagnostics()
    {
        const string Script = """
            #!/usr/bin/env bash
            set -euo pipefail

            main() {
              local count=0
              while read -r line; do
                count=$((count + 1))
                echo "$count: $line"
              done < input.txt

              if (( count > 0 )); then
                echo "read $count lines"
              fi
            }

            main "$@"
            """;

        var tree = ShellSyntaxTree.ParseText(Script, ShellDialect.Bash);

        Assert.Equal(Script, tree.Root.ToFullString());
        Assert.Empty(tree.Diagnostics);
    }

    private static readonly string[] PowerShellFragments =
    [
        "Get-Item", "-Path", "$x", "$env:PATH", "${a b}", "@args", "'sq'", "\"dq\"", "\"$x\"", "\"$($a)\"",
        "$(cmd)", "@(1,2)", "@{a=1}", "[int]", "[System.IO.Path]", "::Method", ".Prop", "(1+2)", "{ $_ }",
        "|", "||", "&&", ";", ",", "=", "+=", "-eq", "-and", "-not", "!", "..", "+", "-", "*", "/", "%",
        "?", ":", "??", "if", "else", "elseif", "while", "do", "until", "for", "foreach", "in", "switch",
        "try", "catch", "finally", "trap", "function", "filter", "class", "enum", "param", "begin", "process",
        "end", "clean", "data", "using", "return", "throw", "exit", "break", "continue", ":lbl", "(", ")",
        "{", "}", "[", "]", "\n", " ", "\t", "`", "#c", "<#b#>", "@\"", "\"@", "@'", "'@", ">", ">>", "2>&1",
        "1", "0x1F", "1.5", "10kb", "$true", "++", "--", "\r\n",
    ];

    [Theory]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    public void RandomPowerShellFragmentSequences_RoundTripExactly(int seed)
    {
        var random = new DeterministicRandom(seed);
        var dialects = new[] { ShellDialect.PowerShell, ShellDialect.PowerShellCore };

        for (var iteration = 0; iteration < 400; iteration++)
        {
            var builder = new StringBuilder();
            var length = 1 + random.Next(13);
            for (var index = 0; index < length; index++)
            {
                builder.Append(PowerShellFragments[random.Next(PowerShellFragments.Length)]);
                if (random.Next(3) == 0)
                {
                    builder.Append(' ');
                }
            }

            var text = builder.ToString();
            var dialect = dialects[random.Next(dialects.Length)];

            Assert.Equal(text, ShellSyntaxTree.ParseText(text, dialect).Root.ToFullString());
        }
    }

    [Theory]
    [InlineData(31)]
    [InlineData(32)]
    public void RandomPowerShellCharacterSoup_RoundTripsExactly(int seed)
    {
        const string Alphabet = "abc \t\n\r'\"`$(){}[]<>|&;#*?!=-+.,:@0123456789";
        var random = new DeterministicRandom(seed);

        for (var iteration = 0; iteration < 400; iteration++)
        {
            var text = string.Create(random.Next(40), random, (span, state) =>
            {
                for (var index = 0; index < span.Length; index++)
                {
                    span[index] = Alphabet[state.Next(Alphabet.Length)];
                }
            });

            Assert.Equal(text, ShellSyntaxTree.ParseText(text, ShellDialect.PowerShellCore).Root.ToFullString());
        }
    }

    [Fact]
    public void EveryPrefixOfARealisticPowerShellScript_RoundTripsExactly()
    {
        const string Script = """
            [CmdletBinding()]
            param(
                [Parameter(Mandatory)][string]$Path,
                [int]$Depth = 3
            )

            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'

            function Get-Report {
                [CmdletBinding()]
                param([string]$Root)

                $items = Get-ChildItem -Path $Root -Recurse -Depth $Depth |
                    Where-Object { -not $_.PSIsContainer } |
                    Sort-Object Length -Descending

                foreach ($item in $items) {
                    [PSCustomObject]@{
                        Name = $item.Name
                        Size = "{0:N0}" -f $item.Length
                    }
                }
            }

            try {
                Get-Report -Root $Path | Format-Table
            }
            catch [System.IO.IOException] {
                Write-Error "failed: $($_.Exception.Message)"
                exit 1
            }
            finally {
                Write-Verbose 'done'
            }
            """;

        for (var length = 0; length <= Script.Length; length++)
        {
            var prefix = Script[..length];

            Assert.Equal(prefix, ShellSyntaxTree.ParseText(prefix, ShellDialect.PowerShellCore).Root.ToFullString());
        }

        // pwsh 7 parses the whole script without an error, so neither should this parser.
        Assert.Empty(ShellSyntaxTree.ParseText(Script, ShellDialect.PowerShellCore).Diagnostics);
    }

    private static readonly string[] CmdFragments =
    [
        "echo", "dir", "/b", "%PATH%", "%1", "%*", "%~dp0", "%%i", "!VAR!", "\"quoted\"", "\"a %B% c\"",
        "^&", "^", "%%", "&", "&&", "||", "|", ">", ">>", "<", "2>&1", "(", ")", "if", "not", "exist",
        "defined", "errorlevel", "==", "GEQ", "else", "for", "in", "do", "/f", "/l", "/r", "/d", "goto",
        "call", ":label", "::c", "rem c", "set", "NAME=v", "/a", "/p", "\r\n", "\n", " ", "\t", "*", "?",
        "\"", "%", "!", ":",
    ];

    [Theory]
    [InlineData(40)]
    [InlineData(41)]
    [InlineData(42)]
    [InlineData(43)]
    [InlineData(44)]
    public void RandomCmdFragmentSequences_RoundTripExactly(int seed)
    {
        var random = new DeterministicRandom(seed);

        for (var iteration = 0; iteration < 400; iteration++)
        {
            var builder = new StringBuilder();
            var length = 1 + random.Next(13);
            for (var index = 0; index < length; index++)
            {
                builder.Append(CmdFragments[random.Next(CmdFragments.Length)]);
                if (random.Next(3) == 0)
                {
                    builder.Append(' ');
                }
            }

            var text = builder.ToString();

            Assert.Equal(text, ShellSyntaxTree.ParseText(text, ShellDialect.Cmd).Root.ToFullString());
        }
    }

    [Theory]
    [InlineData(51)]
    [InlineData(52)]
    public void RandomCmdCharacterSoup_RoundTripsExactly(int seed)
    {
        const string Alphabet = "abc \t\r\n\"%!^&|<>()*?=:;,/0123456789";
        var random = new DeterministicRandom(seed);

        for (var iteration = 0; iteration < 400; iteration++)
        {
            var text = string.Create(random.Next(40), random, (span, state) =>
            {
                for (var index = 0; index < span.Length; index++)
                {
                    span[index] = Alphabet[state.Next(Alphabet.Length)];
                }
            });

            Assert.Equal(text, ShellSyntaxTree.ParseText(text, ShellDialect.Cmd).Root.ToFullString());
        }
    }

    [Fact]
    public void EveryPrefixOfARealisticBatchScript_RoundTripsExactly()
    {
        const string Script = "@echo off\r\n"
            + "setlocal enabledelayedexpansion\r\n"
            + "\r\n"
            + ":: build helper\r\n"
            + "set \"ROOT=%~dp0\"\r\n"
            + "set /a COUNT=0\r\n"
            + "\r\n"
            + "if not exist \"%ROOT%src\" (\r\n"
            + "  echo missing src >&2\r\n"
            + "  goto :error\r\n"
            + ")\r\n"
            + "\r\n"
            + "for /f \"tokens=*\" %%f in ('dir /b \"%ROOT%src\"') do (\r\n"
            + "  set /a COUNT=!COUNT!+1\r\n"
            + "  echo [!COUNT!] %%f\r\n"
            + ")\r\n"
            + "\r\n"
            + "if !COUNT! GEQ 1 (echo done) else (echo empty)\r\n"
            + "goto :eof\r\n"
            + "\r\n"
            + ":error\r\n"
            + "exit /b 1\r\n";

        for (var length = 0; length <= Script.Length; length++)
        {
            var prefix = Script[..length];

            Assert.Equal(prefix, ShellSyntaxTree.ParseText(prefix, ShellDialect.Cmd).Root.ToFullString());
        }

        // cmd.exe runs this script, so a diagnostic on the whole text would be a false positive.
        Assert.Empty(ShellSyntaxTree.ParseText(Script, ShellDialect.Cmd).Diagnostics);
    }

    /// <summary>
    /// A small fixed xorshift generator. The corpus has to stay identical across runtimes, which
    /// <see cref="Random"/> does not guarantee, and this keeps a failing seed reproducible.
    /// </summary>
    private sealed class DeterministicRandom(int seed)
    {
        private uint _state = (uint)seed | 1u;

        public int Next(int exclusiveUpperBound)
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;

            return (int)(_state % (uint)exclusiveUpperBound);
        }
    }
}
