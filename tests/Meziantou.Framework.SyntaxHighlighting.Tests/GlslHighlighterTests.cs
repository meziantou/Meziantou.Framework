namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public sealed class GlslHighlighterTests
{
    [Fact]
    public void Vertex()
    {
        AssertHighlighter("glsl",
"""
#version 330 core
layout (location = 0) in vec3 aPos;
layout (location = 1) in vec2 aTexCoord;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

out vec2 TexCoord;

void main()
{
    gl_Position = projection * view * model * vec4(aPos, 1.0);
    TexCoord = aTexCoord;
}
""",
"""
<span class="hljs-meta">#version 330 core</span>
<span class="hljs-keyword">layout</span> (<span class="hljs-keyword">location</span> = <span class="hljs-number">0</span>) <span class="hljs-keyword">in</span> <span class="hljs-type">vec3</span> aPos;
<span class="hljs-keyword">layout</span> (<span class="hljs-keyword">location</span> = <span class="hljs-number">1</span>) <span class="hljs-keyword">in</span> <span class="hljs-type">vec2</span> aTexCoord;

<span class="hljs-keyword">uniform</span> <span class="hljs-type">mat4</span> model;
<span class="hljs-keyword">uniform</span> <span class="hljs-type">mat4</span> view;
<span class="hljs-keyword">uniform</span> <span class="hljs-type">mat4</span> projection;

<span class="hljs-keyword">out</span> <span class="hljs-type">vec2</span> TexCoord;

<span class="hljs-type">void</span> main()
{
    <span class="hljs-built_in">gl_Position</span> = projection * view * model * <span class="hljs-type">vec4</span>(aPos, <span class="hljs-number">1.0</span>);
    TexCoord = aTexCoord;
}
""");
    }

    [Fact]
    public void Fragment()
    {
        AssertHighlighter("glsl",
"""
#version 330 core
precision mediump float;
in vec2 TexCoord;
out vec4 FragColor;
uniform sampler2D texture1;

void main() {
    vec4 color = texture(texture1, TexCoord);
    if (color.a < 0.1)
        discard;
    FragColor = mix(color, vec4(1.0, 0.5, 0.2, 1.0), 0.25);
}
""",
"""
<span class="hljs-meta">#version 330 core</span>
<span class="hljs-keyword">precision</span> <span class="hljs-keyword">mediump</span> <span class="hljs-type">float</span>;
<span class="hljs-keyword">in</span> <span class="hljs-type">vec2</span> TexCoord;
<span class="hljs-keyword">out</span> <span class="hljs-type">vec4</span> FragColor;
<span class="hljs-keyword">uniform</span> <span class="hljs-type">sampler2D</span> texture1;

<span class="hljs-type">void</span> main() {
    <span class="hljs-type">vec4</span> color = <span class="hljs-built_in">texture</span>(texture1, TexCoord);
    <span class="hljs-keyword">if</span> (color.a &lt; <span class="hljs-number">0.1</span>)
        <span class="hljs-keyword">discard</span>;
    FragColor = <span class="hljs-built_in">mix</span>(color, <span class="hljs-type">vec4</span>(<span class="hljs-number">1.0</span>, <span class="hljs-number">0.5</span>, <span class="hljs-number">0.2</span>, <span class="hljs-number">1.0</span>), <span class="hljs-number">0.25</span>);
}
""");
    }

    [Fact]
    public void Compute()
    {
        AssertHighlighter("glsl",
"""
#version 430
layout(local_size_x = 16, local_size_y = 16) in;
layout(std430, binding = 0) buffer Data { float values[]; };
layout(rgba32f, binding = 1) uniform writeonly image2D img;
shared uint counter;
void main() {
    uvec3 id = gl_GlobalInvocationID;
    atomicAdd(counter, 1u);
    barrier();
    imageStore(img, ivec2(id.xy), vec4(values[id.x]));
}
""",
"""
<span class="hljs-meta">#version 430</span>
<span class="hljs-keyword">layout</span>(<span class="hljs-keyword">local_size_x</span> = <span class="hljs-number">16</span>, <span class="hljs-keyword">local_size_y</span> = <span class="hljs-number">16</span>) <span class="hljs-keyword">in</span>;
<span class="hljs-keyword">layout</span>(<span class="hljs-keyword">std430</span>, <span class="hljs-keyword">binding</span> = <span class="hljs-number">0</span>) <span class="hljs-keyword">buffer</span> Data { <span class="hljs-type">float</span> values[]; };
<span class="hljs-keyword">layout</span>(<span class="hljs-keyword">rgba32f</span>, <span class="hljs-keyword">binding</span> = <span class="hljs-number">1</span>) <span class="hljs-keyword">uniform</span> <span class="hljs-keyword">writeonly</span> <span class="hljs-type">image2D</span> img;
<span class="hljs-keyword">shared</span> <span class="hljs-type">uint</span> counter;
<span class="hljs-type">void</span> main() {
    <span class="hljs-type">uvec3</span> id = <span class="hljs-built_in">gl_GlobalInvocationID</span>;
    <span class="hljs-built_in">atomicAdd</span>(counter, <span class="hljs-number">1</span>u);
    <span class="hljs-built_in">barrier</span>();
    <span class="hljs-built_in">imageStore</span>(img, <span class="hljs-type">ivec2</span>(id.xy), <span class="hljs-type">vec4</span>(values[id.x]));
}
""");
    }

    [Fact]
    public void Legacy()
    {
        AssertHighlighter("glsl",
"""
attribute vec4 position;
varying vec3 vNormal;
void main() {
    gl_FragColor = texture2D(tex, gl_TexCoord[0].st);
    bool b = true && !false;
}
""",
"""
<span class="hljs-keyword">attribute</span> <span class="hljs-type">vec4</span> position;
<span class="hljs-keyword">varying</span> <span class="hljs-type">vec3</span> vNormal;
<span class="hljs-type">void</span> main() {
    <span class="hljs-built_in">gl_FragColor</span> = <span class="hljs-built_in">texture2D</span>(tex, <span class="hljs-built_in">gl_TexCoord</span>[<span class="hljs-number">0</span>].st);
    <span class="hljs-type">bool</span> b = <span class="hljs-literal">true</span> &amp;&amp; !<span class="hljs-literal">false</span>;
}
""");
    }

    [Fact]
    public void Preprocessor()
    {
        AssertHighlighter("glsl",
"""
#define PI 3.14159265
#ifdef GL_ES
precision highp float;
#endif
#extension GL_OES_standard_derivatives : enable
""",
"""
<span class="hljs-meta">#define PI 3.14159265</span>
<span class="hljs-meta">#ifdef GL_ES</span>
<span class="hljs-keyword">precision</span> <span class="hljs-keyword">highp</span> <span class="hljs-type">float</span>;
<span class="hljs-meta">#endif</span>
<span class="hljs-meta">#extension GL_OES_standard_derivatives : enable</span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("glsl",
"""
float a = 1.0e-3;
int b = 0xFF;
uint c = 42u;
double d = .5;
float e = 2.f;
""",
"""
<span class="hljs-type">float</span> a = <span class="hljs-number">1.0e-3</span>;
<span class="hljs-type">int</span> b = <span class="hljs-number">0xFF</span>;
<span class="hljs-type">uint</span> c = <span class="hljs-number">42</span>u;
<span class="hljs-type">double</span> d = <span class="hljs-number">.5</span>;
<span class="hljs-type">float</span> e = <span class="hljs-number">2.</span>f;
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("glsl",
"""
// line comment
/* block
   comment TODO: x */
int x; /* unterminated
""",
"""
<span class="hljs-comment">// line comment</span>
<span class="hljs-comment">/* block
   comment <span class="hljs-doctag">TODO:</span> x */</span>
<span class="hljs-type">int</span> x; <span class="hljs-comment">/* unterminated</span>
""");
    }

    [Fact]
    public void Types()
    {
        AssertHighlighter("glsl",
"""
uimage1D a; usamplerBuffer b; image1D c; samplerBuffer d; dmat4x3 m; atomic_uint ac;
""",
"""
<span class="hljs-type">uimage1D</span> a; <span class="hljs-type">usamplerBuffer</span> b; <span class="hljs-type">image1D</span> c; <span class="hljs-type">samplerBuffer</span> d; <span class="hljs-type">dmat4x3</span> m; <span class="hljs-type">atomic_uint</span> ac;
""");
    }

    [Fact]
    public void IllegalQuote()
    {
        AssertHighlighter("glsl",
"""
const char* s = "not glsl";
int y = 1;
""",
"""
<span class="hljs-keyword">const</span> char* s = &quot;<span class="hljs-built_in">not</span> glsl&quot;;
<span class="hljs-type">int</span> y = <span class="hljs-number">1</span>;
""");
    }

    [Fact]
    public void Empty()
    {
        AssertHighlighter("glsl", "", "");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("glsl",
"""
// héllo
float héllo = 1.0;
""",
"""
<span class="hljs-comment">// héllo</span>
<span class="hljs-type">float</span> héllo = <span class="hljs-number">1.0</span>;
""");
    }

    [Fact]
    public void AliasVert()
    {
        AssertHighlighter("vert",
"""
void main() { gl_Position = vec4(0.0); }
""",
"""
<span class="hljs-type">void</span> main() { <span class="hljs-built_in">gl_Position</span> = <span class="hljs-type">vec4</span>(<span class="hljs-number">0.0</span>); }
""");
    }

    [Fact]
    public void AliasFrag()
    {
        AssertHighlighter("frag",
"""
void main() { gl_FragColor = vec4(1.0); }
""",
"""
<span class="hljs-type">void</span> main() { <span class="hljs-built_in">gl_FragColor</span> = <span class="hljs-type">vec4</span>(<span class="hljs-number">1.0</span>); }
""");
    }
}
