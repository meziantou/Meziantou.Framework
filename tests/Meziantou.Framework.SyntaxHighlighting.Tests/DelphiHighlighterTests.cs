namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class DelphiHighlighterTests
{
    [Fact]
    public void Hello()
    {
        AssertHighlighter("delphi",
"""
program Hello;

{$APPTYPE CONSOLE}

uses
  SysUtils;

begin
  WriteLn('Hello, world!');
  ReadLn;
end.
""",
"""
<span class="hljs-keyword">program</span> Hello;

<span class="hljs-meta">{$APPTYPE CONSOLE}</span>

<span class="hljs-keyword">uses</span>
  SysUtils;

<span class="hljs-keyword">begin</span>
  WriteLn(<span class="hljs-string">&#x27;Hello, world!&#x27;</span>);
  ReadLn;
<span class="hljs-keyword">end</span>.
""");
    }

    [Fact]
    public void Unit()
    {
        AssertHighlighter("delphi",
"""
unit MyUnit;

interface

uses
  System.SysUtils, System.Classes;

type
  TShape = class(TObject)
  private
    FName: string;
    FArea: Double;
  protected
    procedure SetName(const Value: string); virtual;
  public
    constructor Create(const AName: string);
    destructor Destroy; override;
    function GetArea: Double; virtual; abstract;
    property Name: string read FName write SetName;
  published
    property Area: Double read FArea;
  end;

  TCircle = class(TShape)
  strict private
    FRadius: Double;
  public
    function GetArea: Double; override;
  end;

implementation

constructor TShape.Create(const AName: string);
begin
  inherited Create;
  FName := AName;
end;

destructor TShape.Destroy;
begin
  inherited;
end;

procedure TShape.SetName(const Value: string);
begin
  if Value <> FName then
    FName := Value;
end;

function TCircle.GetArea: Double;
begin
  Result := Pi * FRadius * FRadius;
end;

initialization
  RegisterClass(TCircle);

finalization
  UnregisterClass(TCircle);

end.
""",
"""
<span class="hljs-keyword">unit</span> MyUnit;

<span class="hljs-keyword">interface</span>

<span class="hljs-keyword">uses</span>
  System.SysUtils, System.Classes;

<span class="hljs-keyword">type</span>
  <span class="hljs-title">TShape</span> = <span class="hljs-keyword">class</span>(TObject)
  <span class="hljs-keyword">private</span>
    FName: <span class="hljs-keyword">string</span>;
    FArea: Double;
  <span class="hljs-keyword">protected</span>
    <span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">SetName</span><span class="hljs-params">(<span class="hljs-keyword">const</span> Value: <span class="hljs-keyword">string</span>)</span>;</span> <span class="hljs-keyword">virtual</span>;
  <span class="hljs-keyword">public</span>
    <span class="hljs-function"><span class="hljs-keyword">constructor</span> <span class="hljs-title">Create</span><span class="hljs-params">(<span class="hljs-keyword">const</span> AName: <span class="hljs-keyword">string</span>)</span>;</span>
    <span class="hljs-function"><span class="hljs-keyword">destructor</span> <span class="hljs-title">Destroy</span>;</span> <span class="hljs-keyword">override</span>;
    <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">GetArea</span>:</span> Double; <span class="hljs-keyword">virtual</span>; <span class="hljs-keyword">abstract</span>;
    <span class="hljs-keyword">property</span> <span class="hljs-keyword">Name</span>: <span class="hljs-keyword">string</span> <span class="hljs-keyword">read</span> FName <span class="hljs-keyword">write</span> SetName;
  <span class="hljs-keyword">published</span>
    <span class="hljs-keyword">property</span> Area: Double <span class="hljs-keyword">read</span> FArea;
  <span class="hljs-keyword">end</span>;

  <span class="hljs-title">TCircle</span> = <span class="hljs-keyword">class</span>(TShape)
  <span class="hljs-keyword">strict</span> <span class="hljs-keyword">private</span>
    FRadius: Double;
  <span class="hljs-keyword">public</span>
    <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">GetArea</span>:</span> Double; <span class="hljs-keyword">override</span>;
  <span class="hljs-keyword">end</span>;

<span class="hljs-keyword">implementation</span>

<span class="hljs-function"><span class="hljs-keyword">constructor</span> <span class="hljs-title">TShape</span>.<span class="hljs-title">Create</span><span class="hljs-params">(<span class="hljs-keyword">const</span> AName: <span class="hljs-keyword">string</span>)</span>;</span>
<span class="hljs-keyword">begin</span>
  <span class="hljs-keyword">inherited</span> Create;
  FName := AName;
<span class="hljs-keyword">end</span>;

<span class="hljs-function"><span class="hljs-keyword">destructor</span> <span class="hljs-title">TShape</span>.<span class="hljs-title">Destroy</span>;</span>
<span class="hljs-keyword">begin</span>
  <span class="hljs-keyword">inherited</span>;
<span class="hljs-keyword">end</span>;

<span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">TShape</span>.<span class="hljs-title">SetName</span><span class="hljs-params">(<span class="hljs-keyword">const</span> Value: <span class="hljs-keyword">string</span>)</span>;</span>
<span class="hljs-keyword">begin</span>
  <span class="hljs-keyword">if</span> Value &lt;&gt; FName <span class="hljs-keyword">then</span>
    FName := Value;
<span class="hljs-keyword">end</span>;

<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">TCircle</span>.<span class="hljs-title">GetArea</span>:</span> Double;
<span class="hljs-keyword">begin</span>
  Result := Pi * FRadius * FRadius;
<span class="hljs-keyword">end</span>;

<span class="hljs-keyword">initialization</span>
  RegisterClass(TCircle);

<span class="hljs-keyword">finalization</span>
  UnregisterClass(TCircle);

<span class="hljs-keyword">end</span>.
""");
    }

    [Fact]
    public void Keywords_AreCaseInsensitive()
    {
        AssertHighlighter("delphi",
"""
PROGRAM Test;
VAR X: INTEGER;
BEGIN
  X := 10;
  IF X > 5 THEN WriteLn('big') ELSE WriteLn('small');
  Begin End;
END.
""",
"""
<span class="hljs-keyword">PROGRAM</span> Test;
<span class="hljs-keyword">VAR</span> X: INTEGER;
<span class="hljs-keyword">BEGIN</span>
  X := <span class="hljs-number">10</span>;
  <span class="hljs-keyword">IF</span> X &gt; <span class="hljs-number">5</span> <span class="hljs-keyword">THEN</span> WriteLn(<span class="hljs-string">&#x27;big&#x27;</span>) <span class="hljs-keyword">ELSE</span> WriteLn(<span class="hljs-string">&#x27;small&#x27;</span>);
  <span class="hljs-keyword">Begin</span> <span class="hljs-keyword">End</span>;
<span class="hljs-keyword">END</span>.
""");
    }

    [Fact]
    public void FunctionAndClass_AreCaseInsensitive()
    {
        AssertHighlighter("delphi",
"""
Function FOO(x: InTeGeR): STRING;
ProCeDure Bar;
BEGIN end;
TFoo = CLASS(TBar)
""",
"""
<span class="hljs-function"><span class="hljs-keyword">Function</span> <span class="hljs-title">FOO</span><span class="hljs-params">(x: InTeGeR)</span>:</span> <span class="hljs-keyword">STRING</span>;
<span class="hljs-function"><span class="hljs-keyword">ProCeDure</span> <span class="hljs-title">Bar</span>;</span>
<span class="hljs-keyword">BEGIN</span> <span class="hljs-keyword">end</span>;
<span class="hljs-title">TFoo</span> = <span class="hljs-keyword">CLASS</span>(TBar)
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("delphi",
"""
// line comment
{ brace comment
  spanning lines }
(* paren star comment *)
(* multi
   line *)
{ TODO: fix this }
x := 1; // trailing
y := { inline } 2;
z := (* inline *) 3;
{ nested { braces } end }
(* nested (* parens *) end *)
""",
"""
<span class="hljs-comment">// line comment</span>
<span class="hljs-comment">{ brace comment
  spanning lines }</span>
<span class="hljs-comment">(* paren star comment *)</span>
<span class="hljs-comment">(* multi
   line *)</span>
<span class="hljs-comment">{ <span class="hljs-doctag">TODO:</span> fix this }</span>
x := <span class="hljs-number">1</span>; <span class="hljs-comment">// trailing</span>
y := <span class="hljs-comment">{ inline }</span> <span class="hljs-number">2</span>;
z := <span class="hljs-comment">(* inline *)</span> <span class="hljs-number">3</span>;
<span class="hljs-comment">{ nested { braces }</span> <span class="hljs-keyword">end</span> }
<span class="hljs-comment">(* nested (* parens *)</span> <span class="hljs-keyword">end</span> *)
""");
    }

    [Fact]
    public void Comments_DocTags()
    {
        AssertHighlighter("delphi",
"""
// TODO: implement
{ FIXME: broken }
(* NOTE: something *)
""",
"""
<span class="hljs-comment">// <span class="hljs-doctag">TODO:</span> implement</span>
<span class="hljs-comment">{ <span class="hljs-doctag">FIXME:</span> broken }</span>
<span class="hljs-comment">(* <span class="hljs-doctag">NOTE:</span> something *)</span>
""");
    }

    [Fact]
    public void Directives()
    {
        AssertHighlighter("delphi",
"""
{$IFDEF DEBUG}
  WriteLn('debug');
{$ELSE}
  WriteLn('release');
{$ENDIF}
{$R *.res}
(*$R+*)
{$mode objfpc}{$H+}
{$I-}
{$IF Defined(X) and (CompilerVersion >= 20)}
{$DEFINE FOO}
""",
"""
<span class="hljs-meta">{$IFDEF DEBUG}</span>
  WriteLn(<span class="hljs-string">&#x27;debug&#x27;</span>);
<span class="hljs-meta">{$ELSE}</span>
  WriteLn(<span class="hljs-string">&#x27;release&#x27;</span>);
<span class="hljs-meta">{$ENDIF}</span>
<span class="hljs-meta">{$R *.res}</span>
<span class="hljs-meta">(*$R+*)</span>
<span class="hljs-meta">{$mode objfpc}</span><span class="hljs-meta">{$H+}</span>
<span class="hljs-meta">{$I-}</span>
<span class="hljs-meta">{$IF Defined(X) and (CompilerVersion &gt;= 20)}</span>
<span class="hljs-meta">{$DEFINE FOO}</span>
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("delphi",
"""
s := 'Hello';
s := 'It''s';
s := '';
s := '''';
s := 'Line1'#13#10'Line2';
c := #65;
c := #$41;
c := #&101;
c := #%01000001;
c := #1_000;
s := 'multi
line';
s := 'a { b } c // d';
""",
"""
s := <span class="hljs-string">&#x27;Hello&#x27;</span>;
s := <span class="hljs-string">&#x27;It&#x27;&#x27;s&#x27;</span>;
s := <span class="hljs-string">&#x27;&#x27;</span>;
s := <span class="hljs-string">&#x27;&#x27;&#x27;&#x27;</span>;
s := <span class="hljs-string">&#x27;Line1&#x27;</span><span class="hljs-string">#13</span><span class="hljs-string">#10</span><span class="hljs-string">&#x27;Line2&#x27;</span>;
c := <span class="hljs-string">#65</span>;
c := <span class="hljs-string">#$41</span>;
c := <span class="hljs-string">#&amp;101</span>;
c := <span class="hljs-string">#%01000001</span>;
c := <span class="hljs-string">#1_000</span>;
s := <span class="hljs-string">&#x27;multi
line&#x27;</span>;
s := <span class="hljs-string">&#x27;a { b } c // d&#x27;</span>;
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("delphi",
"""
a := 123;
b := 123.456;
c := $7F;
d := $FF_FF;
e := &42;
f := %1010;
g := %1010_1010;
h := 1_000_000;
i := 1.5e10;
j := $;
k := %;
l := 3.;
m := .5;
n := $DEADbeef;
o := a1b2;
p := x_1;
""",
"""
a := <span class="hljs-number">123</span>;
b := <span class="hljs-number">123.456</span>;
c := <span class="hljs-number">$7F</span>;
d := <span class="hljs-number">$FF_FF</span>;
e := <span class="hljs-number">&amp;42</span>;
f := <span class="hljs-number">%1010</span>;
g := <span class="hljs-number">%1010_1010</span>;
h := <span class="hljs-number">1_000_000</span>;
i := <span class="hljs-number">1.5e10</span>;
j := <span class="hljs-number">$</span>;
k := <span class="hljs-number">%</span>;
l := <span class="hljs-number">3</span>.;
m := .<span class="hljs-number">5</span>;
n := <span class="hljs-number">$DEADbeef</span>;
o := a1b2;
p := x_1;
""");
    }

    [Fact]
    public void Numbers_Exponent()
    {
        AssertHighlighter("delphi",
"""
a := 1E10;
b := 1.5e-3;
c := 2.0E+5;
d := 1e;
e := 1else;
f := 1_000e1_0;
g := $1E5;
""",
"""
a := <span class="hljs-number">1E10</span>;
b := <span class="hljs-number">1.5e-3</span>;
c := <span class="hljs-number">2.0E+5</span>;
d := <span class="hljs-number">1</span>e;
e := <span class="hljs-number">1</span><span class="hljs-keyword">else</span>;
f := <span class="hljs-number">1_000e1_0</span>;
g := <span class="hljs-number">$1E5</span>;
""");
    }

    [Fact]
    public void Numbers_EdgeCases()
    {
        AssertHighlighter("delphi",
"""
x := $1A;
y := $G1;
z := $_;
w := &8;
v := &_;
u := %2;
t := #;
r := #$;
q := #$G;
""",
"""
x := <span class="hljs-number">$1A</span>;
y := <span class="hljs-number">$</span>G1;
z := <span class="hljs-number">$_</span>;
w := &amp;<span class="hljs-number">8</span>;
v := &amp;_;
u := <span class="hljs-number">%</span><span class="hljs-number">2</span>;
t := #;
r := #<span class="hljs-number">$</span>;
q := #<span class="hljs-number">$</span>G;
""");
    }

    [Fact]
    public void Operators()
    {
        AssertHighlighter("delphi",
"""
x := a + b - c * d / e;
y := a div b mod c;
z := (a shl 2) shr 1;
b := not (a and b) or c xor d;
if a <> b then;
if (a >= b) and (c <= d) then;
p := @x;
v := p^;
r := a in [1..10];
o := obj as TFoo;
t := obj is TFoo;
""",
"""
x := a + b - c * d / e;
y := a <span class="hljs-keyword">div</span> b <span class="hljs-keyword">mod</span> c;
z := (a <span class="hljs-keyword">shl</span> <span class="hljs-number">2</span>) <span class="hljs-keyword">shr</span> <span class="hljs-number">1</span>;
b := <span class="hljs-keyword">not</span> (a <span class="hljs-keyword">and</span> b) <span class="hljs-keyword">or</span> c xor d;
<span class="hljs-keyword">if</span> a &lt;&gt; b <span class="hljs-keyword">then</span>;
<span class="hljs-keyword">if</span> (a &gt;= b) <span class="hljs-keyword">and</span> (c &lt;= d) <span class="hljs-keyword">then</span>;
p := @x;
v := p^;
r := a <span class="hljs-keyword">in</span> [<span class="hljs-number">1</span>..<span class="hljs-number">10</span>];
o := obj <span class="hljs-keyword">as</span> TFoo;
t := obj <span class="hljs-keyword">is</span> TFoo;
""");
    }

    [Fact]
    public void Loops()
    {
        AssertHighlighter("delphi",
"""
for i := 0 to 10 do
  WriteLn(i);
for i := 10 downto 0 do
  Continue;
while x < 10 do
begin
  Inc(x);
  if x = 5 then Break;
end;
repeat
  Dec(x);
until x = 0;
for s in list do
  Writeln(s);
""",
"""
<span class="hljs-keyword">for</span> i := <span class="hljs-number">0</span> <span class="hljs-keyword">to</span> <span class="hljs-number">10</span> <span class="hljs-keyword">do</span>
  WriteLn(i);
<span class="hljs-keyword">for</span> i := <span class="hljs-number">10</span> <span class="hljs-keyword">downto</span> <span class="hljs-number">0</span> <span class="hljs-keyword">do</span>
  <span class="hljs-keyword">Continue</span>;
<span class="hljs-keyword">while</span> x &lt; <span class="hljs-number">10</span> <span class="hljs-keyword">do</span>
<span class="hljs-keyword">begin</span>
  Inc(x);
  <span class="hljs-keyword">if</span> x = <span class="hljs-number">5</span> <span class="hljs-keyword">then</span> <span class="hljs-keyword">Break</span>;
<span class="hljs-keyword">end</span>;
<span class="hljs-keyword">repeat</span>
  Dec(x);
<span class="hljs-keyword">until</span> x = <span class="hljs-number">0</span>;
<span class="hljs-keyword">for</span> s <span class="hljs-keyword">in</span> list <span class="hljs-keyword">do</span>
  Writeln(s);
""");
    }

    [Fact]
    public void CaseStatement()
    {
        AssertHighlighter("delphi",
"""
case x of
  1: WriteLn('one');
  2, 3: WriteLn('two or three');
  4..10: WriteLn('range');
else
  WriteLn('other');
end;
""",
"""
<span class="hljs-keyword">case</span> x <span class="hljs-keyword">of</span>
  <span class="hljs-number">1</span>: WriteLn(<span class="hljs-string">&#x27;one&#x27;</span>);
  <span class="hljs-number">2</span>, <span class="hljs-number">3</span>: WriteLn(<span class="hljs-string">&#x27;two or three&#x27;</span>);
  <span class="hljs-number">4</span>..<span class="hljs-number">10</span>: WriteLn(<span class="hljs-string">&#x27;range&#x27;</span>);
<span class="hljs-keyword">else</span>
  WriteLn(<span class="hljs-string">&#x27;other&#x27;</span>);
<span class="hljs-keyword">end</span>;
""");
    }

    [Fact]
    public void Exceptions()
    {
        AssertHighlighter("delphi",
"""
try
  DoSomething;
except
  on E: EConvertError do
    ShowMessage(E.Message);
  on E: Exception do
    raise;
end;
try
  x := TList.Create;
finally
  x.Free;
end;
raise Exception.Create('Error');
""",
"""
<span class="hljs-keyword">try</span>
  DoSomething;
<span class="hljs-keyword">except</span>
  <span class="hljs-keyword">on</span> E: EConvertError <span class="hljs-keyword">do</span>
    ShowMessage(E.<span class="hljs-keyword">Message</span>);
  <span class="hljs-keyword">on</span> E: Exception <span class="hljs-keyword">do</span>
    <span class="hljs-keyword">raise</span>;
<span class="hljs-keyword">end</span>;
<span class="hljs-keyword">try</span>
  x := TList.Create;
<span class="hljs-keyword">finally</span>
  x.Free;
<span class="hljs-keyword">end</span>;
<span class="hljs-keyword">raise</span> Exception.Create(<span class="hljs-string">&#x27;Error&#x27;</span>);
""");
    }

    [Fact]
    public void Types()
    {
        AssertHighlighter("delphi",
"""
type
  TPoint = record
    X, Y: Integer;
  end;
  TPacked = packed record
    A: Byte;
    B: Word;
  end;
  TArr = array[0..9] of Integer;
  TDyn = array of string;
  TSet = set of Char;
  TEnum = (eRed, eGreen, eBlue);
  PInt = ^Integer;
  TFile = file of Byte;
""",
"""
<span class="hljs-keyword">type</span>
  TPoint = <span class="hljs-keyword">record</span>
    X, Y: Integer;
  <span class="hljs-keyword">end</span>;
  TPacked = <span class="hljs-keyword">packed</span> <span class="hljs-keyword">record</span>
    A: Byte;
    B: Word;
  <span class="hljs-keyword">end</span>;
  TArr = <span class="hljs-keyword">array</span>[<span class="hljs-number">0</span>..<span class="hljs-number">9</span>] <span class="hljs-keyword">of</span> Integer;
  TDyn = <span class="hljs-keyword">array</span> <span class="hljs-keyword">of</span> <span class="hljs-keyword">string</span>;
  TSet = <span class="hljs-keyword">set</span> <span class="hljs-keyword">of</span> Char;
  TEnum = (eRed, eGreen, eBlue);
  PInt = ^Integer;
  TFile = <span class="hljs-keyword">file</span> <span class="hljs-keyword">of</span> Byte;
""");
    }

    [Fact]
    public void ProceduralTypes()
    {
        AssertHighlighter("delphi",
"""
type
  TNotifyEvent = procedure(Sender: TObject) of object;
  TFunc = function(A, B: Integer): Integer;
  TProcRef = reference to procedure;
""",
"""
<span class="hljs-keyword">type</span>
  TNotifyEvent = <span class="hljs-function"><span class="hljs-keyword">procedure</span><span class="hljs-params">(Sender: TObject)</span> </span><span class="hljs-keyword">of</span> <span class="hljs-keyword">object</span>;
  TFunc = <span class="hljs-function"><span class="hljs-keyword">function</span><span class="hljs-params">(A, B: Integer)</span>:</span> Integer;
  TProcRef = reference <span class="hljs-keyword">to</span> <span class="hljs-function"><span class="hljs-keyword">procedure</span>;</span>
""");
    }

    [Fact]
    public void MethodImplementation()
    {
        AssertHighlighter("delphi",
"""
procedure TForm1.Button1Click(Sender: TObject);
var
  I: Integer;
  S: string;
begin
  S := Edit1.Text;
  for I := 1 to Length(S) do
    Memo1.Lines.Add(S[I]);
  Label1.Caption := IntToStr(I);
end;
""",
"""
<span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">TForm1</span>.<span class="hljs-title">Button1Click</span><span class="hljs-params">(Sender: TObject)</span>;</span>
<span class="hljs-keyword">var</span>
  I: Integer;
  S: <span class="hljs-keyword">string</span>;
<span class="hljs-keyword">begin</span>
  S := Edit1.Text;
  <span class="hljs-keyword">for</span> I := <span class="hljs-number">1</span> <span class="hljs-keyword">to</span> Length(S) <span class="hljs-keyword">do</span>
    Memo1.Lines.Add(S[I]);
  Label1.Caption := IntToStr(I);
<span class="hljs-keyword">end</span>;
""");
    }

    [Fact]
    public void Parameters()
    {
        AssertHighlighter("delphi",
"""
function Sum(const A: array of Integer; var Total: Integer; out Count: Integer; B: Integer = 0): Integer; overload; inline;
procedure Log(const Msg: string = 'default'; Level: Integer = 1);
function Foo(S: string {comment}; (* c2 *) C: Char = #0): Boolean; stdcall; external 'lib.dll' name 'Foo';
procedure NoParams;
function NoParams2: Integer;
procedure P({$IFDEF X}A: Integer{$ENDIF});
class function TFoo.Create2: TFoo; static;
class procedure TFoo.Init;
procedure Q(A: Integer = $FF; B: Integer = 10);
procedure R // comment
  (A: Integer);
procedure S { comment } (A: Integer);
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Sum</span><span class="hljs-params">(<span class="hljs-keyword">const</span> A: <span class="hljs-keyword">array</span> <span class="hljs-keyword">of</span> Integer; <span class="hljs-keyword">var</span> Total: Integer; <span class="hljs-keyword">out</span> Count: Integer; B: Integer = 0)</span>:</span> Integer; <span class="hljs-keyword">overload</span>; <span class="hljs-keyword">inline</span>;
<span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">Log</span><span class="hljs-params">(<span class="hljs-keyword">const</span> Msg: <span class="hljs-keyword">string</span> = <span class="hljs-string">&#x27;default&#x27;</span>; Level: Integer = 1)</span>;</span>
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Foo</span><span class="hljs-params">(S: <span class="hljs-keyword">string</span> <span class="hljs-comment">{comment}</span>; <span class="hljs-comment">(* c2 *)</span> C: Char = <span class="hljs-string">#0</span>)</span>:</span> Boolean; <span class="hljs-keyword">stdcall</span>; <span class="hljs-keyword">external</span> <span class="hljs-string">&#x27;lib.dll&#x27;</span> <span class="hljs-keyword">name</span> <span class="hljs-string">&#x27;Foo&#x27;</span>;
<span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">NoParams</span>;</span>
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">NoParams2</span>:</span> Integer;
<span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">P</span><span class="hljs-params">(<span class="hljs-meta">{$IFDEF X}</span>A: Integer<span class="hljs-meta">{$ENDIF}</span>)</span>;</span>
<span class="hljs-keyword">class</span> <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">TFoo</span>.<span class="hljs-title">Create2</span>:</span> TFoo; <span class="hljs-keyword">static</span>;
<span class="hljs-keyword">class</span> <span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">TFoo</span>.<span class="hljs-title">Init</span>;</span>
<span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">Q</span><span class="hljs-params">(A: Integer = $FF; B: Integer = 10)</span>;</span>
<span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">R</span> <span class="hljs-comment">// comment</span>
  <span class="hljs-params">(A: Integer)</span>;</span>
<span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">S</span> <span class="hljs-comment">{ comment }</span> <span class="hljs-params">(A: Integer)</span>;</span>
""");
    }

    [Fact]
    public void Generics()
    {
        AssertHighlighter("delphi",
"""
type
  TList<T> = class(TObject)
  public
    procedure Add(const Item: T);
  end;
  TDict<TKey, TValue> = class(TEnumerable<TPair<TKey, TValue>>)
  end;
var
  L: TList<Integer>;
begin
  L := TList<Integer>.Create;
end;
""",
"""
<span class="hljs-keyword">type</span>
  TList&lt;T&gt; = <span class="hljs-keyword">class</span>(TObject)
  <span class="hljs-keyword">public</span>
    <span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">Add</span><span class="hljs-params">(<span class="hljs-keyword">const</span> Item: T)</span>;</span>
  <span class="hljs-keyword">end</span>;
  TDict&lt;TKey, TValue&gt; = <span class="hljs-keyword">class</span>(TEnumerable&lt;TPair&lt;TKey, TValue&gt;&gt;)
  <span class="hljs-keyword">end</span>;
<span class="hljs-keyword">var</span>
  L: TList&lt;Integer&gt;;
<span class="hljs-keyword">begin</span>
  L := TList&lt;Integer&gt;.Create;
<span class="hljs-keyword">end</span>;
""");
    }

    [Fact]
    public void Interfaces()
    {
        AssertHighlighter("delphi",
"""
type
  IFoo = interface(IInterface)
    ['{12345678-1234-1234-1234-123456789012}']
    function GetValue: Integer;
    property Value: Integer read GetValue;
  end;
  TFoo = class(TInterfacedObject, IFoo)
    function GetValue: Integer;
  end;
  TBar = class
  end;
  TBaz = class sealed(TFoo)
  end;
  TQux=class(TFoo);
""",
"""
<span class="hljs-keyword">type</span>
  IFoo = <span class="hljs-keyword">interface</span>(IInterface)
    [<span class="hljs-string">&#x27;{12345678-1234-1234-1234-123456789012}&#x27;</span>]
    <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">GetValue</span>:</span> Integer;
    <span class="hljs-keyword">property</span> Value: Integer <span class="hljs-keyword">read</span> GetValue;
  <span class="hljs-keyword">end</span>;
  <span class="hljs-title">TFoo</span> = <span class="hljs-keyword">class</span>(TInterfacedObject, IFoo)
    <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">GetValue</span>:</span> Integer;
  <span class="hljs-keyword">end</span>;
  TBar = <span class="hljs-keyword">class</span>
  <span class="hljs-keyword">end</span>;
  TBaz = <span class="hljs-keyword">class</span> sealed(TFoo)
  <span class="hljs-keyword">end</span>;
  <span class="hljs-title">TQux</span>=<span class="hljs-keyword">class</span>(TFoo);
""");
    }

    [Fact]
    public void ClassTitles()
    {
        AssertHighlighter("delphi",
"""
TFoo = class(TBar)
  type
    TInner = class(TObject)
    end;
end;
X = class ( TBase )
Foo=Class(TBar)
_Foo = class(TBar)
Foo_1 = class(TBar)
T1 = class
(TBar)
""",
"""
<span class="hljs-title">TFoo</span> = <span class="hljs-keyword">class</span>(TBar)
  <span class="hljs-keyword">type</span>
    <span class="hljs-title">TInner</span> = <span class="hljs-keyword">class</span>(TObject)
    <span class="hljs-keyword">end</span>;
<span class="hljs-keyword">end</span>;
<span class="hljs-title">X</span> = <span class="hljs-keyword">class</span> ( TBase )
<span class="hljs-title">Foo</span>=<span class="hljs-keyword">Class</span>(TBar)
<span class="hljs-title">_Foo</span> = <span class="hljs-keyword">class</span>(TBar)
<span class="hljs-title">Foo_1</span> = <span class="hljs-keyword">class</span>(TBar)
<span class="hljs-title">T1</span> = <span class="hljs-keyword">class</span>
(TBar)
""");
    }

    [Fact]
    public void Identifiers_StartingWithUnderscore()
    {
        AssertHighlighter("delphi",
"""
procedure _Internal;
function __Foo(_A: Integer): Integer;
_TFoo = class(TObject)
T_Foo = class(TObject)
""",
"""
<span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">_Internal</span>;</span>
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">__Foo</span><span class="hljs-params">(_A: Integer)</span>:</span> Integer;
<span class="hljs-title">_TFoo</span> = <span class="hljs-keyword">class</span>(TObject)
<span class="hljs-title">T_Foo</span> = <span class="hljs-keyword">class</span>(TObject)
""");
    }

    [Fact]
    public void AnonymousMethods()
    {
        AssertHighlighter("delphi",
"""
TThread.Synchronize(nil,
  procedure
  begin
    Label1.Caption := 'Done';
  end);
F := function(X: Integer): Integer
  begin
    Result := X * 2;
  end;
""",
"""
TThread.Synchronize(<span class="hljs-keyword">nil</span>,
  <span class="hljs-function"><span class="hljs-keyword">procedure</span>
  </span><span class="hljs-keyword">begin</span>
    Label1.Caption := <span class="hljs-string">&#x27;Done&#x27;</span>;
  <span class="hljs-keyword">end</span>);
F := <span class="hljs-function"><span class="hljs-keyword">function</span><span class="hljs-params">(X: Integer)</span>:</span> Integer
  <span class="hljs-keyword">begin</span>
    Result := X * <span class="hljs-number">2</span>;
  <span class="hljs-keyword">end</span>;
""");
    }

    [Fact]
    public void AnonymousMethods_WithoutParameters()
    {
        AssertHighlighter("delphi",
"""
TTask.Run(procedure
  var
    I: Integer;
  begin
    I := 0;
  end);
TTask.Run(PROCEDURE BEGIN X := 1; END);
P := procedure const C = 1; begin end;
TProc = procedure of object;
TFn = function: Integer of object;
procedure Begins;
procedure Offset;
""",
"""
TTask.Run(<span class="hljs-function"><span class="hljs-keyword">procedure</span>
  </span><span class="hljs-keyword">var</span>
    I: Integer;
  <span class="hljs-keyword">begin</span>
    I := <span class="hljs-number">0</span>;
  <span class="hljs-keyword">end</span>);
TTask.Run(<span class="hljs-function"><span class="hljs-keyword">PROCEDURE</span> </span><span class="hljs-keyword">BEGIN</span> X := <span class="hljs-number">1</span>; <span class="hljs-keyword">END</span>);
P := <span class="hljs-function"><span class="hljs-keyword">procedure</span> </span><span class="hljs-keyword">const</span> C = <span class="hljs-number">1</span>; <span class="hljs-keyword">begin</span> <span class="hljs-keyword">end</span>;
TProc = <span class="hljs-function"><span class="hljs-keyword">procedure</span> </span><span class="hljs-keyword">of</span> <span class="hljs-keyword">object</span>;
TFn = <span class="hljs-function"><span class="hljs-keyword">function</span>:</span> Integer <span class="hljs-keyword">of</span> <span class="hljs-keyword">object</span>;
<span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">Begins</span>;</span>
<span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">Offset</span>;</span>
""");
    }

    [Fact]
    public void Asm()
    {
        AssertHighlighter("delphi",
"""
function Add(A, B: Integer): Integer; assembler;
asm
  MOV EAX, A
  ADD EAX, B
end;
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">Add</span><span class="hljs-params">(A, B: Integer)</span>:</span> Integer; <span class="hljs-keyword">assembler</span>;
<span class="hljs-keyword">asm</span>
  MOV EAX, A
  ADD EAX, B
<span class="hljs-keyword">end</span>;
""");
    }

    [Fact]
    public void WithAndGoto()
    {
        AssertHighlighter("delphi",
"""
with Form1 do
begin
  Caption := 'Title';
  Width := 300;
end;
goto label1;
label1:
Exit;
""",
"""
<span class="hljs-keyword">with</span> Form1 <span class="hljs-keyword">do</span>
<span class="hljs-keyword">begin</span>
  Caption := <span class="hljs-string">&#x27;Title&#x27;</span>;
  Width := <span class="hljs-number">300</span>;
<span class="hljs-keyword">end</span>;
<span class="hljs-keyword">goto</span> label1;
label1:
<span class="hljs-keyword">Exit</span>;
""");
    }

    [Fact]
    public void Sections()
    {
        AssertHighlighter("delphi",
"""
const
  MaxSize = 100;
  Pi2: Double = 6.28;
  Names: array[0..1] of string = ('a', 'b');
resourcestring
  SError = 'An error occurred';
threadvar
  Counter: Integer;
""",
"""
<span class="hljs-keyword">const</span>
  MaxSize = <span class="hljs-number">100</span>;
  Pi2: Double = <span class="hljs-number">6.28</span>;
  Names: <span class="hljs-keyword">array</span>[<span class="hljs-number">0</span>..<span class="hljs-number">1</span>] <span class="hljs-keyword">of</span> <span class="hljs-keyword">string</span> = (<span class="hljs-string">&#x27;a&#x27;</span>, <span class="hljs-string">&#x27;b&#x27;</span>);
<span class="hljs-keyword">resourcestring</span>
  SError = <span class="hljs-string">&#x27;An error occurred&#x27;</span>;
<span class="hljs-keyword">threadvar</span>
  Counter: Integer;
""");
    }

    [Fact]
    public void IllegalLexemes()
    {
        AssertHighlighter("delphi",
"""
s := "not a string";
x := a | b;
y := $GHI;
/* c comment */
</tag>
z := 1;
""",
"""
s := &quot;<span class="hljs-keyword">not</span> a <span class="hljs-keyword">string</span>&quot;;
x := a | b;
y := <span class="hljs-number">$</span>GHI;
/* c comment */
&lt;/tag&gt;
z := <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void KeywordsAfterDot()
    {
        AssertHighlighter("delphi",
"""
x := obj.begin;
y := obj.procedure;
z := obj.function(1);
Self.Free;
a := procedure.x;
""",
"""
x := obj.<span class="hljs-keyword">begin</span>;
y := obj.<span class="hljs-keyword">procedure</span>;
z := obj.<span class="hljs-keyword">function</span>(<span class="hljs-number">1</span>);
Self.Free;
a := <span class="hljs-keyword">procedure</span>.x;
""");
    }

    [Fact]
    public void FreePascal()
    {
        AssertHighlighter("delphi",
"""
{$mode objfpc}
program FPC;
generic TList<T> = class
end;
type TIntList = specialize TList<Integer>;
operator + (a, b: TVec): TVec;
begin
end;
begin
end.
""",
"""
<span class="hljs-meta">{$mode objfpc}</span>
<span class="hljs-keyword">program</span> FPC;
<span class="hljs-keyword">generic</span> TList&lt;T&gt; = <span class="hljs-keyword">class</span>
<span class="hljs-keyword">end</span>;
<span class="hljs-keyword">type</span> TIntList = <span class="hljs-keyword">specialize</span> TList&lt;Integer&gt;;
<span class="hljs-keyword">operator</span> + (a, b: TVec): TVec;
<span class="hljs-keyword">begin</span>
<span class="hljs-keyword">end</span>;
<span class="hljs-keyword">begin</span>
<span class="hljs-keyword">end</span>.
""");
    }

    [Fact]
    public void PropertyAndMethodDirectives()
    {
        AssertHighlighter("delphi",
"""
property Items[Index: Integer]: string read GetItem write SetItem; default;
property Count: Integer read FCount stored False nodefault;
property Tag: Integer index 1 read GetTag;
procedure WMPaint(var Msg: TWMPaint); message WM_PAINT;
procedure Old; deprecated 'Use New';
procedure Plat; platform;
""",
"""
<span class="hljs-keyword">property</span> Items[<span class="hljs-keyword">Index</span>: Integer]: <span class="hljs-keyword">string</span> <span class="hljs-keyword">read</span> GetItem <span class="hljs-keyword">write</span> SetItem; <span class="hljs-keyword">default</span>;
<span class="hljs-keyword">property</span> Count: Integer <span class="hljs-keyword">read</span> FCount <span class="hljs-keyword">stored</span> False <span class="hljs-keyword">nodefault</span>;
<span class="hljs-keyword">property</span> Tag: Integer <span class="hljs-keyword">index</span> <span class="hljs-number">1</span> <span class="hljs-keyword">read</span> GetTag;
<span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">WMPaint</span><span class="hljs-params">(<span class="hljs-keyword">var</span> Msg: TWMPaint)</span>;</span> <span class="hljs-keyword">message</span> WM_PAINT;
<span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">Old</span>;</span> <span class="hljs-keyword">deprecated</span> <span class="hljs-string">&#x27;Use New&#x27;</span>;
<span class="hljs-function"><span class="hljs-keyword">procedure</span> <span class="hljs-title">Plat</span>;</span> <span class="hljs-keyword">platform</span>;
""");
    }

    [Fact]
    public void Pointers()
    {
        AssertHighlighter("delphi",
"""
P := @Proc;
P^.Next := nil;
Move(Src^, Dest^, SizeOf(TRec));
FillChar(Buf, SizeOf(Buf), #0);
""",
"""
P := @Proc;
P^.Next := <span class="hljs-keyword">nil</span>;
Move(Src^, Dest^, SizeOf(TRec));
FillChar(Buf, SizeOf(Buf), <span class="hljs-string">#0</span>);
""");
    }

    [Fact]
    public void FormFile()
    {
        AssertHighlighter("delphi",
"""
object Form1: TForm1
  Left = 0
  Top = 0
  Caption = 'Form1'
  ClientHeight = 300
  Color = clBtnFace
  Font.Name = 'Tahoma'
  OldCreateOrder = False
  PixelsPerInch = 96
  object Button1: TButton
    Left = 8
    Caption = 'Click'
    OnClick = Button1Click
  end
end
""",
"""
<span class="hljs-keyword">object</span> Form1: TForm1
  Left = <span class="hljs-number">0</span>
  Top = <span class="hljs-number">0</span>
  Caption = <span class="hljs-string">&#x27;Form1&#x27;</span>
  ClientHeight = <span class="hljs-number">300</span>
  Color = clBtnFace
  Font.<span class="hljs-keyword">Name</span> = <span class="hljs-string">&#x27;Tahoma&#x27;</span>
  OldCreateOrder = False
  PixelsPerInch = <span class="hljs-number">96</span>
  <span class="hljs-keyword">object</span> Button1: TButton
    Left = <span class="hljs-number">8</span>
    Caption = <span class="hljs-string">&#x27;Click&#x27;</span>
    OnClick = Button1Click
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Unterminated_String()
    {
        AssertHighlighter("delphi",
"""
s := 'unterminated
x := 1;
y := 2;
""",
"""
s := <span class="hljs-string">&#x27;unterminated
x := 1;
y := 2;</span>
""");
    }

    [Fact]
    public void Unterminated_Comment()
    {
        AssertHighlighter("delphi",
"""
{ unterminated
x := 1;
(* also
y := 2;
""",
"""
<span class="hljs-comment">{ unterminated
x := 1;
(* also
y := 2;</span>
""");
    }

    [Fact]
    public void Unterminated_Directive()
    {
        AssertHighlighter("delphi",
"""
{$IFDEF X
x := 1;
""",
"""
<span class="hljs-meta">{$IFDEF X
x := 1;</span>
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("delphi",
"""
s := 'héllo';
var Größe: Integer;
Ωmega := 1;
""",
"""
s := <span class="hljs-string">&#x27;héllo&#x27;</span>;
<span class="hljs-keyword">var</span> Größe: Integer;
Ωmega := <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void CrLf()
    {
        AssertHighlighter("delphi", "begin\r\n  x := 1; // c\r\n  { b }\r\nend.", "<span class=\"hljs-keyword\">begin</span>\r\n  x := <span class=\"hljs-number\">1</span>; <span class=\"hljs-comment\">// c</span>\r\n  <span class=\"hljs-comment\">{ b }</span>\r\n<span class=\"hljs-keyword\">end</span>.");
    }

    [Theory]
    [InlineData("delphi")]
    [InlineData("dpr")]
    [InlineData("dfm")]
    [InlineData("pas")]
    [InlineData("pascal")]
    [InlineData("PASCAL")]
    public void Aliases(string language)
    {
        AssertHighlighter(language,
"""
begin x := 'a'; end.
""",
"""
<span class="hljs-keyword">begin</span> x := <span class="hljs-string">&#x27;a&#x27;</span>; <span class="hljs-keyword">end</span>.
""");
    }

    [Fact]
    public async Task LongIdentifier_CompletesInReasonableTime()
    {
        // Without RunStart, the class title pattern (`TFoo = class(`) is tried from every position of a long identifier.
        var code = new string('a', 300_000) + " = clas";

        var budget = TimeSpan.FromSeconds(10);
        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "delphi", out _));
        var finished = await Task.WhenAny(highlight, Task.Delay(budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'delphi' did not finish within {budget.TotalSeconds:F0}s.");
        Assert.Equal(code, await highlight);
    }
}
