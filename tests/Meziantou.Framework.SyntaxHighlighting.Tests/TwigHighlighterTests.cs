namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class TwigHighlighterTests
{
    [Fact]
    public void TemplateInheritance()
    {
        AssertHighlighter("twig",
"""
{% extends "base.html.twig" %}

{% block title %}Welcome{% endblock %}

{% block body %}
  <h1>{{ page.title|upper }}</h1>
  <ul>
  {% for user in users %}
    <li>{{ user.username|e }}</li>
  {% else %}
    <li><em>no user found</em></li>
  {% endfor %}
  </ul>
{% endblock %}
""",
"""
<span class="hljs-template-tag">{%</span> <span class="hljs-name">extends</span> <span class="hljs-string">&quot;base.html.twig&quot;</span> <span class="hljs-template-tag">%}</span><span class="language-html">

</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">block</span> title <span class="hljs-template-tag">%}</span><span class="language-html">Welcome</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endblock</span> <span class="hljs-template-tag">%}</span><span class="language-html">

</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">block</span> body <span class="hljs-template-tag">%}</span><span class="language-html">
  <span class="hljs-tag">&lt;<span class="hljs-name">h1</span>&gt;</span></span><span class="hljs-template-variable">{{ page.title<span class="hljs-punctuation">|</span><span class="hljs-keyword">upper</span> }}</span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">h1</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">ul</span>&gt;</span>
  </span><span class="hljs-template-tag">{%</span> <span class="hljs-name">for</span> user <span class="hljs-keyword">in</span> users <span class="hljs-template-tag">%}</span><span class="language-html">
    <span class="hljs-tag">&lt;<span class="hljs-name">li</span>&gt;</span></span><span class="hljs-template-variable">{{ user.username<span class="hljs-punctuation">|</span>e }}</span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">li</span>&gt;</span>
  </span><span class="hljs-template-tag">{%</span> <span class="hljs-name">else</span> <span class="hljs-template-tag">%}</span><span class="language-html">
    <span class="hljs-tag">&lt;<span class="hljs-name">li</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">em</span>&gt;</span>no user found<span class="hljs-tag">&lt;/<span class="hljs-name">em</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">li</span>&gt;</span>
  </span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endfor</span> <span class="hljs-template-tag">%}</span><span class="language-html">
  <span class="hljs-tag">&lt;/<span class="hljs-name">ul</span>&gt;</span>
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endblock</span> <span class="hljs-template-tag">%}</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("twig",
"""
{# note: disabled template because we no longer use this
    {% for user in users %}
        ...
    {% endfor %}
#}
{# TODO: remove #}
""",
"""
<span class="hljs-comment">{# <span class="hljs-doctag">note:</span> disabled template because we no longer use this
    {% for user in users %}
        ...
    {% endfor %}
#}</span><span class="language-html">
</span><span class="hljs-comment">{# <span class="hljs-doctag">TODO:</span> remove #}</span>
""");
    }

    [Fact]
    public void SetAndFilters()
    {
        AssertHighlighter("twig",
"""
{% set foo = 'bar' %}
{% set items = [1, 2, 3] %}
{{ items|join(', ') }} {{ name|default('Anonymous')|title }} {{ "now"|date("Y-m-d") }}
{{ text|striptags|slice(0, 100)|raw }} {{ price|number_format(2, '.', ',') }} {{ list|length }}
{{ value|custom_filter }} {{ data|json_encode|raw }}
""",
"""
<span class="hljs-template-tag">{%</span> <span class="hljs-name">set</span> foo = <span class="hljs-string">&#x27;bar&#x27;</span> <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">set</span> items = [<span class="hljs-number">1</span>, <span class="hljs-number">2</span>, <span class="hljs-number">3</span>] <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-variable">{{ items<span class="hljs-punctuation">|</span><span class="hljs-keyword">join</span>(<span class="hljs-string">&#x27;, &#x27;</span>) }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ name<span class="hljs-punctuation">|</span><span class="hljs-keyword">default</span>(<span class="hljs-string">&#x27;Anonymous&#x27;</span>)<span class="hljs-punctuation">|</span><span class="hljs-keyword">title</span> }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ <span class="hljs-string">&quot;now&quot;</span><span class="hljs-punctuation">|</span><span class="hljs-keyword">date</span>(<span class="hljs-string">&quot;Y-m-d&quot;</span>) }}</span><span class="language-html">
</span><span class="hljs-template-variable">{{ text<span class="hljs-punctuation">|</span><span class="hljs-keyword">striptags</span><span class="hljs-punctuation">|</span><span class="hljs-keyword">slice</span>(<span class="hljs-number">0</span>, <span class="hljs-number">100</span>)<span class="hljs-punctuation">|</span><span class="hljs-keyword">raw</span> }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ price<span class="hljs-punctuation">|</span><span class="hljs-keyword">number_format</span>(<span class="hljs-number">2</span>, <span class="hljs-string">&#x27;.&#x27;</span>, <span class="hljs-string">&#x27;,&#x27;</span>) }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ list<span class="hljs-punctuation">|</span><span class="hljs-keyword">length</span> }}</span><span class="language-html">
</span><span class="hljs-template-variable">{{ value<span class="hljs-punctuation">|</span>custom_filter }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ data<span class="hljs-punctuation">|</span><span class="hljs-keyword">json_encode</span><span class="hljs-punctuation">|</span><span class="hljs-keyword">raw</span> }}</span>
""");
    }

    [Fact]
    public void Functions()
    {
        AssertHighlighter("twig",
"""
{{ path('blog_show', {'slug': post.slug}) }}
{{ asset('images/logo.png') }} {{ url('homepage') }} {{ form_start(form) }} {{ form_widget(form.name) }}
{{ dump(user) }} {{ range(1, 10) }} {{ max(1, 3, 2) }} {{ include('template.html') }}
{{ asset_version('x') }} {{ random(['a', 'b']) }} {{ form_end(form) }}
""",
"""
<span class="hljs-template-variable">{{ <span class="hljs-name">path</span>(<span class="hljs-string">&#x27;blog_show&#x27;</span>, {<span class="hljs-string">&#x27;slug&#x27;</span>: post.slug}) }}</span><span class="language-html">
</span><span class="hljs-template-variable">{{ <span class="hljs-name">asset</span>(<span class="hljs-string">&#x27;images/logo.png&#x27;</span>) }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ <span class="hljs-name">url</span>(<span class="hljs-string">&#x27;homepage&#x27;</span>) }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ <span class="hljs-name">form_start</span>(form) }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ <span class="hljs-name">form_widget</span>(form.name) }}</span><span class="language-html">
</span><span class="hljs-template-variable">{{ <span class="hljs-name">dump</span>(user) }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ <span class="hljs-name">range</span>(<span class="hljs-number">1</span>, <span class="hljs-number">10</span>) }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ <span class="hljs-name">max</span>(<span class="hljs-number">1</span>, <span class="hljs-number">3</span>, <span class="hljs-number">2</span>) }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ <span class="hljs-name">include</span>(<span class="hljs-string">&#x27;template.html&#x27;</span>) }}</span><span class="language-html">
</span><span class="hljs-template-variable">{{ <span class="hljs-name">asset_version</span>(<span class="hljs-string">&#x27;x&#x27;</span>) }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ <span class="hljs-name">random</span>([<span class="hljs-string">&#x27;a&#x27;</span>, <span class="hljs-string">&#x27;b&#x27;</span>]) }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ <span class="hljs-name">form_end</span>(form) }}</span>
""");
    }

    [Fact]
    public void IfAndNumbers()
    {
        AssertHighlighter("twig",
"""
{% if user.isLoggedIn and not user.banned %}
  Hello {{ user.name }}!
{% elseif user.isGuest %}
  Hello guest
{% endif %}
{% if 0 in items %}zero{% endif %}{{ 0 }} {{ 10 }} {{ x > 0 ? 'pos' : 'neg' }}
""",
"""
<span class="hljs-template-tag">{%</span> <span class="hljs-name">if</span> user.isLoggedIn and not user.banned <span class="hljs-template-tag">%}</span><span class="language-html">
  Hello </span><span class="hljs-template-variable">{{ user.name }}</span><span class="language-html">!
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">elseif</span> user.isGuest <span class="hljs-template-tag">%}</span><span class="language-html">
  Hello guest
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endif</span> <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">if</span> <span class="hljs-number">0</span> <span class="hljs-keyword">in</span> items <span class="hljs-template-tag">%}</span><span class="language-html">zero</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endif</span> <span class="hljs-template-tag">%}</span><span class="hljs-template-variable">{{ <span class="hljs-number">0</span> }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ <span class="hljs-number">10</span> }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ x &gt; <span class="hljs-number">0</span> ? <span class="hljs-string">&#x27;pos&#x27;</span> : <span class="hljs-string">&#x27;neg&#x27;</span> }}</span>
""");
    }

    [Fact]
    public void Macro()
    {
        AssertHighlighter("twig",
"""
{% macro input(name, value, type = "text", size = 20) %}
  <input type="{{ type }}" name="{{ name }}" value="{{ value|e }}" size="{{ size }}" />
{% endmacro %}
{% import "forms.html" as forms %}
{% from 'forms.html' import input as input_field %}
{{ forms.input('username') }}
""",
"""
<span class="hljs-template-tag">{%</span> <span class="hljs-name">macro</span> input(name, value, type = <span class="hljs-string">&quot;text&quot;</span>, size = <span class="hljs-number">20</span>) <span class="hljs-template-tag">%}</span><span class="language-html">
  <span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;</span></span></span><span class="hljs-template-variable">{{ type }}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">&quot;</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;</span></span></span><span class="hljs-template-variable">{{ name }}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">&quot;</span> <span class="hljs-attr">value</span>=<span class="hljs-string">&quot;</span></span></span><span class="hljs-template-variable">{{ value<span class="hljs-punctuation">|</span>e }}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">&quot;</span> <span class="hljs-attr">size</span>=<span class="hljs-string">&quot;</span></span></span><span class="hljs-template-variable">{{ size }}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">&quot;</span> /&gt;</span>
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endmacro</span> <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">import</span> <span class="hljs-string">&quot;forms.html&quot;</span> as forms <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">from</span> <span class="hljs-string">&#x27;forms.html&#x27;</span> import input as input_field <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-variable">{{ forms.input(<span class="hljs-string">&#x27;username&#x27;</span>) }}</span>
""");
    }

    [Fact]
    public void TagNames()
    {
        AssertHighlighter("twig",
"""
{% form_theme form 'form.html.twig' %}
{% transchoice count %}x{% endtranschoice %}
{% trans_default_domain 'app' %}
{% blockquote %}x{% endblockquote %}
{% endform_theme %}
{% apply upper %}text{% endapply %}
{% autoescape 'html' %}{% endautoescape %}
{% cache 'key' %}{% endcache %}
{% sandbox %}{% endsandbox %}
{% with { foo: 42 } only %}{% endwith %}
{% embed "teasers_skeleton.twig" %}{% endembed %}
{% verbatim %}{{ raw }}{% endverbatim %}
{% custom_tag arg %}
{% do 1 + 2 %} {% flush %} {% deprecated 'msg' %} {% use 'blocks.html' %} {% stopwatch 'e' %}{% endstopwatch %}
""",
"""
<span class="hljs-template-tag">{%</span> <span class="hljs-name">form_theme</span> <span class="hljs-name">form</span> <span class="hljs-string">&#x27;form.html.twig&#x27;</span> <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">transchoice</span> count <span class="hljs-template-tag">%}</span><span class="language-html">x</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endtranschoice</span> <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">trans_default_domain</span> <span class="hljs-string">&#x27;app&#x27;</span> <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">blockquote</span> <span class="hljs-template-tag">%}</span><span class="language-html">x</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endblockquote</span> <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endform_theme</span> <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">apply</span> upper <span class="hljs-template-tag">%}</span><span class="language-html">text</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endapply</span> <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">autoescape</span> <span class="hljs-string">&#x27;html&#x27;</span> <span class="hljs-template-tag">%}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endautoescape</span> <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">cache</span> <span class="hljs-string">&#x27;key&#x27;</span> <span class="hljs-template-tag">%}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endcache</span> <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">sandbox</span> <span class="hljs-template-tag">%}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endsandbox</span> <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">with</span> { foo: <span class="hljs-number">42</span> } only <span class="hljs-template-tag">%}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endwith</span> <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">embed</span> <span class="hljs-string">&quot;teasers_skeleton.twig&quot;</span> <span class="hljs-template-tag">%}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endembed</span> <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">verbatim</span> <span class="hljs-template-tag">%}</span><span class="hljs-template-variable">{{ raw }}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endverbatim</span> <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">custom_tag</span> arg <span class="hljs-template-tag">%}</span><span class="language-html">
</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">do</span> <span class="hljs-number">1</span> + <span class="hljs-number">2</span> <span class="hljs-template-tag">%}</span><span class="language-html"> </span><span class="hljs-template-tag">{%</span> <span class="hljs-name">flush</span> <span class="hljs-template-tag">%}</span><span class="language-html"> </span><span class="hljs-template-tag">{%</span> <span class="hljs-name">deprecated</span> <span class="hljs-string">&#x27;msg&#x27;</span> <span class="hljs-template-tag">%}</span><span class="language-html"> </span><span class="hljs-template-tag">{%</span> <span class="hljs-name">use</span> <span class="hljs-string">&#x27;blocks.html&#x27;</span> <span class="hljs-template-tag">%}</span><span class="language-html"> </span><span class="hljs-template-tag">{%</span> <span class="hljs-name">stopwatch</span> <span class="hljs-string">&#x27;e&#x27;</span> <span class="hljs-template-tag">%}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endstopwatch</span> <span class="hljs-template-tag">%}</span>
""");
    }

    [Fact]
    public void TagInAttributeValue()
    {
        AssertHighlighter("twig",
"""
<a href="{{ path('home') }}" class="{% if active %}active{% endif %}">{{ label }}</a>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;</span></span></span><span class="hljs-template-variable">{{ <span class="hljs-name">path</span>(<span class="hljs-string">&#x27;home&#x27;</span>) }}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">&quot;</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;</span></span></span><span class="hljs-template-tag">{%</span> <span class="hljs-name">if</span> active <span class="hljs-template-tag">%}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">active</span></span></span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endif</span> <span class="hljs-template-tag">%}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">&quot;</span>&gt;</span></span><span class="hljs-template-variable">{{ label }}</span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span></span>
""");
    }

    [Fact]
    public void TagsInScript()
    {
        AssertHighlighter("twig",
"""
<script>
  var config = {{ config|json_encode|raw }};
  var name = "{{ name }}";
  {% if debug %}console.log(config);{% endif %}
</script>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span><span class="language-javascript">
  <span class="hljs-keyword">var</span> config = </span></span><span class="hljs-template-variable">{{ config<span class="hljs-punctuation">|</span><span class="hljs-keyword">json_encode</span><span class="hljs-punctuation">|</span><span class="hljs-keyword">raw</span> }}</span><span class="language-html"><span class="language-javascript">;
  <span class="hljs-keyword">var</span> name = <span class="hljs-string">&quot;</span></span></span><span class="hljs-template-variable">{{ name }}</span><span class="language-html"><span class="language-javascript"><span class="hljs-string">&quot;;
  </span></span></span><span class="hljs-template-tag">{%</span> <span class="hljs-name">if</span> debug <span class="hljs-template-tag">%}</span><span class="language-html"><span class="language-javascript"><span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(config);</span></span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endif</span> <span class="hljs-template-tag">%}</span><span class="language-html"><span class="language-javascript">
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span></span>
""");
    }

    [Fact]
    public void WhitespaceControl()
    {
        AssertHighlighter("twig",
"""
{%- if foo -%}
  {{- bar -}}
{%~ endif ~%}
""",
"""
<span class="hljs-template-tag">{%-</span> <span class="hljs-name">if</span> foo <span class="hljs-template-tag">-%}</span><span class="language-html">
  </span><span class="hljs-template-variable">{{- bar -}}</span><span class="language-html">
</span><span class="hljs-template-tag">{%~</span> <span class="hljs-name">endif</span> <span class="hljs-template-tag">~%}</span>
""");
    }

    [Fact]
    public void NestedBraces()
    {
        AssertHighlighter("twig",
"""
{{ {'a': 1, 'b': {'c': 2}} }} {{ {{ x }} }}
""",
"""
<span class="hljs-template-variable">{{ {<span class="hljs-string">&#x27;a&#x27;</span>: <span class="hljs-number">1</span>, <span class="hljs-string">&#x27;b&#x27;</span>: {<span class="hljs-string">&#x27;c&#x27;</span>: <span class="hljs-number">2</span>}}</span><span class="language-html"> }} </span><span class="hljs-template-variable">{{ <span class="hljs-template-variable">{{ x }}</span> }}</span>
""");
    }

    [Fact]
    public void EscapedQuotes()
    {
        AssertHighlighter("twig",
"""
{{ "hello #{name}" }} {{ 'it\'s' }} {{ "a \" b" }}
""",
"""
<span class="hljs-template-variable">{{ <span class="hljs-string">&quot;hello #{name}&quot;</span> }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ <span class="hljs-string">&#x27;it\&#x27;s&#x27;</span> }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ <span class="hljs-string">&quot;a \&quot; b&quot;</span> }}</span>
""");
    }

    [Fact]
    public void UnterminatedString()
    {
        AssertHighlighter("twig",
"""
{{ 'abc }}
<p>x</p>
""",
"""
<span class="hljs-template-variable">{{ <span class="hljs-string">&#x27;abc }}
&lt;p&gt;x&lt;/p&gt;</span></span>
""");
    }

    [Fact]
    public void UnterminatedTag()
    {
        AssertHighlighter("twig",
"""
<p>{% if x</p>
{{ name
<b>bold</b>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span></span><span class="hljs-template-tag">{%</span> <span class="hljs-name">if</span> x&lt;/p&gt;
{{ name
&lt;b&gt;bold&lt;/b&gt;
""");
    }

    [Fact]
    public void CaseInsensitive()
    {
        AssertHighlighter("twig",
"""
{% IF X %}{{ x|UPPER }}{% ENDIF %}{{ PATH('x') }}
""",
"""
<span class="hljs-template-tag">{%</span> <span class="hljs-name">IF</span> X <span class="hljs-template-tag">%}</span><span class="hljs-template-variable">{{ x<span class="hljs-punctuation">|</span><span class="hljs-keyword">UPPER</span> }}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">ENDIF</span> <span class="hljs-template-tag">%}</span><span class="hljs-template-variable">{{ <span class="hljs-name">PATH</span>(<span class="hljs-string">&#x27;x&#x27;</span>) }}</span>
""");
    }

    [Fact]
    public void FilterSyntax()
    {
        AssertHighlighter("twig",
"""
{{ x|filter:arg }} {{ x | upper }} {{ x|u.truncate(8) }}
""",
"""
<span class="hljs-template-variable">{{ x<span class="hljs-punctuation">|</span><span class="hljs-keyword">filter</span>:arg }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ x | upper }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ x<span class="hljs-punctuation">|</span><span class="hljs-keyword">u</span>.truncate(<span class="hljs-number">8</span>) }}</span>
""");
    }

    [Fact]
    public void InKeyword()
    {
        AssertHighlighter("twig",
"""
{% for i in 0..10 %}{{ i }}{% endfor %}{% if 'a' in 'abc' %}{% endif %}{% for key, value in array %}{% endfor %}
""",
"""
<span class="hljs-template-tag">{%</span> <span class="hljs-name">for</span> i <span class="hljs-keyword">in</span> <span class="hljs-number">0</span>..<span class="hljs-number">10</span> <span class="hljs-template-tag">%}</span><span class="hljs-template-variable">{{ i }}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endfor</span> <span class="hljs-template-tag">%}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">if</span> <span class="hljs-string">&#x27;a&#x27;</span> <span class="hljs-keyword">in</span> <span class="hljs-string">&#x27;abc&#x27;</span> <span class="hljs-template-tag">%}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endif</span> <span class="hljs-template-tag">%}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">for</span> key, value <span class="hljs-keyword">in</span> array <span class="hljs-template-tag">%}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endfor</span> <span class="hljs-template-tag">%}</span>
""");
    }

    [Fact]
    public void CrLf()
    {
        AssertHighlighter("twig",
"{% if x %}\r\n<p>{{ y }}</p>\r\n{% endif %}",
"<span class=\"hljs-template-tag\">{%</span> <span class=\"hljs-name\">if</span> x <span class=\"hljs-template-tag\">%}</span><span class=\"language-html\">\r\n<span class=\"hljs-tag\">&lt;<span class=\"hljs-name\">p</span>&gt;</span></span><span class=\"hljs-template-variable\">{{ y }}</span><span class=\"language-html\"><span class=\"hljs-tag\">&lt;/<span class=\"hljs-name\">p</span>&gt;</span>\r\n</span><span class=\"hljs-template-tag\">{%</span> <span class=\"hljs-name\">endif</span> <span class=\"hljs-template-tag\">%}</span>");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("twig",
"""
<p>{{ prénom|title }} — {% trans %}Héllo{% endtrans %}</p>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span></span><span class="hljs-template-variable">{{ prénom<span class="hljs-punctuation">|</span><span class="hljs-keyword">title</span> }}</span><span class="language-html"> — </span><span class="hljs-template-tag">{%</span> <span class="hljs-name">trans</span> <span class="hljs-template-tag">%}</span><span class="language-html">Héllo</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endtrans</span> <span class="hljs-template-tag">%}</span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span></span>
""");
    }

    [Fact]
    public void CraftCmsAlias()
    {
        AssertHighlighter("craftcms",
"""
{% for i in 0..10 %}{{ i }}{% endfor %}{% if 'a' in 'abc' %}{% endif %}{% for key, value in array %}{% endfor %}
""",
"""
<span class="hljs-template-tag">{%</span> <span class="hljs-name">for</span> i <span class="hljs-keyword">in</span> <span class="hljs-number">0</span>..<span class="hljs-number">10</span> <span class="hljs-template-tag">%}</span><span class="hljs-template-variable">{{ i }}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endfor</span> <span class="hljs-template-tag">%}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">if</span> <span class="hljs-string">&#x27;a&#x27;</span> <span class="hljs-keyword">in</span> <span class="hljs-string">&#x27;abc&#x27;</span> <span class="hljs-template-tag">%}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endif</span> <span class="hljs-template-tag">%}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">for</span> key, value <span class="hljs-keyword">in</span> array <span class="hljs-template-tag">%}</span><span class="hljs-template-tag">{%</span> <span class="hljs-name">endfor</span> <span class="hljs-template-tag">%}</span>
""");
    }
}
