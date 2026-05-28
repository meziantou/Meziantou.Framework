namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class DosHighlighterTests
{

    [Fact]
    public void Command_Echo()
    {
        AssertHighlighter("dos",
"""
echo Hello, world!
""",
"""
<span class="hljs-built_in">echo</span> Hello, world!
""");
    }

    [Fact]
    public void Command_EchoOff()
    {
        AssertHighlighter("dos",
"""
@echo off
""",
"""
@<span class="hljs-built_in">echo</span> off
""");
    }

    [Fact]
    public void Command_EchoOn()
    {
        AssertHighlighter("dos",
"""
@echo on
""",
"""
@<span class="hljs-built_in">echo</span> on
""");
    }

    [Fact]
    public void Command_EchoBlank()
    {
        AssertHighlighter("dos",
"""
echo.
""",
"""
<span class="hljs-built_in">echo</span>.
""");
    }

    [Fact]
    public void Command_Cd()
    {
        AssertHighlighter("dos",
"""
cd C:\Users\alice
""",
"""
<span class="hljs-built_in">cd</span> C:\Users\alice
""");
    }

    [Fact]
    public void Command_CdSlash()
    {
        AssertHighlighter("dos",
"""
cd /d D:\projects
""",
"""
<span class="hljs-built_in">cd</span> /d D:\projects
""");
    }

    [Fact]
    public void Command_Dir()
    {
        AssertHighlighter("dos",
"""
dir /b *.txt
""",
"""
<span class="hljs-built_in">dir</span> /b *.txt
""");
    }

    [Fact]
    public void Command_Copy()
    {
        AssertHighlighter("dos",
"""
copy source.txt dest.txt
""",
"""
<span class="hljs-built_in">copy</span> source.txt dest.txt
""");
    }

    [Fact]
    public void Command_Xcopy()
    {
        AssertHighlighter("dos",
"""
xcopy /E /I /Y source dest
""",
"""
<span class="hljs-built_in">xcopy</span> /E /I /Y source dest
""");
    }

    [Fact]
    public void Command_Move()
    {
        AssertHighlighter("dos",
"""
move old.txt new.txt
""",
"""
<span class="hljs-built_in">move</span> old.txt new.txt
""");
    }

    [Fact]
    public void Command_Del()
    {
        AssertHighlighter("dos",
"""
del /Q temp.txt
""",
"""
<span class="hljs-built_in">del</span> /Q temp.txt
""");
    }

    [Fact]
    public void Command_Erase()
    {
        AssertHighlighter("dos",
"""
erase *.bak
""",
"""
<span class="hljs-built_in">erase</span> *.bak
""");
    }

    [Fact]
    public void Command_Mkdir()
    {
        AssertHighlighter("dos",
"""
mkdir C:\app\logs
""",
"""
<span class="hljs-built_in">mkdir</span> C:\app\logs
""");
    }

    [Fact]
    public void Command_Md()
    {
        AssertHighlighter("dos",
"""
md data
""",
"""
<span class="hljs-built_in">md</span> data
""");
    }

    [Fact]
    public void Command_Rmdir()
    {
        AssertHighlighter("dos",
"""
rmdir /S /Q oldfolder
""",
"""
<span class="hljs-built_in">rmdir</span> /S /Q oldfolder
""");
    }

    [Fact]
    public void Command_Type()
    {
        AssertHighlighter("dos",
"""
type readme.txt
""",
"""
<span class="hljs-built_in">type</span> readme.txt
""");
    }

    [Fact]
    public void Command_Find()
    {
        AssertHighlighter("dos",
"""
find "error" *.log
""",
"""
<span class="hljs-built_in">find</span> &quot;error&quot; *.log
""");
    }

    [Fact]
    public void Command_Findstr()
    {
        AssertHighlighter("dos",
"""
findstr /R "^TODO" *.txt
""",
"""
<span class="hljs-built_in">findstr</span> /R &quot;^TODO&quot; *.txt
""");
    }

    [Fact]
    public void Command_Sort()
    {
        AssertHighlighter("dos",
"""
sort < input.txt > output.txt
""",
"""
<span class="hljs-built_in">sort</span> &lt; input.txt &gt; output.txt
""");
    }

    [Fact]
    public void Command_More()
    {
        AssertHighlighter("dos",
"""
more readme.txt
""",
"""
<span class="hljs-built_in">more</span> readme.txt
""");
    }

    [Fact]
    public void Command_Cls()
    {
        AssertHighlighter("dos",
"""
cls
""",
"""
<span class="hljs-built_in">cls</span>
""");
    }

    [Fact]
    public void Command_Title()
    {
        AssertHighlighter("dos",
"""
title My Script
""",
"""
<span class="hljs-built_in">title</span> My Script
""");
    }

    [Fact]
    public void Command_Color()
    {
        AssertHighlighter("dos",
"""
color 0A
""",
"""
<span class="hljs-built_in">color</span> <span class="hljs-number">0A</span>
""");
    }

    [Fact]
    public void Command_Pause()
    {
        AssertHighlighter("dos",
"""
pause
""",
"""
<span class="hljs-built_in">pause</span>
""");
    }

    [Fact]
    public void Command_PauseSuppress()
    {
        AssertHighlighter("dos",
"""
pause > nul
""",
"""
<span class="hljs-built_in">pause</span> &gt; <span class="hljs-built_in">nul</span>
""");
    }

    [Fact]
    public void Command_Rem()
    {
        AssertHighlighter("dos",
"""
rem this is a comment
""",
"""
<span class="hljs-comment">rem this is a comment</span>
""");
    }

    [Fact]
    public void Command_Tasklist()
    {
        AssertHighlighter("dos",
"""
tasklist /FI "imagename eq notepad.exe"
""",
"""
tasklist /FI &quot;imagename eq notepad.exe&quot;
""");
    }

    [Fact]
    public void Command_Taskkill()
    {
        AssertHighlighter("dos",
"""
taskkill /IM notepad.exe /F
""",
"""
<span class="hljs-built_in">taskkill</span> /IM notepad.exe /F
""");
    }

    [Fact]
    public void Command_Start()
    {
        AssertHighlighter("dos",
"""
start "" notepad.exe readme.txt
""",
"""
<span class="hljs-built_in">start</span> &quot;&quot; notepad.exe readme.txt
""");
    }

    [Fact]
    public void Command_Where()
    {
        AssertHighlighter("dos",
"""
where git
""",
"""
where git
""");
    }

    [Fact]
    public void Command_Robocopy()
    {
        AssertHighlighter("dos",
"""
robocopy src dest /MIR /R:3 /W:5
""",
"""
robocopy src dest /MIR /R:<span class="hljs-number">3</span> /W:<span class="hljs-number">5</span>
""");
    }

    [Fact]
    public void Command_Net()
    {
        AssertHighlighter("dos",
"""
net use Z: \\server\share
""",
"""
<span class="hljs-built_in">net</span> use Z: \\server\share
""");
    }

    [Fact]
    public void Command_Reg()
    {
        AssertHighlighter("dos",
"""
reg query "HKLM\Software\Microsoft\Windows NT\CurrentVersion"
""",
"""
reg query &quot;HKLM\Software\Microsoft\Windows NT\CurrentVersion&quot;
""");
    }

    [Fact]
    public void Command_Sc()
    {
        AssertHighlighter("dos",
"""
sc query MyService
""",
"""
sc query MyService
""");
    }

    [Fact]
    public void Command_Powershell()
    {
        AssertHighlighter("dos",
"""
powershell -NoProfile -Command "Get-Process"
""",
"""
powershell -NoProfile -Command &quot;Get-Process&quot;
""");
    }

    [Fact]
    public void Variable_SetSimple()
    {
        AssertHighlighter("dos",
"""
set name=alice
""",
"""
<span class="hljs-built_in">set</span> name=alice
""");
    }

    [Fact]
    public void Variable_SetWithSpaces()
    {
        AssertHighlighter("dos",
"""
set "name=alice and bob"
""",
"""
<span class="hljs-built_in">set</span> &quot;name=alice and bob&quot;
""");
    }

    [Fact]
    public void Variable_SetNumeric()
    {
        AssertHighlighter("dos",
"""
set /A count=10+5
""",
"""
<span class="hljs-built_in">set</span> /A count=<span class="hljs-number">10</span>+<span class="hljs-number">5</span>
""");
    }

    [Fact]
    public void Variable_SetUserInput()
    {
        AssertHighlighter("dos",
"""
set /P name=Enter your name:
""",
"""
<span class="hljs-built_in">set</span> /P name=Enter your name:
""");
    }

    [Fact]
    public void Variable_ExpandPercent()
    {
        AssertHighlighter("dos",
"""
echo Hello, %name%!
""",
"""
<span class="hljs-built_in">echo</span> Hello, <span class="hljs-variable">%name%</span>!
""");
    }

    [Fact]
    public void Variable_ExpandDelayed()
    {
        AssertHighlighter("dos",
"""
echo !name!
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-variable">!name!</span>
""");
    }

    [Fact]
    public void Variable_EnvPath()
    {
        AssertHighlighter("dos",
"""
echo %PATH%
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-variable">%PATH%</span>
""");
    }

    [Fact]
    public void Variable_EnvUserProfile()
    {
        AssertHighlighter("dos",
"""
cd %USERPROFILE%
""",
"""
<span class="hljs-built_in">cd</span> <span class="hljs-variable">%USERPROFILE%</span>
""");
    }

    [Fact]
    public void Variable_EnvErrorlevel()
    {
        AssertHighlighter("dos",
"""
echo %ERRORLEVEL%
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-variable">%ERRORLEVEL%</span>
""");
    }

    [Fact]
    public void Variable_EnvDate()
    {
        AssertHighlighter("dos",
"""
echo %DATE% %TIME%
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-variable">%DATE%</span> <span class="hljs-variable">%TIME%</span>
""");
    }

    [Fact]
    public void Variable_EnvComputerName()
    {
        AssertHighlighter("dos",
"""
echo %COMPUTERNAME%
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-variable">%COMPUTERNAME%</span>
""");
    }

    [Fact]
    public void Variable_EnvTemp()
    {
        AssertHighlighter("dos",
"""
cd %TEMP%
""",
"""
<span class="hljs-built_in">cd</span> <span class="hljs-variable">%TEMP%</span>
""");
    }

    [Fact]
    public void Variable_PositionalArg()
    {
        AssertHighlighter("dos",
"""
echo first=%1 second=%2
""",
"""
<span class="hljs-built_in">echo</span> first=%<span class="hljs-number">1</span> second=%<span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void Variable_AllArgs()
    {
        AssertHighlighter("dos",
"""
echo all=%*
""",
"""
<span class="hljs-built_in">echo</span> all=%*
""");
    }

    [Fact]
    public void Variable_ScriptPath()
    {
        AssertHighlighter("dos",
"""
echo Script: %~dp0
""",
"""
<span class="hljs-built_in">echo</span> Script: %~dp0
""");
    }

    [Fact]
    public void Variable_ScriptName()
    {
        AssertHighlighter("dos",
"""
echo Name: %~nx0
""",
"""
<span class="hljs-built_in">echo</span> Name: %~nx0
""");
    }

    [Fact]
    public void Variable_ParamSubstring()
    {
        AssertHighlighter("dos",
"""
echo %name:~0,3%
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-variable">%name:~0,3%</span>
""");
    }

    [Fact]
    public void Variable_ParamReplace()
    {
        AssertHighlighter("dos",
"""
echo %path:;=\n%
""",
"""
<span class="hljs-built_in">echo</span> <span class="hljs-variable">%path:;=\n%</span>
""");
    }

    [Fact]
    public void Variable_Unset()
    {
        AssertHighlighter("dos",
"""
set name=
""",
"""
<span class="hljs-built_in">set</span> name=
""");
    }

    [Fact]
    public void EnableDelayedExpansion_Setlocal()
    {
        AssertHighlighter("dos",
"""
setlocal enabledelayedexpansion
""",
"""
<span class="hljs-built_in">setlocal</span> enabledelayedexpansion
""");
    }

    [Fact]
    public void EnableDelayedExpansion_Endlocal()
    {
        AssertHighlighter("dos",
"""
endlocal
""",
"""
<span class="hljs-built_in">endlocal</span>
""");
    }

    [Fact]
    public void EnableDelayedExpansion_SetlocalEnv()
    {
        AssertHighlighter("dos",
"""
setlocal enableextensions enabledelayedexpansion
""",
"""
<span class="hljs-built_in">setlocal</span> enableextensions enabledelayedexpansion
""");
    }

    [Fact]
    public void LabelGoto_Label()
    {
        AssertHighlighter("dos",
"""
:start
""",
"""
:<span class="hljs-built_in">start</span>
""");
    }

    [Fact]
    public void LabelGoto_Goto()
    {
        AssertHighlighter("dos",
"""
goto :start
""",
"""
<span class="hljs-keyword">goto</span> :<span class="hljs-built_in">start</span>
""");
    }

    [Fact]
    public void LabelGoto_GotoEof()
    {
        AssertHighlighter("dos",
"""
goto :eof
""",
"""
<span class="hljs-keyword">goto</span> :eof
""");
    }

    [Fact]
    public void LabelGoto_CallLabel()
    {
        AssertHighlighter("dos",
"""
call :do_work arg1 arg2
""",
"""
<span class="hljs-keyword">call</span> :do_work arg1 arg2
""");
    }

    [Fact]
    public void LabelGoto_CallBatch()
    {
        AssertHighlighter("dos",
"""
call helper.bat arg1
""",
"""
<span class="hljs-keyword">call</span> helper.bat arg1
""");
    }

    [Fact]
    public void ControlFlow_IfEqual()
    {
        AssertHighlighter("dos",
"""
if "%x%"=="hello" echo matched
""",
"""
<span class="hljs-keyword">if</span> &quot;<span class="hljs-variable">%x%</span>&quot;==&quot;hello&quot; <span class="hljs-built_in">echo</span> matched
""");
    }

    [Fact]
    public void ControlFlow_IfNotEqual()
    {
        AssertHighlighter("dos",
"""
if not "%x%"=="hello" echo no match
""",
"""
<span class="hljs-keyword">if</span> <span class="hljs-keyword">not</span> &quot;<span class="hljs-variable">%x%</span>&quot;==&quot;hello&quot; <span class="hljs-built_in">echo</span> no match
""");
    }

    [Fact]
    public void ControlFlow_IfElse()
    {
        AssertHighlighter("dos",
"""
if "%x%"=="" (
    echo empty
) else (
    echo has value: %x%
)
""",
"""
<span class="hljs-keyword">if</span> &quot;<span class="hljs-variable">%x%</span>&quot;==&quot;&quot; (
    <span class="hljs-built_in">echo</span> empty
) <span class="hljs-keyword">else</span> (
    <span class="hljs-built_in">echo</span> has value: <span class="hljs-variable">%x%</span>
)
""");
    }

    [Fact]
    public void ControlFlow_IfDefined()
    {
        AssertHighlighter("dos",
"""
if defined name echo Name is set
""",
"""
<span class="hljs-keyword">if</span> <span class="hljs-keyword">defined</span> name <span class="hljs-built_in">echo</span> Name is <span class="hljs-built_in">set</span>
""");
    }

    [Fact]
    public void ControlFlow_IfNotDefined()
    {
        AssertHighlighter("dos",
"""
if not defined name set name=default
""",
"""
<span class="hljs-keyword">if</span> <span class="hljs-keyword">not</span> <span class="hljs-keyword">defined</span> name <span class="hljs-built_in">set</span> name=default
""");
    }

    [Fact]
    public void ControlFlow_IfExist()
    {
        AssertHighlighter("dos",
"""
if exist config.ini echo found
""",
"""
<span class="hljs-keyword">if</span> <span class="hljs-keyword">exist</span> config.ini <span class="hljs-built_in">echo</span> found
""");
    }

    [Fact]
    public void ControlFlow_IfNotExist()
    {
        AssertHighlighter("dos",
"""
if not exist build mkdir build
""",
"""
<span class="hljs-keyword">if</span> <span class="hljs-keyword">not</span> <span class="hljs-keyword">exist</span> build <span class="hljs-built_in">mkdir</span> build
""");
    }

    [Fact]
    public void ControlFlow_IfErrorlevel()
    {
        AssertHighlighter("dos",
"""
if errorlevel 1 echo command failed
""",
"""
<span class="hljs-keyword">if</span> <span class="hljs-keyword">errorlevel</span> <span class="hljs-number">1</span> <span class="hljs-built_in">echo</span> command failed
""");
    }

    [Fact]
    public void ControlFlow_IfErrorlevelNeq()
    {
        AssertHighlighter("dos",
"""
if %ERRORLEVEL% NEQ 0 goto :error
""",
"""
<span class="hljs-keyword">if</span> <span class="hljs-variable">%ERRORLEVEL%</span> <span class="hljs-keyword">NEQ</span> <span class="hljs-number">0</span> <span class="hljs-keyword">goto</span> :error
""");
    }

    [Fact]
    public void ControlFlow_IfNumeric()
    {
        AssertHighlighter("dos",
"""
if %count% GTR 10 echo too many
""",
"""
<span class="hljs-keyword">if</span> <span class="hljs-variable">%count%</span> <span class="hljs-keyword">GTR</span> <span class="hljs-number">10</span> <span class="hljs-built_in">echo</span> too many
""");
    }

    [Fact]
    public void ControlFlow_IfCaseInsensitive()
    {
        AssertHighlighter("dos",
"""
if /I "%x%"=="hello" echo case-insensitive match
""",
"""
<span class="hljs-keyword">if</span> /I &quot;<span class="hljs-variable">%x%</span>&quot;==&quot;hello&quot; <span class="hljs-built_in">echo</span> case-insensitive match
""");
    }

    [Fact]
    public void ControlFlow_ForBasic()
    {
        AssertHighlighter("dos",
"""
for %%f in (*.txt) do echo %%f
""",
"""
<span class="hljs-keyword">for</span> <span class="hljs-variable">%%f</span> <span class="hljs-keyword">in</span> (*.txt) <span class="hljs-keyword">do</span> <span class="hljs-built_in">echo</span> <span class="hljs-variable">%%f</span>
""");
    }

    [Fact]
    public void ControlFlow_ForLinesInFile()
    {
        AssertHighlighter("dos",
"""
for /F "tokens=*" %%l in (input.txt) do echo %%l
""",
"""
<span class="hljs-keyword">for</span> /F &quot;tokens=*&quot; <span class="hljs-variable">%%l</span> <span class="hljs-keyword">in</span> (input.txt) <span class="hljs-keyword">do</span> <span class="hljs-built_in">echo</span> <span class="hljs-variable">%%l</span>
""");
    }

    [Fact]
    public void ControlFlow_ForLinesParse()
    {
        AssertHighlighter("dos",
"""
for /F "tokens=1,2 delims=," %%a in (data.csv) do echo %%a = %%b
""",
"""
<span class="hljs-keyword">for</span> /F &quot;tokens=<span class="hljs-number">1</span>,<span class="hljs-number">2</span> delims=,&quot; <span class="hljs-variable">%%a</span> <span class="hljs-keyword">in</span> (data.csv) <span class="hljs-keyword">do</span> <span class="hljs-built_in">echo</span> <span class="hljs-variable">%%a</span> = <span class="hljs-variable">%%b</span>
""");
    }

    [Fact]
    public void ControlFlow_ForLinesSkip()
    {
        AssertHighlighter("dos",
"""
for /F "skip=1 tokens=*" %%l in (data.txt) do echo %%l
""",
"""
<span class="hljs-keyword">for</span> /F &quot;skip=<span class="hljs-number">1</span> tokens=*&quot; <span class="hljs-variable">%%l</span> <span class="hljs-keyword">in</span> (data.txt) <span class="hljs-keyword">do</span> <span class="hljs-built_in">echo</span> <span class="hljs-variable">%%l</span>
""");
    }

    [Fact]
    public void ControlFlow_ForCommandOutput()
    {
        AssertHighlighter("dos",
"""
for /F "tokens=*" %%v in ('where git') do set GIT_PATH=%%v
""",
"""
<span class="hljs-keyword">for</span> /F &quot;tokens=*&quot; <span class="hljs-variable">%%v</span> <span class="hljs-keyword">in</span> (&#x27;where git&#x27;) <span class="hljs-keyword">do</span> <span class="hljs-built_in">set</span> GIT_PATH=<span class="hljs-variable">%%v</span>
""");
    }

    [Fact]
    public void ControlFlow_ForRecursive()
    {
        AssertHighlighter("dos",
"""
for /R %%f in (*.log) do echo %%f
""",
"""
<span class="hljs-keyword">for</span> /R <span class="hljs-variable">%%f</span> <span class="hljs-keyword">in</span> (*.log) <span class="hljs-keyword">do</span> <span class="hljs-built_in">echo</span> <span class="hljs-variable">%%f</span>
""");
    }

    [Fact]
    public void ControlFlow_ForDirs()
    {
        AssertHighlighter("dos",
"""
for /D %%d in (*) do echo Folder: %%d
""",
"""
<span class="hljs-keyword">for</span> /D <span class="hljs-variable">%%d</span> <span class="hljs-keyword">in</span> (*) <span class="hljs-keyword">do</span> <span class="hljs-built_in">echo</span> Folder: <span class="hljs-variable">%%d</span>
""");
    }

    [Fact]
    public void ControlFlow_ForNumbered()
    {
        AssertHighlighter("dos",
"""
for /L %%i in (1, 1, 10) do echo %%i
""",
"""
<span class="hljs-keyword">for</span> /L <span class="hljs-variable">%%i</span> <span class="hljs-keyword">in</span> (<span class="hljs-number">1</span>, <span class="hljs-number">1</span>, <span class="hljs-number">10</span>) <span class="hljs-keyword">do</span> <span class="hljs-built_in">echo</span> <span class="hljs-variable">%%i</span>
""");
    }

    [Fact]
    public void Redirection_Stdout()
    {
        AssertHighlighter("dos",
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
        AssertHighlighter("dos",
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
        AssertHighlighter("dos",
"""
somecmd 2> err.txt
""",
"""
somecmd <span class="hljs-number">2</span>&gt; err.txt
""");
    }

    [Fact]
    public void Redirection_MergeErr()
    {
        AssertHighlighter("dos",
"""
somecmd > out.txt 2>&1
""",
"""
somecmd &gt; out.txt <span class="hljs-number">2</span>&gt;&amp;<span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Redirection_Null()
    {
        AssertHighlighter("dos",
"""
somecmd > nul 2>&1
""",
"""
somecmd &gt; <span class="hljs-built_in">nul</span> <span class="hljs-number">2</span>&gt;&amp;<span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Redirection_StdinFromFile()
    {
        AssertHighlighter("dos",
"""
sort < input.txt
""",
"""
<span class="hljs-built_in">sort</span> &lt; input.txt
""");
    }

    [Fact]
    public void Chain_And()
    {
        AssertHighlighter("dos",
"""
mkdir build && cd build
""",
"""
<span class="hljs-built_in">mkdir</span> build &amp;&amp; <span class="hljs-built_in">cd</span> build
""");
    }

    [Fact]
    public void Chain_Or()
    {
        AssertHighlighter("dos",
"""
where git || echo Git not found
""",
"""
where git || <span class="hljs-built_in">echo</span> Git <span class="hljs-keyword">not</span> found
""");
    }

    [Fact]
    public void Chain_Semicolon()
    {
        AssertHighlighter("dos",
"""
echo first & echo second & echo third
""",
"""
<span class="hljs-built_in">echo</span> first &amp; <span class="hljs-built_in">echo</span> second &amp; <span class="hljs-built_in">echo</span> third
""");
    }

    [Fact]
    public void Comment_Rem()
    {
        AssertHighlighter("dos",
"""
rem this is a comment
""",
"""
<span class="hljs-comment">rem this is a comment</span>
""");
    }

    [Fact]
    public void Comment_DoubleColon()
    {
        AssertHighlighter("dos",
"""
:: alternative comment syntax
""",
"""
:: alternative comment syntax
""");
    }

    [Fact]
    public void Composite_HelloScript()
    {
        AssertHighlighter("dos",
"""
@echo off
setlocal

set name=World
if not "%~1"=="" set name=%~1

echo Hello, %name%!

endlocal
""",
"""
@<span class="hljs-built_in">echo</span> off
<span class="hljs-built_in">setlocal</span>

<span class="hljs-built_in">set</span> name=World
<span class="hljs-keyword">if</span> <span class="hljs-keyword">not</span> &quot;%~<span class="hljs-number">1</span>&quot;==&quot;&quot; <span class="hljs-built_in">set</span> name=%~<span class="hljs-number">1</span>

<span class="hljs-built_in">echo</span> Hello, <span class="hljs-variable">%name%</span>!

<span class="hljs-built_in">endlocal</span>
""");
    }

    [Fact]
    public void Composite_BackupScript()
    {
        AssertHighlighter("dos",
"""
@echo off
setlocal enableextensions enabledelayedexpansion

set SRC=%~1
set DEST=%~2
if "%SRC%"=="" set SRC=.\data
if "%DEST%"=="" set DEST=.\backup

if not exist "%DEST%" mkdir "%DEST%"

for /F "tokens=2 delims==" %%d in ('wmic os get localdatetime /value') do set DATETIME=%%d
set STAMP=!DATETIME:~0,8!-!DATETIME:~8,6!

robocopy "%SRC%" "%DEST%\!STAMP!" /MIR /R:3 /W:5

if !ERRORLEVEL! GEQ 8 (
    echo Backup failed with code !ERRORLEVEL!
    exit /B 1
)

echo Backup created at %DEST%\!STAMP!
endlocal
""",
"""
@<span class="hljs-built_in">echo</span> off
<span class="hljs-built_in">setlocal</span> enableextensions enabledelayedexpansion

<span class="hljs-built_in">set</span> SRC=%~<span class="hljs-number">1</span>
<span class="hljs-built_in">set</span> DEST=%~<span class="hljs-number">2</span>
<span class="hljs-keyword">if</span> &quot;<span class="hljs-variable">%SRC%</span>&quot;==&quot;&quot; <span class="hljs-built_in">set</span> SRC=.\data
<span class="hljs-keyword">if</span> &quot;<span class="hljs-variable">%DEST%</span>&quot;==&quot;&quot; <span class="hljs-built_in">set</span> DEST=.\backup

<span class="hljs-keyword">if</span> <span class="hljs-keyword">not</span> <span class="hljs-keyword">exist</span> &quot;<span class="hljs-variable">%DEST%</span>&quot; <span class="hljs-built_in">mkdir</span> &quot;<span class="hljs-variable">%DEST%</span>&quot;

<span class="hljs-keyword">for</span> /F &quot;tokens=<span class="hljs-number">2</span> delims==&quot; <span class="hljs-variable">%%d</span> <span class="hljs-keyword">in</span> (&#x27;wmic os get localdatetime /value&#x27;) <span class="hljs-keyword">do</span> <span class="hljs-built_in">set</span> DATETIME=<span class="hljs-variable">%%d</span>
<span class="hljs-built_in">set</span> STAMP=<span class="hljs-variable">!DATETIME:~0,8!</span>-<span class="hljs-variable">!DATETIME:~8,6!</span>

robocopy &quot;<span class="hljs-variable">%SRC%</span>&quot; &quot;<span class="hljs-variable">%DEST%</span>\<span class="hljs-variable">!STAMP!</span>&quot; /MIR /R:<span class="hljs-number">3</span> /W:<span class="hljs-number">5</span>

<span class="hljs-keyword">if</span> <span class="hljs-variable">!ERRORLEVEL!</span> <span class="hljs-keyword">GEQ</span> <span class="hljs-number">8</span> (
    <span class="hljs-built_in">echo</span> Backup failed with code <span class="hljs-variable">!ERRORLEVEL!</span>
    <span class="hljs-keyword">exit</span> /B <span class="hljs-number">1</span>
)

<span class="hljs-built_in">echo</span> Backup created <span class="hljs-built_in">at</span> <span class="hljs-variable">%DEST%</span>\<span class="hljs-variable">!STAMP!</span>
<span class="hljs-built_in">endlocal</span>
""");
    }

    [Fact]
    public void Composite_ArgParser()
    {
        AssertHighlighter("dos",
"""
@echo off
setlocal enableextensions

set VERBOSE=0
set NAME=

:loop
if "%~1"=="" goto :done
if /I "%~1"=="-v" (
    set VERBOSE=1
    shift
    goto :loop
)
if /I "%~1"=="--name" (
    set NAME=%~2
    shift
    shift
    goto :loop
)
echo Unknown arg: %~1
exit /B 1

:done
echo verbose=%VERBOSE% name=%NAME%
""",
"""
@<span class="hljs-built_in">echo</span> off
<span class="hljs-built_in">setlocal</span> enableextensions

<span class="hljs-built_in">set</span> VERBOSE=<span class="hljs-number">0</span>
<span class="hljs-built_in">set</span> NAME=

:loop
<span class="hljs-keyword">if</span> &quot;%~<span class="hljs-number">1</span>&quot;==&quot;&quot; <span class="hljs-keyword">goto</span> :done
<span class="hljs-keyword">if</span> /I &quot;%~<span class="hljs-number">1</span>&quot;==&quot;-v&quot; (
    <span class="hljs-built_in">set</span> VERBOSE=<span class="hljs-number">1</span>
    <span class="hljs-built_in">shift</span>
    <span class="hljs-keyword">goto</span> :loop
)
<span class="hljs-keyword">if</span> /I &quot;%~<span class="hljs-number">1</span>&quot;==&quot;--name&quot; (
    <span class="hljs-built_in">set</span> NAME=%~<span class="hljs-number">2</span>
    <span class="hljs-built_in">shift</span>
    <span class="hljs-built_in">shift</span>
    <span class="hljs-keyword">goto</span> :loop
)
<span class="hljs-built_in">echo</span> Unknown arg: %~<span class="hljs-number">1</span>
<span class="hljs-keyword">exit</span> /B <span class="hljs-number">1</span>

:done
<span class="hljs-built_in">echo</span> verbose=<span class="hljs-variable">%VERBOSE%</span> name=<span class="hljs-variable">%NAME%</span>
""");
    }

    [Fact]
    public void Composite_BuildScript()
    {
        AssertHighlighter("dos",
"""
@echo off
setlocal

echo [1/3] Restoring packages...
dotnet restore || goto :error

echo [2/3] Building...
dotnet build --configuration Release --no-restore || goto :error

echo [3/3] Testing...
dotnet test --no-build || goto :error

echo Build succeeded.
exit /B 0

:error
echo Build failed with code %ERRORLEVEL%.
exit /B %ERRORLEVEL%
""",
"""
@<span class="hljs-built_in">echo</span> off
<span class="hljs-built_in">setlocal</span>

<span class="hljs-built_in">echo</span> [<span class="hljs-number">1</span>/<span class="hljs-number">3</span>] Restoring packages...
dotnet <span class="hljs-built_in">restore</span> || <span class="hljs-keyword">goto</span> :error

<span class="hljs-built_in">echo</span> [<span class="hljs-number">2</span>/<span class="hljs-number">3</span>] Building...
dotnet build --configuration Release --no-<span class="hljs-built_in">restore</span> || <span class="hljs-keyword">goto</span> :error

<span class="hljs-built_in">echo</span> [<span class="hljs-number">3</span>/<span class="hljs-number">3</span>] Testing...
dotnet test --no-build || <span class="hljs-keyword">goto</span> :error

<span class="hljs-built_in">echo</span> Build succeeded.
<span class="hljs-keyword">exit</span> /B <span class="hljs-number">0</span>

:error
<span class="hljs-built_in">echo</span> Build failed with code <span class="hljs-variable">%ERRORLEVEL%</span>.
<span class="hljs-keyword">exit</span> /B <span class="hljs-variable">%ERRORLEVEL%</span>
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("dos",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("dos",
"""
rem just a comment
""",
"""
<span class="hljs-comment">rem just a comment</span>
""");
    }

    [Fact]
    public void SpecialEdge_OnlyLabel()
    {
        AssertHighlighter("dos",
"""
:main
""",
"""
:main
""");
    }

    [Fact]
    public void SpecialEdge_Caret()
    {
        AssertHighlighter("dos",
"""
echo a ^& b
""",
"""
<span class="hljs-built_in">echo</span> a ^&amp; b
""");
    }

    [Fact]
    public void SpecialEdge_CaretContinue()
    {
        AssertHighlighter("dos",
"""
echo first ^
  second ^
  third
""",
"""
<span class="hljs-built_in">echo</span> first ^
  second ^
  third
""");
    }
}
