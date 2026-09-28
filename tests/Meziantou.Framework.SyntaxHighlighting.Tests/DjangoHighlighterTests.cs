namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class DjangoHighlighterTests
{
    [Fact]
    public void TemplateInheritance()
    {
        AssertHighlighter("django",
"""
{% extends "base.html" %}
{% load static i18n %}

{% block title %}{{ section.title }}{% endblock %}

{% block content %}
<h1>{{ section.title|upper }}</h1>
{% for story in story_list %}
  <h2><a href="{{ story.get_absolute_url }}">{{ story.headline|truncatewords:10 }}</a></h2>
  <p>{{ story.tease|default:"nothing"|linebreaks }}</p>
{% empty %}
  <p>No stories.</p>
{% endfor %}
{% endblock %}
""",
"""
<span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">extends</span></span> &quot;base.html&quot; %}</span><span class="language-html">
</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">load</span></span> static i18n %}</span><span class="language-html">

</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">block</span></span> title %}</span><span class="hljs-template-variable">{{ section.title }}</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">endblock</span></span> %}</span><span class="language-html">

</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">block</span></span> content %}</span><span class="language-html">
<span class="hljs-tag">&lt;<span class="hljs-name">h1</span>&gt;</span></span><span class="hljs-template-variable">{{ section.title|<span class="hljs-name">upper</span> }}</span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">h1</span>&gt;</span>
</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">for</span></span> story <span class="hljs-keyword">in</span> story_list %}</span><span class="language-html">
  <span class="hljs-tag">&lt;<span class="hljs-name">h2</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;</span></span></span><span class="hljs-template-variable">{{ story.get_absolute_url }}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">&quot;</span>&gt;</span></span><span class="hljs-template-variable">{{ story.headline|<span class="hljs-name">truncatewords</span>:10 }}</span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">h2</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span></span><span class="hljs-template-variable">{{ story.tease|<span class="hljs-name">default</span>:<span class="hljs-string">&quot;nothing&quot;</span>|<span class="hljs-name">linebreaks</span> }}</span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">empty</span></span> %}</span><span class="language-html">
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>No stories.<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">endfor</span></span> %}</span><span class="language-html">
</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">endblock</span></span> %}</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("django",
"""
{# single line comment #}
{% comment "Optional note" %}
  <p>Commented out text with {{ create_date|date:"c" }}</p>
{% endcomment %}
{#TODO: fix#}
""",
"""
<span class="hljs-comment">{# single line comment #}</span><span class="language-html">
</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">comment</span></span> &quot;Optional note&quot; %}</span><span class="language-html">
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Commented out text with </span><span class="hljs-template-variable">{{ create_date|<span class="hljs-name">date</span>:<span class="hljs-string">&quot;c&quot;</span> }}</span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">endcomment</span></span> %}</span><span class="language-html">
</span><span class="hljs-comment">{#<span class="hljs-doctag">TODO:</span> fix#}</span>
""");
    }

    [Fact]
    public void IfElifElse()
    {
        AssertHighlighter("django",
"""
{% if athlete_list and not coach_list %}
  <p>{{ athlete_list|length }} athletes</p>
{% elif athlete_in_locker_room_list %}
  <p>Athletes should be out</p>
{% else %}
  <p>No athletes.</p>
{% endif %}
""",
"""
<span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">if</span></span> athlete_list and not coach_list %}</span><span class="language-html">
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span></span><span class="hljs-template-variable">{{ athlete_list|<span class="hljs-name">length</span> }}</span><span class="language-html"> athletes<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">elif</span></span> athlete_in_locker_room_list %}</span><span class="language-html">
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Athletes should be out<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">else</span></span> %}</span><span class="language-html">
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>No athletes.<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">endif</span></span> %}</span>
""");
    }

    [Fact]
    public void Filters()
    {
        AssertHighlighter("django",
"""
{{ value|date:"D d M Y" }} {{ value|add:"2" }} {{ name|lower|capfirst }} {{ text|truncatechars:9 }}
{{ value|default_if_none:'n/a' }} {{ list|join:", " }} {{ value|stringformat:"E" }} {{ my_date|date:"Y-m-d" }}
{{ value|custom_filter }} {{ value|safe }}
""",
"""
<span class="hljs-template-variable">{{ value|<span class="hljs-name">date</span>:<span class="hljs-string">&quot;D d M Y&quot;</span> }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ value|<span class="hljs-name">add</span>:<span class="hljs-string">&quot;2&quot;</span> }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ name|<span class="hljs-name">lower</span>|<span class="hljs-name">capfirst</span> }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ text|<span class="hljs-name">truncatechars</span>:9 }}</span><span class="language-html">
</span><span class="hljs-template-variable">{{ value|<span class="hljs-name">default_if_none</span>:<span class="hljs-string">&#x27;n/a&#x27;</span> }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ list|<span class="hljs-name">join</span>:<span class="hljs-string">&quot;, &quot;</span> }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ value|<span class="hljs-name">stringformat</span>:<span class="hljs-string">&quot;E&quot;</span> }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ my_date|<span class="hljs-name">date</span>:<span class="hljs-string">&quot;Y-m-d&quot;</span> }}</span><span class="language-html">
</span><span class="hljs-template-variable">{{ value|custom_filter }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ value|<span class="hljs-name">safe</span> }}</span>
""");
    }

    [Fact]
    public void TagsWithArguments()
    {
        AssertHighlighter("django",
"""
{% with total=business.employees.count %}{{ total }} employee{{ total|pluralize }}{% endwith %}
{% url 'news-year-archive' 2012 as the_url %}<a href="{% url 'app:view' pk=obj.pk %}">link</a>
{% csrf_token %} {% static 'css/style.css' %} {% trans "Hello" %}
{% blocktrans with amount=article.price %}That will cost $ {{ amount }}.{% endblocktrans %}
{% cycle 'row1' 'row2' as rowcolors %} {% regroup cities by country as country_list %}
""",
"""
<span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">with</span></span> total=business.employees.count %}</span><span class="hljs-template-variable">{{ total }}</span><span class="language-html"> employee</span><span class="hljs-template-variable">{{ total|<span class="hljs-name">pluralize</span> }}</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">endwith</span></span> %}</span><span class="language-html">
</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">url</span></span> &#x27;news-year-archive&#x27; 2012 <span class="hljs-keyword">as</span> the_url %}</span><span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;</span></span></span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">url</span></span> &#x27;app:view&#x27; pk=obj.pk %}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">&quot;</span>&gt;</span>link<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">csrf_token</span></span> %}</span><span class="language-html"> </span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">static</span></span> &#x27;css/style.css&#x27; %}</span><span class="language-html"> </span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">trans</span></span> &quot;Hello&quot; %}</span><span class="language-html">
</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">blocktrans</span></span> with amount=article.price %}</span><span class="language-html">That will cost $ </span><span class="hljs-template-variable">{{ amount }}</span><span class="language-html">.</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">endblocktrans</span></span> %}</span><span class="language-html">
</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">cycle</span></span> &#x27;row1&#x27; &#x27;row2&#x27; <span class="hljs-keyword">as</span> rowcolors %}</span><span class="language-html"> </span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">regroup</span></span> cities <span class="hljs-keyword">by</span> country <span class="hljs-keyword">as</span> country_list %}</span>
""");
    }

    [Fact]
    public void JinjaTemplate()
    {
        AssertHighlighter("jinja",
"""
{% set navigation = [('index.html', 'Index'), ('about.html', 'About')] %}
{% macro input(name, value='', type='text') -%}
  <input type="{{ type }}" name="{{ name }}" value="{{ value|e }}">
{%- endmacro %}
{% for key, value in my_dict.items() if value is not none %}
  {{ loop.index }}: {{ key }} = {{ value }}
{% endfor %}
{% raw %}{{ not processed }}{% endraw %}
{{ "Hello %s"|format(name) }} {{ items|map(attribute='name')|join(', ') }}
""",
"""
<span class="hljs-template-tag">{% <span class="hljs-name">set</span> navigation = [(&#x27;index.html&#x27;, &#x27;Index&#x27;), (&#x27;about.html&#x27;, &#x27;About&#x27;)] %}</span><span class="language-html">
</span><span class="hljs-template-tag">{% <span class="hljs-name">macro</span> input(name, value=&#x27;&#x27;, type=&#x27;text&#x27;) -%}</span><span class="language-html">
  <span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">type</span>=<span class="hljs-string">&quot;</span></span></span><span class="hljs-template-variable">{{ type }}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">&quot;</span> <span class="hljs-attr">name</span>=<span class="hljs-string">&quot;</span></span></span><span class="hljs-template-variable">{{ name }}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">&quot;</span> <span class="hljs-attr">value</span>=<span class="hljs-string">&quot;</span></span></span><span class="hljs-template-variable">{{ value|e }}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">&quot;</span>&gt;</span>
</span><span class="hljs-template-tag">{%- <span class="hljs-name">endmacro</span> %}</span><span class="language-html">
</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">for</span></span> key, value <span class="hljs-keyword">in</span> my_dict.items() if value is not none %}</span><span class="language-html">
  </span><span class="hljs-template-variable">{{ loop.index }}</span><span class="language-html">: </span><span class="hljs-template-variable">{{ key }}</span><span class="language-html"> = </span><span class="hljs-template-variable">{{ value }}</span><span class="language-html">
</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">endfor</span></span> %}</span><span class="language-html">
</span><span class="hljs-template-tag">{% <span class="hljs-name">raw</span> %}</span><span class="hljs-template-variable">{{ not processed }}</span><span class="hljs-template-tag">{% <span class="hljs-name">endraw</span> %}</span><span class="language-html">
</span><span class="hljs-template-variable">{{ &quot;Hello %s&quot;|format(name) }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ items|map(attribute=&#x27;name&#x27;)|<span class="hljs-name">join</span>(&#x27;, &#x27;) }}</span>
""");
    }

    [Fact]
    public void TagInAttributeValue()
    {
        AssertHighlighter("django",
"""
<option value="{{ item.id }}" {% if item.id == selected %}selected{% endif %}>{{ item.name }}</option>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">option</span> <span class="hljs-attr">value</span>=<span class="hljs-string">&quot;</span></span></span><span class="hljs-template-variable">{{ item.id }}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-string">&quot;</span> </span></span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">if</span></span> item.id == selected %}</span><span class="language-html"><span class="hljs-tag"><span class="hljs-attr">selected</span></span></span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">endif</span></span> %}</span><span class="language-html"><span class="hljs-tag">&gt;</span></span><span class="hljs-template-variable">{{ item.name }}</span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">option</span>&gt;</span></span>
""");
    }

    [Fact]
    public void TagsInScript()
    {
        AssertHighlighter("django",
"""
<script>
  var user = "{{ user.name|escapejs }}";
  var items = {{ items|safe }};
  {% if debug %}console.log(user);{% endif %}
</script>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span><span class="language-javascript">
  <span class="hljs-keyword">var</span> user = <span class="hljs-string">&quot;</span></span></span><span class="hljs-template-variable">{{ user.name|<span class="hljs-name">escapejs</span> }}</span><span class="language-html"><span class="language-javascript"><span class="hljs-string">&quot;;
  var items = </span></span></span><span class="hljs-template-variable">{{ items|<span class="hljs-name">safe</span> }}</span><span class="language-html"><span class="language-javascript">;
  </span></span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">if</span></span> debug %}</span><span class="language-html"><span class="language-javascript"><span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(user);</span></span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">endif</span></span> %}</span><span class="language-html"><span class="language-javascript">
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span></span>
""");
    }

    [Fact]
    public void Verbatim()
    {
        AssertHighlighter("django",
"""
{% verbatim %}
  {{if dying}}Still alive.{{/if}}
{% endverbatim %}
""",
"""
<span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">verbatim</span></span> %}</span><span class="language-html">
  </span><span class="hljs-template-variable">{{if dying}}</span><span class="language-html">Still alive.</span><span class="hljs-template-variable">{{/if}}</span><span class="language-html">
</span><span class="hljs-template-tag">{% <span class="hljs-name">endverbatim</span> %}</span>
""");
    }

    [Fact]
    public void UnterminatedBlockComment()
    {
        AssertHighlighter("django",
"""
{% comment %}
<p>x</p>
""",
"""
<span class="hljs-comment">{% comment %}
&lt;p&gt;x&lt;/p&gt;</span>
""");
    }

    [Fact]
    public void UnterminatedTag()
    {
        AssertHighlighter("django",
"""
<p>{% if x</p>
{{ name
<b>bold</b>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span></span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">if</span></span> x&lt;/p&gt;
{{ name
&lt;b&gt;bold&lt;/b&gt;</span>
""");
    }

    [Fact]
    public void CaseInsensitive()
    {
        AssertHighlighter("django",
"""
{% IF X %}{{ x|UPPER }}{% ENDIF %}{% Comment %}c{% EndComment %}
""",
"""
<span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">IF</span></span> X %}</span><span class="hljs-template-variable">{{ x|<span class="hljs-name">UPPER</span> }}</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">ENDIF</span></span> %}</span><span class="hljs-comment">{% Comment %}c{% EndComment %}</span>
""");
    }

    [Fact]
    public void StringsInFilters()
    {
        AssertHighlighter("django",
"""
{{ x|default:"a|b" }} {{ x|yesno:'yes,no,maybe' }} {{ x|date:"a \" b" }}
""",
"""
<span class="hljs-template-variable">{{ x|<span class="hljs-name">default</span>:<span class="hljs-string">&quot;a|b&quot;</span> }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ x|<span class="hljs-name">yesno</span>:<span class="hljs-string">&#x27;yes,no,maybe&#x27;</span> }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ x|<span class="hljs-name">date</span>:<span class="hljs-string">&quot;a \&quot; b&quot;</span> }}</span>
""");
    }

    [Fact]
    public void InByAsKeywords()
    {
        AssertHighlighter("django",
"""
{% for x in items %}{% regroup a by b as c %}{% with a as b %}{% endwith %}{% endfor %}
""",
"""
<span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">for</span></span> x <span class="hljs-keyword">in</span> items %}</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">regroup</span></span> a <span class="hljs-keyword">by</span> b <span class="hljs-keyword">as</span> c %}</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">with</span></span> a <span class="hljs-keyword">as</span> b %}</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">endwith</span></span> %}</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">endfor</span></span> %}</span>
""");
    }

    [Fact]
    public void WhitespaceControl()
    {
        AssertHighlighter("django",
"""
{%- if x -%} y {%+ endif +%}
""",
"""
<span class="hljs-template-tag">{%- <span class="hljs-name"><span class="hljs-name">if</span></span> x -%}</span><span class="language-html"> y </span><span class="hljs-template-tag">{%+ <span class="hljs-name"><span class="hljs-name">endif</span></span> +%}</span>
""");
    }

    [Fact]
    public void CrLf()
    {
        AssertHighlighter("django",
"{% if x %}\r\n<p>{{ y }}</p>\r\n{% endif %}",
"<span class=\"hljs-template-tag\">{% <span class=\"hljs-name\"><span class=\"hljs-name\">if</span></span> x %}</span><span class=\"language-html\">\r\n<span class=\"hljs-tag\">&lt;<span class=\"hljs-name\">p</span>&gt;</span></span><span class=\"hljs-template-variable\">{{ y }}</span><span class=\"language-html\"><span class=\"hljs-tag\">&lt;/<span class=\"hljs-name\">p</span>&gt;</span>\r\n</span><span class=\"hljs-template-tag\">{% <span class=\"hljs-name\"><span class=\"hljs-name\">endif</span></span> %}</span>");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("django",
"""
<p>{{ prénom|title }} — {% trans "Héllo" %}</p>
""",
"""
<span class="language-html"><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span></span><span class="hljs-template-variable">{{ prénom|<span class="hljs-name">title</span> }}</span><span class="language-html"> — </span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">trans</span></span> &quot;Héllo&quot; %}</span><span class="language-html"><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span></span>
""");
    }

    [Fact]
    public void Numbers()
    {
        AssertHighlighter("django",
"""
{% widthratio this_value max_value 100 %} {{ 42 }} {{ value|add:3 }} {{ x|slice:":2" }}
""",
"""
<span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">widthratio</span></span> this_value max_value 100 %}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ 42 }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ value|<span class="hljs-name">add</span>:3 }}</span><span class="language-html"> </span><span class="hljs-template-variable">{{ x|<span class="hljs-name">slice</span>:<span class="hljs-string">&quot;:2&quot;</span> }}</span>
""");
    }

    [Fact]
    public void Jinja2Alias()
    {
        AssertHighlighter("jinja2",
"""
{% for x in items %}{% regroup a by b as c %}{% with a as b %}{% endwith %}{% endfor %}
""",
"""
<span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">for</span></span> x <span class="hljs-keyword">in</span> items %}</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">regroup</span></span> a <span class="hljs-keyword">by</span> b <span class="hljs-keyword">as</span> c %}</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">with</span></span> a <span class="hljs-keyword">as</span> b %}</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">endwith</span></span> %}</span><span class="hljs-template-tag">{% <span class="hljs-name"><span class="hljs-name">endfor</span></span> %}</span>
""");
    }

    [Fact]
    public void J2Alias()
    {
        AssertHighlighter("j2",
"""
{%- if x -%} y {%+ endif +%}
""",
"""
<span class="hljs-template-tag">{%- <span class="hljs-name"><span class="hljs-name">if</span></span> x -%}</span><span class="language-html"> y </span><span class="hljs-template-tag">{%+ <span class="hljs-name"><span class="hljs-name">endif</span></span> +%}</span>
""");
    }
}
