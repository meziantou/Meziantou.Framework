namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class MatlabHighlighterTests
{
    [Fact]
    public void Hello()
    {
        AssertHighlighter("matlab",
"""
disp('Hello, world!')
fprintf("Hello, %s\n", name);
""",
"""
<span class="hljs-built_in">disp</span>(<span class="hljs-string">&#x27;Hello, world!&#x27;</span>)
fprintf(<span class="hljs-string">&quot;Hello, %s\n&quot;</span>, name);
""");
    }

    [Fact]
    public void Script()
    {
        AssertHighlighter("matlab",
"""
clear; clc;
% Parameters
N = 100;
t = linspace(0, 2*pi, N);
y = sin(t) + 0.1*randn(size(t));

% Fit
p = polyfit(t, y, 3);
yfit = polyval(p, t);

figure;
plot(t, y, 'o', t, yfit, '-');
legend('data', 'fit');
title(sprintf('Fit with N = %d', N));
xlabel('t'); ylabel('y');
""",
"""
clear; clc;
<span class="hljs-comment">% Parameters</span>
N = <span class="hljs-number">100</span>;
t = <span class="hljs-built_in">linspace</span>(<span class="hljs-number">0</span>, <span class="hljs-number">2</span>*<span class="hljs-built_in">pi</span>, N);
y = <span class="hljs-built_in">sin</span>(t) + <span class="hljs-number">0.1</span>*<span class="hljs-built_in">randn</span>(<span class="hljs-built_in">size</span>(t));

<span class="hljs-comment">% Fit</span>
p = polyfit(t, y, <span class="hljs-number">3</span>);
yfit = polyval(p, t);

<span class="hljs-built_in">figure</span>;
<span class="hljs-built_in">plot</span>(t, y, <span class="hljs-string">&#x27;o&#x27;</span>, t, yfit, <span class="hljs-string">&#x27;-&#x27;</span>);
<span class="hljs-built_in">legend</span>(<span class="hljs-string">&#x27;data&#x27;</span>, <span class="hljs-string">&#x27;fit&#x27;</span>);
title(sprintf(<span class="hljs-string">&#x27;Fit with N = %d&#x27;</span>, N));
xlabel(<span class="hljs-string">&#x27;t&#x27;</span>); ylabel(<span class="hljs-string">&#x27;y&#x27;</span>);
""");
    }

    [Fact]
    public void Function()
    {
        AssertHighlighter("matlab",
"""
function y = square(x)
% SQUARE Compute the square of x.
    y = x.^2;
end
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">y</span> = <span class="hljs-title">square</span><span class="hljs-params">(x)</span></span>
<span class="hljs-comment">% SQUARE Compute the square of x.</span>
    y = x.^<span class="hljs-number">2</span>;
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Function_Outputs()
    {
        AssertHighlighter("matlab",
"""
function [s, p] = sumprod(a, b)
    s = a + b;
    p = a * b;
end

function noargs
    disp('none')
end

function [] = noout(x)
end

function varargout = f(varargin)
end
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-params">[s, p]</span> = <span class="hljs-title">sumprod</span><span class="hljs-params">(a, b)</span></span>
    s = a + b;
    p = a * b;
<span class="hljs-keyword">end</span>

<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">noargs</span></span>
    <span class="hljs-built_in">disp</span>(<span class="hljs-string">&#x27;none&#x27;</span>)
<span class="hljs-keyword">end</span>

<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-params">[]</span> = <span class="hljs-title">noout</span><span class="hljs-params">(x)</span></span>
<span class="hljs-keyword">end</span>

<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">varargout</span> = <span class="hljs-title">f</span><span class="hljs-params">(varargin)</span></span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Function_TrailingComment()
    {
        AssertHighlighter("matlab",
"""
function y = f(x) % inline comment
    y = x;
end
function g() % another
end
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">y</span> = <span class="hljs-title">f</span><span class="hljs-params">(x)</span> <span class="hljs-comment">% inline comment</span></span>
    y = x;
<span class="hljs-keyword">end</span>
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">g</span><span class="hljs-params">()</span> <span class="hljs-comment">% another</span></span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Function_Nested()
    {
        AssertHighlighter("matlab",
"""
function outer()
    x = 1;
    function inner()
        disp(x);
    end
    inner();
end
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">outer</span><span class="hljs-params">()</span></span>
    x = <span class="hljs-number">1</span>;
    <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">inner</span><span class="hljs-params">()</span></span>
        <span class="hljs-built_in">disp</span>(x);
    <span class="hljs-keyword">end</span>
    inner();
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Function_Titles()
    {
        AssertHighlighter("matlab",
"""
function result = compute(x)
function [a,b]=swap(b,a)
function _bad()
function y=f(x),y=x;end
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">result</span> = <span class="hljs-title">compute</span><span class="hljs-params">(x)</span></span>
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-params">[a,b]</span>=<span class="hljs-title">swap</span><span class="hljs-params">(b,a)</span></span>
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">_bad</span><span class="hljs-params">()</span></span>
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">y</span>=<span class="hljs-title">f</span><span class="hljs-params">(x)</span>,<span class="hljs-title">y</span>=<span class="hljs-title">x</span>;<span class="hljs-title">end</span></span>
""");
    }

    [Fact]
    public void ArgumentsBlock()
    {
        AssertHighlighter("matlab",
"""
function out = f(x, opts)
    arguments
        x (1,:) double
        opts.Name string = "default"
    end
    out = x;
end
""",
"""
<span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">out</span> = <span class="hljs-title">f</span><span class="hljs-params">(x, opts)</span></span>
    <span class="hljs-keyword">arguments</span>
        x (<span class="hljs-number">1</span>,:) double
        opts.Name string = <span class="hljs-string">&quot;default&quot;</span>
    <span class="hljs-keyword">end</span>
    out = x;
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Classdef()
    {
        AssertHighlighter("matlab",
"""
classdef BankAccount < handle
    properties
        Balance = 0;
    end
    properties (Access = private)
        Owner
    end
    methods
        function obj = BankAccount(initial)
            obj.Balance = initial;
        end
        function deposit(obj, amount)
            obj.Balance = obj.Balance + amount;
        end
    end
    events
        InsufficientFunds
    end
    enumeration
        Red, Green
    end
end
""",
"""
<span class="hljs-keyword">classdef</span> BankAccount &lt; handle
    <span class="hljs-keyword">properties</span>
        Balance = <span class="hljs-number">0</span>;
    <span class="hljs-keyword">end</span>
    <span class="hljs-keyword">properties</span> (Access = private)
        Owner
    <span class="hljs-keyword">end</span>
    <span class="hljs-keyword">methods</span>
        <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">obj</span> = <span class="hljs-title">BankAccount</span><span class="hljs-params">(initial)</span></span>
            obj.Balance = initial;
        <span class="hljs-keyword">end</span>
        <span class="hljs-function"><span class="hljs-keyword">function</span> <span class="hljs-title">deposit</span><span class="hljs-params">(obj, amount)</span></span>
            obj.Balance = obj.Balance + amount;
        <span class="hljs-keyword">end</span>
    <span class="hljs-keyword">end</span>
    <span class="hljs-keyword">events</span>
        InsufficientFunds
    <span class="hljs-keyword">end</span>
    <span class="hljs-keyword">enumeration</span>
        Red, Green
    <span class="hljs-keyword">end</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void ControlFlow()
    {
        AssertHighlighter("matlab",
"""
if x > 0
    y = 1;
elseif x < 0
    y = -1;
else
    y = 0;
end
for i = 1:10
    continue;
end
while true
    break;
end
switch mode
    case 'a'
        disp(1);
    case {'b', 'c'}
        disp(2);
    otherwise
        disp(3);
end
try
    error('x');
catch err
    disp(err.message);
end
parfor k = 1:n
end
spmd
end
global G
persistent P
return
""",
"""
<span class="hljs-keyword">if</span> x &gt; <span class="hljs-number">0</span>
    y = <span class="hljs-number">1</span>;
<span class="hljs-keyword">elseif</span> x &lt; <span class="hljs-number">0</span>
    y = <span class="hljs-number">-1</span>;
<span class="hljs-keyword">else</span>
    y = <span class="hljs-number">0</span>;
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">for</span> <span class="hljs-built_in">i</span> = <span class="hljs-number">1</span>:<span class="hljs-number">10</span>
    <span class="hljs-keyword">continue</span>;
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">while</span> <span class="hljs-built_in">true</span>
    <span class="hljs-keyword">break</span>;
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">switch</span> mode
    <span class="hljs-keyword">case</span> <span class="hljs-string">&#x27;a&#x27;</span>
        <span class="hljs-built_in">disp</span>(<span class="hljs-number">1</span>);
    <span class="hljs-keyword">case</span> {<span class="hljs-string">&#x27;b&#x27;</span>, <span class="hljs-string">&#x27;c&#x27;</span>}
        <span class="hljs-built_in">disp</span>(<span class="hljs-number">2</span>);
    <span class="hljs-keyword">otherwise</span>
        <span class="hljs-built_in">disp</span>(<span class="hljs-number">3</span>);
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">try</span>
    error(<span class="hljs-string">&#x27;x&#x27;</span>);
<span class="hljs-keyword">catch</span> err
    <span class="hljs-built_in">disp</span>(err.message);
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">parfor</span> k = <span class="hljs-number">1</span>:n
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">spmd</span>
<span class="hljs-keyword">end</span>
<span class="hljs-keyword">global</span> G
<span class="hljs-keyword">persistent</span> P
<span class="hljs-keyword">return</span>
""");
    }

    [Fact]
    public void BuiltIns()
    {
        AssertHighlighter("matlab",
"""
x = sin(pi / 4) + cos(0);
y = zeros(3, 3) + ones(3) + eye(3);
z = sqrt(abs(-4));
n = numel(A); m = size(A, 1);
plot(x, y); hold on; figure;
r = rand(3); c = inf; d = nan; e = eps;
k = i + j;
t = table(1, 2);
q = max(v); w = min(v);
""",
"""
x = <span class="hljs-built_in">sin</span>(<span class="hljs-built_in">pi</span> / <span class="hljs-number">4</span>) + <span class="hljs-built_in">cos</span>(<span class="hljs-number">0</span>);
y = <span class="hljs-built_in">zeros</span>(<span class="hljs-number">3</span>, <span class="hljs-number">3</span>) + <span class="hljs-built_in">ones</span>(<span class="hljs-number">3</span>) + <span class="hljs-built_in">eye</span>(<span class="hljs-number">3</span>);
z = <span class="hljs-built_in">sqrt</span>(<span class="hljs-built_in">abs</span>(<span class="hljs-number">-4</span>));
n = <span class="hljs-built_in">numel</span>(A); m = <span class="hljs-built_in">size</span>(A, <span class="hljs-number">1</span>);
<span class="hljs-built_in">plot</span>(x, y); <span class="hljs-built_in">hold</span> on; <span class="hljs-built_in">figure</span>;
r = <span class="hljs-built_in">rand</span>(<span class="hljs-number">3</span>); c = <span class="hljs-built_in">inf</span>; d = <span class="hljs-built_in">nan</span>; e = <span class="hljs-built_in">eps</span>;
k = <span class="hljs-built_in">i</span> + <span class="hljs-built_in">j</span>;
t = <span class="hljs-built_in">table</span>(<span class="hljs-number">1</span>, <span class="hljs-number">2</span>);
q = <span class="hljs-built_in">max</span>(v); w = <span class="hljs-built_in">min</span>(v);
""");
    }

    [Fact]
    public void Booleans()
    {
        AssertHighlighter("matlab",
"""
a = true;
b = false;
c = trueCount;
d = isfalse;
e = falsehood;
f = ~true;
g = TRUE;
""",
"""
a = <span class="hljs-built_in">true</span>;
b = <span class="hljs-built_in">false</span>;
c = trueCount;
d = isfalse;
e = falsehood;
f = ~<span class="hljs-built_in">true</span>;
g = TRUE;
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("matlab",
"""
a = 1;
b = 1.5;
c = .5;
d = 1e10;
e = 1.5e-3;
f = 0x1F;
g = 3i;
h = 2j;
k = -1;
m = 1E+5;
""",
"""
a = <span class="hljs-number">1</span>;
b = <span class="hljs-number">1.5</span>;
c = <span class="hljs-number">.5</span>;
d = <span class="hljs-number">1e10</span>;
e = <span class="hljs-number">1.5e-3</span>;
f = <span class="hljs-number">0x1F</span>;
g = <span class="hljs-number">3</span><span class="hljs-built_in">i</span>;
h = <span class="hljs-number">2</span><span class="hljs-built_in">j</span>;
k = <span class="hljs-number">-1</span>;
m = <span class="hljs-number">1E+5</span>;
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("matlab", "s = 'single';\ns = 'it''s';\ns = '';\nt = \"double\";\nt = \"say \"\"hi\"\"\";\nu = ['abc', 'def'];\nv = {'a', \"b\"};\nw = strcat('a', 'b');\nx = 'multi\nline';", "s = <span class=\"hljs-string\">&#x27;single&#x27;</span>;\ns = <span class=\"hljs-string\">&#x27;it&#x27;&#x27;s&#x27;</span>;\ns = <span class=\"hljs-string\">&#x27;&#x27;</span>;\nt = <span class=\"hljs-string\">&quot;double&quot;</span>;\nt = <span class=\"hljs-string\">&quot;say &quot;&quot;hi&quot;&quot;&quot;</span>;\nu = [<span class=\"hljs-string\">&#x27;abc&#x27;</span>, <span class=\"hljs-string\">&#x27;def&#x27;</span>];\nv = {<span class=\"hljs-string\">&#x27;a&#x27;</span>, <span class=\"hljs-string\">&quot;b&quot;</span>};\nw = strcat(<span class=\"hljs-string\">&#x27;a&#x27;</span>, <span class=\"hljs-string\">&#x27;b&#x27;</span>);\nx = <span class=\"hljs-string\">&#x27;multi\nline&#x27;</span>;");
    }

    [Fact]
    public void Strings_DoubleQuoted_Transpose()
    {
        AssertHighlighter("matlab",
"""
x = "a"';
y = "a" + "b";
""",
"""
x = <span class="hljs-string">&quot;a&quot;</span>&#x27;;
y = <span class="hljs-string">&quot;a&quot;</span> + <span class="hljs-string">&quot;b&quot;</span>;
""");
    }

    [Fact]
    public void Transpose()
    {
        AssertHighlighter("matlab",
"""
a = b';
c = [1 2 3]';
d = x.';
e = x'';
f = (a + b)';
g = {1, 2}';
h = 5';
k = a' * b';
m = A(1:end)';
n = true';
o = "str"';
p = x_1';
q = end';
r = a'.*b';
""",
"""
a = b&#x27;;
c = [<span class="hljs-number">1</span> <span class="hljs-number">2</span> <span class="hljs-number">3</span>]&#x27;;
d = x.&#x27;;
e = x&#x27;&#x27;;
f = (a + b)&#x27;;
g = {<span class="hljs-number">1</span>, <span class="hljs-number">2</span>}&#x27;;
h = <span class="hljs-number">5</span>&#x27;;
k = a&#x27; * b&#x27;;
m = A(<span class="hljs-number">1</span>:<span class="hljs-keyword">end</span>)&#x27;;
n = <span class="hljs-built_in">true</span>&#x27;;
o = <span class="hljs-string">&quot;str&quot;</span>&#x27;;
p = x_1&#x27;;
q = end&#x27;;
r = a&#x27;.*b&#x27;;
""");
    }

    [Fact]
    public void Transpose_AfterIdentifiersAndBrackets()
    {
        AssertHighlighter("matlab",
"""
x = end';
y = pi';
z = sin(x)';
w = x{1}';
v = x(1).y';
""",
"""
x = end&#x27;;
y = pi&#x27;;
z = <span class="hljs-built_in">sin</span>(x)&#x27;;
w = x{<span class="hljs-number">1</span>}&#x27;;
v = x(<span class="hljs-number">1</span>).y&#x27;;
""");
    }

    [Fact]
    public void Transpose_VersusString()
    {
        AssertHighlighter("matlab",
"""
disp 'hello'
x = a ';
y = [a' b'];
z = [a 'str'];
w = a+'x';
v = f('x')';
""",
"""
<span class="hljs-built_in">disp</span> <span class="hljs-string">&#x27;hello&#x27;</span>
x = a <span class="hljs-string">&#x27;;
y = [a&#x27;</span> b&#x27;];
z = [a <span class="hljs-string">&#x27;str&#x27;</span>];
w = a+<span class="hljs-string">&#x27;x&#x27;</span>;
v = f(<span class="hljs-string">&#x27;x&#x27;</span>)&#x27;;
""");
    }

    [Fact]
    public void Matrices()
    {
        AssertHighlighter("matlab",
"""
A = [1 2 3; 4 5 6; 7 8 9];
B = A(2, :);
C = A(:, end);
D = A .* B;
E = A \ b;
F = A ^ 2;
G = A ~= B;
H = {1, 'two', [3 4]};
I = H{2};
J = s.field.sub;
K = @(x) x.^2 + 1;
L = @sin;
""",
"""
A = [<span class="hljs-number">1</span> <span class="hljs-number">2</span> <span class="hljs-number">3</span>; <span class="hljs-number">4</span> <span class="hljs-number">5</span> <span class="hljs-number">6</span>; <span class="hljs-number">7</span> <span class="hljs-number">8</span> <span class="hljs-number">9</span>];
B = A(<span class="hljs-number">2</span>, :);
C = A(:, <span class="hljs-keyword">end</span>);
D = A .* B;
E = A \ b;
F = A ^ <span class="hljs-number">2</span>;
G = A ~= B;
H = {<span class="hljs-number">1</span>, <span class="hljs-string">&#x27;two&#x27;</span>, [<span class="hljs-number">3</span> <span class="hljs-number">4</span>]};
I = H{<span class="hljs-number">2</span>};
J = s.field.sub;
K = @(x) x.^<span class="hljs-number">2</span> + <span class="hljs-number">1</span>;
L = @<span class="hljs-built_in">sin</span>;
""");
    }

    [Fact]
    public void FunctionHandles()
    {
        AssertHighlighter("matlab",
"""
result = cellfun(@(x) x * 2, {1, 2, 3}, 'UniformOutput', false);
data = readtable('file.csv');
sorted = sortrows(data, 'Name');
[tf, loc] = ismember(a, b);
""",
"""
result = <span class="hljs-built_in">cellfun</span>(@(x) x * <span class="hljs-number">2</span>, {<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>}, <span class="hljs-string">&#x27;UniformOutput&#x27;</span>, <span class="hljs-built_in">false</span>);
data = <span class="hljs-built_in">readtable</span>(<span class="hljs-string">&#x27;file.csv&#x27;</span>);
sorted = <span class="hljs-built_in">sortrows</span>(data, <span class="hljs-string">&#x27;Name&#x27;</span>);
[tf, loc] = <span class="hljs-built_in">ismember</span>(a, b);
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("matlab",
"""
% line comment
x = 1; % trailing comment
%{
block comment
spanning lines
%}
y = 2;
  %{
  indented block
  %}
%{ not a block
z = 3;
%% Cell title
% TODO: fix this
""",
"""
<span class="hljs-comment">% line comment</span>
x = <span class="hljs-number">1</span>; <span class="hljs-comment">% trailing comment</span>
<span class="hljs-comment">%{
block comment
spanning lines
%}</span>
y = <span class="hljs-number">2</span>;
<span class="hljs-comment">  %{
  indented block
  %}</span>
<span class="hljs-comment">%{ not a block</span>
z = <span class="hljs-number">3</span>;
<span class="hljs-comment">%% Cell title</span>
<span class="hljs-comment">% <span class="hljs-doctag">TODO:</span> fix this</span>
""");
    }

    [Fact]
    public void BlockComment_TrailingBlankLine()
    {
        AssertHighlighter("matlab",
"""
%{
comment
%}

x = 1;
""",
"""
<span class="hljs-comment">%{
comment
%}
</span>
x = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void BlockComment_PrecedingBlankLines()
    {
        AssertHighlighter("matlab",
"""
x = 1;


%{
comment
%}
y = 2;
""",
"""
x = <span class="hljs-number">1</span>;
<span class="hljs-comment">

%{
comment
%}</span>
y = <span class="hljs-number">2</span>;
""");
    }

    [Fact]
    public void BlockComment_DelimitersMustBeAlone()
    {
        AssertHighlighter("matlab",
"""
a = 1; %{
b = 2;
%}
c = 3;
""",
"""
a = <span class="hljs-number">1</span>; <span class="hljs-comment">%{</span>
b = <span class="hljs-number">2</span>;
<span class="hljs-comment">%}</span>
c = <span class="hljs-number">3</span>;
""");
    }

    [Fact]
    public void BlockComment_Empty()
    {
        AssertHighlighter("matlab",
"""
%{


%}
x = 1;
""",
"""
<span class="hljs-comment">%{


%}</span>
x = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void LineContinuation()
    {
        AssertHighlighter("matlab",
"""
x = [1, 2, ...
     3, 4];
y = a + ... comment here
    b;
""",
"""
x = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, ...
     <span class="hljs-number">3</span>, <span class="hljs-number">4</span>];
y = a + ... comment here
    b;
""");
    }

    [Fact]
    public void CommandSyntax()
    {
        AssertHighlighter("matlab",
"""
format long
clc
clear all
close all
hold on
warning off
""",
"""
format long
clc
clear all
close all
<span class="hljs-built_in">hold</span> on
warning off
""");
    }

    [Fact]
    public void IllegalLexemes()
    {
        AssertHighlighter("matlab",
"""
x = a // b;
y = # not a comment;
z = /* c */ 1;
w = a /pi;
v = a / pi;
u = a/pi;
""",
"""
x = a // b;
y = # not a comment;
z = /* c */ <span class="hljs-number">1</span>;
w = a /<span class="hljs-built_in">pi</span>;
v = a / <span class="hljs-built_in">pi</span>;
u = a/<span class="hljs-built_in">pi</span>;
""");
    }

    [Fact]
    public void Unterminated_String()
    {
        AssertHighlighter("matlab",
"""
s = 'unterminated
x = 1;
""",
"""
s = <span class="hljs-string">&#x27;unterminated
x = 1;</span>
""");
    }

    [Fact]
    public void Unterminated_DoubleQuotedString()
    {
        AssertHighlighter("matlab",
"""
s = "unterminated
x = 1;
""",
"""
s = <span class="hljs-string">&quot;unterminated
x = 1;</span>
""");
    }

    [Fact]
    public void Unterminated_BlockComment()
    {
        AssertHighlighter("matlab",
"""
%{
never closed
x = 1;
""",
"""
<span class="hljs-comment">%{
never closed
x = 1;</span>
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("matlab",
"""
s = 'héllo';
% commentaire é
x = 1;
""",
"""
s = <span class="hljs-string">&#x27;héllo&#x27;</span>;
<span class="hljs-comment">% commentaire é</span>
x = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void CrLf()
    {
        AssertHighlighter("matlab", "x = a';\r\n%{\r\ncomment\r\n%}\r\ny = 'str'; % c\r\n", "x = a&#x27;;\r\n<span class=\"hljs-comment\">%{\r\ncomment\r\n%}</span>\r\ny = <span class=\"hljs-string\">&#x27;str&#x27;</span>; <span class=\"hljs-comment\">% c</span>\r\n");
    }

    // Without its guard, one pattern is quadratic on each of these inputs: the identifier-with-transpose pattern on a
    // long identifier, the `\s+/\w+` illegal pattern on a long whitespace run, and the `^\s*%{` block comment delimiters
    // on a long run of blank lines (outside and inside a block comment).
    [Theory]
    [InlineData("identifier")]
    [InlineData("spaces")]
    [InlineData("blankLines")]
    [InlineData("blankLinesInComment")]
    public async Task PathologicalInput_CompletesInReasonableTime(string kind)
    {
        var code = kind switch
        {
            "identifier" => new string('a', 300_000),
            "spaces" => new string(' ', 300_000) + "x",
            "blankLines" => string.Concat(Enumerable.Repeat("  \n", 100_000)) + "x",
            "blankLinesInComment" => "%{\nx" + string.Concat(Enumerable.Repeat("  \n", 100_000)) + "x",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        var budget = TimeSpan.FromSeconds(10);
        var isFallback = false;
        var highlight = Task.Run(() => HighlightWithFallbackDetection(code, "matlab", out isFallback));
        var finished = await Task.WhenAny(highlight, Task.Delay(budget)) == highlight;

        Assert.True(finished, $"Highlighting {code.Length} characters of 'matlab' did not finish within {budget.TotalSeconds:F0}s.");
        Assert.False(isFallback);
    }
}
