namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class VueHighlighterTests
{
    [Fact]
    public void SingleFileComponent()
    {
        AssertHighlighter("vue",
"""
<template>
  <div id="app" :class="{ active: isActive }">
    <h1>{{ title }}</h1>
    <button @click="count++">Count is: {{ count }}</button>
  </div>
</template>

<script>
export default {
  data() {
    return { count: 0, title: 'Hello' }
  }
}
</script>

<style scoped>
.active { color: red; }
</style>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">template</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">id</span>=<span class="hljs-string">&quot;app&quot;</span> <span class="hljs-attr">:class</span>=&quot;<span class="language-javascript">{ <span class="hljs-attr">active</span>: isActive }</span>&quot;&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">h1</span>&gt;</span><span class="hljs-template-variable">{{<span class="language-javascript"> title </span>}}</span><span class="hljs-tag">&lt;/<span class="hljs-name">h1</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">button</span> <span class="hljs-attr">@click</span>=&quot;<span class="language-javascript">count++</span>&quot;&gt;</span>Count is: <span class="hljs-template-variable">{{<span class="language-javascript"> count </span>}}</span><span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">template</span>&gt;</span>

<span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span><span class="language-javascript">
<span class="hljs-keyword">export</span> <span class="hljs-keyword">default</span> {
  <span class="hljs-title function_">data</span>(<span class="hljs-params"></span>) {
    <span class="hljs-keyword">return</span> { <span class="hljs-attr">count</span>: <span class="hljs-number">0</span>, <span class="hljs-attr">title</span>: <span class="hljs-string">&#x27;Hello&#x27;</span> }
  }
}
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>

<span class="hljs-tag">&lt;<span class="hljs-name">style</span> <span class="hljs-attr">scoped</span>&gt;</span><span class="language-css">
<span class="hljs-selector-class">.active</span> { <span class="hljs-attribute">color</span>: red; }
</span><span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>
""");
    }

    [Fact]
    public void ScriptSetupTypeScript()
    {
        AssertHighlighter("vue",
"""
<script setup lang="ts">
import { ref, computed } from 'vue'

const props = defineProps<{ msg: string }>()
const count = ref<number>(0)
const double = computed(() => count.value * 2)
</script>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">script</span> <span class="hljs-attr">setup</span> <span class="hljs-attr">lang</span>=<span class="hljs-string">&quot;ts&quot;</span>&gt;</span><span class="language-typescript">
<span class="hljs-keyword">import</span> { ref, computed } <span class="hljs-keyword">from</span> <span class="hljs-string">&#x27;vue&#x27;</span>

<span class="hljs-keyword">const</span> props = defineProps&lt;{ <span class="hljs-attr">msg</span>: <span class="hljs-built_in">string</span> }&gt;()
<span class="hljs-keyword">const</span> count = ref&lt;<span class="hljs-built_in">number</span>&gt;(<span class="hljs-number">0</span>)
<span class="hljs-keyword">const</span> double = <span class="hljs-title function_">computed</span>(<span class="hljs-function">() =&gt;</span> count.<span class="hljs-property">value</span> * <span class="hljs-number">2</span>)
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
""");
    }

    [Fact]
    public void ScriptTsx()
    {
        AssertHighlighter("vue",
"""
<script lang='tsx'>
const x: number = 1
</script>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">script</span> <span class="hljs-attr">lang</span>=<span class="hljs-string">&#x27;tsx&#x27;</span>&gt;</span><span class="language-typescript">
<span class="hljs-keyword">const</span> <span class="hljs-attr">x</span>: <span class="hljs-built_in">number</span> = <span class="hljs-number">1</span>
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
""");
    }

    [Fact]
    public void Directives()
    {
        AssertHighlighter("vue",
"""
<ul>
  <li v-for="(item, index) in items" :key="item.id" v-if="item.visible">
    {{ index }} - {{ item.name.toUpperCase() }}
  </li>
  <li v-else>Empty</li>
</ul>
<input v-model.trim="query" @keyup.enter="search(query)" :disabled='loading'>
<MyComponent v-bind:title="post.title" v-on:update:model-value="onUpdate" />
<component :is="view" :[attrName]="value" @[eventName]="handler"></component>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">ul</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">li</span> <span class="hljs-attr">v-for</span>=&quot;<span class="language-javascript">(item, index) <span class="hljs-keyword">in</span> items</span>&quot; <span class="hljs-attr">:key</span>=&quot;<span class="language-javascript">item.<span class="hljs-property">id</span></span>&quot; <span class="hljs-attr">v-if</span>=&quot;<span class="language-javascript">item.<span class="hljs-property">visible</span></span>&quot;&gt;</span>
    <span class="hljs-template-variable">{{<span class="language-javascript"> index </span>}}</span> - <span class="hljs-template-variable">{{<span class="language-javascript"> item.<span class="hljs-property">name</span>.<span class="hljs-title function_">toUpperCase</span>() </span>}}</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">li</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">li</span> <span class="hljs-attr">v-else</span>&gt;</span>Empty<span class="hljs-tag">&lt;/<span class="hljs-name">li</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">ul</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">v-model.trim</span>=&quot;<span class="language-javascript">query</span>&quot; <span class="hljs-attr">@keyup.enter</span>=&quot;<span class="language-javascript"><span class="hljs-title function_">search</span>(query)</span>&quot; <span class="hljs-attr">:disabled</span>=&#x27;<span class="language-javascript">loading</span>&#x27;&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">MyComponent</span> <span class="hljs-attr">v-bind:title</span>=&quot;<span class="language-javascript">post.<span class="hljs-property">title</span></span>&quot; <span class="hljs-attr">v-on:update:model-value</span>=&quot;<span class="language-javascript">onUpdate</span>&quot; /&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">component</span> <span class="hljs-attr">:is</span>=&quot;<span class="language-javascript">view</span>&quot; <span class="hljs-attr">:[attrName]</span>=&quot;<span class="language-javascript">value</span>&quot; <span class="hljs-attr">@[eventName]</span>=&quot;<span class="language-javascript">handler</span>&quot;&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">component</span>&gt;</span>
""");
    }

    [Fact]
    public void Slots()
    {
        AssertHighlighter("vue",
"""
<BaseLayout>
  <template #header="{ title }">
    <h1>{{ title }}</h1>
  </template>
  <template v-slot:default>
    <p>Main</p>
  </template>
</BaseLayout>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">BaseLayout</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">template</span> <span class="hljs-attr">#header</span>=&quot;<span class="language-javascript">{ title }</span>&quot;&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">h1</span>&gt;</span><span class="hljs-template-variable">{{<span class="language-javascript"> title </span>}}</span><span class="hljs-tag">&lt;/<span class="hljs-name">h1</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">template</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">template</span> <span class="hljs-attr">v-slot:default</span>&gt;</span>
    <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Main<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
  <span class="hljs-tag">&lt;/<span class="hljs-name">template</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">BaseLayout</span>&gt;</span>
""");
    }

    [Fact]
    public void PlainAttributes()
    {
        AssertHighlighter("vue",
"""
<a href="/docs" class="link" target=_blank data-v-123>Docs &amp; more</a>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;/docs&quot;</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&quot;link&quot;</span> <span class="hljs-attr">target</span>=<span class="hljs-string">_blank</span> <span class="hljs-attr">data-v-123</span>&gt;</span>Docs <span class="hljs-symbol">&amp;amp;</span> more<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
""");
    }

    [Fact]
    public void StyleLanguages()
    {
        AssertHighlighter("vue",
"""
<style lang="scss" scoped>
$color: red;
.a { .b { color: $color; } }
</style>
<style lang="less">
@c: blue;
.a { color: @c; }
</style>
<style lang="stylus">
.a
  color red
</style>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">style</span> <span class="hljs-attr">lang</span>=<span class="hljs-string">&quot;scss&quot;</span> <span class="hljs-attr">scoped</span>&gt;</span><span class="language-scss">
<span class="hljs-variable">$color</span>: red;
<span class="hljs-selector-class">.a</span> { <span class="hljs-selector-class">.b</span> { <span class="hljs-attribute">color</span>: <span class="hljs-variable">$color</span>; } }
</span><span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">style</span> <span class="hljs-attr">lang</span>=<span class="hljs-string">&quot;less&quot;</span>&gt;</span><span class="language-less">
<span class="hljs-variable">@c:</span> blue;
<span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-variable">@c</span>; }
</span><span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">style</span> <span class="hljs-attr">lang</span>=<span class="hljs-string">&quot;stylus&quot;</span>&gt;</span>
.a
  color red
<span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>
""");
    }

    [Fact]
    public void TemplateInUnsupportedLanguage()
    {
        AssertHighlighter("vue",
"""
<template lang="pug">
div#app
  h1 {{ title }}
  p(v-if="show") Hello
</template>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">template</span> <span class="hljs-attr">lang</span>=<span class="hljs-string">&quot;pug&quot;</span>&gt;</span>
div#app
  h1 {{ title }}
  p(v-if=&quot;show&quot;) Hello
<span class="hljs-tag">&lt;/<span class="hljs-name">template</span>&gt;</span>
""");
    }

    [Fact]
    public void Interpolations()
    {
        AssertHighlighter("vue",
"""
<p>{{}}</p><p>{{
  multi.line
}}</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-template-variable">{{}}</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-template-variable">{{<span class="language-javascript">
  multi.<span class="hljs-property">line</span>
</span>}}</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void UnterminatedInterpolation()
    {
        AssertHighlighter("vue",
"""
<div>
  <p>{{ oops </p>
  <p>{{ ok }}</p>
</div>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">div</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-template-variable">{{<span class="language-javascript"> oops </span></span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-template-variable">{{<span class="language-javascript"> ok </span>}}</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("vue",
"""
<!-- TODO: remove -->
<template>
  <!-- a comment -->
  <p v-html="raw"></p>
</template>
""",
"""
<span class="hljs-comment">&lt;!-- <span class="hljs-doctag">TODO:</span> remove --&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">template</span>&gt;</span>
  <span class="hljs-comment">&lt;!-- a comment --&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span> <span class="hljs-attr">v-html</span>=&quot;<span class="language-javascript">raw</span>&quot;&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">template</span>&gt;</span>
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("vue",
"""
<p :title="'héllo'">{{ 名前 }} — ünïcode</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span> <span class="hljs-attr">:title</span>=&quot;<span class="language-javascript"><span class="hljs-string">&#x27;héllo&#x27;</span></span>&quot;&gt;</span><span class="hljs-template-variable">{{<span class="language-javascript"> 名前 </span>}}</span> — ünïcode<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void CustomBlock()
    {
        AssertHighlighter("vue",
"""
<i18n lang="json">
{ "en": { "hello": "Hello" } }
</i18n>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">i18n</span> <span class="hljs-attr">lang</span>=<span class="hljs-string">&quot;json&quot;</span>&gt;</span>
{ &quot;en&quot;: { &quot;hello&quot;: &quot;Hello&quot; } }
<span class="hljs-tag">&lt;/<span class="hljs-name">i18n</span>&gt;</span>
""");
    }

    [Fact]
    public void ConsecutiveBlocksDoNotShareState()
    {
        AssertHighlighter("vue",
"""
<script>
const tag = "</script>";
</script>
<script setup>
import { ref } from "vue";
</script>
<style lang="scss">
a { content: "</style>";
</style>
<style lang="scss">
a { color: red; }
</style>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span><span class="language-javascript">
<span class="hljs-keyword">const</span> tag = <span class="hljs-string">&quot;</span></span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>&quot;;
<span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">script</span> <span class="hljs-attr">setup</span>&gt;</span><span class="language-javascript">
<span class="hljs-keyword">import</span> { ref } <span class="hljs-keyword">from</span> <span class="hljs-string">&quot;vue&quot;</span>;
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">style</span> <span class="hljs-attr">lang</span>=<span class="hljs-string">&quot;scss&quot;</span>&gt;</span><span class="language-scss">
<span class="hljs-selector-tag">a</span> { <span class="hljs-attribute">content</span>: <span class="hljs-string">&quot;</span></span><span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>&quot;;
<span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">style</span> <span class="hljs-attr">lang</span>=<span class="hljs-string">&quot;scss&quot;</span>&gt;</span><span class="language-scss">
<span class="hljs-selector-tag">a</span> { <span class="hljs-attribute">color</span>: red; }
</span><span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>
""");
    }
}
