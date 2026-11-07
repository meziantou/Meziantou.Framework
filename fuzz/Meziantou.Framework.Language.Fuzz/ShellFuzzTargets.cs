#pragma warning disable MA0243 // Tab characters in raw string literals are part of the test input
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Language.Shell;

namespace Meziantou.Framework.Language.Fuzz;

/// <summary>
/// Shell scripts in the POSIX (sh, bash, zsh), PowerShell (5.1, 7), and cmd dialects: the shared invariants, the
/// statements of the root reproducing the text, and <see cref="ShellSyntaxTree.ParseCommand(string, ShellParseOptions)"/>.
/// </summary>
internal static class ShellFuzzTargets
{
    private static readonly ShellDialect[] Dialects = [ShellDialect.Sh, ShellDialect.Bash, ShellDialect.Zsh, ShellDialect.PowerShell, ShellDialect.PowerShellCore, ShellDialect.Cmd];

    /// <summary>
    /// The nesting limits a depth byte picks from. <see cref="ShellParseOptions.MaxRecursionDepth"/> is not validated, so
    /// zero is one of them: the parser must cope with it as it copes with any other limit.
    /// </summary>
    private static readonly int[] Depths = [128, 0, 1, 2, 3, 5, 8, 32];

    /// <summary>The fragments of ShellRoundTripFuzzTests, and the punctuation and keywords of PowerShell and cmd.</summary>
    private static readonly string[] Tokens =
    [
        "echo", "ls", "-la", "foo", "$VAR", "${VAR}", "${VAR:-d}", "${#VAR}", "${VAR//a/b}", "'sq'", "\"dq\"", "\"$VAR\"", "$(cmd)", "`cmd`",
        "$((1+2))", "|", "||", "&&", ";", "&", ">", ">>", "<", "2>", "2>&1", "&>", "|&", "<<EOF", "<<-EOF", "<<<", "EOF", "\n", " ", "\t", "\\",
        "#c", "(", ")", "{", "}", "[[", "]]", "((", "))", "if", "then", "elif", "else", "fi", "for", "in", "do",
        "done", "while", "until", "case", "esac", ";;", ";&", ";;&", "function", "a=b", "a=(1 2)", "a+=x", "<(x)", ">(x)", "*", "?", "!", "\"",
        "'", "$", "`", "select", "time", "coproc", "\r\n", "]", "[", "$'\\n'", "$\"x\"", "foreach", "repeat", "=(cmd)", "**/*", "~",
        "param(", "[string]", "$_", "$null", "@(", "@{", "$(", "-eq", "-ne", "-match", "-and", "-or", "-not", "::", ".", "..", "|>", "??", "?:", "??=",
        "try", "catch", "finally", "trap", "switch", "default", "begin", "process", "end", "clean", "class", "enum", "using", "return", "throw", "break", "continue",
        "@'\n", "\n'@", "@\"\n", "\n\"@", "`n", "<#", "#>", "#requires", ":label", "-and", "1..10",
        "@echo off", "set ", "set /a ", "set \"x=1\"", "%x%", "!x!", "%%i", "%~dp0", "goto ", ":eof", "call ", "rem ", "::", "^", "if exist ", "if errorlevel 1 ", "for /f ", "for /l ", "setlocal enabledelayedexpansion", "endlocal", "else (", " equ ", "==", "(", ")",
    ];

