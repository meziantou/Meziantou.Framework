namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class KqlHighlighterTests
{
    [Fact]
    public void Query_Basic()
    {
        AssertHighlighter("kql",
"""
StormEvents
| where StartTime between (datetime(2007-11-01) .. datetime(2007-12-01))
| where State == "FLORIDA"
| count
""",
"""
StormEvents
<span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> StartTime <span class="hljs-keyword">between</span> (<span class="hljs-type">datetime</span>(<span class="hljs-number">2007-11-01</span>) <span class="hljs-operator">..</span> <span class="hljs-type">datetime</span>(<span class="hljs-number">2007-12-01</span>))
<span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> State <span class="hljs-operator">==</span> <span class="hljs-string">&quot;FLORIDA&quot;</span>
<span class="hljs-operator">|</span> <span class="hljs-keyword">count</span>
""");
    }

    [Fact]
    public void Query_Summarize_KustoAlias()
    {
        AssertHighlighter("kusto",
"""
StormEvents
| summarize EventCount = count(), TotalDamage = sum(DamageProperty) by State
| top 10 by EventCount desc
| render columnchart
""",
"""
StormEvents
<span class="hljs-operator">|</span> <span class="hljs-keyword">summarize</span> EventCount = <span class="hljs-built_in">count</span>(), TotalDamage = <span class="hljs-built_in">sum</span>(DamageProperty) <span class="hljs-keyword">by</span> State
<span class="hljs-operator">|</span> <span class="hljs-keyword">top</span> <span class="hljs-number">10</span> <span class="hljs-keyword">by</span> EventCount <span class="hljs-keyword">desc</span>
<span class="hljs-operator">|</span> <span class="hljs-keyword">render</span> <span class="hljs-literal">columnchart</span>
""");
    }

    [Fact]
    public void Query_ProjectExtend()
    {
        AssertHighlighter("kql",
"""
Logs
| extend Duration = EndTime - StartTime, Day = bin(Timestamp, 1d)
| project-away Internal*
| project-rename NewName = OldName
| project Timestamp, Level, Message
| order by Timestamp asc nulls last
| take 100
""",
"""
Logs
<span class="hljs-operator">|</span> <span class="hljs-keyword">extend</span> Duration = EndTime <span class="hljs-operator">-</span> StartTime, Day = <span class="hljs-built_in">bin</span>(Timestamp, <span class="hljs-number">1d</span>)
<span class="hljs-operator">|</span> <span class="hljs-keyword">project-away</span> Internal<span class="hljs-operator">*</span>
<span class="hljs-operator">|</span> <span class="hljs-keyword">project-rename</span> NewName = OldName
<span class="hljs-operator">|</span> <span class="hljs-keyword">project</span> Timestamp, Level, Message
<span class="hljs-operator">|</span> <span class="hljs-keyword">order</span> <span class="hljs-keyword">by</span> Timestamp <span class="hljs-keyword">asc</span> <span class="hljs-keyword">nulls</span> <span class="hljs-keyword">last</span>
<span class="hljs-operator">|</span> <span class="hljs-keyword">take</span> <span class="hljs-number">100</span>
""");
    }

    [Fact]
    public void Let_Statements()
    {
        AssertHighlighter("kql",
"""
let threshold = 100;
let start = ago(7d);
let GetErrors = (level:string, since:timespan = 1h) {
    Logs | where Level == level and Timestamp > ago(since)
};
GetErrors("Error", 30m)
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-variable">threshold</span> = <span class="hljs-number">100</span>;
<span class="hljs-keyword">let</span> <span class="hljs-variable">start</span> = <span class="hljs-built_in">ago</span>(<span class="hljs-number">7d</span>);
<span class="hljs-keyword">let</span> <span class="hljs-variable">GetErrors</span> = (level:<span class="hljs-type">string</span>, since:<span class="hljs-type">timespan</span> = <span class="hljs-number">1h</span>) {
    Logs <span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> Level <span class="hljs-operator">==</span> level <span class="hljs-keyword">and</span> Timestamp <span class="hljs-operator">&gt;</span> <span class="hljs-built_in">ago</span>(since)
};
GetErrors(<span class="hljs-string">&quot;Error&quot;</span>, <span class="hljs-number">30m</span>)
""");
    }

    [Fact]
    public void Join_Kinds()
    {
        AssertHighlighter("kql",
"""
Requests
| join kind=leftouter hint.strategy=broadcast (
    Dependencies
    | where Success == false
) on $left.OperationId == $right.OperationId
| lookup kind=inner Users on UserId
| union withsource=TableName Traces, Exceptions
""",
"""
Requests
<span class="hljs-operator">|</span> <span class="hljs-keyword">join</span> <span class="hljs-keyword">kind</span>=<span class="hljs-keyword">leftouter</span> <span class="hljs-keyword">hint.strategy</span>=<span class="hljs-keyword">broadcast</span> (
    Dependencies
    <span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> Success <span class="hljs-operator">==</span> <span class="hljs-literal">false</span>
) <span class="hljs-keyword">on</span> <span class="hljs-variable language_">$left</span>.OperationId <span class="hljs-operator">==</span> <span class="hljs-variable language_">$right</span>.OperationId
<span class="hljs-operator">|</span> <span class="hljs-keyword">lookup</span> <span class="hljs-keyword">kind</span>=<span class="hljs-keyword">inner</span> Users <span class="hljs-keyword">on</span> UserId
<span class="hljs-operator">|</span> <span class="hljs-keyword">union</span> <span class="hljs-keyword">withsource</span>=TableName Traces, Exceptions
""");
    }

    [Fact]
    public void String_Operators()
    {
        AssertHighlighter("kql",
"""
Events
| where Name has "error" and Name !has "warning"
| where Url contains "api" or Url !contains_cs "API"
| where Path startswith "/home" and Path !endswith ".tmp"
| where User =~ "ADMIN" and Host !~ "localhost"
| where Country in ("FR", "US") and City !in ("Paris")
| where Code in~ ("abc") and Tag !in~ ("x")
| where Message has_any ("timeout", "refused") and Tags has_all ("a", "b")
| where Text matches regex @"\d+" and Price !between (1 .. 10)
| where Name hasprefix "pre" and Name hassuffix_cs "Suf"
""",
"""
Events
<span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> Name <span class="hljs-keyword">has</span> <span class="hljs-string">&quot;error&quot;</span> <span class="hljs-keyword">and</span> Name <span class="hljs-keyword">!has</span> <span class="hljs-string">&quot;warning&quot;</span>
<span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> Url <span class="hljs-keyword">contains</span> <span class="hljs-string">&quot;api&quot;</span> <span class="hljs-keyword">or</span> Url <span class="hljs-keyword">!contains_cs</span> <span class="hljs-string">&quot;API&quot;</span>
<span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> Path <span class="hljs-keyword">startswith</span> <span class="hljs-string">&quot;/home&quot;</span> <span class="hljs-keyword">and</span> Path <span class="hljs-keyword">!endswith</span> <span class="hljs-string">&quot;.tmp&quot;</span>
<span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> User <span class="hljs-operator">=~</span> <span class="hljs-string">&quot;ADMIN&quot;</span> <span class="hljs-keyword">and</span> Host <span class="hljs-operator">!~</span> <span class="hljs-string">&quot;localhost&quot;</span>
<span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> Country <span class="hljs-keyword">in</span> (<span class="hljs-string">&quot;FR&quot;</span>, <span class="hljs-string">&quot;US&quot;</span>) <span class="hljs-keyword">and</span> City <span class="hljs-keyword">!in</span> (<span class="hljs-string">&quot;Paris&quot;</span>)
<span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> Code <span class="hljs-keyword">in~</span> (<span class="hljs-string">&quot;abc&quot;</span>) <span class="hljs-keyword">and</span> Tag <span class="hljs-keyword">!in~</span> (<span class="hljs-string">&quot;x&quot;</span>)
<span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> Message <span class="hljs-keyword">has_any</span> (<span class="hljs-string">&quot;timeout&quot;</span>, <span class="hljs-string">&quot;refused&quot;</span>) <span class="hljs-keyword">and</span> Tags <span class="hljs-keyword">has_all</span> (<span class="hljs-string">&quot;a&quot;</span>, <span class="hljs-string">&quot;b&quot;</span>)
<span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> Text <span class="hljs-keyword">matches</span> <span class="hljs-keyword">regex</span> <span class="hljs-string">@&quot;\d+&quot;</span> <span class="hljs-keyword">and</span> Price <span class="hljs-keyword">!between</span> (<span class="hljs-number">1</span> <span class="hljs-operator">..</span> <span class="hljs-number">10</span>)
<span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> Name <span class="hljs-keyword">hasprefix</span> <span class="hljs-string">&quot;pre&quot;</span> <span class="hljs-keyword">and</span> Name <span class="hljs-keyword">hassuffix_cs</span> <span class="hljs-string">&quot;Suf&quot;</span>
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("kql",
"""
print a = "double \"quoted\"", b = 'single \'quoted\'', c = @"C:\temp\file", d = @'it''s', e = h"secret", f = H@'verbatim secret'
print g = ```multi
line `` string```
print unterminated = "never closed
print after = 1
""",
"""
<span class="hljs-keyword">print</span> a = <span class="hljs-string">&quot;double \&quot;quoted\&quot;&quot;</span>, b = <span class="hljs-string">&#x27;single \&#x27;quoted\&#x27;&#x27;</span>, c = <span class="hljs-string">@&quot;C:\temp\file&quot;</span>, d = <span class="hljs-string">@&#x27;it&#x27;&#x27;s&#x27;</span>, e = <span class="hljs-string">h&quot;secret&quot;</span>, f = <span class="hljs-string">H@&#x27;verbatim secret&#x27;</span>
<span class="hljs-keyword">print</span> g = <span class="hljs-string">```multi
line `` string```</span>
<span class="hljs-keyword">print</span> unterminated = <span class="hljs-string">&quot;never closed</span>
<span class="hljs-keyword">print</span> after = <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Numbers_Timespans()
    {
        AssertHighlighter("kql",
"""
print i = 42, r = 3.14, e = 1e10, h = 0xFF, t1 = 1d, t2 = 30m, t3 = 2h, t4 = 10s, t5 = 100ms, t6 = 5microseconds, t7 = 1.5hours, t8 = 3ticks
print ts = timespan(1.02:03:04), tm = time(1d), d = datetime(2024-01-01 10:00:00), dn = datetime(null), dt = date(2024-01-01)
""",
"""
<span class="hljs-keyword">print</span> i = <span class="hljs-number">42</span>, r = <span class="hljs-number">3.14</span>, e = <span class="hljs-number">1e10</span>, h = <span class="hljs-number">0xFF</span>, t1 = <span class="hljs-number">1d</span>, t2 = <span class="hljs-number">30m</span>, t3 = <span class="hljs-number">2h</span>, t4 = <span class="hljs-number">10s</span>, t5 = <span class="hljs-number">100ms</span>, t6 = <span class="hljs-number">5microseconds</span>, t7 = <span class="hljs-number">1.5hours</span>, t8 = <span class="hljs-number">3ticks</span>
<span class="hljs-keyword">print</span> ts = <span class="hljs-type">timespan</span>(<span class="hljs-number">1.02:03:04</span>), tm = <span class="hljs-type">time</span>(<span class="hljs-number">1d</span>), d = <span class="hljs-type">datetime</span>(<span class="hljs-number">2024-01-01 10:00:00</span>), dn = <span class="hljs-type">datetime</span>(<span class="hljs-number">null</span>), dt = <span class="hljs-type">date</span>(<span class="hljs-number">2024-01-01</span>)
""");
    }

    [Fact]
    public void Data_Types()
    {
        AssertHighlighter("kql",
"""
datatable(Name:string, Age:int, Score:real, Active:bool, Born:datetime, Meta:dynamic, Id:guid, Span:timespan, Big:long, Price:decimal)
[
    "Alice", 30, 1.5, true, datetime(1994-01-01), dynamic({"a": 1}), guid(74be27de-1e4e-49d9-b579-fe0b331d3642), 1h, 1, 1.0,
]
| extend s = tostring(Age), l = tolong(Score), n = int(null)
""",
"""
<span class="hljs-keyword">datatable</span>(Name:<span class="hljs-type">string</span>, Age:<span class="hljs-type">int</span>, Score:<span class="hljs-type">real</span>, Active:<span class="hljs-type">bool</span>, Born:<span class="hljs-type">datetime</span>, Meta:<span class="hljs-type">dynamic</span>, Id:<span class="hljs-type">guid</span>, Span:<span class="hljs-type">timespan</span>, Big:<span class="hljs-type">long</span>, Price:<span class="hljs-type">decimal</span>)
[
    <span class="hljs-string">&quot;Alice&quot;</span>, <span class="hljs-number">30</span>, <span class="hljs-number">1.5</span>, <span class="hljs-literal">true</span>, <span class="hljs-type">datetime</span>(<span class="hljs-number">1994-01-01</span>), <span class="hljs-type">dynamic</span>({<span class="hljs-string">&quot;a&quot;</span>: <span class="hljs-number">1</span>}), <span class="hljs-type">guid</span>(74be27de-1e4e-49d9-b579-fe0b331d3642), <span class="hljs-number">1h</span>, <span class="hljs-number">1</span>, <span class="hljs-number">1.0</span>,
]
<span class="hljs-operator">|</span> <span class="hljs-keyword">extend</span> s = <span class="hljs-built_in">tostring</span>(Age), l = <span class="hljs-built_in">tolong</span>(Score), n = <span class="hljs-type">int</span>(null)
""");
    }

    [Fact]
    public void Mv_Expand()
    {
        AssertHighlighter("kql",
"""
T
| mv-expand Items to typeof(string)
| mv-apply Tags on (top 1 by Tags)
| make-series Count = count() default = 0 on Timestamp from ago(1d) to now() step 1h by Region
| parse Message with "Error " Code:int " at " Location
| parse-where kind=regex Message with @"(\d+)" Number
| evaluate bag_unpack(Properties)
| top-nested 3 of State by sum(Damage)
| sample 10
| distinct Region
| serialize rn = row_number()
| extend prevValue = prev(Value, 1)
""",
"""
T
<span class="hljs-operator">|</span> <span class="hljs-keyword">mv-expand</span> Items <span class="hljs-keyword">to</span> <span class="hljs-keyword">typeof</span>(<span class="hljs-type">string</span>)
<span class="hljs-operator">|</span> <span class="hljs-keyword">mv-apply</span> Tags <span class="hljs-keyword">on</span> (<span class="hljs-keyword">top</span> <span class="hljs-number">1</span> <span class="hljs-keyword">by</span> Tags)
<span class="hljs-operator">|</span> <span class="hljs-keyword">make-series</span> Count = <span class="hljs-built_in">count</span>() <span class="hljs-keyword">default</span> = <span class="hljs-number">0</span> <span class="hljs-keyword">on</span> Timestamp <span class="hljs-keyword">from</span> <span class="hljs-built_in">ago</span>(<span class="hljs-number">1d</span>) <span class="hljs-keyword">to</span> <span class="hljs-built_in">now</span>() <span class="hljs-keyword">step</span> <span class="hljs-number">1h</span> <span class="hljs-keyword">by</span> Region
<span class="hljs-operator">|</span> <span class="hljs-keyword">parse</span> Message <span class="hljs-keyword">with</span> <span class="hljs-string">&quot;Error &quot;</span> Code:<span class="hljs-type">int</span> <span class="hljs-string">&quot; at &quot;</span> Location
<span class="hljs-operator">|</span> <span class="hljs-keyword">parse-where</span> <span class="hljs-keyword">kind</span>=<span class="hljs-keyword">regex</span> Message <span class="hljs-keyword">with</span> <span class="hljs-string">@&quot;(\d+)&quot;</span> Number
<span class="hljs-operator">|</span> <span class="hljs-keyword">evaluate</span> <span class="hljs-built_in">bag_unpack</span>(Properties)
<span class="hljs-operator">|</span> <span class="hljs-keyword">top-nested</span> <span class="hljs-number">3</span> <span class="hljs-keyword">of</span> State <span class="hljs-keyword">by</span> <span class="hljs-built_in">sum</span>(Damage)
<span class="hljs-operator">|</span> <span class="hljs-keyword">sample</span> <span class="hljs-number">10</span>
<span class="hljs-operator">|</span> <span class="hljs-keyword">distinct</span> Region
<span class="hljs-operator">|</span> <span class="hljs-keyword">serialize</span> rn = <span class="hljs-built_in">row_number</span>()
<span class="hljs-operator">|</span> <span class="hljs-keyword">extend</span> prevValue = <span class="hljs-built_in">prev</span>(Value, <span class="hljs-number">1</span>)
""");
    }

    [Fact]
    public void Management_Commands()
    {
        AssertHighlighter("kql",
"""
.show tables
.create table Logs (Timestamp:datetime, Level:string, Message:string)
.alter-merge table Logs (Extra:dynamic)
.set-or-append Logs <| OtherLogs | where Level == "Error"
  .drop table OldLogs ifexists
""",
"""
<span class="hljs-keyword">.show</span> tables
<span class="hljs-keyword">.create</span> <span class="hljs-keyword">table</span> Logs (Timestamp:<span class="hljs-type">datetime</span>, Level:<span class="hljs-type">string</span>, Message:<span class="hljs-type">string</span>)
<span class="hljs-keyword">.alter-merge</span> <span class="hljs-keyword">table</span> Logs (Extra:<span class="hljs-type">dynamic</span>)
<span class="hljs-keyword">.set-or-append</span> Logs <span class="hljs-operator">&lt;|</span> OtherLogs <span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> Level <span class="hljs-operator">==</span> <span class="hljs-string">&quot;Error&quot;</span>
  <span class="hljs-keyword">.drop</span> <span class="hljs-keyword">table</span> OldLogs ifexists
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("kql",
"""
// Find the top errors
Logs // the table
| where Level == "Error" // only errors
// | where disabled
| count
""",
"""
<span class="hljs-comment">// Find the top errors</span>
Logs <span class="hljs-comment">// the table</span>
<span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> Level <span class="hljs-operator">==</span> <span class="hljs-string">&quot;Error&quot;</span> <span class="hljs-comment">// only errors</span>
<span class="hljs-comment">// | where disabled</span>
<span class="hljs-operator">|</span> <span class="hljs-keyword">count</span>
""");
    }

    [Fact]
    public void Render()
    {
        AssertHighlighter("kql",
"""
Perf
| summarize avg(CounterValue) by bin(TimeGenerated, 5m), Computer
| render timechart with (title="CPU", ytitle="Percent")
""",
"""
Perf
<span class="hljs-operator">|</span> <span class="hljs-keyword">summarize</span> <span class="hljs-built_in">avg</span>(CounterValue) <span class="hljs-keyword">by</span> <span class="hljs-built_in">bin</span>(TimeGenerated, <span class="hljs-number">5m</span>), Computer
<span class="hljs-operator">|</span> <span class="hljs-keyword">render</span> <span class="hljs-literal">timechart</span> <span class="hljs-keyword">with</span> (title=<span class="hljs-string">&quot;CPU&quot;</span>, ytitle=<span class="hljs-string">&quot;Percent&quot;</span>)
""");
    }

    [Fact]
    public void Search_Union()
    {
        AssertHighlighter("kql",
"""
search in (T1, T2) "error"
| union isfuzzy=true (T3 | where x > 1), (T4)
| where * has "x"
| facet by Region
| getschema
""",
"""
<span class="hljs-keyword">search</span> <span class="hljs-keyword">in</span> (T1, T2) <span class="hljs-string">&quot;error&quot;</span>
<span class="hljs-operator">|</span> <span class="hljs-keyword">union</span> <span class="hljs-keyword">isfuzzy</span>=<span class="hljs-literal">true</span> (T3 <span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> x <span class="hljs-operator">&gt;</span> <span class="hljs-number">1</span>), (T4)
<span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> <span class="hljs-operator">*</span> <span class="hljs-keyword">has</span> <span class="hljs-string">&quot;x&quot;</span>
<span class="hljs-operator">|</span> <span class="hljs-keyword">facet</span> <span class="hljs-keyword">by</span> Region
<span class="hljs-operator">|</span> <span class="hljs-keyword">getschema</span>
""");
    }

    [Fact]
    public void Aggregations()
    {
        AssertHighlighter("kql",
"""
T
| summarize arg_max(Timestamp, *), dcount(User), make_set(Tag), percentiles(Duration, 50, 95, 99), countif(Failed), avgif(Latency, Ok) by Service
| extend p = round(percentile_Duration_50, 2), c = coalesce(a, b), x = iff(y > 0, "pos", "neg"), z = case(a == 1, "one", "other")
""",
"""
T
<span class="hljs-operator">|</span> <span class="hljs-keyword">summarize</span> <span class="hljs-built_in">arg_max</span>(Timestamp, <span class="hljs-operator">*</span>), <span class="hljs-built_in">dcount</span>(User), <span class="hljs-built_in">make_set</span>(Tag), <span class="hljs-built_in">percentiles</span>(Duration, <span class="hljs-number">50</span>, <span class="hljs-number">95</span>, <span class="hljs-number">99</span>), <span class="hljs-built_in">countif</span>(Failed), <span class="hljs-built_in">avgif</span>(Latency, Ok) <span class="hljs-keyword">by</span> Service
<span class="hljs-operator">|</span> <span class="hljs-keyword">extend</span> p = <span class="hljs-built_in">round</span>(percentile_Duration_50, <span class="hljs-number">2</span>), c = <span class="hljs-built_in">coalesce</span>(a, b), x = <span class="hljs-built_in">iff</span>(y <span class="hljs-operator">&gt;</span> <span class="hljs-number">0</span>, <span class="hljs-string">&quot;pos&quot;</span>, <span class="hljs-string">&quot;neg&quot;</span>), z = <span class="hljs-built_in">case</span>(a <span class="hljs-operator">==</span> <span class="hljs-number">1</span>, <span class="hljs-string">&quot;one&quot;</span>, <span class="hljs-string">&quot;other&quot;</span>)
""");
    }

    [Fact]
    public void Identifiers_NotKeywords()
    {
        AssertHighlighter("kql",
"""
T
| extend where_clause = 1, counter = count_, my-column = 2, projectName = "x", rangeMin = 0
| project ['where'], ["my col"], Table.Column
""",
"""
T
<span class="hljs-operator">|</span> <span class="hljs-keyword">extend</span> where_clause = <span class="hljs-number">1</span>, counter = count_, my-column = <span class="hljs-number">2</span>, projectName = <span class="hljs-string">&quot;x&quot;</span>, rangeMin = <span class="hljs-number">0</span>
<span class="hljs-operator">|</span> <span class="hljs-keyword">project</span> [<span class="hljs-string">&#x27;where&#x27;</span>], [<span class="hljs-string">&quot;my col&quot;</span>], Table.Column
""");
    }

    [Fact]
    public void Declare_Params()
    {
        AssertHighlighter("kql",
"""
declare query_parameters(start:datetime = datetime(2024-01-01), n:int = 10);
set notruncation;
T | take n
""",
"""
<span class="hljs-keyword">declare</span> <span class="hljs-keyword">query_parameters</span>(start:<span class="hljs-type">datetime</span> = <span class="hljs-type">datetime</span>(<span class="hljs-number">2024-01-01</span>), n:<span class="hljs-type">int</span> = <span class="hljs-number">10</span>);
<span class="hljs-keyword">set</span> notruncation;
T <span class="hljs-operator">|</span> <span class="hljs-keyword">take</span> n
""");
    }

    [Fact]
    public void Materialize_Toscalar()
    {
        AssertHighlighter("kql",
"""
let total = toscalar(T | count);
let cached = materialize(T | where x > 1);
cached | extend ratio = todouble(count_) / total
""",
"""
<span class="hljs-keyword">let</span> <span class="hljs-variable">total</span> = <span class="hljs-built_in">toscalar</span>(T <span class="hljs-operator">|</span> <span class="hljs-keyword">count</span>);
<span class="hljs-keyword">let</span> <span class="hljs-variable">cached</span> = <span class="hljs-keyword">materialize</span>(T <span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> x <span class="hljs-operator">&gt;</span> <span class="hljs-number">1</span>);
cached <span class="hljs-operator">|</span> <span class="hljs-keyword">extend</span> ratio = <span class="hljs-built_in">todouble</span>(count_) <span class="hljs-operator">/</span> total
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("kql",
"""
print a = 1 + 2 * 3 / 4 % 5 - 6, b = 1 <> 2, c = 3 >= 2 and 1 <= 2, d = 1 != 2, e = 1 < 2 or 2 > 1
""",
"""
<span class="hljs-keyword">print</span> a = <span class="hljs-number">1</span> <span class="hljs-operator">+</span> <span class="hljs-number">2</span> <span class="hljs-operator">*</span> <span class="hljs-number">3</span> <span class="hljs-operator">/</span> <span class="hljs-number">4</span> <span class="hljs-operator">%</span> <span class="hljs-number">5</span> <span class="hljs-operator">-</span> <span class="hljs-number">6</span>, b = <span class="hljs-number">1</span> <span class="hljs-operator">&lt;&gt;</span> <span class="hljs-number">2</span>, c = <span class="hljs-number">3</span> <span class="hljs-operator">&gt;=</span> <span class="hljs-number">2</span> <span class="hljs-keyword">and</span> <span class="hljs-number">1</span> <span class="hljs-operator">&lt;=</span> <span class="hljs-number">2</span>, d = <span class="hljs-number">1</span> <span class="hljs-operator">!=</span> <span class="hljs-number">2</span>, e = <span class="hljs-number">1</span> <span class="hljs-operator">&lt;</span> <span class="hljs-number">2</span> <span class="hljs-keyword">or</span> <span class="hljs-number">2</span> <span class="hljs-operator">&gt;</span> <span class="hljs-number">1</span>
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("kql",
"""

""",
"""

""");
    }

    [Fact]
    public void Crlf()
    {
        AssertHighlighter("kql", "T\r\n| where x == \"a\" // c\r\n| take 1d\r\n", "T\r\n<span class=\"hljs-operator\">|</span> <span class=\"hljs-keyword\">where</span> x <span class=\"hljs-operator\">==</span> <span class=\"hljs-string\">&quot;a&quot;</span> <span class=\"hljs-comment\">// c</span>\r\n<span class=\"hljs-operator\">|</span> <span class=\"hljs-keyword\">take</span> <span class=\"hljs-number\">1d</span>\r\n");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("kql",
"""
T | where Name == "café" and Ville has "ñ" // commentaire é
""",
"""
T <span class="hljs-operator">|</span> <span class="hljs-keyword">where</span> Name <span class="hljs-operator">==</span> <span class="hljs-string">&quot;café&quot;</span> <span class="hljs-keyword">and</span> Ville <span class="hljs-keyword">has</span> <span class="hljs-string">&quot;ñ&quot;</span> <span class="hljs-comment">// commentaire é</span>
""");
    }
}
