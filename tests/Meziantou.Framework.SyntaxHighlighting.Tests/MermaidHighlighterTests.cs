namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class MermaidHighlighterTests
{
    [Fact]
    public void Flowchart_Basic()
    {
        AssertHighlighter("mermaid",
"""
flowchart LR
    A[Christmas] -->|Get money| B(Go shopping)
    B --> C{Let me think}
    C -->|One| D[Laptop]
    C -->|Two| E[iPhone]
    C -->|Three| F[fa:fa-car Car]
""",
"""
<span class="hljs-keyword">flowchart</span> <span class="hljs-keyword">LR</span>
    A[<span class="hljs-string">Christmas</span>] <span class="hljs-operator">--&gt;</span>|<span class="hljs-string">Get money</span>| B(<span class="hljs-string">Go shopping</span>)
    B <span class="hljs-operator">--&gt;</span> C{<span class="hljs-string">Let me think</span>}
    C <span class="hljs-operator">--&gt;</span>|<span class="hljs-string">One</span>| D[<span class="hljs-string">Laptop</span>]
    C <span class="hljs-operator">--&gt;</span>|<span class="hljs-string">Two</span>| E[<span class="hljs-string">iPhone</span>]
    C <span class="hljs-operator">--&gt;</span>|<span class="hljs-string">Three</span>| F[<span class="hljs-string">fa:fa-car Car</span>]
""");
    }

    [Fact]
    public void Flowchart_GraphTD_MmdAlias()
    {
        AssertHighlighter("mmd",
"""
graph TD
    Start --> Stop
""",
"""
<span class="hljs-keyword">graph</span> <span class="hljs-keyword">TD</span>
    Start <span class="hljs-operator">--&gt;</span> Stop
""");
    }

    [Fact]
    public void Flowchart_Shapes()
    {
        AssertHighlighter("mermaid",
"""
flowchart TB
    id1[Rectangle]
    id2(Rounded)
    id3([Stadium])
    id4[[Subroutine]]
    id5[(Database)]
    id6((Circle))
    id7>Asymmetric]
    id8{Rhombus}
    id9{{Hexagon}}
    id10[/Parallelogram/]
    id11[\Alt parallelogram\]
    id12[/Trapezoid\]
    id13[\Alt trapezoid/]
    id14(((Double circle)))
""",
"""
<span class="hljs-keyword">flowchart</span> <span class="hljs-keyword">TB</span>
    id1[<span class="hljs-string">Rectangle</span>]
    id2(<span class="hljs-string">Rounded</span>)
    id3([<span class="hljs-string">Stadium</span>])
    id4[[<span class="hljs-string">Subroutine</span>]]
    id5[(<span class="hljs-string">Database</span>)]
    id6((<span class="hljs-string">Circle</span>))
    id7&gt;<span class="hljs-string">Asymmetric</span>]
    id8{<span class="hljs-string">Rhombus</span>}
    id9{{<span class="hljs-string">Hexagon</span>}}
    id10[/<span class="hljs-string">Parallelogram</span>/]
    id11[\<span class="hljs-string">Alt parallelogram</span>\]
    id12[/<span class="hljs-string">Trapezoid</span>\]
    id13[\<span class="hljs-string">Alt trapezoid</span>/]
    id14(((<span class="hljs-string">Double circle</span>)))
""");
    }

    [Fact]
    public void Flowchart_Links()
    {
        AssertHighlighter("mermaid",
"""
flowchart LR
    A --> B
    A --- C
    A -.-> D
    A -.- E
    A ==> F
    A === G
    A ~~~ H
    A --o I
    A --x J
    A <--> K
    A o--o L
    A x--x M
    A ----> N
    A -- text --> B
    A -. dotted text .-> B
    A == thick text ==> B
    A -- open text --- B
    A & B --> C & D
""",
"""
<span class="hljs-keyword">flowchart</span> <span class="hljs-keyword">LR</span>
    A <span class="hljs-operator">--&gt;</span> B
    A <span class="hljs-operator">---</span> C
    A <span class="hljs-operator">-.-&gt;</span> D
    A <span class="hljs-operator">-.-</span> E
    A <span class="hljs-operator">==&gt;</span> F
    A <span class="hljs-operator">===</span> G
    A <span class="hljs-operator">~~~</span> H
    A <span class="hljs-operator">--o</span> I
    A <span class="hljs-operator">--x</span> J
    A <span class="hljs-operator">&lt;--&gt;</span> K
    A <span class="hljs-operator">o--o</span> L
    A <span class="hljs-operator">x--x</span> M
    A <span class="hljs-operator">----&gt;</span> N
    A <span class="hljs-operator">--</span> <span class="hljs-string">text</span> <span class="hljs-operator">--&gt;</span> B
    A <span class="hljs-operator">-.</span> <span class="hljs-string">dotted text</span> <span class="hljs-operator">.-&gt;</span> B
    A <span class="hljs-operator">==</span> <span class="hljs-string">thick text</span> <span class="hljs-operator">==&gt;</span> B
    A <span class="hljs-operator">--</span> <span class="hljs-string">open text</span> <span class="hljs-operator">---</span> B
    A &amp; B <span class="hljs-operator">--&gt;</span> C &amp; D
""");
    }

    [Fact]
    public void Flowchart_LinkTextArrowEnds()
    {
        AssertHighlighter("mermaid",
"""
flowchart LR
    A -- text --x B --> C
    D -- no --o E
    A -. text .-x B
    A -. text ..-o B
    A == text ==x B
    A == text ===o B
    A <-- text --> B
    A x-- text --x B
    A o== text ==o B
    A <-. text .-> B
    A -- text --- B
    A -- text ---- B
    box -- text --> ox
""",
"""
<span class="hljs-keyword">flowchart</span> <span class="hljs-keyword">LR</span>
    A <span class="hljs-operator">--</span> <span class="hljs-string">text</span> <span class="hljs-operator">--x</span> B <span class="hljs-operator">--&gt;</span> C
    D <span class="hljs-operator">--</span> <span class="hljs-string">no</span> <span class="hljs-operator">--o</span> E
    A <span class="hljs-operator">-.</span> <span class="hljs-string">text</span> <span class="hljs-operator">.-x</span> B
    A <span class="hljs-operator">-.</span> <span class="hljs-string">text</span> <span class="hljs-operator">..-o</span> B
    A <span class="hljs-operator">==</span> <span class="hljs-string">text</span> <span class="hljs-operator">==x</span> B
    A <span class="hljs-operator">==</span> <span class="hljs-string">text</span> <span class="hljs-operator">===o</span> B
    A <span class="hljs-operator">&lt;--</span> <span class="hljs-string">text</span> <span class="hljs-operator">--&gt;</span> B
    A <span class="hljs-operator">x--</span> <span class="hljs-string">text</span> <span class="hljs-operator">--x</span> B
    A <span class="hljs-operator">o==</span> <span class="hljs-string">text</span> <span class="hljs-operator">==o</span> B
    A <span class="hljs-operator">&lt;-.</span> <span class="hljs-string">text</span> <span class="hljs-operator">.-&gt;</span> B
    A <span class="hljs-operator">--</span> <span class="hljs-string">text</span> <span class="hljs-operator">---</span> B
    A <span class="hljs-operator">--</span> <span class="hljs-string">text</span> <span class="hljs-operator">----</span> B
    box <span class="hljs-operator">--</span> <span class="hljs-string">text</span> <span class="hljs-operator">--&gt;</span> ox
""");
    }

    [Fact]
    public void Flowchart_LinkTextAfterLongerArrow()
    {
        AssertHighlighter("mermaid",
"""
flowchart LR
    A --- B -- text --> C
    A === B == text ==> C
    A -.- B -. text .-> C
""",
"""
<span class="hljs-keyword">flowchart</span> <span class="hljs-keyword">LR</span>
    A <span class="hljs-operator">---</span> B <span class="hljs-operator">--</span> <span class="hljs-string">text</span> <span class="hljs-operator">--&gt;</span> C
    A <span class="hljs-operator">===</span> B <span class="hljs-operator">==</span> <span class="hljs-string">text</span> <span class="hljs-operator">==&gt;</span> C
    A <span class="hljs-operator">-.-</span> B <span class="hljs-operator">-.</span> <span class="hljs-string">text</span> <span class="hljs-operator">.-&gt;</span> C
""");
    }

    [Fact]
    public void Flowchart_QuotedText()
    {
        AssertHighlighter("mermaid",
"""
flowchart LR
    id1["This is the (text) in the box"]
    id2["`**Markdown** text
    on two lines`"]
    A -->|"quoted label"| B
""",
"""
<span class="hljs-keyword">flowchart</span> <span class="hljs-keyword">LR</span>
    id1[<span class="hljs-string">&quot;This is the (text) in the box&quot;</span>]
    id2[<span class="hljs-string">&quot;`**Markdown** text
    on two lines`&quot;</span>]
    A <span class="hljs-operator">--&gt;</span>|<span class="hljs-string">&quot;quoted label&quot;</span>| B
""");
    }

    [Fact]
    public void Flowchart_Subgraph()
    {
        AssertHighlighter("mermaid",
"""
flowchart TB
    c1-->a2
    subgraph one [Title One]
    direction RL
    a1-->a2
    end
    subgraph two
    b1-->b2
    end
    one --> two
""",
"""
<span class="hljs-keyword">flowchart</span> <span class="hljs-keyword">TB</span>
    c1<span class="hljs-operator">--&gt;</span>a2
    <span class="hljs-keyword">subgraph</span> one [<span class="hljs-string">Title One</span>]
    <span class="hljs-keyword">direction</span> <span class="hljs-keyword">RL</span>
    a1<span class="hljs-operator">--&gt;</span>a2
    <span class="hljs-keyword">end</span>
    <span class="hljs-keyword">subgraph</span> two
    b1<span class="hljs-operator">--&gt;</span>b2
    <span class="hljs-keyword">end</span>
    one <span class="hljs-operator">--&gt;</span> two
""");
    }

    [Fact]
    public void Flowchart_Styling()
    {
        AssertHighlighter("mermaid",
"""
flowchart LR
    A:::someclass --> B
    classDef someclass fill:#f96,stroke:#333,stroke-width:4px
    classDef default fill:#fff
    class A,B someclass
    style B fill:#bbf,stroke:#f66,stroke-width:2px,color:#fff,stroke-dasharray: 5 5
    linkStyle 0 stroke:#ff3,stroke-width:4px,color:red;
    linkStyle default interpolate basis
""",
"""
<span class="hljs-keyword">flowchart</span> <span class="hljs-keyword">LR</span>
    A:::<span class="hljs-title class_">someclass</span> <span class="hljs-operator">--&gt;</span> B
    <span class="hljs-keyword">classDef</span> <span class="hljs-title class_">someclass</span> <span class="hljs-attr">fill</span>:<span class="hljs-number">#f96</span>,<span class="hljs-attr">stroke</span>:<span class="hljs-number">#333</span>,<span class="hljs-attr">stroke-width</span>:<span class="hljs-number">4px</span>
    <span class="hljs-keyword">classDef</span> <span class="hljs-title class_">default</span> <span class="hljs-attr">fill</span>:<span class="hljs-number">#fff</span>
    <span class="hljs-keyword">class</span> A,B someclass
    <span class="hljs-keyword">style</span> B <span class="hljs-attr">fill</span>:<span class="hljs-number">#bbf</span>,<span class="hljs-attr">stroke</span>:<span class="hljs-number">#f66</span>,<span class="hljs-attr">stroke-width</span>:<span class="hljs-number">2px</span>,<span class="hljs-attr">color</span>:<span class="hljs-number">#fff</span>,<span class="hljs-attr">stroke-dasharray</span>: <span class="hljs-number">5</span> <span class="hljs-number">5</span>
    <span class="hljs-keyword">linkStyle</span> <span class="hljs-number">0</span> <span class="hljs-attr">stroke</span>:<span class="hljs-number">#ff3</span>,<span class="hljs-attr">stroke-width</span>:<span class="hljs-number">4px</span>,<span class="hljs-attr">color</span>:red;
    <span class="hljs-keyword">linkStyle</span> <span class="hljs-keyword">default</span> <span class="hljs-keyword">interpolate</span> basis
""");
    }

    [Fact]
    public void Flowchart_Click()
    {
        AssertHighlighter("mermaid",
"""
flowchart LR
    A-->B
    click A callback "Tooltip for a callback"
    click B href "https://www.github.com" "This is a tooltip for a link" _blank
""",
"""
<span class="hljs-keyword">flowchart</span> <span class="hljs-keyword">LR</span>
    A<span class="hljs-operator">--&gt;</span>B
    <span class="hljs-keyword">click</span> A <span class="hljs-keyword">callback</span> <span class="hljs-string">&quot;Tooltip for a callback&quot;</span>
    <span class="hljs-keyword">click</span> B <span class="hljs-keyword">href</span> <span class="hljs-string">&quot;https://www.github.com&quot;</span> <span class="hljs-string">&quot;This is a tooltip for a link&quot;</span> <span class="hljs-keyword">_blank</span>
""");
    }

    [Fact]
    public void Flowchart_ShapeData()
    {
        AssertHighlighter("mermaid",
"""
flowchart TD
    A@{ shape: rect, label: "This is a process" }
    B@{ shape: circle }
    e1@--> C
""",
"""
<span class="hljs-keyword">flowchart</span> <span class="hljs-keyword">TD</span>
    A@{ <span class="hljs-attr">shape</span>: rect, <span class="hljs-attr">label</span>: <span class="hljs-string">&quot;This is a process&quot;</span> }
    B@{ <span class="hljs-attr">shape</span>: circle }
    e1@<span class="hljs-operator">--&gt;</span> C
""");
    }

    [Fact]
    public void Flowchart_Comments()
    {
        AssertHighlighter("mermaid",
"""
%% A comment before the diagram
flowchart LR
    %% this is a comment A -- text --> B{node}
    A -- text --> B -- text2 --> C
""",
"""
<span class="hljs-comment">%% A comment before the diagram</span>
<span class="hljs-keyword">flowchart</span> <span class="hljs-keyword">LR</span>
    <span class="hljs-comment">%% this is a comment A -- text --&gt; B{node}</span>
    A <span class="hljs-operator">--</span> <span class="hljs-string">text</span> <span class="hljs-operator">--&gt;</span> B <span class="hljs-operator">--</span> <span class="hljs-string">text2</span> <span class="hljs-operator">--&gt;</span> C
""");
    }

    [Fact]
    public void Directive_Init()
    {
        AssertHighlighter("mermaid",
"""
%%{init: {"theme": "forest", 'themeVariables': {"primaryColor": "#ff0000"}}}%%
graph TD
    A --> B
""",
"""
<span class="hljs-meta">%%{<span class="hljs-keyword">init</span>: {<span class="hljs-string">&quot;theme&quot;</span>: <span class="hljs-string">&quot;forest&quot;</span>, <span class="hljs-string">&#x27;themeVariables&#x27;</span>: {<span class="hljs-string">&quot;primaryColor&quot;</span>: <span class="hljs-string">&quot;#ff0000&quot;</span>}}}%%</span>
<span class="hljs-keyword">graph</span> <span class="hljs-keyword">TD</span>
    A <span class="hljs-operator">--&gt;</span> B
""");
    }

    [Fact]
    public void FrontMatter_Yaml()
    {
        AssertHighlighter("mermaid",
"""
---
title: Hello Title
config:
  theme: base
  themeVariables:
    primaryColor: "#00ff00"
---
flowchart
    Hello --> World
""",
"""
<span class="language-yaml"><span class="hljs-meta">---</span>
<span class="hljs-attr">title:</span> <span class="hljs-string">Hello</span> <span class="hljs-string">Title</span>
<span class="hljs-attr">config:</span>
  <span class="hljs-attr">theme:</span> <span class="hljs-string">base</span>
  <span class="hljs-attr">themeVariables:</span>
    <span class="hljs-attr">primaryColor:</span> <span class="hljs-string">&quot;#00ff00&quot;</span>
<span class="hljs-meta">---</span></span>
<span class="hljs-keyword">flowchart</span>
    Hello <span class="hljs-operator">--&gt;</span> World
""");
    }

    [Fact]
    public void Sequence_Basic()
    {
        AssertHighlighter("mermaid",
"""
sequenceDiagram
    participant Alice
    actor Bob as Bob the Builder
    Alice->>John: Hello John, how are you?
    John-->>Alice: Great!
    Alice-)John: See you later!
    Alice-xBob: Lost
    Alice->>+John: Activate
    John-->>-Alice: Deactivate
    Note right of John: Text in note
    Note over Alice,John: A typical interaction
""",
"""
<span class="hljs-keyword">sequenceDiagram</span>
    <span class="hljs-keyword">participant</span> Alice
    <span class="hljs-keyword">actor</span> Bob <span class="hljs-keyword">as</span> Bob the Builder
    Alice<span class="hljs-operator">-&gt;&gt;</span>John: <span class="hljs-string">Hello John, how are you?</span>
    John<span class="hljs-operator">--&gt;&gt;</span>Alice: <span class="hljs-string">Great!</span>
    Alice<span class="hljs-operator">-)</span>John: <span class="hljs-string">See you later!</span>
    Alice<span class="hljs-operator">-x</span>Bob: <span class="hljs-string">Lost</span>
    Alice<span class="hljs-operator">-&gt;&gt;+</span>John: <span class="hljs-string">Activate</span>
    John<span class="hljs-operator">--&gt;&gt;-</span>Alice: <span class="hljs-string">Deactivate</span>
    <span class="hljs-keyword">Note</span> <span class="hljs-keyword">right</span> <span class="hljs-keyword">of</span> John: <span class="hljs-string">Text in note</span>
    <span class="hljs-keyword">Note</span> <span class="hljs-keyword">over</span> Alice,John: <span class="hljs-string">A typical interaction</span>
""");
    }

    [Fact]
    public void Sequence_Blocks()
    {
        AssertHighlighter("mermaid",
"""
sequenceDiagram
    autonumber
    loop Every minute
        John-->>Alice: Great!
    end
    alt is sick
        Bob->>Alice: Not so good :(
    else is well and fine
        Bob->>Alice: Feeling fresh like a daisy
    end
    opt Extra response
        Bob->>Alice: Thanks for asking
    end
    par Alice to Bob
        Alice->>Bob: Hello guys!
    and Alice to John
        Alice->>John: Hello guys!
    end
    critical Establish a connection
        Service-->DB: connect
    option Network timeout
        Service-->Service: Log error
    end
    break when the booking process fails
        API-->Consumer: show failure
    end
    rect rgb(191, 223, 255)
    Alice->>Bob: Colored
    end
""",
"""
<span class="hljs-keyword">sequenceDiagram</span>
    <span class="hljs-keyword">autonumber</span>
    <span class="hljs-keyword">loop</span> Every minute
        John<span class="hljs-operator">--&gt;&gt;</span>Alice: <span class="hljs-string">Great!</span>
    <span class="hljs-keyword">end</span>
    <span class="hljs-keyword">alt</span> is sick
        Bob<span class="hljs-operator">-&gt;&gt;</span>Alice: <span class="hljs-string">Not so good :(</span>
    <span class="hljs-keyword">else</span> is well and fine
        Bob<span class="hljs-operator">-&gt;&gt;</span>Alice: <span class="hljs-string">Feeling fresh like a daisy</span>
    <span class="hljs-keyword">end</span>
    <span class="hljs-keyword">opt</span> Extra response
        Bob<span class="hljs-operator">-&gt;&gt;</span>Alice: <span class="hljs-string">Thanks for asking</span>
    <span class="hljs-keyword">end</span>
    <span class="hljs-keyword">par</span> Alice to Bob
        Alice<span class="hljs-operator">-&gt;&gt;</span>Bob: <span class="hljs-string">Hello guys!</span>
    <span class="hljs-keyword">and</span> Alice to John
        Alice<span class="hljs-operator">-&gt;&gt;</span>John: <span class="hljs-string">Hello guys!</span>
    <span class="hljs-keyword">end</span>
    <span class="hljs-keyword">critical</span> Establish a connection
        Service<span class="hljs-operator">--&gt;</span>DB: <span class="hljs-string">connect</span>
    <span class="hljs-keyword">option</span> Network timeout
        Service<span class="hljs-operator">--&gt;</span>Service: <span class="hljs-string">Log error</span>
    <span class="hljs-keyword">end</span>
    <span class="hljs-keyword">break</span> when the booking process fails
        API<span class="hljs-operator">--&gt;</span>Consumer: <span class="hljs-string">show failure</span>
    <span class="hljs-keyword">end</span>
    <span class="hljs-keyword">rect</span> rgb(191, 223, 255)
    Alice<span class="hljs-operator">-&gt;&gt;</span>Bob: <span class="hljs-string">Colored</span>
    <span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Sequence_ParticipantTypes()
    {
        AssertHighlighter("mermaid",
"""
sequenceDiagram
    participant database@{ "type": "database" }
    participant queue
    database->>queue: Enqueue
    par_over Parallel
    properties queue: {"class": "internal"}
    end
""",
"""
<span class="hljs-keyword">sequenceDiagram</span>
    <span class="hljs-keyword">participant</span> database@{ <span class="hljs-string">&quot;type&quot;</span>: <span class="hljs-string">&quot;database&quot;</span> }
    <span class="hljs-keyword">participant</span> queue
    database<span class="hljs-operator">-&gt;&gt;</span>queue: <span class="hljs-string">Enqueue</span>
    <span class="hljs-keyword">par_over</span> Parallel
    <span class="hljs-keyword">properties</span> queue: <span class="hljs-string">{&quot;class&quot;: &quot;internal&quot;}</span>
    <span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Sequence_CreateDestroy()
    {
        AssertHighlighter("mermaid",
"""
sequenceDiagram
    box Aqua Group Description
    participant A
    end
    create participant Carl
    Alice->>Carl: Hi Carl!
    destroy Carl
    Alice-xCarl: We are too many
    activate John
    deactivate John
    link Alice: Dashboard @ https://dashboard.contoso.com/alice
""",
"""
<span class="hljs-keyword">sequenceDiagram</span>
    <span class="hljs-keyword">box</span> Aqua Group Description
    <span class="hljs-keyword">participant</span> A
    <span class="hljs-keyword">end</span>
    <span class="hljs-keyword">create</span> <span class="hljs-keyword">participant</span> Carl
    Alice<span class="hljs-operator">-&gt;&gt;</span>Carl: <span class="hljs-string">Hi Carl!</span>
    <span class="hljs-keyword">destroy</span> Carl
    Alice<span class="hljs-operator">-x</span>Carl: <span class="hljs-string">We are too many</span>
    <span class="hljs-keyword">activate</span> John
    <span class="hljs-keyword">deactivate</span> John
    <span class="hljs-keyword">link</span> Alice: <span class="hljs-string">Dashboard @ https://dashboard.contoso.com/alice</span>
""");
    }

    [Fact]
    public void Class_Basic()
    {
        AssertHighlighter("mermaid",
"""
classDiagram
    Animal <|-- Duck
    Animal <|-- Fish
    Animal : +int age
    Animal : +String gender
    Animal: +isMammal()
    Animal: +mate()
    class Duck{
        +String beakColor
        +swim()
        +quack() bool
    }
    class Shape{
        <<interface>>
        noOfVertices
        draw()
    }
    classA --|> classB : Inheritance
    classC --* classD : Composition
    classE --o classF : Aggregation
    classG --> classH : Association
    classI -- classJ : Link(Solid)
    classK ..> classL : Dependency
    classM ..|> classN : Realization
    classO .. classP : Link(Dashed)
    Customer "1" --> "*" Ticket
    note for Duck "can fly\ncan swim"
    class Square~Shape~
""",
"""
<span class="hljs-keyword">classDiagram</span>
    Animal <span class="hljs-operator">&lt;|--</span> Duck
    Animal <span class="hljs-operator">&lt;|--</span> Fish
    Animal : +int age
    Animal : +String gender
    Animal: +<span class="hljs-title function_">isMammal</span>()
    Animal: +<span class="hljs-title function_">mate</span>()
    <span class="hljs-keyword">class</span> <span class="hljs-title class_">Duck</span>{
        +String beakColor
        +<span class="hljs-title function_">swim</span>()
        +<span class="hljs-title function_">quack</span>() bool
    }
    <span class="hljs-keyword">class</span> <span class="hljs-title class_">Shape</span>{
        <span class="hljs-meta">&lt;&lt;interface&gt;&gt;</span>
        noOfVertices
        <span class="hljs-title function_">draw</span>()
    }
    classA <span class="hljs-operator">--|&gt;</span> classB : Inheritance
    classC <span class="hljs-operator">--*</span> classD : Composition
    classE <span class="hljs-operator">--o</span> classF : Aggregation
    classG <span class="hljs-operator">--&gt;</span> classH : Association
    classI <span class="hljs-operator">--</span> classJ : <span class="hljs-title function_">Link</span>(Solid)
    classK <span class="hljs-operator">..&gt;</span> classL : Dependency
    classM <span class="hljs-operator">..|&gt;</span> classN : Realization
    classO <span class="hljs-operator">..</span> classP : <span class="hljs-title function_">Link</span>(Dashed)
    Customer <span class="hljs-string">&quot;1&quot;</span> <span class="hljs-operator">--&gt;</span> <span class="hljs-string">&quot;*&quot;</span> Ticket
    <span class="hljs-keyword">note</span> <span class="hljs-keyword">for</span> Duck <span class="hljs-string">&quot;can fly\ncan swim&quot;</span>
    <span class="hljs-keyword">class</span> <span class="hljs-title class_">Square</span>~Shape~
""");
    }

    [Fact]
    public void Class_Namespace()
    {
        AssertHighlighter("mermaid",
"""
classDiagram-v2
namespace BaseShapes {
    class Triangle
    class Rectangle {
      double width
    }
}
direction RL
""",
"""
<span class="hljs-keyword">classDiagram-v2</span>
<span class="hljs-keyword">namespace</span> BaseShapes {
    <span class="hljs-keyword">class</span> <span class="hljs-title class_">Triangle</span>
    <span class="hljs-keyword">class</span> <span class="hljs-title class_">Rectangle</span> {
      double width
    }
}
<span class="hljs-keyword">direction</span> <span class="hljs-keyword">RL</span>
""");
    }

    [Fact]
    public void State_Basic()
    {
        AssertHighlighter("mermaid",
"""
stateDiagram-v2
    [*] --> Still
    Still --> [*]
    Still --> Moving
    Moving --> Still : stopped
    Moving --> Crash
    Crash --> [*]
    state "This is a state description" as s2
    s2 : This is another description
    state fork_state <<fork>>
    state if_state <<choice>>
""",
"""
<span class="hljs-keyword">stateDiagram-v2</span>
    <span class="hljs-symbol">[*]</span> <span class="hljs-operator">--&gt;</span> Still
    Still <span class="hljs-operator">--&gt;</span> <span class="hljs-symbol">[*]</span>
    Still <span class="hljs-operator">--&gt;</span> Moving
    Moving <span class="hljs-operator">--&gt;</span> Still : <span class="hljs-string">stopped</span>
    Moving <span class="hljs-operator">--&gt;</span> Crash
    Crash <span class="hljs-operator">--&gt;</span> <span class="hljs-symbol">[*]</span>
    <span class="hljs-keyword">state</span> <span class="hljs-string">&quot;This is a state description&quot;</span> <span class="hljs-keyword">as</span> s2
    s2 : <span class="hljs-string">This is another description</span>
    <span class="hljs-keyword">state</span> fork_state <span class="hljs-meta">&lt;&lt;fork&gt;&gt;</span>
    <span class="hljs-keyword">state</span> if_state <span class="hljs-meta">&lt;&lt;choice&gt;&gt;</span>
""");
    }

    [Fact]
    public void State_Composite()
    {
        AssertHighlighter("mermaid",
"""
stateDiagram
    state First {
        [*] --> second
        second --> [*]
    }
    state Active {
        [*] --> NumLockOff
        --
        [*] --> CapsLockOff
    }
    note right of First : single line note
    note left of Active
        A note can also
        be defined on several lines, left of it
    end note
    classDef movement font-style:italic;
    Still:::movement
""",
"""
<span class="hljs-keyword">stateDiagram</span>
    <span class="hljs-keyword">state</span> First {
        <span class="hljs-symbol">[*]</span> <span class="hljs-operator">--&gt;</span> second
        second <span class="hljs-operator">--&gt;</span> <span class="hljs-symbol">[*]</span>
    }
    <span class="hljs-keyword">state</span> Active {
        <span class="hljs-symbol">[*]</span> <span class="hljs-operator">--&gt;</span> NumLockOff
        <span class="hljs-operator">--</span>
        <span class="hljs-symbol">[*]</span> <span class="hljs-operator">--&gt;</span> CapsLockOff
    }
    <span class="hljs-keyword">note</span> <span class="hljs-keyword">right</span> <span class="hljs-keyword">of</span> First : <span class="hljs-string">single line note</span>
    <span class="hljs-keyword">note</span> <span class="hljs-keyword">left</span> <span class="hljs-keyword">of</span> Active<span class="hljs-string">
        A note can also
        be defined on several lines, left of it
</span>    <span class="hljs-keyword">end</span> <span class="hljs-keyword">note</span>
    <span class="hljs-keyword">classDef</span> <span class="hljs-title class_">movement</span> <span class="hljs-attr">font-style</span>:italic;
    Still:::<span class="hljs-title class_">movement</span>
""");
    }

    [Fact]
    public void Er_Basic()
    {
        AssertHighlighter("mermaid",
"""
erDiagram
    CUSTOMER ||--o{ ORDER : places
    ORDER ||--|{ LINE-ITEM : contains
    CUSTOMER }|..|{ DELIVERY-ADDRESS : uses
    CUSTOMER {
        string name
        string custNumber PK "The customer number"
        varchar(255) email UK
    }
    CAR 1 to zero or more NAMED-DRIVER : allows
    PERSON only one to optionally zero or more CAR : "is driver of"
""",
"""
<span class="hljs-keyword">erDiagram</span>
    CUSTOMER <span class="hljs-operator">||--o{</span> ORDER : <span class="hljs-string">places</span>
    ORDER <span class="hljs-operator">||--|{</span> LINE-ITEM : <span class="hljs-string">contains</span>
    CUSTOMER <span class="hljs-operator">}|..|{</span> DELIVERY-ADDRESS : <span class="hljs-string">uses</span>
    CUSTOMER {
        <span class="hljs-type">string</span> name
        <span class="hljs-type">string</span> custNumber <span class="hljs-keyword">PK</span> <span class="hljs-string">&quot;The customer number&quot;</span>
        <span class="hljs-type">varchar(255)</span> email <span class="hljs-keyword">UK</span>
    }
    CAR 1 <span class="hljs-keyword">to</span> <span class="hljs-keyword">zero</span> <span class="hljs-keyword">or</span> <span class="hljs-keyword">more</span> NAMED-DRIVER : <span class="hljs-string">allows</span>
    PERSON <span class="hljs-keyword">only</span> <span class="hljs-keyword">one</span> <span class="hljs-keyword">to</span> <span class="hljs-keyword">optionally</span> <span class="hljs-keyword">zero</span> <span class="hljs-keyword">or</span> <span class="hljs-keyword">more</span> CAR : <span class="hljs-string">&quot;is driver of&quot;</span>
""");
    }

    [Fact]
    public void Gantt_Basic()
    {
        AssertHighlighter("mermaid",
"""
gantt
    title A Gantt Diagram
    dateFormat YYYY-MM-DD
    excludes weekends
    section Section
        A task          :a1, 2014-01-01, 30d
        Another task    :after a1, 20d
    section Another
        Task in Another :2014-01-12, 12d
        another task    :24d
        Done task       :done, des1, 2014-01-06,2014-01-08
        Critical task   :crit, active, 3d
        Milestone       :milestone, m1, 17:49, 2m
    click a1 href "https://mermaid.js.org"
""",
"""
<span class="hljs-keyword">gantt</span>
    <span class="hljs-keyword">title</span> A Gantt Diagram
    <span class="hljs-keyword">dateFormat</span> YYYY-MM-DD
    <span class="hljs-keyword">excludes</span> weekends
    <span class="hljs-keyword">section</span> Section
        A task          :a1, <span class="hljs-number">2014-01-01</span>, <span class="hljs-number">30d</span>
        Another task    :<span class="hljs-keyword">after</span> a1, <span class="hljs-number">20d</span>
    <span class="hljs-keyword">section</span> Another
        Task in Another :<span class="hljs-number">2014-01-12</span>, <span class="hljs-number">12d</span>
        another task    :<span class="hljs-number">24d</span>
        Done task       :<span class="hljs-keyword">done</span>, des1, <span class="hljs-number">2014-01-06</span>,<span class="hljs-number">2014-01-08</span>
        Critical task   :<span class="hljs-keyword">crit</span>, <span class="hljs-keyword">active</span>, <span class="hljs-number">3d</span>
        Milestone       :<span class="hljs-keyword">milestone</span>, m1, <span class="hljs-number">17:49</span>, <span class="hljs-number">2m</span>
    <span class="hljs-keyword">click</span> a1 <span class="hljs-keyword">href</span> <span class="hljs-string">&quot;https://mermaid.js.org&quot;</span>
""");
    }

    [Fact]
    public void Pie_Basic()
    {
        AssertHighlighter("mermaid",
"""
pie showData
    title Key elements in Product X
    "Calcium" : 42.96
    "Potassium" : 50.05
    "Magnesium" : 10.01
    "Iron" :  5
""",
"""
<span class="hljs-keyword">pie</span> <span class="hljs-keyword">showData</span>
    <span class="hljs-keyword">title</span> Key elements in Product X
    <span class="hljs-string">&quot;Calcium&quot;</span> : <span class="hljs-number">42.96</span>
    <span class="hljs-string">&quot;Potassium&quot;</span> : <span class="hljs-number">50.05</span>
    <span class="hljs-string">&quot;Magnesium&quot;</span> : <span class="hljs-number">10.01</span>
    <span class="hljs-string">&quot;Iron&quot;</span> :  <span class="hljs-number">5</span>
""");
    }

    [Fact]
    public void Pie_TitleOnDeclarationLine()
    {
        AssertHighlighter("mermaid",
"""
pie title Pets adopted by volunteers 2024
    "Dogs" : 386
pie showData title Pets %% comment
""",
"""
<span class="hljs-keyword">pie</span> <span class="hljs-keyword">title</span> Pets adopted by volunteers 2024
    <span class="hljs-string">&quot;Dogs&quot;</span> : <span class="hljs-number">386</span>
pie <span class="hljs-keyword">showData</span> <span class="hljs-keyword">title</span> Pets <span class="hljs-comment">%% comment</span>
""");
    }

    [Fact]
    public void Journey_Basic()
    {
        AssertHighlighter("mermaid",
"""
journey
    title My working day
    section Go to work
      Make tea: 5: Me
      Go upstairs: 3: Me
      Do work: 1: Me, Cat
    section Go home
      Go downstairs: 5: Me
""",
"""
<span class="hljs-keyword">journey</span>
    <span class="hljs-keyword">title</span> My working day
    <span class="hljs-keyword">section</span> Go to work
      Make tea: <span class="hljs-number">5</span>: Me
      Go upstairs: <span class="hljs-number">3</span>: Me
      Do work: <span class="hljs-number">1</span>: Me, Cat
    <span class="hljs-keyword">section</span> Go home
      Go downstairs: <span class="hljs-number">5</span>: Me
""");
    }

    [Fact]
    public void GitGraph_Basic()
    {
        AssertHighlighter("mermaid",
"""
gitGraph
    commit
    commit id: "Alpha" tag: "v1.0.0"
    branch develop
    checkout develop
    commit type: HIGHLIGHT
    checkout main
    merge develop
    cherry-pick id: "Alpha"
    commit type: REVERSE
""",
"""
<span class="hljs-keyword">gitGraph</span>
    <span class="hljs-keyword">commit</span>
    <span class="hljs-keyword">commit</span> <span class="hljs-attr">id</span>: <span class="hljs-string">&quot;Alpha&quot;</span> <span class="hljs-attr">tag</span>: <span class="hljs-string">&quot;v1.0.0&quot;</span>
    <span class="hljs-keyword">branch</span> develop
    <span class="hljs-keyword">checkout</span> develop
    <span class="hljs-keyword">commit</span> <span class="hljs-attr">type</span>: <span class="hljs-keyword">HIGHLIGHT</span>
    <span class="hljs-keyword">checkout</span> main
    <span class="hljs-keyword">merge</span> develop
    <span class="hljs-keyword">cherry-pick</span> <span class="hljs-attr">id</span>: <span class="hljs-string">&quot;Alpha&quot;</span>
    <span class="hljs-keyword">commit</span> <span class="hljs-attr">type</span>: <span class="hljs-keyword">REVERSE</span>
""");
    }

    [Fact]
    public void Mindmap_Basic()
    {
        AssertHighlighter("mermaid",
"""
mindmap
  root((mindmap))
    Origins
      Long history
      ::icon(fa fa-book)
      Popularisation
        British popular psychology author Tony Buzan
    Research
      On effectiveness<br/>and features
    id)I am a cloud(
    id))I am a bang((
    id{{Hexagon}}:::urgent
""",
"""
<span class="hljs-keyword">mindmap</span>
  root((<span class="hljs-string">mindmap</span>))
    Origins
      Long history
      <span class="hljs-meta">::icon(fa fa-book)</span>
      Popularisation
        British popular psychology author Tony Buzan
    Research
      On effectiveness&lt;br/&gt;and features
    id)<span class="hljs-string">I am a cloud</span>(
    id))<span class="hljs-string">I am a bang</span>((
    id{{<span class="hljs-string">Hexagon</span>}}:::<span class="hljs-title class_">urgent</span>
""");
    }

    [Fact]
    public void Mindmap_NodesWithoutId()
    {
        AssertHighlighter("mermaid",
"""
mindmap
  ((Root))
    [Square]
    (Rounded)
    ))Bang((
    )Cloud(
    {{Hexagon}}
""",
"""
<span class="hljs-keyword">mindmap</span>
  ((<span class="hljs-string">Root</span>))
    [<span class="hljs-string">Square</span>]
    (<span class="hljs-string">Rounded</span>)
    ))<span class="hljs-string">Bang</span>((
    )<span class="hljs-string">Cloud</span>(
    {{<span class="hljs-string">Hexagon</span>}}
""");
    }

    [Fact]
    public void Timeline_Basic()
    {
        AssertHighlighter("mermaid",
"""
timeline
    title History of Social Media Platform
    section 2002 - 2006
        2002 : LinkedIn
        2004 : Facebook : Google
    2005 : YouTube
""",
"""
<span class="hljs-keyword">timeline</span>
    <span class="hljs-keyword">title</span> History of Social Media Platform
    <span class="hljs-keyword">section</span> 2002 - 2006
        2002 : LinkedIn
        2004 : Facebook : Google
    2005 : YouTube
""");
    }

    [Fact]
    public void Quadrant_Basic()
    {
        AssertHighlighter("mermaid",
"""
quadrantChart
    title Reach and engagement of campaigns
    x-axis Low Reach --> High Reach
    y-axis Low Engagement --> High Engagement
    quadrant-1 We should expand
    quadrant-2 Need to promote
    Campaign A: [0.3, 0.6]
    Campaign B: [0.45, 0.23]
""",
"""
<span class="hljs-keyword">quadrantChart</span>
    <span class="hljs-keyword">title</span> Reach and engagement of campaigns
    <span class="hljs-keyword">x-axis</span> Low Reach <span class="hljs-operator">--&gt;</span> High Reach
    <span class="hljs-keyword">y-axis</span> Low Engagement <span class="hljs-operator">--&gt;</span> High Engagement
    <span class="hljs-keyword">quadrant-1</span> We should expand
    <span class="hljs-keyword">quadrant-2</span> Need to promote
    Campaign A: [<span class="hljs-number">0.3</span>, <span class="hljs-number">0.6</span>]
    Campaign B: [<span class="hljs-number">0.45</span>, <span class="hljs-number">0.23</span>]
""");
    }

    [Fact]
    public void Requirement_Basic()
    {
        AssertHighlighter("mermaid",
"""
requirementDiagram
    requirement test_req {
    id: 1
    text: the test text.
    risk: high
    verifymethod: test
    }
    element test_entity {
    type: simulation
    }
    test_entity - satisfies -> test_req
""",
"""
<span class="hljs-keyword">requirementDiagram</span>
    <span class="hljs-keyword">requirement</span> test_req {
    <span class="hljs-attr">id</span>: 1
    <span class="hljs-attr">text</span>: the test text.
    <span class="hljs-attr">risk</span>: <span class="hljs-literal">high</span>
    <span class="hljs-attr">verifymethod</span>: <span class="hljs-literal">test</span>
    }
    <span class="hljs-keyword">element</span> test_entity {
    <span class="hljs-attr">type</span>: simulation
    }
    test_entity <span class="hljs-operator">-</span> <span class="hljs-keyword">satisfies</span> <span class="hljs-operator">-&gt;</span> test_req
""");
    }

    [Fact]
    public void C4_Basic()
    {
        AssertHighlighter("mermaid",
"""
C4Context
    title System Context diagram for Internet Banking System
    Person(customerA, "Banking Customer A", "A customer of the bank.")
    System(SystemAA, "Internet Banking System", "Allows customers to view information.")
    Enterprise_Boundary(b1, "BankBoundary") {
        SystemDb_Ext(SystemE, "Mainframe Banking System", $tags="v1.0")
    }
    Rel(customerA, SystemAA, "Uses")
    UpdateRelStyle(customerA, SystemAA, $textColor="blue", $offsetY="-10")
""",
"""
<span class="hljs-keyword">C4Context</span>
    <span class="hljs-keyword">title</span> System Context diagram for Internet Banking System
    <span class="hljs-built_in">Person</span>(customerA, <span class="hljs-string">&quot;Banking Customer A&quot;</span>, <span class="hljs-string">&quot;A customer of the bank.&quot;</span>)
    <span class="hljs-built_in">System</span>(SystemAA, <span class="hljs-string">&quot;Internet Banking System&quot;</span>, <span class="hljs-string">&quot;Allows customers to view information.&quot;</span>)
    <span class="hljs-built_in">Enterprise_Boundary</span>(b1, <span class="hljs-string">&quot;BankBoundary&quot;</span>) {
        <span class="hljs-built_in">SystemDb_Ext</span>(SystemE, <span class="hljs-string">&quot;Mainframe Banking System&quot;</span>, <span class="hljs-variable">$tags</span>=<span class="hljs-string">&quot;v1.0&quot;</span>)
    }
    <span class="hljs-built_in">Rel</span>(customerA, SystemAA, <span class="hljs-string">&quot;Uses&quot;</span>)
    <span class="hljs-built_in">UpdateRelStyle</span>(customerA, SystemAA, <span class="hljs-variable">$textColor</span>=<span class="hljs-string">&quot;blue&quot;</span>, <span class="hljs-variable">$offsetY</span>=<span class="hljs-string">&quot;-10&quot;</span>)
""");
    }

    [Fact]
    public void XyChart_Basic()
    {
        AssertHighlighter("mermaid",
"""
xychart-beta
    title "Sales Revenue"
    x-axis [jan, feb, mar, apr]
    y-axis "Revenue (in $)" 4000 --> 11000
    bar [5000, 6000, 7500, 8200]
    line [5000, 6000, 7500, 8200]
""",
"""
<span class="hljs-keyword">xychart-beta</span>
    <span class="hljs-keyword">title</span> <span class="hljs-string">&quot;Sales Revenue&quot;</span>
    <span class="hljs-keyword">x-axis</span> [jan, feb, mar, apr]
    <span class="hljs-keyword">y-axis</span> <span class="hljs-string">&quot;Revenue (in $)&quot;</span> <span class="hljs-number">4000</span> <span class="hljs-operator">--&gt;</span> <span class="hljs-number">11000</span>
    <span class="hljs-keyword">bar</span> [<span class="hljs-number">5000</span>, <span class="hljs-number">6000</span>, <span class="hljs-number">7500</span>, <span class="hljs-number">8200</span>]
    <span class="hljs-keyword">line</span> [<span class="hljs-number">5000</span>, <span class="hljs-number">6000</span>, <span class="hljs-number">7500</span>, <span class="hljs-number">8200</span>]
""");
    }

    [Fact]
    public void Sankey_Basic()
    {
        AssertHighlighter("mermaid",
"""
sankey-beta
Agricultural 'waste',Bio-conversion,124.729
Bio-conversion,Liquid,0.597
"Solar, PV",Electricity,59.901
""",
"""
<span class="hljs-keyword">sankey-beta</span>
Agricultural &#x27;waste&#x27;,Bio-conversion,<span class="hljs-number">124.729</span>
Bio-conversion,Liquid,<span class="hljs-number">0.597</span>
<span class="hljs-string">&quot;Solar, PV&quot;</span>,Electricity,<span class="hljs-number">59.901</span>
""");
    }

    [Fact]
    public void Block_Basic()
    {
        AssertHighlighter("mermaid",
"""
block-beta
columns 3
a:3
block:group1:2
  columns 2
  h i j k
end
g
space
db[("DB")]
a --> db
""",
"""
<span class="hljs-keyword">block-beta</span>
<span class="hljs-keyword">columns</span> <span class="hljs-number">3</span>
a:<span class="hljs-number">3</span>
<span class="hljs-keyword">block</span>:group1:<span class="hljs-number">2</span>
  <span class="hljs-keyword">columns</span> <span class="hljs-number">2</span>
  h i j k
<span class="hljs-keyword">end</span>
g
<span class="hljs-keyword">space</span>
db[(<span class="hljs-string">&quot;DB&quot;</span>)]
a <span class="hljs-operator">--&gt;</span> db
""");
    }

    [Fact]
    public void Packet_Basic()
    {
        AssertHighlighter("mermaid",
"""
packet-beta
title TCP Packet
0-15: "Source Port"
16-31: "Destination Port"
""",
"""
<span class="hljs-keyword">packet-beta</span>
<span class="hljs-keyword">title</span> TCP Packet
<span class="hljs-number">0</span>-<span class="hljs-number">15</span>: <span class="hljs-string">&quot;Source Port&quot;</span>
<span class="hljs-number">16</span>-<span class="hljs-number">31</span>: <span class="hljs-string">&quot;Destination Port&quot;</span>
""");
    }

    [Fact]
    public void Architecture_Basic()
    {
        AssertHighlighter("mermaid",
"""
architecture-beta
    group api(cloud)[API]
    service db(database)[Database] in api
    service server(server)[Server] in api
    db:L -- R:server
""",
"""
<span class="hljs-keyword">architecture-beta</span>
    <span class="hljs-keyword">group</span> api(<span class="hljs-string">cloud</span>)[<span class="hljs-string">API</span>]
    <span class="hljs-keyword">service</span> db(<span class="hljs-string">database</span>)[<span class="hljs-string">Database</span>] <span class="hljs-keyword">in</span> api
    <span class="hljs-keyword">service</span> server(<span class="hljs-string">server</span>)[<span class="hljs-string">Server</span>] <span class="hljs-keyword">in</span> api
    db:L <span class="hljs-operator">--</span> R:server
""");
    }

    [Fact]
    public void Kanban_Basic()
    {
        AssertHighlighter("mermaid",
"""
kanban
  Todo
    [Create Documentation]
    docs[Create Blog about the new diagram]
  id9[Ready for deploy]@{ assigned: 'knsv', priority: 'High' }
""",
"""
<span class="hljs-keyword">kanban</span>
  Todo
    [<span class="hljs-string">Create Documentation</span>]
    docs[<span class="hljs-string">Create Blog about the new diagram</span>]
  id9[<span class="hljs-string">Ready for deploy</span>]@{ <span class="hljs-attr">assigned</span>: <span class="hljs-string">&#x27;knsv&#x27;</span>, <span class="hljs-attr">priority</span>: <span class="hljs-string">&#x27;High&#x27;</span> }
""");
    }

    [Fact]
    public void Radar_Basic()
    {
        AssertHighlighter("mermaid",
"""
radar-beta
  axis m["Math"], s["Science"]
  curve a["Alice"]{85, 90}
  max 100
  min 0
""",
"""
<span class="hljs-keyword">radar-beta</span>
  <span class="hljs-keyword">axis</span> m[<span class="hljs-string">&quot;Math&quot;</span>], s[<span class="hljs-string">&quot;Science&quot;</span>]
  <span class="hljs-keyword">curve</span> a[<span class="hljs-string">&quot;Alice&quot;</span>]{<span class="hljs-number">85</span>, <span class="hljs-number">90</span>}
  <span class="hljs-keyword">max</span> <span class="hljs-number">100</span>
  <span class="hljs-keyword">min</span> <span class="hljs-number">0</span>
""");
    }

    [Fact]
    public void Unknown_NoDeclaration()
    {
        AssertHighlighter("mermaid",
"""
A --> B
"quoted"
%% comment
""",
"""
A --&gt; B
<span class="hljs-string">&quot;quoted&quot;</span>
<span class="hljs-comment">%% comment</span>
""");
    }

    [Fact]
    public void Unterminated_Shape()
    {
        AssertHighlighter("mermaid",
"""
flowchart LR
    A[unterminated --> B
    C --> D
""",
"""
<span class="hljs-keyword">flowchart</span> <span class="hljs-keyword">LR</span>
    A[<span class="hljs-string">unterminated --&gt; B</span>
    C <span class="hljs-operator">--&gt;</span> D
""");
    }

    [Fact]
    public void Unterminated_String()
    {
        AssertHighlighter("mermaid",
"""
flowchart LR
    A["unterminated] --> B
    C --> D
""",
"""
<span class="hljs-keyword">flowchart</span> <span class="hljs-keyword">LR</span>
    A[<span class="hljs-string">&quot;unterminated] --&gt; B</span>
    C <span class="hljs-operator">--&gt;</span> D
""");
    }

    [Fact]
    public void Unterminated_Directive()
    {
        AssertHighlighter("mermaid",
"""
%%{init: {"theme": "dark"}
graph TD
    A --> B
""",
"""
<span class="hljs-meta">%%{<span class="hljs-keyword">init</span>: {<span class="hljs-string">&quot;theme&quot;</span>: <span class="hljs-string">&quot;dark&quot;</span>}</span>
<span class="hljs-keyword">graph</span> <span class="hljs-keyword">TD</span>
    A <span class="hljs-operator">--&gt;</span> B
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("mermaid",
"""

""",
"""

""");
    }

    [Fact]
    public void Crlf()
    {
        AssertHighlighter("mermaid", "graph TD\r\n  A[x] --> B\r\n", "<span class=\"hljs-keyword\">graph</span> <span class=\"hljs-keyword\">TD</span>\r\n  A[<span class=\"hljs-string\">x</span>] <span class=\"hljs-operator\">--&gt;</span> B\r\n");
    }
}