    public static IReadOnlyList<FuzzTarget> Targets { get; } =
    [
        new("language-shell", Parse) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    /// <summary>Byte 0: the dialect. Byte 1: the nesting limit, and bit 7 also parses the text as a single command. Then the script.</summary>
    private static void Parse(ReadOnlySpan<byte> data)
    {
        var dialect = FuzzInput.Pick(Dialects, FuzzInput.ReadByte(ref data));
        var flags = FuzzInput.ReadByte(ref data);
        var text = FuzzText.Decode(data);
        var options = new ShellParseOptions(dialect) { MaxRecursionDepth = FuzzInput.Pick(Depths, (byte)(flags & 0x7F)) };

        var tree = ShellSyntaxTree.ParseText(text, options);
        var shape = SyntaxTreeInvariants.Check(tree, text, "SHELL", diagnosticsOnNodes: false, diagnosticsInSourceOrder: false);
        var root = tree.GetRoot();
        FuzzAssert.Equal(text, root.Statements.ToFullString() + root.EndOfFileToken.ToFullString(), "The statements and the end of the file do not reproduce the text");

        var walker = new CountingWalker();
        try
        {
            walker.Visit(root);
            SyntaxTreeInvariants.CheckWalker(shape, walker.TokenCount, walker.TriviaCount);
        }
        catch (InsufficientExecutionStackException)
        {
            // Documented by the walker for a tree nested too deeply to walk
        }

        if ((flags & 0x80) != 0)
        {
            var statement = ShellSyntaxTree.ParseCommand(text, options);
            SyntaxTreeInvariants.CheckSubtree(statement, text);
        }
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        const string BashScript = """
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

        const string ShScript = """
            #!/bin/sh
            main() {
              count=0
              while read -r line; do
                count=$((count + 1))
                echo "$count: $line"
              done < input.txt
              [ "$count" -gt 0 ] && echo "read $count lines" || echo none
              until false; do break; done
            }
            main "$@" > /dev/null 2>&1 &
            wait
            """;

        const string ZshScript = """
            setopt extended_glob
            for f (**/*.txt(.N)) print -r -- $f
            foreach x (a b c)
              echo $x
            end
            repeat 3 echo hi
            typeset -A map=(k v)
            echo ${(k)map} =(ls) <(cat file) $'\t' "${x:-default}"
            select opt in a b; do break; done
            coproc { sleep 1; }
            [[ $a == b* && -n "$c" ]] || (( i++ ))
            """;

        const string PowerShellScript = """
            #requires -Version 5.1
            param([string]$Name = "world", [switch]$Force)
            <# block
               comment #>
            function Get-Greeting {
                [CmdletBinding()]
                param([Parameter(Mandatory)][string]$Who)
                begin { $count = 0 }
                process { "Hello, $Who! $($count + 1)" }
                end { return $count }
            }
            $items = @(1, 2, 3) | Where-Object { $_ -gt 1 } | ForEach-Object { $_ * 2 }
            $hash = @{ Key = 'value'; Other = 1..10 }
            try { throw "x" } catch [System.Exception] { Write-Error $_ } finally { }
            switch -Regex ($Name) { '^w' { 'w' } default { 'other' } }
            :outer foreach ($i in $items) { if ($i -eq 4) { break outer } else { continue } }
            $here = @"
            text $Name `n
            "@
            class Point { [int]$X; Point([int]$x) { $this.X = $x } }
            [Math]::Max(1, 2).ToString() > out.txt 2>&1
            """;

        const string PwshScript = """
            $value = $null ?? 'default'
            $value ??= 'other'
            $result = $true ? 'yes' : 'no'
            Get-Item . && Write-Output ok || Write-Output failed
            $obj?.Property
            ${env:PATH} -split ';' -match 'dotnet' -and -not $false
            clean { Remove-Item temp }
            """;

        const string CmdScript = """
            @echo off
            setlocal enabledelayedexpansion
            set "NAME=world"
            set /a COUNT=1+2*3
            if exist "%~dp0file.txt" (
              echo found %NAME% !COUNT!
            ) else if errorlevel 1 (
              goto :error
            ) else echo none
            for /f "tokens=1,2 delims=," %%i in (data.csv) do echo %%i ^& %%j
            for /l %%n in (1,1,5) do call :sub %%n
            rem comment
            :: another comment
            goto :eof
            :error
            echo failed 1>&2 & exit /b 1
            endlocal
            """;

        yield return FuzzSeed.FromText("bash", [1, 0], BashScript);
        yield return FuzzSeed.FromText("bash-command", [1, 0x80], "git commit -m \"message\" && git push origin HEAD:refs/heads/main | tee log; echo done");
        yield return FuzzSeed.FromText("sh", [0, 0], ShScript);
        yield return FuzzSeed.FromText("sh-depth", [0, 2], "if a; then if b; then if c; then { ( echo $( echo $( echo x ) ) ) }; fi; fi; fi");
        yield return FuzzSeed.FromText("zsh", [2, 0], ZshScript);
        yield return FuzzSeed.FromText("powershell", [3, 0], PowerShellScript);
        yield return FuzzSeed.FromText("pwsh", [4, 0], PwshScript);
        yield return FuzzSeed.FromText("pwsh-command", [4, 0x80], "Get-ChildItem -Recurse | Select-Object -First 5; Write-Host done");
        yield return FuzzSeed.FromText("cmd", [5, 0], CmdScript);
        yield return FuzzSeed.FromText("cmd-command", [5, 0x80], "dir /s /b *.txt | findstr /i \"x\" > out.txt & echo ok");
        yield return FuzzSeed.FromText("depth-zero", [1, 1], "f() { echo $(( (1 + 2) * 3 )); }; f");
    }

    private sealed class CountingWalker() : ShellSyntaxWalker(SyntaxWalkerDepth.Trivia)
    {
        public int TokenCount { get; private set; }

        public int TriviaCount { get; private set; }

        public override void VisitToken(SyntaxToken token)
        {
            TokenCount++;
            base.VisitToken(token);
        }

        public override void VisitTrivia(SyntaxTrivia trivia) => TriviaCount++;
    }
}
