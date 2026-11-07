namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public sealed class VerilogHighlighterTests
{
    [Fact]
    public void Counter()
    {
        AssertHighlighter("verilog",
"""
`timescale 1ns / 1ps
module counter #(parameter WIDTH = 8) (
    input  wire             clk,
    input  wire             rst_n,
    output reg [WIDTH-1:0]  count
);
    always @(posedge clk or negedge rst_n) begin
        if (!rst_n)
            count <= {WIDTH{1'b0}};
        else
            count <= count + 1;
    end
endmodule
""",
"""
<span class="hljs-meta">`<span class="hljs-keyword">timescale</span> 1ns / 1ps</span>
<span class="hljs-keyword">module</span> counter #(<span class="hljs-keyword">parameter</span> WIDTH = <span class="hljs-number">8</span>) (
    <span class="hljs-keyword">input</span>  <span class="hljs-keyword">wire</span>             clk,
    <span class="hljs-keyword">input</span>  <span class="hljs-keyword">wire</span>             rst_n,
    <span class="hljs-keyword">output</span> <span class="hljs-keyword">reg</span> [WIDTH-<span class="hljs-number">1</span>:<span class="hljs-number">0</span>]  count
);
    <span class="hljs-keyword">always</span> @(<span class="hljs-keyword">posedge</span> clk <span class="hljs-keyword">or</span> <span class="hljs-keyword">negedge</span> rst_n) <span class="hljs-keyword">begin</span>
        <span class="hljs-keyword">if</span> (!rst_n)
            count &lt;= {WIDTH{<span class="hljs-number">1&#x27;b0</span>}};
        <span class="hljs-keyword">else</span>
            count &lt;= count + <span class="hljs-number">1</span>;
    <span class="hljs-keyword">end</span>
<span class="hljs-keyword">endmodule</span>
""");
    }

    [Fact]
    public void Instance()
    {
        AssertHighlighter("verilog",
"""
adder #(8) u1 (.a(a), .b(b), .sum(sum));
adder #(.WIDTH(16), .DEPTH(4)) u2 (.a(x), .b(y));
fifo #(parameter N = 4) f ();
dff #(4 u3 (.d(d));
ram #(.DEPTH($clog2((A + 1) * 2))) u4 ();
mailbox #(packet #(8)) mb;
""",
"""
adder <span class="hljs-variable">#(8)</span> u1 (<span class="hljs-variable">.a</span>(a), <span class="hljs-variable">.b</span>(b), <span class="hljs-variable">.sum</span>(sum));
adder <span class="hljs-variable">#(.WIDTH(16), .DEPTH(4))</span> u2 (<span class="hljs-variable">.a</span>(x), <span class="hljs-variable">.b</span>(y));
fifo #(<span class="hljs-keyword">parameter</span> N = <span class="hljs-number">4</span>) f ();
dff #(<span class="hljs-number">4</span> u3 (<span class="hljs-variable">.d</span>(d));
ram <span class="hljs-variable">#(.DEPTH($clog2((A + 1) * 2)))</span> u4 ();
mailbox <span class="hljs-variable">#(packet #(8))</span> mb;
""");
    }

    [Fact]
    public void Testbench()
    {
        AssertHighlighter("verilog",
"""
module tb;
  reg clk = 0;
  always #5 clk = ~clk;
  initial begin
    $dumpfile("tb.vcd");
    $dumpvars(0, tb);
    #100;
    $display("count = %d at %t", count, $time);
    $finish;
  end
endmodule
""",
"""
<span class="hljs-keyword">module</span> tb;
  <span class="hljs-keyword">reg</span> clk = <span class="hljs-number">0</span>;
  <span class="hljs-keyword">always</span> #<span class="hljs-number">5</span> clk = ~clk;
  <span class="hljs-keyword">initial</span> <span class="hljs-keyword">begin</span>
    <span class="hljs-built_in">$dumpfile</span>(<span class="hljs-string">&quot;tb.vcd&quot;</span>);
    <span class="hljs-built_in">$dumpvars</span>(<span class="hljs-number">0</span>, tb);
    #<span class="hljs-number">100</span>;
    <span class="hljs-built_in">$display</span>(<span class="hljs-string">&quot;count = %d at %t&quot;</span>, count, <span class="hljs-built_in">$time</span>);
    <span class="hljs-built_in">$finish</span>;
  <span class="hljs-keyword">end</span>
<span class="hljs-keyword">endmodule</span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("verilog",
"""
assign a = 4'b1010;
assign b = 8'hFF_00;
assign c = 'd42;
assign d = 16'o777;
assign e = 1_000;
assign f = 4'bxxzz;
assign g = 32'hDEAD_BEEF;
""",
"""
<span class="hljs-keyword">assign</span> a = <span class="hljs-number">4&#x27;b1010</span>;
<span class="hljs-keyword">assign</span> b = <span class="hljs-number">8&#x27;hFF_00</span>;
<span class="hljs-keyword">assign</span> c = <span class="hljs-number">&#x27;d42</span>;
<span class="hljs-keyword">assign</span> d = <span class="hljs-number">16&#x27;o777</span>;
<span class="hljs-keyword">assign</span> e = <span class="hljs-number">1_000</span>;
<span class="hljs-keyword">assign</span> f = <span class="hljs-number">4&#x27;bxxzz</span>;
<span class="hljs-keyword">assign</span> g = <span class="hljs-number">32&#x27;hDEAD_BEEF</span>;
""");
    }

    [Fact]
    public void Directives()
    {
        AssertHighlighter("verilog",
"""
`define WIDTH 8
`ifdef SIMULATION
`include "defs.vh"
`else
`endif // trailing comment
`default_nettype none
`undefineall
x = `__FILE__ + `__LINE__ + `WIDTH;
""",
"""
<span class="hljs-meta">`<span class="hljs-keyword">define</span> WIDTH 8</span>
<span class="hljs-meta">`<span class="hljs-keyword">ifdef</span> SIMULATION</span>
<span class="hljs-meta">`<span class="hljs-keyword">include</span> &quot;defs.vh&quot;</span>
<span class="hljs-meta">`<span class="hljs-keyword">else</span></span>
<span class="hljs-meta">`<span class="hljs-keyword">endif</span> </span><span class="hljs-comment">// trailing comment</span>
<span class="hljs-meta">`<span class="hljs-keyword">default_nettype</span> none</span>
<span class="hljs-meta">`<span class="hljs-keyword">undefineall</span></span>
x = <span class="hljs-variable constant_">`__FILE__</span> + <span class="hljs-variable constant_">`__LINE__</span> + `WIDTH;
""");
    }

    [Fact]
    public void Systemverilog()
    {
        AssertHighlighter("verilog",
"""
package pkg;
  typedef enum logic [1:0] {IDLE, RUN, DONE} state_t;
endpackage

class Packet extends Base;
  rand bit [7:0] data;
  constraint c { data inside {[0:100]}; }
  function new();
    super.new();
    this.data = 0;
  endfunction
endclass

interface bus_if(input logic clk);
  logic valid;
  modport master (output valid);
endinterface

always_ff @(posedge clk) begin
  unique case (state)
    IDLE: state <= RUN;
    default: state <= IDLE;
  endcase
end
""",
"""
<span class="hljs-keyword">package</span> pkg;
  <span class="hljs-keyword">typedef</span> <span class="hljs-keyword">enum</span> <span class="hljs-keyword">logic</span> [<span class="hljs-number">1</span>:<span class="hljs-number">0</span>] {IDLE, RUN, DONE} state_t;
<span class="hljs-keyword">endpackage</span>

<span class="hljs-keyword">class</span> Packet <span class="hljs-keyword">extends</span> Base;
  <span class="hljs-keyword">rand</span> <span class="hljs-keyword">bit</span> [<span class="hljs-number">7</span>:<span class="hljs-number">0</span>] data;
  <span class="hljs-keyword">constraint</span> c { data <span class="hljs-keyword">inside</span> {[<span class="hljs-number">0</span>:<span class="hljs-number">100</span>]}; }
  <span class="hljs-keyword">function</span> <span class="hljs-keyword">new</span>();
    <span class="hljs-keyword">super</span><span class="hljs-variable">.new</span>();
    <span class="hljs-keyword">this</span><span class="hljs-variable">.data</span> = <span class="hljs-number">0</span>;
  <span class="hljs-keyword">endfunction</span>
<span class="hljs-keyword">endclass</span>

<span class="hljs-keyword">interface</span> bus_if(<span class="hljs-keyword">input</span> <span class="hljs-keyword">logic</span> clk);
  <span class="hljs-keyword">logic</span> valid;
  <span class="hljs-keyword">modport</span> master (<span class="hljs-keyword">output</span> valid);
<span class="hljs-keyword">endinterface</span>

<span class="hljs-keyword">always_ff</span> @(<span class="hljs-keyword">posedge</span> clk) <span class="hljs-keyword">begin</span>
  <span class="hljs-keyword">unique</span> <span class="hljs-keyword">case</span> (state)
    IDLE: state &lt;= RUN;
    <span class="hljs-keyword">default</span>: state &lt;= IDLE;
  <span class="hljs-keyword">endcase</span>
<span class="hljs-keyword">end</span>
""");
    }

    [Fact]
    public void Assertions()
    {
        AssertHighlighter("verilog",
"""
property p_req_ack;
  @(posedge clk) req |-> ##[1:3] ack;
endproperty
assert property (p_req_ack) else $error("no ack");
""",
"""
<span class="hljs-keyword">property</span> p_req_ack;
  @(<span class="hljs-keyword">posedge</span> clk) req |-&gt; ##[<span class="hljs-number">1</span>:<span class="hljs-number">3</span>] ack;
<span class="hljs-keyword">endproperty</span>
<span class="hljs-keyword">assert</span> <span class="hljs-keyword">property</span> (p_req_ack) <span class="hljs-keyword">else</span> <span class="hljs-built_in">$error</span>(<span class="hljs-string">&quot;no ack&quot;</span>);
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("verilog",
"""
// line comment
/* block
   comment */
wire x; /* unterminated
""",
"""
<span class="hljs-comment">// line comment</span>
<span class="hljs-comment">/* block
   comment */</span>
<span class="hljs-keyword">wire</span> x; <span class="hljs-comment">/* unterminated</span>
""");
    }

    [Fact]
    public void Strings()
    {
        AssertHighlighter("verilog",
"""
$display("escaped \"quote\" and \\ backslash");
$display("unterminated
wire y;
""",
"""
<span class="hljs-built_in">$display</span>(<span class="hljs-string">&quot;escaped \&quot;quote\&quot; and \\ backslash&quot;</span>);
<span class="hljs-built_in">$display</span>(<span class="hljs-string">&quot;unterminated
wire y;</span>
""");
    }

    [Fact]
    public void DollarNames()
    {
        AssertHighlighter("verilog",
"""
$async$and$array(a, b);
$value$plusargs("N=%d", n);
""",
"""
<span class="hljs-built_in">$async$and$array</span>(a, b);
<span class="hljs-built_in">$value$plusargs</span>(<span class="hljs-string">&quot;N=%d&quot;</span>, n);
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("verilog", "", "");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("verilog",
"""
// héllo
wire héllo;
""",
"""
<span class="hljs-comment">// héllo</span>
<span class="hljs-keyword">wire</span> héllo;
""");
    }

    [Fact]
    public void AliasV()
    {
        AssertHighlighter("v",
"""
module m; endmodule
""",
"""
<span class="hljs-keyword">module</span> m; <span class="hljs-keyword">endmodule</span>
""");
    }

    [Fact]
    public void AliasSv()
    {
        AssertHighlighter("sv",
"""
module m; logic x; endmodule
""",
"""
<span class="hljs-keyword">module</span> m; <span class="hljs-keyword">logic</span> x; <span class="hljs-keyword">endmodule</span>
""");
    }

    [Fact]
    public void AliasSvh()
    {
        AssertHighlighter("svh",
"""
`ifndef PKG_SVH
`define PKG_SVH
`endif
""",
"""
<span class="hljs-meta">`<span class="hljs-keyword">ifndef</span> PKG_SVH</span>
<span class="hljs-meta">`<span class="hljs-keyword">define</span> PKG_SVH</span>
<span class="hljs-meta">`<span class="hljs-keyword">endif</span></span>
""");
    }
}
