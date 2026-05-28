namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class PowerShellHighlighterTests
{

    [Fact]
    public void Variable_Simple()
    {
        AssertHighlighter("powershell",
"""
$name = "alice"
""",
"""
<span class="hljs-variable">$name</span> = <span class="hljs-string">&quot;alice&quot;</span>
""");
    }

    [Fact]
    public void Variable_Integer()
    {
        AssertHighlighter("powershell",
"""
$count = 42
""",
"""
<span class="hljs-variable">$count</span> = <span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void Variable_Reference()
    {
        AssertHighlighter("powershell",
"""
Write-Host $name
""",
"""
<span class="hljs-built_in">Write-Host</span> <span class="hljs-variable">$name</span>
""");
    }

    [Fact]
    public void Variable_PropertyAccess()
    {
        AssertHighlighter("powershell",
"""
$user.Name
""",
"""
<span class="hljs-variable">$user</span>.Name
""");
    }

    [Fact]
    public void Variable_Indexer()
    {
        AssertHighlighter("powershell",
"""
$arr[0]
""",
"""
<span class="hljs-variable">$arr</span>[<span class="hljs-number">0</span>]
""");
    }

    [Fact]
    public void Variable_ScopeGlobal()
    {
        AssertHighlighter("powershell",
"""
$global:counter = 0
""",
"""
<span class="hljs-variable">$global:counter</span> = <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void Variable_ScopeScript()
    {
        AssertHighlighter("powershell",
"""
$script:logger = $null
""",
"""
<span class="hljs-variable">$script:logger</span> = <span class="hljs-variable">$null</span>
""");
    }

    [Fact]
    public void Variable_ScopeLocal()
    {
        AssertHighlighter("powershell",
"""
$local:temp = 1
""",
"""
<span class="hljs-variable">$local:temp</span> = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Variable_ScopePrivate()
    {
        AssertHighlighter("powershell",
"""
$private:secret = "hi"
""",
"""
<span class="hljs-variable">$private:secret</span> = <span class="hljs-string">&quot;hi&quot;</span>
""");
    }

    [Fact]
    public void Variable_Env()
    {
        AssertHighlighter("powershell",
"""
$env:PATH
""",
"""
<span class="hljs-variable">$env:PATH</span>
""");
    }

    [Fact]
    public void Variable_EnvAssign()
    {
        AssertHighlighter("powershell",
"""
$env:DEBUG = "1"
""",
"""
<span class="hljs-variable">$env:DEBUG</span> = <span class="hljs-string">&quot;1&quot;</span>
""");
    }

    [Fact]
    public void Variable_AutomaticTrue()
    {
        AssertHighlighter("powershell",
"""
$x = $true
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-variable">$true</span>
""");
    }

    [Fact]
    public void Variable_AutomaticFalse()
    {
        AssertHighlighter("powershell",
"""
$x = $false
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-variable">$false</span>
""");
    }

    [Fact]
    public void Variable_AutomaticNull()
    {
        AssertHighlighter("powershell",
"""
$x = $null
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-variable">$null</span>
""");
    }

    [Fact]
    public void Variable_AutomaticUnderscore()
    {
        AssertHighlighter("powershell",
"""
Get-Item | ForEach-Object { $_.Name }
""",
"""
<span class="hljs-built_in">Get-Item</span> | <span class="hljs-built_in">ForEach-Object</span> { <span class="hljs-variable">$_</span>.Name }
""");
    }

    [Fact]
    public void Variable_AutomaticArgs()
    {
        AssertHighlighter("powershell",
"""
function f { $args }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">f</span></span> { <span class="hljs-variable">$args</span> }
""");
    }

    [Fact]
    public void Variable_AutomaticPsItem()
    {
        AssertHighlighter("powershell",
"""
Get-Process | Where-Object { $PSItem.CPU -gt 100 }
""",
"""
<span class="hljs-built_in">Get-Process</span> | <span class="hljs-built_in">Where-Object</span> { <span class="hljs-variable">$PSItem</span>.CPU <span class="hljs-operator">-gt</span> <span class="hljs-number">100</span> }
""");
    }

    [Fact]
    public void Variable_AutomaticHome()
    {
        AssertHighlighter("powershell",
"""
cd $HOME
""",
"""
<span class="hljs-built_in">cd</span> <span class="hljs-variable">$HOME</span>
""");
    }

    [Fact]
    public void Variable_AutomaticError()
    {
        AssertHighlighter("powershell",
"""
$Error[0]
""",
"""
<span class="hljs-variable">$Error</span>[<span class="hljs-number">0</span>]
""");
    }

    [Fact]
    public void Variable_AutomaticHost()
    {
        AssertHighlighter("powershell",
"""
$Host.Version
""",
"""
<span class="hljs-variable">$Host</span>.Version
""");
    }

    [Fact]
    public void Variable_AutomaticPsVersionTable()
    {
        AssertHighlighter("powershell",
"""
$PSVersionTable
""",
"""
<span class="hljs-variable">$PSVersionTable</span>
""");
    }

    [Fact]
    public void Variable_BracedName()
    {
        AssertHighlighter("powershell",
"""
${complex name with spaces} = 1
""",
"""
<span class="hljs-variable">$</span>{complex name with spaces} = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Variable_TypedDecl()
    {
        AssertHighlighter("powershell",
"""
[int]$count = 42
""",
"""
[<span class="hljs-built_in">int</span>]<span class="hljs-variable">$count</span> = <span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void Variable_CastInline()
    {
        AssertHighlighter("powershell",
"""
$n = [int]"42"
""",
"""
<span class="hljs-variable">$n</span> = [<span class="hljs-built_in">int</span>]<span class="hljs-string">&quot;42&quot;</span>
""");
    }

    [Fact]
    public void Cmdlet_GetProcess()
    {
        AssertHighlighter("powershell",
"""
Get-Process
""",
"""
<span class="hljs-built_in">Get-Process</span>
""");
    }

    [Fact]
    public void Cmdlet_GetChildItem()
    {
        AssertHighlighter("powershell",
"""
Get-ChildItem -Path C:\ -Recurse
""",
"""
<span class="hljs-built_in">Get-ChildItem</span> <span class="hljs-literal">-Path</span> C:\ <span class="hljs-literal">-Recurse</span>
""");
    }

    [Fact]
    public void Cmdlet_WriteHost()
    {
        AssertHighlighter("powershell",
"""
Write-Host "Hello, world!"
""",
"""
<span class="hljs-built_in">Write-Host</span> <span class="hljs-string">&quot;Hello, world!&quot;</span>
""");
    }

    [Fact]
    public void Cmdlet_WriteOutput()
    {
        AssertHighlighter("powershell",
"""
Write-Output $value
""",
"""
<span class="hljs-built_in">Write-Output</span> <span class="hljs-variable">$value</span>
""");
    }

    [Fact]
    public void Cmdlet_WriteVerbose()
    {
        AssertHighlighter("powershell",
"""
Write-Verbose "Loading config..."
""",
"""
<span class="hljs-built_in">Write-Verbose</span> <span class="hljs-string">&quot;Loading config...&quot;</span>
""");
    }

    [Fact]
    public void Cmdlet_WriteError()
    {
        AssertHighlighter("powershell",
"""
Write-Error "Something went wrong"
""",
"""
<span class="hljs-built_in">Write-Error</span> <span class="hljs-string">&quot;Something went wrong&quot;</span>
""");
    }

    [Fact]
    public void Cmdlet_NewItem()
    {
        AssertHighlighter("powershell",
"""
New-Item -ItemType Directory -Path "C:\logs"
""",
"""
<span class="hljs-built_in">New-Item</span> <span class="hljs-literal">-ItemType</span> Directory <span class="hljs-literal">-Path</span> <span class="hljs-string">&quot;C:\logs&quot;</span>
""");
    }

    [Fact]
    public void Cmdlet_RemoveItem()
    {
        AssertHighlighter("powershell",
"""
Remove-Item -Path "C:\temp" -Recurse -Force
""",
"""
<span class="hljs-built_in">Remove-Item</span> <span class="hljs-literal">-Path</span> <span class="hljs-string">&quot;C:\temp&quot;</span> <span class="hljs-literal">-Recurse</span> <span class="hljs-literal">-Force</span>
""");
    }

    [Fact]
    public void Cmdlet_SelectObject()
    {
        AssertHighlighter("powershell",
"""
Get-Process | Select-Object Name, CPU, WS
""",
"""
<span class="hljs-built_in">Get-Process</span> | <span class="hljs-built_in">Select-Object</span> Name, CPU, WS
""");
    }

    [Fact]
    public void Cmdlet_WhereObject()
    {
        AssertHighlighter("powershell",
"""
Get-Process | Where-Object { $_.CPU -gt 100 }
""",
"""
<span class="hljs-built_in">Get-Process</span> | <span class="hljs-built_in">Where-Object</span> { <span class="hljs-variable">$_</span>.CPU <span class="hljs-operator">-gt</span> <span class="hljs-number">100</span> }
""");
    }

    [Fact]
    public void Cmdlet_ForEachObject()
    {
        AssertHighlighter("powershell",
"""
Get-ChildItem | ForEach-Object { $_.FullName }
""",
"""
<span class="hljs-built_in">Get-ChildItem</span> | <span class="hljs-built_in">ForEach-Object</span> { <span class="hljs-variable">$_</span>.FullName }
""");
    }

    [Fact]
    public void Cmdlet_SortObject()
    {
        AssertHighlighter("powershell",
"""
Get-Process | Sort-Object CPU -Descending
""",
"""
<span class="hljs-built_in">Get-Process</span> | <span class="hljs-built_in">Sort-Object</span> CPU <span class="hljs-literal">-Descending</span>
""");
    }

    [Fact]
    public void Cmdlet_GroupObject()
    {
        AssertHighlighter("powershell",
"""
Get-Process | Group-Object Company
""",
"""
<span class="hljs-built_in">Get-Process</span> | <span class="hljs-built_in">Group-Object</span> Company
""");
    }

    [Fact]
    public void Cmdlet_MeasureObject()
    {
        AssertHighlighter("powershell",
"""
Get-Process | Measure-Object WS -Sum -Average
""",
"""
<span class="hljs-built_in">Get-Process</span> | <span class="hljs-built_in">Measure-Object</span> WS <span class="hljs-literal">-Sum</span> <span class="hljs-literal">-Average</span>
""");
    }

    [Fact]
    public void Cmdlet_InvokeWebRequest()
    {
        AssertHighlighter("powershell",
"""
Invoke-WebRequest -Uri "https://example.com" -OutFile data.html
""",
"""
<span class="hljs-built_in">Invoke-WebRequest</span> <span class="hljs-literal">-Uri</span> <span class="hljs-string">&quot;https://example.com&quot;</span> <span class="hljs-literal">-OutFile</span> data.html
""");
    }

    [Fact]
    public void Cmdlet_InvokeRestMethod()
    {
        AssertHighlighter("powershell",
"""
$data = Invoke-RestMethod -Uri "https://api.example.com/users" -Method Get
""",
"""
<span class="hljs-variable">$data</span> = <span class="hljs-built_in">Invoke-RestMethod</span> <span class="hljs-literal">-Uri</span> <span class="hljs-string">&quot;https://api.example.com/users&quot;</span> <span class="hljs-literal">-Method</span> Get
""");
    }

    [Fact]
    public void Cmdlet_StartProcess()
    {
        AssertHighlighter("powershell",
"""
Start-Process -FilePath "notepad.exe" -ArgumentList "C:\readme.txt"
""",
"""
<span class="hljs-built_in">Start-Process</span> <span class="hljs-literal">-FilePath</span> <span class="hljs-string">&quot;notepad.exe&quot;</span> <span class="hljs-literal">-ArgumentList</span> <span class="hljs-string">&quot;C:\readme.txt&quot;</span>
""");
    }

    [Fact]
    public void Cmdlet_ImportModule()
    {
        AssertHighlighter("powershell",
"""
Import-Module Az
""",
"""
<span class="hljs-built_in">Import-Module</span> Az
""");
    }

    [Fact]
    public void Cmdlet_ExportCsv()
    {
        AssertHighlighter("powershell",
"""
Get-Process | Export-Csv -Path procs.csv -NoTypeInformation
""",
"""
<span class="hljs-built_in">Get-Process</span> | <span class="hljs-built_in">Export-Csv</span> <span class="hljs-literal">-Path</span> procs.csv <span class="hljs-literal">-NoTypeInformation</span>
""");
    }

    [Fact]
    public void Cmdlet_ConvertToJson()
    {
        AssertHighlighter("powershell",
"""
$user | ConvertTo-Json -Depth 5
""",
"""
<span class="hljs-variable">$user</span> | <span class="hljs-built_in">ConvertTo-Json</span> <span class="hljs-literal">-Depth</span> <span class="hljs-number">5</span>
""");
    }

    [Fact]
    public void Cmdlet_ConvertFromJson()
    {
        AssertHighlighter("powershell",
"""
$data = Get-Content config.json | ConvertFrom-Json
""",
"""
<span class="hljs-variable">$data</span> = <span class="hljs-built_in">Get-Content</span> config.json | <span class="hljs-built_in">ConvertFrom-Json</span>
""");
    }

    [Fact]
    public void Cmdlet_TestPath()
    {
        AssertHighlighter("powershell",
"""
if (Test-Path -Path "C:\app") { Write-Host "exists" }
""",
"""
<span class="hljs-keyword">if</span> (<span class="hljs-built_in">Test-Path</span> <span class="hljs-literal">-Path</span> <span class="hljs-string">&quot;C:\app&quot;</span>) { <span class="hljs-built_in">Write-Host</span> <span class="hljs-string">&quot;exists&quot;</span> }
""");
    }

    [Fact]
    public void Cmdlet_AliasGcm()
    {
        AssertHighlighter("powershell",
"""
gcm Get-Process
""",
"""
<span class="hljs-built_in">gcm</span> <span class="hljs-built_in">Get-Process</span>
""");
    }

    [Fact]
    public void Cmdlet_AliasLs()
    {
        AssertHighlighter("powershell",
"""
ls *.txt
""",
"""
<span class="hljs-built_in">ls</span> *.txt
""");
    }

    [Fact]
    public void Cmdlet_AliasGc()
    {
        AssertHighlighter("powershell",
"""
gc README.md
""",
"""
<span class="hljs-built_in">gc</span> README.md
""");
    }

    [Fact]
    public void Parameter_Switch()
    {
        AssertHighlighter("powershell",
"""
Get-Process -Verbose
""",
"""
<span class="hljs-built_in">Get-Process</span> <span class="hljs-literal">-Verbose</span>
""");
    }

    [Fact]
    public void Parameter_NamedString()
    {
        AssertHighlighter("powershell",
"""
Get-Process -Name explorer
""",
"""
<span class="hljs-built_in">Get-Process</span> <span class="hljs-literal">-Name</span> explorer
""");
    }

    [Fact]
    public void Parameter_NamedQuoted()
    {
        AssertHighlighter("powershell",
"""
Get-Process -Name "Visual Studio"
""",
"""
<span class="hljs-built_in">Get-Process</span> <span class="hljs-literal">-Name</span> <span class="hljs-string">&quot;Visual Studio&quot;</span>
""");
    }

    [Fact]
    public void Parameter_CommonParam()
    {
        AssertHighlighter("powershell",
"""
Get-Process -ErrorAction SilentlyContinue
""",
"""
<span class="hljs-built_in">Get-Process</span> <span class="hljs-literal">-ErrorAction</span> SilentlyContinue
""");
    }

    [Fact]
    public void Parameter_WhatIf()
    {
        AssertHighlighter("powershell",
"""
Remove-Item temp.txt -WhatIf
""",
"""
<span class="hljs-built_in">Remove-Item</span> temp.txt <span class="hljs-literal">-WhatIf</span>
""");
    }

    [Fact]
    public void Parameter_Confirm()
    {
        AssertHighlighter("powershell",
"""
Remove-Item temp.txt -Confirm
""",
"""
<span class="hljs-built_in">Remove-Item</span> temp.txt <span class="hljs-literal">-Confirm</span>
""");
    }

    [Fact]
    public void Parameter_Positional()
    {
        AssertHighlighter("powershell",
"""
Get-Process explorer
""",
"""
<span class="hljs-built_in">Get-Process</span> explorer
""");
    }

    [Fact]
    public void ComparisonOperator_Eq()
    {
        AssertHighlighter("powershell",
"""
$a -eq $b
""",
"""
<span class="hljs-variable">$a</span> <span class="hljs-operator">-eq</span> <span class="hljs-variable">$b</span>
""");
    }

    [Fact]
    public void ComparisonOperator_Ne()
    {
        AssertHighlighter("powershell",
"""
$a -ne $b
""",
"""
<span class="hljs-variable">$a</span> <span class="hljs-operator">-ne</span> <span class="hljs-variable">$b</span>
""");
    }

    [Fact]
    public void ComparisonOperator_Lt()
    {
        AssertHighlighter("powershell",
"""
$a -lt $b
""",
"""
<span class="hljs-variable">$a</span> <span class="hljs-operator">-lt</span> <span class="hljs-variable">$b</span>
""");
    }

    [Fact]
    public void ComparisonOperator_Gt()
    {
        AssertHighlighter("powershell",
"""
$a -gt $b
""",
"""
<span class="hljs-variable">$a</span> <span class="hljs-operator">-gt</span> <span class="hljs-variable">$b</span>
""");
    }

    [Fact]
    public void ComparisonOperator_Le()
    {
        AssertHighlighter("powershell",
"""
$a -le $b
""",
"""
<span class="hljs-variable">$a</span> <span class="hljs-operator">-le</span> <span class="hljs-variable">$b</span>
""");
    }

    [Fact]
    public void ComparisonOperator_Ge()
    {
        AssertHighlighter("powershell",
"""
$a -ge $b
""",
"""
<span class="hljs-variable">$a</span> <span class="hljs-operator">-ge</span> <span class="hljs-variable">$b</span>
""");
    }

    [Fact]
    public void ComparisonOperator_CaseSensEq()
    {
        AssertHighlighter("powershell",
"""
$a -ceq $b
""",
"""
<span class="hljs-variable">$a</span> <span class="hljs-operator">-ceq</span> <span class="hljs-variable">$b</span>
""");
    }

    [Fact]
    public void ComparisonOperator_ExplicitInsensEq()
    {
        AssertHighlighter("powershell",
"""
$a -ieq $b
""",
"""
<span class="hljs-variable">$a</span> <span class="hljs-operator">-ieq</span> <span class="hljs-variable">$b</span>
""");
    }

    [Fact]
    public void ComparisonOperator_Like()
    {
        AssertHighlighter("powershell",
"""
$name -like "*alice*"
""",
"""
<span class="hljs-variable">$name</span> <span class="hljs-operator">-like</span> <span class="hljs-string">&quot;*alice*&quot;</span>
""");
    }

    [Fact]
    public void ComparisonOperator_NotLike()
    {
        AssertHighlighter("powershell",
"""
$name -notlike "*test*"
""",
"""
<span class="hljs-variable">$name</span> <span class="hljs-operator">-notlike</span> <span class="hljs-string">&quot;*test*&quot;</span>
""");
    }

    [Fact]
    public void ComparisonOperator_Match()
    {
        AssertHighlighter("powershell",
"""
$text -match "^\d{3}-\d{4}$"
""",
"""
<span class="hljs-variable">$text</span> <span class="hljs-operator">-match</span> <span class="hljs-string">&quot;^\d{3}-\d{4}<span class="hljs-variable">$</span>&quot;</span>
""");
    }

    [Fact]
    public void ComparisonOperator_NotMatch()
    {
        AssertHighlighter("powershell",
"""
$text -notmatch "@example.com"
""",
"""
<span class="hljs-variable">$text</span> <span class="hljs-operator">-notmatch</span> <span class="hljs-string">&quot;@example.com&quot;</span>
""");
    }

    [Fact]
    public void ComparisonOperator_Contains()
    {
        AssertHighlighter("powershell",
"""
$list -contains "alice"
""",
"""
<span class="hljs-variable">$list</span> <span class="hljs-operator">-contains</span> <span class="hljs-string">&quot;alice&quot;</span>
""");
    }

    [Fact]
    public void ComparisonOperator_NotContains()
    {
        AssertHighlighter("powershell",
"""
$list -notcontains "bob"
""",
"""
<span class="hljs-variable">$list</span> <span class="hljs-operator">-notcontains</span> <span class="hljs-string">&quot;bob&quot;</span>
""");
    }

    [Fact]
    public void ComparisonOperator_In()
    {
        AssertHighlighter("powershell",
"""
"alice" -in $list
""",
"""
<span class="hljs-string">&quot;alice&quot;</span> <span class="hljs-operator">-in</span> <span class="hljs-variable">$list</span>
""");
    }

    [Fact]
    public void ComparisonOperator_NotIn()
    {
        AssertHighlighter("powershell",
"""
"bob" -notin $list
""",
"""
<span class="hljs-string">&quot;bob&quot;</span> <span class="hljs-operator">-notin</span> <span class="hljs-variable">$list</span>
""");
    }

    [Fact]
    public void ComparisonOperator_Is()
    {
        AssertHighlighter("powershell",
"""
$x -is [int]
""",
"""
<span class="hljs-variable">$x</span> <span class="hljs-operator">-is</span> [<span class="hljs-built_in">int</span>]
""");
    }

    [Fact]
    public void ComparisonOperator_IsNot()
    {
        AssertHighlighter("powershell",
"""
$x -isnot [string]
""",
"""
<span class="hljs-variable">$x</span> <span class="hljs-operator">-isnot</span> [<span class="hljs-built_in">string</span>]
""");
    }

    [Fact]
    public void ComparisonOperator_As()
    {
        AssertHighlighter("powershell",
"""
$x = "42" -as [int]
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-string">&quot;42&quot;</span> <span class="hljs-operator">-as</span> [<span class="hljs-built_in">int</span>]
""");
    }

    [Fact]
    public void ComparisonOperator_Replace()
    {
        AssertHighlighter("powershell",
"""
$text -replace "old", "new"
""",
"""
<span class="hljs-variable">$text</span> <span class="hljs-operator">-replace</span> <span class="hljs-string">&quot;old&quot;</span>, <span class="hljs-string">&quot;new&quot;</span>
""");
    }

    [Fact]
    public void ComparisonOperator_Split()
    {
        AssertHighlighter("powershell",
"""
$text -split ","
""",
"""
<span class="hljs-variable">$text</span> <span class="hljs-operator">-split</span> <span class="hljs-string">&quot;,&quot;</span>
""");
    }

    [Fact]
    public void ComparisonOperator_Join()
    {
        AssertHighlighter("powershell",
"""
$items -join ", "
""",
"""
<span class="hljs-variable">$items</span> <span class="hljs-operator">-join</span> <span class="hljs-string">&quot;, &quot;</span>
""");
    }

    [Fact]
    public void LogicalOperator_And()
    {
        AssertHighlighter("powershell",
"""
$a -and $b
""",
"""
<span class="hljs-variable">$a</span> <span class="hljs-operator">-and</span> <span class="hljs-variable">$b</span>
""");
    }

    [Fact]
    public void LogicalOperator_Or()
    {
        AssertHighlighter("powershell",
"""
$a -or $b
""",
"""
<span class="hljs-variable">$a</span> <span class="hljs-operator">-or</span> <span class="hljs-variable">$b</span>
""");
    }

    [Fact]
    public void LogicalOperator_Xor()
    {
        AssertHighlighter("powershell",
"""
$a -xor $b
""",
"""
<span class="hljs-variable">$a</span> <span class="hljs-operator">-xor</span> <span class="hljs-variable">$b</span>
""");
    }

    [Fact]
    public void LogicalOperator_Not()
    {
        AssertHighlighter("powershell",
"""
-not $a
""",
"""
<span class="hljs-operator">-not</span> <span class="hljs-variable">$a</span>
""");
    }

    [Fact]
    public void LogicalOperator_Bang()
    {
        AssertHighlighter("powershell",
"""
!$a
""",
"""
!<span class="hljs-variable">$a</span>
""");
    }

    [Fact]
    public void BitwiseOperator_Band()
    {
        AssertHighlighter("powershell",
"""
$a -band 0xFF
""",
"""
<span class="hljs-variable">$a</span> <span class="hljs-operator">-band</span> <span class="hljs-number">0</span>xFF
""");
    }

    [Fact]
    public void BitwiseOperator_Bor()
    {
        AssertHighlighter("powershell",
"""
$a -bor 0x80
""",
"""
<span class="hljs-variable">$a</span> <span class="hljs-operator">-bor</span> <span class="hljs-number">0</span>x80
""");
    }

    [Fact]
    public void BitwiseOperator_Bxor()
    {
        AssertHighlighter("powershell",
"""
$a -bxor 0x10
""",
"""
<span class="hljs-variable">$a</span> <span class="hljs-operator">-bxor</span> <span class="hljs-number">0</span>x10
""");
    }

    [Fact]
    public void BitwiseOperator_Bnot()
    {
        AssertHighlighter("powershell",
"""
-bnot $a
""",
"""
<span class="hljs-operator">-bnot</span> <span class="hljs-variable">$a</span>
""");
    }

    [Fact]
    public void BitwiseOperator_Shl()
    {
        AssertHighlighter("powershell",
"""
$a -shl 2
""",
"""
<span class="hljs-variable">$a</span> <span class="hljs-operator">-shl</span> <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void BitwiseOperator_Shr()
    {
        AssertHighlighter("powershell",
"""
$a -shr 1
""",
"""
<span class="hljs-variable">$a</span> <span class="hljs-operator">-shr</span> <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void ArithmeticOperator_Add()
    {
        AssertHighlighter("powershell",
"""
$x = 1 + 2
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-number">1</span> + <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void ArithmeticOperator_Sub()
    {
        AssertHighlighter("powershell",
"""
$x = 5 - 3
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-number">5</span> - <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void ArithmeticOperator_Mul()
    {
        AssertHighlighter("powershell",
"""
$x = 4 * 7
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-number">4</span> * <span class="hljs-number">7</span>
""");
    }

    [Fact]
    public void ArithmeticOperator_Div()
    {
        AssertHighlighter("powershell",
"""
$x = 10 / 2
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-number">10</span> / <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void ArithmeticOperator_Mod()
    {
        AssertHighlighter("powershell",
"""
$x = 10 % 3
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-number">10</span> % <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void ArithmeticOperator_AddAssign()
    {
        AssertHighlighter("powershell",
"""
$x += 1
""",
"""
<span class="hljs-variable">$x</span> += <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void ArithmeticOperator_SubAssign()
    {
        AssertHighlighter("powershell",
"""
$x -= 1
""",
"""
<span class="hljs-variable">$x</span> -= <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void ArithmeticOperator_MulAssign()
    {
        AssertHighlighter("powershell",
"""
$x *= 2
""",
"""
<span class="hljs-variable">$x</span> *= <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void ArithmeticOperator_DivAssign()
    {
        AssertHighlighter("powershell",
"""
$x /= 2
""",
"""
<span class="hljs-variable">$x</span> /= <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void ArithmeticOperator_ModAssign()
    {
        AssertHighlighter("powershell",
"""
$x %= 5
""",
"""
<span class="hljs-variable">$x</span> %= <span class="hljs-number">5</span>
""");
    }

    [Fact]
    public void ArithmeticOperator_Inc()
    {
        AssertHighlighter("powershell",
"""
$x++
""",
"""
<span class="hljs-variable">$x</span>++
""");
    }

    [Fact]
    public void ArithmeticOperator_Dec()
    {
        AssertHighlighter("powershell",
"""
$x--
""",
"""
<span class="hljs-variable">$x</span><span class="hljs-literal">--</span>
""");
    }

    [Fact]
    public void Format_Simple()
    {
        AssertHighlighter("powershell",
"""
"Name: {0}" -f $name
""",
"""
<span class="hljs-string">&quot;Name: {0}&quot;</span> <span class="hljs-operator">-f</span> <span class="hljs-variable">$name</span>
""");
    }

    [Fact]
    public void Format_Multi()
    {
        AssertHighlighter("powershell",
"""
"{0} = {1}" -f $key, $value
""",
"""
<span class="hljs-string">&quot;{0} = {1}&quot;</span> <span class="hljs-operator">-f</span> <span class="hljs-variable">$key</span>, <span class="hljs-variable">$value</span>
""");
    }

    [Fact]
    public void Format_Numeric()
    {
        AssertHighlighter("powershell",
"""
"{0:N2}" -f 1234.567
""",
"""
<span class="hljs-string">&quot;{0:N2}&quot;</span> <span class="hljs-operator">-f</span> <span class="hljs-number">1234.567</span>
""");
    }

    [Fact]
    public void Format_Date()
    {
        AssertHighlighter("powershell",
"""
"{0:yyyy-MM-dd}" -f (Get-Date)
""",
"""
<span class="hljs-string">&quot;{0:yyyy-MM-dd}&quot;</span> <span class="hljs-operator">-f</span> (<span class="hljs-built_in">Get-Date</span>)
""");
    }

    [Fact]
    public void Range_Simple()
    {
        AssertHighlighter("powershell",
"""
1..10
""",
"""
<span class="hljs-number">1</span>..<span class="hljs-number">10</span>
""");
    }

    [Fact]
    public void Range_Negative()
    {
        AssertHighlighter("powershell",
"""
-5..5
""",
"""
<span class="hljs-literal">-5</span>..<span class="hljs-number">5</span>
""");
    }

    [Fact]
    public void Range_Descending()
    {
        AssertHighlighter("powershell",
"""
10..1
""",
"""
<span class="hljs-number">10</span>..<span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Range_InLoop()
    {
        AssertHighlighter("powershell",
"""
foreach ($i in 1..5) { Write-Host $i }
""",
"""
<span class="hljs-keyword">foreach</span> (<span class="hljs-variable">$i</span> <span class="hljs-keyword">in</span> <span class="hljs-number">1</span>..<span class="hljs-number">5</span>) { <span class="hljs-built_in">Write-Host</span> <span class="hljs-variable">$i</span> }
""");
    }

    [Fact]
    public void PipelineOperator_Single()
    {
        AssertHighlighter("powershell",
"""
Get-Process | Sort-Object Name
""",
"""
<span class="hljs-built_in">Get-Process</span> | <span class="hljs-built_in">Sort-Object</span> Name
""");
    }

    [Fact]
    public void PipelineOperator_Multi()
    {
        AssertHighlighter("powershell",
"""
Get-Process | Where-Object { $_.CPU -gt 10 } | Sort-Object CPU -Descending | Select-Object -First 5
""",
"""
<span class="hljs-built_in">Get-Process</span> | <span class="hljs-built_in">Where-Object</span> { <span class="hljs-variable">$_</span>.CPU <span class="hljs-operator">-gt</span> <span class="hljs-number">10</span> } | <span class="hljs-built_in">Sort-Object</span> CPU <span class="hljs-literal">-Descending</span> | <span class="hljs-built_in">Select-Object</span> <span class="hljs-literal">-First</span> <span class="hljs-number">5</span>
""");
    }

    [Fact]
    public void PipelineOperator_ChainAnd()
    {
        AssertHighlighter("powershell",
"""
Get-Item file.txt && Remove-Item file.txt
""",
"""
<span class="hljs-built_in">Get-Item</span> file.txt <span class="hljs-operator">&amp;&amp;</span> <span class="hljs-built_in">Remove-Item</span> file.txt
""");
    }

    [Fact]
    public void PipelineOperator_ChainOr()
    {
        AssertHighlighter("powershell",
"""
Test-Path file.txt || New-Item file.txt
""",
"""
<span class="hljs-built_in">Test-Path</span> file.txt <span class="hljs-operator">||</span> <span class="hljs-built_in">New-Item</span> file.txt
""");
    }

    [Fact]
    public void PipelineOperator_Background()
    {
        AssertHighlighter("powershell",
"""
Get-Process &
""",
"""
<span class="hljs-built_in">Get-Process</span> &amp;
""");
    }

    [Fact]
    public void Redirection_Stdout()
    {
        AssertHighlighter("powershell",
"""
Get-Process > procs.txt
""",
"""
<span class="hljs-built_in">Get-Process</span> &gt; procs.txt
""");
    }

    [Fact]
    public void Redirection_Append()
    {
        AssertHighlighter("powershell",
"""
Get-Process >> procs.txt
""",
"""
<span class="hljs-built_in">Get-Process</span> &gt;&gt; procs.txt
""");
    }

    [Fact]
    public void Redirection_Stderr()
    {
        AssertHighlighter("powershell",
"""
cmd /c invalid 2> err.txt
""",
"""
cmd /c invalid <span class="hljs-number">2</span>&gt; err.txt
""");
    }

    [Fact]
    public void Redirection_MergeErr()
    {
        AssertHighlighter("powershell",
"""
Get-Process 2>&1 | Out-File log.txt
""",
"""
<span class="hljs-built_in">Get-Process</span> <span class="hljs-number">2</span>&gt;&amp;<span class="hljs-number">1</span> | <span class="hljs-built_in">Out-File</span> log.txt
""");
    }

    [Fact]
    public void Redirection_AllStreams()
    {
        AssertHighlighter("powershell",
"""
Get-Process *>&1 | Out-File log.txt
""",
"""
<span class="hljs-built_in">Get-Process</span> *&gt;&amp;<span class="hljs-number">1</span> | <span class="hljs-built_in">Out-File</span> log.txt
""");
    }

    [Fact]
    public void CallOperator_Invoke()
    {
        AssertHighlighter("powershell",
"""
& $cmdName -Arg1 "value"
""",
"""
&amp; <span class="hljs-variable">$cmdName</span> <span class="hljs-literal">-Arg1</span> <span class="hljs-string">&quot;value&quot;</span>
""");
    }

    [Fact]
    public void CallOperator_DotSource()
    {
        AssertHighlighter("powershell",
"""
. ./common.ps1
""",
"""
. ./common.ps1
""");
    }

    [Fact]
    public void String_SingleQuote()
    {
        AssertHighlighter("powershell",
"""
$s = 'hello'
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">&#x27;hello&#x27;</span>
""");
    }

    [Fact]
    public void String_DoubleQuote()
    {
        AssertHighlighter("powershell",
"""
$s = "hello"
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">&quot;hello&quot;</span>
""");
    }

    [Fact]
    public void String_InterpVariable()
    {
        AssertHighlighter("powershell",
"""
$msg = "Hello $name"
""",
"""
<span class="hljs-variable">$msg</span> = <span class="hljs-string">&quot;Hello <span class="hljs-variable">$name</span>&quot;</span>
""");
    }

    [Fact]
    public void String_InterpProperty()
    {
        AssertHighlighter("powershell",
"""
$msg = "User: $($user.Name)"
""",
"""
<span class="hljs-variable">$msg</span> = <span class="hljs-string">&quot;User: <span class="hljs-variable">$</span>(<span class="hljs-variable">$user</span>.Name)&quot;</span>
""");
    }

    [Fact]
    public void String_InterpExpression()
    {
        AssertHighlighter("powershell",
"""
$msg = "Total: $($total * 2)"
""",
"""
<span class="hljs-variable">$msg</span> = <span class="hljs-string">&quot;Total: <span class="hljs-variable">$</span>(<span class="hljs-variable">$total</span> * 2)&quot;</span>
""");
    }

    [Fact]
    public void String_EscapeBacktickN()
    {
        AssertHighlighter("powershell",
"""
$s = "line1`nline2"
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">&quot;line1`nline2&quot;</span>
""");
    }

    [Fact]
    public void String_EscapeBacktickT()
    {
        AssertHighlighter("powershell",
"""
$s = "a`tb"
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">&quot;a`tb&quot;</span>
""");
    }

    [Fact]
    public void String_EscapeBacktickR()
    {
        AssertHighlighter("powershell",
"""
$s = "a`rb"
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">&quot;a`rb&quot;</span>
""");
    }

    [Fact]
    public void String_EscapeDoubleBacktick()
    {
        AssertHighlighter("powershell",
"""
$s = "literal``backtick"
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">&quot;literal``backtick&quot;</span>
""");
    }

    [Fact]
    public void String_EscapeDoubleQuote()
    {
        AssertHighlighter("powershell",
""""
$s = "She said ""hi"""
"""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">&quot;She said &quot;</span><span class="hljs-string">&quot;hi&quot;</span><span class="hljs-string">&quot;&quot;</span>
""");
    }

    [Fact]
    public void String_HereStringInterpolated()
    {
        AssertHighlighter("powershell",
"""
$s = @"
first line
second line with $name
"@
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">@&quot;
first line
second line with <span class="hljs-variable">$name</span>
&quot;@</span>
""");
    }

    [Fact]
    public void String_HereStringLiteral()
    {
        AssertHighlighter("powershell",
"""
$s = @'
first line
second $literal
'@
""",
"""
<span class="hljs-variable">$s</span> = <span class="hljs-string">@&#x27;
first line
second $literal
&#x27;@</span>
""");
    }

    [Fact]
    public void Number_Integer()
    {
        AssertHighlighter("powershell",
"""
$x = 42
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void Number_Negative()
    {
        AssertHighlighter("powershell",
"""
$x = -42
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-literal">-42</span>
""");
    }

    [Fact]
    public void Number_Hex()
    {
        AssertHighlighter("powershell",
"""
$x = 0xDEADBEEF
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-number">0</span>xDEADBEEF
""");
    }

    [Fact]
    public void Number_Decimal()
    {
        AssertHighlighter("powershell",
"""
$x = 3.14
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-number">3.14</span>
""");
    }

    [Fact]
    public void Number_Exponent()
    {
        AssertHighlighter("powershell",
"""
$x = 1.5e10
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-number">1.5</span>e10
""");
    }

    [Fact]
    public void Number_Long()
    {
        AssertHighlighter("powershell",
"""
$x = 100L
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-number">100</span>L
""");
    }

    [Fact]
    public void Number_KbMb()
    {
        AssertHighlighter("powershell",
"""
$x = 4KB; $y = 2MB; $z = 1GB
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-number">4</span>KB; <span class="hljs-variable">$y</span> = <span class="hljs-number">2</span>MB; <span class="hljs-variable">$z</span> = <span class="hljs-number">1</span>GB
""");
    }

    [Fact]
    public void Number_TypeSuffixInt()
    {
        AssertHighlighter("powershell",
"""
$x = [int]42
""",
"""
<span class="hljs-variable">$x</span> = [<span class="hljs-built_in">int</span>]<span class="hljs-number">42</span>
""");
    }

    [Fact]
    public void Array_Inline()
    {
        AssertHighlighter("powershell",
"""
$a = 1, 2, 3
""",
"""
<span class="hljs-variable">$a</span> = <span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>
""");
    }

    [Fact]
    public void Array_AtParen()
    {
        AssertHighlighter("powershell",
"""
$a = @(1, 2, 3)
""",
"""
<span class="hljs-variable">$a</span> = <span class="hljs-selector-tag">@</span>(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>)
""");
    }

    [Fact]
    public void Array_AtEmpty()
    {
        AssertHighlighter("powershell",
"""
$a = @()
""",
"""
<span class="hljs-variable">$a</span> = <span class="hljs-selector-tag">@</span>()
""");
    }

    [Fact]
    public void Array_Strings()
    {
        AssertHighlighter("powershell",
"""
$a = @("alice", "bob", "carol")
""",
"""
<span class="hljs-variable">$a</span> = <span class="hljs-selector-tag">@</span>(<span class="hljs-string">&quot;alice&quot;</span>, <span class="hljs-string">&quot;bob&quot;</span>, <span class="hljs-string">&quot;carol&quot;</span>)
""");
    }

    [Fact]
    public void Array_MultiLine()
    {
        AssertHighlighter("powershell",
"""
$a = @(
  "alice",
  "bob",
  "carol"
)
""",
"""
<span class="hljs-variable">$a</span> = <span class="hljs-selector-tag">@</span>(
  <span class="hljs-string">&quot;alice&quot;</span>,
  <span class="hljs-string">&quot;bob&quot;</span>,
  <span class="hljs-string">&quot;carol&quot;</span>
)
""");
    }

    [Fact]
    public void Array_Index()
    {
        AssertHighlighter("powershell",
"""
$a[0]
""",
"""
<span class="hljs-variable">$a</span>[<span class="hljs-number">0</span>]
""");
    }

    [Fact]
    public void Array_IndexRange()
    {
        AssertHighlighter("powershell",
"""
$a[0..3]
""",
"""
<span class="hljs-variable">$a</span>[<span class="hljs-number">0</span><span class="hljs-type">..3</span>]
""");
    }

    [Fact]
    public void Array_IndexNeg()
    {
        AssertHighlighter("powershell",
"""
$a[-1]
""",
"""
<span class="hljs-variable">$a</span>[-<span class="hljs-number">1</span>]
""");
    }

    [Fact]
    public void Hashtable_Empty()
    {
        AssertHighlighter("powershell",
"""
$h = @{}
""",
"""
<span class="hljs-variable">$h</span> = <span class="hljs-selector-tag">@</span>{}
""");
    }

    [Fact]
    public void Hashtable_Single()
    {
        AssertHighlighter("powershell",
"""
$h = @{ name = "alice" }
""",
"""
<span class="hljs-variable">$h</span> = <span class="hljs-selector-tag">@</span>{ name = <span class="hljs-string">&quot;alice&quot;</span> }
""");
    }

    [Fact]
    public void Hashtable_Multi()
    {
        AssertHighlighter("powershell",
"""
$h = @{
  name = "alice"
  age = 30
  active = $true
}
""",
"""
<span class="hljs-variable">$h</span> = <span class="hljs-selector-tag">@</span>{
  name = <span class="hljs-string">&quot;alice&quot;</span>
  age = <span class="hljs-number">30</span>
  active = <span class="hljs-variable">$true</span>
}
""");
    }

    [Fact]
    public void Hashtable_Nested()
    {
        AssertHighlighter("powershell",
"""
$h = @{
  user = @{
    name = "alice"
    email = "alice@example.com"
  }
}
""",
"""
<span class="hljs-variable">$h</span> = <span class="hljs-selector-tag">@</span>{
  user = <span class="hljs-selector-tag">@</span>{
    name = <span class="hljs-string">&quot;alice&quot;</span>
    email = <span class="hljs-string">&quot;alice@example.com&quot;</span>
  }
}
""");
    }

    [Fact]
    public void Hashtable_OrderedAt()
    {
        AssertHighlighter("powershell",
"""
$h = [ordered]@{ a = 1; b = 2; c = 3 }
""",
"""
<span class="hljs-variable">$h</span> = [<span class="hljs-type">ordered</span>]<span class="hljs-selector-tag">@</span>{ a = <span class="hljs-number">1</span>; b = <span class="hljs-number">2</span>; c = <span class="hljs-number">3</span> }
""");
    }

    [Fact]
    public void Hashtable_AccessProp()
    {
        AssertHighlighter("powershell",
"""
$h.Name
""",
"""
<span class="hljs-variable">$h</span>.Name
""");
    }

    [Fact]
    public void Hashtable_AccessIndex()
    {
        AssertHighlighter("powershell",
"""
$h["name"]
""",
"""
<span class="hljs-variable">$h</span>[<span class="hljs-string">&quot;name&quot;</span>]
""");
    }

    [Fact]
    public void Splatting_Hashtable()
    {
        AssertHighlighter("powershell",
"""
$params = @{ Path = "C:\app"; Recurse = $true }
Get-ChildItem @params
""",
"""
<span class="hljs-variable">$params</span> = <span class="hljs-selector-tag">@</span>{ Path = <span class="hljs-string">&quot;C:\app&quot;</span>; Recurse = <span class="hljs-variable">$true</span> }
<span class="hljs-built_in">Get-ChildItem</span> @params
""");
    }

    [Fact]
    public void Splatting_ArraySplat()
    {
        AssertHighlighter("powershell",
"""
$args = @("explorer", "notepad")
Get-Process @args
""",
"""
<span class="hljs-variable">$args</span> = <span class="hljs-selector-tag">@</span>(<span class="hljs-string">&quot;explorer&quot;</span>, <span class="hljs-string">&quot;notepad&quot;</span>)
<span class="hljs-built_in">Get-Process</span> @args
""");
    }

    [Fact]
    public void ControlFlow_If()
    {
        AssertHighlighter("powershell",
"""
if ($x -gt 0) { Write-Host "positive" }
""",
"""
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$x</span> <span class="hljs-operator">-gt</span> <span class="hljs-number">0</span>) { <span class="hljs-built_in">Write-Host</span> <span class="hljs-string">&quot;positive&quot;</span> }
""");
    }

    [Fact]
    public void ControlFlow_IfElse()
    {
        AssertHighlighter("powershell",
"""
if ($x -gt 0) { Write-Host "positive" } else { Write-Host "non-positive" }
""",
"""
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$x</span> <span class="hljs-operator">-gt</span> <span class="hljs-number">0</span>) { <span class="hljs-built_in">Write-Host</span> <span class="hljs-string">&quot;positive&quot;</span> } <span class="hljs-keyword">else</span> { <span class="hljs-built_in">Write-Host</span> <span class="hljs-string">&quot;non-positive&quot;</span> }
""");
    }

    [Fact]
    public void ControlFlow_IfElseIf()
    {
        AssertHighlighter("powershell",
"""
if ($x -gt 0) {
  "positive"
} elseif ($x -lt 0) {
  "negative"
} else {
  "zero"
}
""",
"""
<span class="hljs-keyword">if</span> (<span class="hljs-variable">$x</span> <span class="hljs-operator">-gt</span> <span class="hljs-number">0</span>) {
  <span class="hljs-string">&quot;positive&quot;</span>
} <span class="hljs-keyword">elseif</span> (<span class="hljs-variable">$x</span> <span class="hljs-operator">-lt</span> <span class="hljs-number">0</span>) {
  <span class="hljs-string">&quot;negative&quot;</span>
} <span class="hljs-keyword">else</span> {
  <span class="hljs-string">&quot;zero&quot;</span>
}
""");
    }

    [Fact]
    public void ControlFlow_SwitchSimple()
    {
        AssertHighlighter("powershell",
"""
switch ($x) {
  1 { "one" }
  2 { "two" }
  default { "other" }
}
""",
"""
<span class="hljs-keyword">switch</span> (<span class="hljs-variable">$x</span>) {
  <span class="hljs-number">1</span> { <span class="hljs-string">&quot;one&quot;</span> }
  <span class="hljs-number">2</span> { <span class="hljs-string">&quot;two&quot;</span> }
  default { <span class="hljs-string">&quot;other&quot;</span> }
}
""");
    }

    [Fact]
    public void ControlFlow_SwitchRegex()
    {
        AssertHighlighter("powershell",
"""
switch -Regex ($text) {
  "^\d+$" { "number" }
  "^[a-z]+$" { "word" }
  default { "other" }
}
""",
"""
<span class="hljs-keyword">switch</span> <span class="hljs-operator">-Regex</span> (<span class="hljs-variable">$text</span>) {
  <span class="hljs-string">&quot;^\d+<span class="hljs-variable">$</span>&quot;</span> { <span class="hljs-string">&quot;number&quot;</span> }
  <span class="hljs-string">&quot;^[a-z]+<span class="hljs-variable">$</span>&quot;</span> { <span class="hljs-string">&quot;word&quot;</span> }
  default { <span class="hljs-string">&quot;other&quot;</span> }
}
""");
    }

    [Fact]
    public void ControlFlow_SwitchWildcard()
    {
        AssertHighlighter("powershell",
"""
switch -Wildcard ($name) {
  "alice*" { "starts with alice" }
  "*bob"   { "ends with bob" }
}
""",
"""
<span class="hljs-keyword">switch</span> <span class="hljs-operator">-Wildcard</span> (<span class="hljs-variable">$name</span>) {
  <span class="hljs-string">&quot;alice*&quot;</span> { <span class="hljs-string">&quot;starts with alice&quot;</span> }
  <span class="hljs-string">&quot;*bob&quot;</span>   { <span class="hljs-string">&quot;ends with bob&quot;</span> }
}
""");
    }

    [Fact]
    public void ControlFlow_For()
    {
        AssertHighlighter("powershell",
"""
for ($i = 0; $i -lt 10; $i++) { Write-Host $i }
""",
"""
<span class="hljs-keyword">for</span> (<span class="hljs-variable">$i</span> = <span class="hljs-number">0</span>; <span class="hljs-variable">$i</span> <span class="hljs-operator">-lt</span> <span class="hljs-number">10</span>; <span class="hljs-variable">$i</span>++) { <span class="hljs-built_in">Write-Host</span> <span class="hljs-variable">$i</span> }
""");
    }

    [Fact]
    public void ControlFlow_Foreach()
    {
        AssertHighlighter("powershell",
"""
foreach ($item in $list) { Write-Host $item }
""",
"""
<span class="hljs-keyword">foreach</span> (<span class="hljs-variable">$item</span> <span class="hljs-keyword">in</span> <span class="hljs-variable">$list</span>) { <span class="hljs-built_in">Write-Host</span> <span class="hljs-variable">$item</span> }
""");
    }

    [Fact]
    public void ControlFlow_While()
    {
        AssertHighlighter("powershell",
"""
while ($i -lt 10) { $i++ }
""",
"""
<span class="hljs-keyword">while</span> (<span class="hljs-variable">$i</span> <span class="hljs-operator">-lt</span> <span class="hljs-number">10</span>) { <span class="hljs-variable">$i</span>++ }
""");
    }

    [Fact]
    public void ControlFlow_DoWhile()
    {
        AssertHighlighter("powershell",
"""
do {
  $i++
} while ($i -lt 10)
""",
"""
<span class="hljs-keyword">do</span> {
  <span class="hljs-variable">$i</span>++
} <span class="hljs-keyword">while</span> (<span class="hljs-variable">$i</span> <span class="hljs-operator">-lt</span> <span class="hljs-number">10</span>)
""");
    }

    [Fact]
    public void ControlFlow_DoUntil()
    {
        AssertHighlighter("powershell",
"""
do {
  $i++
} until ($i -ge 10)
""",
"""
<span class="hljs-keyword">do</span> {
  <span class="hljs-variable">$i</span>++
} <span class="hljs-keyword">until</span> (<span class="hljs-variable">$i</span> <span class="hljs-operator">-ge</span> <span class="hljs-number">10</span>)
""");
    }

    [Fact]
    public void ControlFlow_Break()
    {
        AssertHighlighter("powershell",
"""
foreach ($i in 1..10) {
  if ($i -eq 5) { break }
  Write-Host $i
}
""",
"""
<span class="hljs-keyword">foreach</span> (<span class="hljs-variable">$i</span> <span class="hljs-keyword">in</span> <span class="hljs-number">1</span>..<span class="hljs-number">10</span>) {
  <span class="hljs-keyword">if</span> (<span class="hljs-variable">$i</span> <span class="hljs-operator">-eq</span> <span class="hljs-number">5</span>) { <span class="hljs-keyword">break</span> }
  <span class="hljs-built_in">Write-Host</span> <span class="hljs-variable">$i</span>
}
""");
    }

    [Fact]
    public void ControlFlow_Continue()
    {
        AssertHighlighter("powershell",
"""
foreach ($i in 1..10) {
  if ($i % 2 -eq 0) { continue }
  Write-Host $i
}
""",
"""
<span class="hljs-keyword">foreach</span> (<span class="hljs-variable">$i</span> <span class="hljs-keyword">in</span> <span class="hljs-number">1</span>..<span class="hljs-number">10</span>) {
  <span class="hljs-keyword">if</span> (<span class="hljs-variable">$i</span> % <span class="hljs-number">2</span> <span class="hljs-operator">-eq</span> <span class="hljs-number">0</span>) { <span class="hljs-keyword">continue</span> }
  <span class="hljs-built_in">Write-Host</span> <span class="hljs-variable">$i</span>
}
""");
    }

    [Fact]
    public void ControlFlow_TryCatch()
    {
        AssertHighlighter("powershell",
"""
try {
  Get-Item C:\missing
} catch {
  Write-Error $_
}
""",
"""
<span class="hljs-keyword">try</span> {
  <span class="hljs-built_in">Get-Item</span> C:\missing
} <span class="hljs-keyword">catch</span> {
  <span class="hljs-built_in">Write-Error</span> <span class="hljs-variable">$_</span>
}
""");
    }

    [Fact]
    public void ControlFlow_TryCatchType()
    {
        AssertHighlighter("powershell",
"""
try {
  Invoke-WebRequest -Uri $url
} catch [System.Net.WebException] {
  Write-Error "Network error"
} catch {
  Write-Error "Generic error"
}
""",
"""
<span class="hljs-keyword">try</span> {
  <span class="hljs-built_in">Invoke-WebRequest</span> <span class="hljs-literal">-Uri</span> <span class="hljs-variable">$url</span>
} <span class="hljs-keyword">catch</span> [<span class="hljs-type">System.Net.WebException</span>] {
  <span class="hljs-built_in">Write-Error</span> <span class="hljs-string">&quot;Network error&quot;</span>
} <span class="hljs-keyword">catch</span> {
  <span class="hljs-built_in">Write-Error</span> <span class="hljs-string">&quot;Generic error&quot;</span>
}
""");
    }

    [Fact]
    public void ControlFlow_TryFinally()
    {
        AssertHighlighter("powershell",
"""
try {
  $fs = [IO.File]::OpenRead($path)
  $fs.Read($buffer, 0, $buffer.Length) | Out-Null
} finally {
  if ($fs) { $fs.Close() }
}
""",
"""
<span class="hljs-keyword">try</span> {
  <span class="hljs-variable">$fs</span> = [<span class="hljs-type">IO.File</span>]::OpenRead(<span class="hljs-variable">$path</span>)
  <span class="hljs-variable">$fs</span>.Read(<span class="hljs-variable">$buffer</span>, <span class="hljs-number">0</span>, <span class="hljs-variable">$buffer</span>.Length) | <span class="hljs-built_in">Out-Null</span>
} <span class="hljs-keyword">finally</span> {
  <span class="hljs-keyword">if</span> (<span class="hljs-variable">$fs</span>) { <span class="hljs-variable">$fs</span>.Close() }
}
""");
    }

    [Fact]
    public void ControlFlow_Throw()
    {
        AssertHighlighter("powershell",
"""
throw "Something went wrong"
""",
"""
<span class="hljs-keyword">throw</span> <span class="hljs-string">&quot;Something went wrong&quot;</span>
""");
    }

    [Fact]
    public void ControlFlow_ThrowTyped()
    {
        AssertHighlighter("powershell",
"""
throw [System.IO.FileNotFoundException]::new("Missing file")
""",
"""
<span class="hljs-keyword">throw</span> [<span class="hljs-type">System.IO.FileNotFoundException</span>]::new(<span class="hljs-string">&quot;Missing file&quot;</span>)
""");
    }

    [Fact]
    public void ControlFlow_Return()
    {
        AssertHighlighter("powershell",
"""
function f { return 42 }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">f</span></span> { <span class="hljs-keyword">return</span> <span class="hljs-number">42</span> }
""");
    }

    [Fact]
    public void TernaryAndNullOps_Ternary()
    {
        AssertHighlighter("powershell",
"""
$status = ($x -gt 0) ? "positive" : "non-positive"
""",
"""
<span class="hljs-variable">$status</span> = (<span class="hljs-variable">$x</span> <span class="hljs-operator">-gt</span> <span class="hljs-number">0</span>) <span class="hljs-operator">?</span> <span class="hljs-string">&quot;positive&quot;</span> : <span class="hljs-string">&quot;non-positive&quot;</span>
""");
    }

    [Fact]
    public void TernaryAndNullOps_NullCoalesce()
    {
        AssertHighlighter("powershell",
"""
$value = $maybe ?? "default"
""",
"""
<span class="hljs-variable">$value</span> = <span class="hljs-variable">$maybe</span> <span class="hljs-operator">??</span> <span class="hljs-string">&quot;default&quot;</span>
""");
    }

    [Fact]
    public void TernaryAndNullOps_NullCoalesceAssign()
    {
        AssertHighlighter("powershell",
"""
$value ??= "default"
""",
"""
<span class="hljs-variable">$value</span> <span class="hljs-operator">??=</span> <span class="hljs-string">&quot;default&quot;</span>
""");
    }

    [Fact]
    public void TernaryAndNullOps_NullConditional()
    {
        AssertHighlighter("powershell",
"""
$name = $user?.Profile?.Name
""",
"""
<span class="hljs-variable">$name</span> = <span class="hljs-variable">$user</span><span class="hljs-operator">?.</span>Profile<span class="hljs-operator">?.</span>Name
""");
    }

    [Fact]
    public void TernaryAndNullOps_NullCondIndex()
    {
        AssertHighlighter("powershell",
"""
$first = $arr?[0]
""",
"""
<span class="hljs-variable">$first</span> = <span class="hljs-variable">$arr</span><span class="hljs-operator">?</span>[<span class="hljs-number">0</span>]
""");
    }

    [Fact]
    public void TernaryAndNullOps_TernaryNoParens()
    {
        AssertHighlighter("powershell",
"""
$msg = $isReady ? "yes" : "no"
""",
"""
<span class="hljs-variable">$msg</span> = <span class="hljs-variable">$isReady</span> <span class="hljs-operator">?</span> <span class="hljs-string">&quot;yes&quot;</span> : <span class="hljs-string">&quot;no&quot;</span>
""");
    }

    [Fact]
    public void TernaryAndNullOps_TernaryNested()
    {
        AssertHighlighter("powershell",
"""
$grade = ($score -ge 90) ? "A" : ($score -ge 80) ? "B" : "C"
""",
"""
<span class="hljs-variable">$grade</span> = (<span class="hljs-variable">$score</span> <span class="hljs-operator">-ge</span> <span class="hljs-number">90</span>) <span class="hljs-operator">?</span> <span class="hljs-string">&quot;A&quot;</span> : (<span class="hljs-variable">$score</span> <span class="hljs-operator">-ge</span> <span class="hljs-number">80</span>) <span class="hljs-operator">?</span> <span class="hljs-string">&quot;B&quot;</span> : <span class="hljs-string">&quot;C&quot;</span>
""");
    }

    [Fact]
    public void TernaryAndNullOps_NullCoalesceChained()
    {
        AssertHighlighter("powershell",
"""
$value = $a ?? $b ?? "fallback"
""",
"""
<span class="hljs-variable">$value</span> = <span class="hljs-variable">$a</span> <span class="hljs-operator">??</span> <span class="hljs-variable">$b</span> <span class="hljs-operator">??</span> <span class="hljs-string">&quot;fallback&quot;</span>
""");
    }

    [Fact]
    public void TernaryAndNullOps_NullCoalesceWithExpr()
    {
        AssertHighlighter("powershell",
"""
$count = $items.Count ?? 0
""",
"""
<span class="hljs-variable">$count</span> = <span class="hljs-variable">$items</span>.Count <span class="hljs-operator">??</span> <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void TernaryAndNullOps_NullCondMethod()
    {
        AssertHighlighter("powershell",
"""
$len = $name?.Length
""",
"""
<span class="hljs-variable">$len</span> = <span class="hljs-variable">$name</span><span class="hljs-operator">?.</span>Length
""");
    }

    [Fact]
    public void TernaryAndNullOps_NullCondIndexString()
    {
        AssertHighlighter("powershell",
"""
$val = $hash?["key"]
""",
"""
<span class="hljs-variable">$val</span> = <span class="hljs-variable">$hash</span><span class="hljs-operator">?</span>[<span class="hljs-string">&quot;key&quot;</span>]
""");
    }

    [Fact]
    public void TernaryAndNullOps_NullCondChain()
    {
        AssertHighlighter("powershell",
"""
$x = $a?.b?.c?[0]
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-variable">$a</span><span class="hljs-operator">?.</span>b<span class="hljs-operator">?.</span>c<span class="hljs-operator">?</span>[<span class="hljs-number">0</span>]
""");
    }

    [Fact]
    public void TernaryAndNullOps_PipelineChainCombo()
    {
        AssertHighlighter("powershell",
"""
Test-Path file.txt && Get-Content file.txt || Write-Error "missing"
""",
"""
<span class="hljs-built_in">Test-Path</span> file.txt <span class="hljs-operator">&amp;&amp;</span> <span class="hljs-built_in">Get-Content</span> file.txt <span class="hljs-operator">||</span> <span class="hljs-built_in">Write-Error</span> <span class="hljs-string">&quot;missing&quot;</span>
""");
    }

    [Fact]
    public void TernaryAndNullOps_NullCoalesceAssignToCoalesced()
    {
        AssertHighlighter("powershell",
"""
$cache ??= @{}
""",
"""
<span class="hljs-variable">$cache</span> <span class="hljs-operator">??=</span> <span class="hljs-selector-tag">@</span>{}
""");
    }

    [Fact]
    public void TernaryAndNullOps_StaticCallUnaffected()
    {
        AssertHighlighter("powershell",
"""
[Math]::Max($a, $b)
""",
"""
[<span class="hljs-type">Math</span>]::Max(<span class="hljs-variable">$a</span>, <span class="hljs-variable">$b</span>)
""");
    }

    [Fact]
    public void Function_Simple()
    {
        AssertHighlighter("powershell",
"""
function Greet { Write-Host "Hello" }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Greet</span></span> { <span class="hljs-built_in">Write-Host</span> <span class="hljs-string">&quot;Hello&quot;</span> }
""");
    }

    [Fact]
    public void Function_WithArgs()
    {
        AssertHighlighter("powershell",
"""
function Greet($name) { Write-Host "Hello, $name" }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Greet</span><span class="hljs-params">(<span class="hljs-variable">$name</span>)</span></span> { <span class="hljs-built_in">Write-Host</span> <span class="hljs-string">&quot;Hello, <span class="hljs-variable">$name</span>&quot;</span> }
""");
    }

    [Fact]
    public void Function_TypedParams()
    {
        AssertHighlighter("powershell",
"""
function Add([int]$a, [int]$b) { $a + $b }
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Add</span><span class="hljs-params">([int]<span class="hljs-variable">$a</span>, [int]<span class="hljs-variable">$b</span>)</span></span> { <span class="hljs-variable">$a</span> + <span class="hljs-variable">$b</span> }
""");
    }

    [Fact]
    public void Function_ParamBlock()
    {
        AssertHighlighter("powershell",
"""
function Greet {
  param(
    [string]$Name,
    [int]$Age = 0
  )
  Write-Host "Hello $Name, age $Age"
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Greet</span></span> {
  <span class="hljs-keyword">param</span>(
    [<span class="hljs-built_in">string</span>]<span class="hljs-variable">$Name</span>,
    [<span class="hljs-built_in">int</span>]<span class="hljs-variable">$Age</span> = <span class="hljs-number">0</span>
  )
  <span class="hljs-built_in">Write-Host</span> <span class="hljs-string">&quot;Hello <span class="hljs-variable">$Name</span>, age <span class="hljs-variable">$Age</span>&quot;</span>
}
""");
    }

    [Fact]
    public void Function_CmdletBinding()
    {
        AssertHighlighter("powershell",
"""
function Get-Thing {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)]
    [string]$Name
  )
  Write-Host $Name
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Get-Thing</span></span> {
  <span class="hljs-function">[<span class="hljs-type">CmdletBinding</span>()]</span>
  <span class="hljs-keyword">param</span>(
    [<span class="hljs-type">Parameter</span>(<span class="hljs-type">Mandatory</span>)]
    [<span class="hljs-built_in">string</span>]<span class="hljs-variable">$Name</span>
  )
  <span class="hljs-built_in">Write-Host</span> <span class="hljs-variable">$Name</span>
}
""");
    }

    [Fact]
    public void Function_AdvancedFunction()
    {
        AssertHighlighter("powershell",
"""
function Get-Thing {
  [CmdletBinding(SupportsShouldProcess)]
  [OutputType([string])]
  param(
    [Parameter(Mandatory, ValueFromPipeline)]
    [ValidateNotNullOrEmpty()]
    [string]$Name
  )
  process {
    if ($PSCmdlet.ShouldProcess($Name, "Get")) {
      Write-Output $Name
    }
  }
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Get-Thing</span></span> {
  [<span class="hljs-type">CmdletBinding</span>(<span class="hljs-type">SupportsShouldProcess</span>)]
  <span class="hljs-function">[<span class="hljs-type">OutputType</span>([<span class="hljs-built_in">string</span>])]</span>
  <span class="hljs-keyword">param</span>(
    [<span class="hljs-type">Parameter</span>(<span class="hljs-type">Mandatory</span>, <span class="hljs-type">ValueFromPipeline</span>)]
    [<span class="hljs-type">ValidateNotNullOrEmpty</span>()]
    [<span class="hljs-built_in">string</span>]<span class="hljs-variable">$Name</span>
  )
  <span class="hljs-keyword">process</span> {
    <span class="hljs-keyword">if</span> (<span class="hljs-variable">$PSCmdlet</span>.ShouldProcess(<span class="hljs-variable">$Name</span>, <span class="hljs-string">&quot;Get&quot;</span>)) {
      <span class="hljs-built_in">Write-Output</span> <span class="hljs-variable">$Name</span>
    }
  }
}
""");
    }

    [Fact]
    public void Function_PipelineBlocks()
    {
        AssertHighlighter("powershell",
"""
function Process-Item {
  begin   { Write-Host "Start" }
  process { Write-Host $_ }
  end     { Write-Host "End" }
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Process-Item</span></span> {
  <span class="hljs-keyword">begin</span>   { <span class="hljs-built_in">Write-Host</span> <span class="hljs-string">&quot;Start&quot;</span> }
  <span class="hljs-keyword">process</span> { <span class="hljs-built_in">Write-Host</span> <span class="hljs-variable">$_</span> }
  <span class="hljs-keyword">end</span>     { <span class="hljs-built_in">Write-Host</span> <span class="hljs-string">&quot;End&quot;</span> }
}
""");
    }

    [Fact]
    public void Function_Filter()
    {
        AssertHighlighter("powershell",
"""
filter Capitalize { $_.ToUpper() }
""",
"""
<span class="hljs-keyword">filter</span> Capitalize { <span class="hljs-variable">$_</span>.ToUpper() }
""");
    }

    [Fact]
    public void Function_CommentBasedHelp()
    {
        AssertHighlighter("powershell",
"""
<#
.SYNOPSIS
  Greets the user.
.PARAMETER Name
  The name to greet.
.EXAMPLE
  Greet -Name "Alice"
#>
function Greet {
  param([string]$Name)
  Write-Host "Hello $Name"
}
""",
"""
<span class="hljs-comment">&lt;#
<span class="hljs-doctag">.SYNOPSIS</span>
  Greets the user.
<span class="hljs-doctag">.PARAMETER Name</span>
  The name to greet.
<span class="hljs-doctag">.EXAMPLE</span>
  Greet -Name &quot;Alice&quot;
#&gt;</span>
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Greet</span></span> {
  <span class="hljs-keyword">param</span>([<span class="hljs-built_in">string</span>]<span class="hljs-variable">$Name</span>)
  <span class="hljs-built_in">Write-Host</span> <span class="hljs-string">&quot;Hello <span class="hljs-variable">$Name</span>&quot;</span>
}
""");
    }

    [Fact]
    public void Class_Empty()
    {
        AssertHighlighter("powershell",
"""
class Animal {}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Animal</span></span> {}
""");
    }

    [Fact]
    public void Class_Properties()
    {
        AssertHighlighter("powershell",
"""
class User {
  [string]$Name
  [int]$Age
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">User</span></span> {
  [<span class="hljs-built_in">string</span>]<span class="hljs-variable">$Name</span>
  [<span class="hljs-built_in">int</span>]<span class="hljs-variable">$Age</span>
}
""");
    }

    [Fact]
    public void Class_Constructor()
    {
        AssertHighlighter("powershell",
"""
class User {
  [string]$Name
  User([string]$name) {
    $this.Name = $name
  }
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">User</span></span> {
  [<span class="hljs-built_in">string</span>]<span class="hljs-variable">$Name</span>
  User([<span class="hljs-built_in">string</span>]<span class="hljs-variable">$name</span>) {
    <span class="hljs-keyword">$this</span>.Name = <span class="hljs-variable">$name</span>
  }
}
""");
    }

    [Fact]
    public void Class_Method()
    {
        AssertHighlighter("powershell",
"""
class User {
  [string]$Name
  [string] Greet() { return "Hello, $($this.Name)" }
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">User</span></span> {
  [<span class="hljs-built_in">string</span>]<span class="hljs-variable">$Name</span>
  <span class="hljs-function">[<span class="hljs-built_in">string</span>] <span class="hljs-title">Greet</span></span>() { <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;Hello, <span class="hljs-variable">$</span>(<span class="hljs-keyword">$this</span>.Name)&quot;</span> }
}
""");
    }

    [Fact]
    public void Class_StaticMember()
    {
        AssertHighlighter("powershell",
"""
class Math {
  static [int]$Pi = 3
  static [int] Double([int]$x) { return $x * 2 }
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Math</span></span> {
  <span class="hljs-keyword">static</span> [<span class="hljs-built_in">int</span>]<span class="hljs-variable">$Pi</span> = <span class="hljs-number">3</span>
  <span class="hljs-keyword">static</span> <span class="hljs-function">[<span class="hljs-built_in">int</span>] <span class="hljs-title">Double</span></span>([<span class="hljs-built_in">int</span>]<span class="hljs-variable">$x</span>) { <span class="hljs-keyword">return</span> <span class="hljs-variable">$x</span> * <span class="hljs-number">2</span> }
}
""");
    }

    [Fact]
    public void Class_Inheritance()
    {
        AssertHighlighter("powershell",
"""
class Dog : Animal {
  Dog() : base() {}
  [string] Bark() { return "woof" }
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">Dog</span> : <span class="hljs-title">Animal</span></span> {
  Dog() : base() {}
  <span class="hljs-function">[<span class="hljs-built_in">string</span>] <span class="hljs-title">Bark</span></span>() { <span class="hljs-keyword">return</span> <span class="hljs-string">&quot;woof&quot;</span> }
}
""");
    }

    [Fact]
    public void Class_Hidden()
    {
        AssertHighlighter("powershell",
"""
class User {
  hidden [string]$_password
}
""",
"""
<span class="hljs-class"><span class="hljs-keyword">class</span> <span class="hljs-title">User</span></span> {
  <span class="hljs-keyword">hidden</span> [<span class="hljs-built_in">string</span>]<span class="hljs-variable">$_password</span>
}
""");
    }

    [Fact]
    public void Class_EnumBasic()
    {
        AssertHighlighter("powershell",
"""
enum Color { Red; Green; Blue }
""",
"""
<span class="hljs-class"><span class="hljs-keyword">enum</span> <span class="hljs-title">Color</span></span> { Red; Green; Blue }
""");
    }

    [Fact]
    public void Class_EnumValued()
    {
        AssertHighlighter("powershell",
"""
enum Status { Active = 1; Inactive = 2; Pending = 4 }
""",
"""
<span class="hljs-class"><span class="hljs-keyword">enum</span> <span class="hljs-title">Status</span></span> { Active = <span class="hljs-number">1</span>; Inactive = <span class="hljs-number">2</span>; Pending = <span class="hljs-number">4</span> }
""");
    }

    [Fact]
    public void Attribute_ValidateSet()
    {
        AssertHighlighter("powershell",
"""
param(
  [ValidateSet("dev", "test", "prod")]
  [string]$Environment
)
""",
"""
<span class="hljs-keyword">param</span>(
  [<span class="hljs-type">ValidateSet</span>(<span class="hljs-string">&quot;dev&quot;</span>, <span class="hljs-string">&quot;test&quot;</span>, <span class="hljs-string">&quot;prod&quot;</span>)]
  [<span class="hljs-built_in">string</span>]<span class="hljs-variable">$Environment</span>
)
""");
    }

    [Fact]
    public void Attribute_ValidateRange()
    {
        AssertHighlighter("powershell",
"""
param(
  [ValidateRange(1, 100)]
  [int]$Count
)
""",
"""
<span class="hljs-keyword">param</span>(
  [<span class="hljs-type">ValidateRange</span>(<span class="hljs-number">1</span>, <span class="hljs-number">100</span>)]
  [<span class="hljs-built_in">int</span>]<span class="hljs-variable">$Count</span>
)
""");
    }

    [Fact]
    public void Attribute_ValidatePattern()
    {
        AssertHighlighter("powershell",
"""
param(
  [ValidatePattern("^[a-z]+$")]
  [string]$Username
)
""",
"""
<span class="hljs-keyword">param</span>(
  [<span class="hljs-type">ValidatePattern</span>(<span class="hljs-string">&quot;^[a-z]+<span class="hljs-variable">$</span>&quot;</span>)]
  [<span class="hljs-built_in">string</span>]<span class="hljs-variable">$Username</span>
)
""");
    }

    [Fact]
    public void Attribute_Alias()
    {
        AssertHighlighter("powershell",
"""
param(
  [Alias("CN", "ComputerName")]
  [string]$Name
)
""",
"""
<span class="hljs-keyword">param</span>(
  [<span class="hljs-type">Alias</span>(<span class="hljs-string">&quot;CN&quot;</span>, <span class="hljs-string">&quot;ComputerName&quot;</span>)]
  [<span class="hljs-built_in">string</span>]<span class="hljs-variable">$Name</span>
)
""");
    }

    [Fact]
    public void Attribute_OutputType()
    {
        AssertHighlighter("powershell",
"""
function f {
  [OutputType([string], [int])]
  param()
  "hi"
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">f</span></span> {
  <span class="hljs-function">[<span class="hljs-type">OutputType</span>([<span class="hljs-built_in">string</span>], [<span class="hljs-built_in">int</span>])]</span>
  <span class="hljs-keyword">param</span>()
  <span class="hljs-string">&quot;hi&quot;</span>
}
""");
    }

    [Fact]
    public void Type_Cast()
    {
        AssertHighlighter("powershell",
"""
$n = [int]"42"
""",
"""
<span class="hljs-variable">$n</span> = [<span class="hljs-built_in">int</span>]<span class="hljs-string">&quot;42&quot;</span>
""");
    }

    [Fact]
    public void Type_Typeof()
    {
        AssertHighlighter("powershell",
"""
[int] | Get-Member
""",
"""
[<span class="hljs-built_in">int</span>] | <span class="hljs-built_in">Get-Member</span>
""");
    }

    [Fact]
    public void Type_Generic()
    {
        AssertHighlighter("powershell",
"""
$list = [System.Collections.Generic.List[string]]::new()
""",
"""
<span class="hljs-variable">$list</span> = [<span class="hljs-type">System.Collections.Generic.List</span>[<span class="hljs-built_in">string</span>]]::new()
""");
    }

    [Fact]
    public void Type_ArrayShorthand()
    {
        AssertHighlighter("powershell",
"""
[int[]]$nums = @(1, 2, 3)
""",
"""
[<span class="hljs-built_in">int</span>[]]<span class="hljs-variable">$nums</span> = <span class="hljs-selector-tag">@</span>(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>)
""");
    }

    [Fact]
    public void Type_StaticMethod()
    {
        AssertHighlighter("powershell",
"""
[string]::Format("{0}", $value)
""",
"""
[<span class="hljs-built_in">string</span>]::Format(<span class="hljs-string">&quot;{0}&quot;</span>, <span class="hljs-variable">$value</span>)
""");
    }

    [Fact]
    public void Type_StaticProperty()
    {
        AssertHighlighter("powershell",
"""
[Math]::PI
""",
"""
[<span class="hljs-type">Math</span>]::PI
""");
    }

    [Fact]
    public void Type_Constructor()
    {
        AssertHighlighter("powershell",
"""
$now = [DateTime]::new(2026, 5, 26)
""",
"""
<span class="hljs-variable">$now</span> = [<span class="hljs-built_in">DateTime</span>]::new(<span class="hljs-number">2026</span>, <span class="hljs-number">5</span>, <span class="hljs-number">26</span>)
""");
    }

    [Fact]
    public void Comment_Line()
    {
        AssertHighlighter("powershell",
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
        AssertHighlighter("powershell",
"""
$x = 1   # set to one
""",
"""
<span class="hljs-variable">$x</span> = <span class="hljs-number">1</span>   <span class="hljs-comment"># set to one</span>
""");
    }

    [Fact]
    public void Comment_Block()
    {
        AssertHighlighter("powershell",
"""
<# block comment #>
""",
"""
<span class="hljs-comment">&lt;# block comment #&gt;</span>
""");
    }

    [Fact]
    public void Comment_BlockMultiLine()
    {
        AssertHighlighter("powershell",
"""
<#
  this spans
  several lines
#>
""",
"""
<span class="hljs-comment">&lt;#
  this spans
  several lines
#&gt;</span>
""");
    }

    [Fact]
    public void Comment_Region()
    {
        AssertHighlighter("powershell",
"""
#region setup
$a = 1
$b = 2
#endregion
""",
"""
<span class="hljs-comment">#region setup</span>
<span class="hljs-variable">$a</span> = <span class="hljs-number">1</span>
<span class="hljs-variable">$b</span> = <span class="hljs-number">2</span>
<span class="hljs-comment">#endregion</span>
""");
    }

    [Fact]
    public void Composite_TopProcesses()
    {
        AssertHighlighter("powershell",
"""
Get-Process |
  Where-Object { $_.WorkingSet -gt 100MB } |
  Sort-Object WorkingSet -Descending |
  Select-Object -First 10 Name, Id, WorkingSet
""",
"""
<span class="hljs-built_in">Get-Process</span> |
  <span class="hljs-built_in">Where-Object</span> { <span class="hljs-variable">$_</span>.WorkingSet <span class="hljs-operator">-gt</span> <span class="hljs-number">100</span>MB } |
  <span class="hljs-built_in">Sort-Object</span> WorkingSet <span class="hljs-literal">-Descending</span> |
  <span class="hljs-built_in">Select-Object</span> <span class="hljs-literal">-First</span> <span class="hljs-number">10</span> Name, Id, WorkingSet
""");
    }

    [Fact]
    public void Composite_RetryWithBackoff()
    {
        AssertHighlighter("powershell",
"""
function Invoke-WithRetry {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory)] [scriptblock]$ScriptBlock,
    [int]$MaxAttempts = 3,
    [int]$DelaySeconds = 1
  )
  for ($i = 1; $i -le $MaxAttempts; $i++) {
    try {
      return & $ScriptBlock
    } catch {
      if ($i -eq $MaxAttempts) { throw }
      Start-Sleep -Seconds ($DelaySeconds * $i)
    }
  }
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Invoke-WithRetry</span></span> {
  <span class="hljs-function">[<span class="hljs-type">CmdletBinding</span>()]</span>
  <span class="hljs-keyword">param</span>(
    [<span class="hljs-type">Parameter</span>(<span class="hljs-type">Mandatory</span>)] [<span class="hljs-type">scriptblock</span>]<span class="hljs-variable">$ScriptBlock</span>,
    [<span class="hljs-built_in">int</span>]<span class="hljs-variable">$MaxAttempts</span> = <span class="hljs-number">3</span>,
    [<span class="hljs-built_in">int</span>]<span class="hljs-variable">$DelaySeconds</span> = <span class="hljs-number">1</span>
  )
  <span class="hljs-keyword">for</span> (<span class="hljs-variable">$i</span> = <span class="hljs-number">1</span>; <span class="hljs-variable">$i</span> <span class="hljs-operator">-le</span> <span class="hljs-variable">$MaxAttempts</span>; <span class="hljs-variable">$i</span>++) {
    <span class="hljs-keyword">try</span> {
      <span class="hljs-keyword">return</span> &amp; <span class="hljs-variable">$ScriptBlock</span>
    } <span class="hljs-keyword">catch</span> {
      <span class="hljs-keyword">if</span> (<span class="hljs-variable">$i</span> <span class="hljs-operator">-eq</span> <span class="hljs-variable">$MaxAttempts</span>) { <span class="hljs-keyword">throw</span> }
      <span class="hljs-built_in">Start-Sleep</span> <span class="hljs-literal">-Seconds</span> (<span class="hljs-variable">$DelaySeconds</span> * <span class="hljs-variable">$i</span>)
    }
  }
}
""");
    }

    [Fact]
    public void Composite_FileSync()
    {
        AssertHighlighter("powershell",
"""
function Sync-Folder {
  param([string]$Source, [string]$Destination)

  if (-not (Test-Path $Destination)) {
    New-Item -ItemType Directory -Path $Destination | Out-Null
  }

  Get-ChildItem -Path $Source -Recurse | ForEach-Object {
    $rel = $_.FullName.Substring($Source.Length)
    $dest = Join-Path $Destination $rel
    if ($_.PSIsContainer) {
      if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest | Out-Null }
    } else {
      Copy-Item -Path $_.FullName -Destination $dest -Force
    }
  }
}
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Sync-Folder</span></span> {
  <span class="hljs-keyword">param</span>([<span class="hljs-built_in">string</span>]<span class="hljs-variable">$Source</span>, [<span class="hljs-built_in">string</span>]<span class="hljs-variable">$Destination</span>)

  <span class="hljs-keyword">if</span> (<span class="hljs-operator">-not</span> (<span class="hljs-built_in">Test-Path</span> <span class="hljs-variable">$Destination</span>)) {
    <span class="hljs-built_in">New-Item</span> <span class="hljs-literal">-ItemType</span> Directory <span class="hljs-literal">-Path</span> <span class="hljs-variable">$Destination</span> | <span class="hljs-built_in">Out-Null</span>
  }

  <span class="hljs-built_in">Get-ChildItem</span> <span class="hljs-literal">-Path</span> <span class="hljs-variable">$Source</span> <span class="hljs-literal">-Recurse</span> | <span class="hljs-built_in">ForEach-Object</span> {
    <span class="hljs-variable">$rel</span> = <span class="hljs-variable">$_</span>.FullName.Substring(<span class="hljs-variable">$Source</span>.Length)
    <span class="hljs-variable">$dest</span> = <span class="hljs-built_in">Join-Path</span> <span class="hljs-variable">$Destination</span> <span class="hljs-variable">$rel</span>
    <span class="hljs-keyword">if</span> (<span class="hljs-variable">$_</span>.PSIsContainer) {
      <span class="hljs-keyword">if</span> (<span class="hljs-operator">-not</span> (<span class="hljs-built_in">Test-Path</span> <span class="hljs-variable">$dest</span>)) { <span class="hljs-built_in">New-Item</span> <span class="hljs-literal">-ItemType</span> Directory <span class="hljs-literal">-Path</span> <span class="hljs-variable">$dest</span> | <span class="hljs-built_in">Out-Null</span> }
    } <span class="hljs-keyword">else</span> {
      <span class="hljs-built_in">Copy-Item</span> <span class="hljs-literal">-Path</span> <span class="hljs-variable">$_</span>.FullName <span class="hljs-literal">-Destination</span> <span class="hljs-variable">$dest</span> <span class="hljs-literal">-Force</span>
    }
  }
}
""");
    }

    [Fact]
    public void Composite_RestApiCall()
    {
        AssertHighlighter("powershell",
"""
$headers = @{
  "Authorization" = "Bearer $token"
  "Content-Type"  = "application/json"
}

$body = @{
  name  = "alice"
  email = "alice@example.com"
} | ConvertTo-Json

try {
  $response = Invoke-RestMethod -Uri $url -Method Post -Headers $headers -Body $body
  Write-Host "Created: $($response.id)"
} catch {
  Write-Error "Failed: $_"
}
""",
"""
<span class="hljs-variable">$headers</span> = <span class="hljs-selector-tag">@</span>{
  <span class="hljs-string">&quot;Authorization&quot;</span> = <span class="hljs-string">&quot;Bearer <span class="hljs-variable">$token</span>&quot;</span>
  <span class="hljs-string">&quot;Content-Type&quot;</span>  = <span class="hljs-string">&quot;application/json&quot;</span>
}

<span class="hljs-variable">$body</span> = <span class="hljs-selector-tag">@</span>{
  name  = <span class="hljs-string">&quot;alice&quot;</span>
  email = <span class="hljs-string">&quot;alice@example.com&quot;</span>
} | <span class="hljs-built_in">ConvertTo-Json</span>

<span class="hljs-keyword">try</span> {
  <span class="hljs-variable">$response</span> = <span class="hljs-built_in">Invoke-RestMethod</span> <span class="hljs-literal">-Uri</span> <span class="hljs-variable">$url</span> <span class="hljs-literal">-Method</span> Post <span class="hljs-literal">-Headers</span> <span class="hljs-variable">$headers</span> <span class="hljs-literal">-Body</span> <span class="hljs-variable">$body</span>
  <span class="hljs-built_in">Write-Host</span> <span class="hljs-string">&quot;Created: <span class="hljs-variable">$</span>(<span class="hljs-variable">$response</span>.id)&quot;</span>
} <span class="hljs-keyword">catch</span> {
  <span class="hljs-built_in">Write-Error</span> <span class="hljs-string">&quot;Failed: <span class="hljs-variable">$_</span>&quot;</span>
}
""");
    }

    [Fact]
    public void SpecialEdge_Empty()
    {
        AssertHighlighter("powershell",
"""

""",
"""

""");
    }

    [Fact]
    public void SpecialEdge_OnlyComment()
    {
        AssertHighlighter("powershell",
"""
# just a comment
""",
"""
<span class="hljs-comment"># just a comment</span>
""");
    }

    [Fact]
    public void SpecialEdge_BlankBetween()
    {
        AssertHighlighter("powershell",
"""
$a = 1

$b = 2
""",
"""
<span class="hljs-variable">$a</span> = <span class="hljs-number">1</span>

<span class="hljs-variable">$b</span> = <span class="hljs-number">2</span>
""");
    }

    [Fact]
    public void SpecialEdge_Shebang()
    {
        AssertHighlighter("powershell",
"""
#!/usr/bin/env pwsh
Write-Host "hi"
""",
"""
<span class="hljs-comment">#!/usr/bin/env pwsh</span>
<span class="hljs-built_in">Write-Host</span> <span class="hljs-string">&quot;hi&quot;</span>
""");
    }

    [Fact]
    public void SpecialEdge_BacktickContinue()
    {
        AssertHighlighter("powershell",
"""
Get-Process `
  | Sort-Object Name `
  | Select-Object -First 5
""",
"""
<span class="hljs-built_in">Get-Process</span> `
  | <span class="hljs-built_in">Sort-Object</span> Name `
  | <span class="hljs-built_in">Select-Object</span> <span class="hljs-literal">-First</span> <span class="hljs-number">5</span>
""");
    }

    [Fact]
    public void SpecialEdge_SemicolonSeparator()
    {
        AssertHighlighter("powershell",
"""
$a = 1; $b = 2; $c = 3
""",
"""
<span class="hljs-variable">$a</span> = <span class="hljs-number">1</span>; <span class="hljs-variable">$b</span> = <span class="hljs-number">2</span>; <span class="hljs-variable">$c</span> = <span class="hljs-number">3</span>
""");
    }
}
