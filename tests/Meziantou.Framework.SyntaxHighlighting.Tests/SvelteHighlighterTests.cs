namespace Meziantou.Framework.SyntaxHighlighting.Tests;

public class SvelteHighlighterTests
{
    [Fact]
    public void Component()
    {
        AssertHighlighter("svelte",
"""
<script>
  let count = $state(0);
  let doubled = $derived(count * 2);

  function increment() {
    count += 1;
  }
</script>

<button onclick={increment}>
  Clicked {count} {count === 1 ? 'time' : 'times'}
</button>
<p>{doubled}</p>

<style>
  button { color: #ff3e00; }
</style>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">script</span>&gt;</span><span class="language-javascript">
  <span class="hljs-keyword">let</span> count = </span><span class="hljs-built_in">$state</span><span class="language-javascript">(<span class="hljs-number">0</span>);
  <span class="hljs-keyword">let</span> doubled = </span><span class="hljs-built_in">$derived</span><span class="language-javascript">(count * <span class="hljs-number">2</span>);

  <span class="hljs-keyword">function</span> <span class="hljs-title function_">increment</span>(<span class="hljs-params"></span>) {
    count += <span class="hljs-number">1</span>;
  }
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>

<span class="hljs-tag">&lt;<span class="hljs-name">button</span> <span class="hljs-attr">onclick</span>=<span class="hljs-template-variable">{<span class="language-javascript">increment</span>}</span>&gt;</span>
  Clicked <span class="hljs-template-variable">{<span class="language-javascript">count</span>}</span> <span class="hljs-template-variable">{<span class="language-javascript">count === <span class="hljs-number">1</span> ? <span class="hljs-string">&#x27;time&#x27;</span> : <span class="hljs-string">&#x27;times&#x27;</span></span>}</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-template-variable">{<span class="language-javascript">doubled</span>}</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>

<span class="hljs-tag">&lt;<span class="hljs-name">style</span>&gt;</span><span class="language-css">
  <span class="hljs-selector-tag">button</span> { <span class="hljs-attribute">color</span>: <span class="hljs-number">#ff3e00</span>; }
</span><span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>
""");
    }

    [Fact]
    public void RunesInTypeScript()
    {
        AssertHighlighter("svelte",
"""
<script lang="ts">
  interface Props { name: string; items?: string[] }
  let { name, items = [] }: Props = $props();
  let total = $derived.by(() => items.length);
  let raw = $state.raw({ a: 1 });
  $effect(() => {
    console.log(name, $state.snapshot(raw));
  });
  // $state(0) in a comment, and a store: $count
</script>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">script</span> <span class="hljs-attr">lang</span>=<span class="hljs-string">&quot;ts&quot;</span>&gt;</span><span class="language-typescript">
  <span class="hljs-keyword">interface</span> <span class="hljs-title class_">Props</span> { <span class="hljs-attr">name</span>: <span class="hljs-built_in">string</span>; <span class="hljs-attr">items</span>?: <span class="hljs-built_in">string</span>[] }
  <span class="hljs-keyword">let</span> { name, items = [] }: <span class="hljs-title class_">Props</span> = </span><span class="hljs-built_in">$props</span><span class="language-typescript">();
  <span class="hljs-keyword">let</span> total = </span><span class="hljs-built_in">$derived.by</span><span class="language-typescript">(<span class="hljs-function">() =&gt;</span> items.<span class="hljs-property">length</span>);
  <span class="hljs-keyword">let</span> raw = </span><span class="hljs-built_in">$state.raw</span><span class="language-typescript">({ <span class="hljs-attr">a</span>: <span class="hljs-number">1</span> });
  </span><span class="hljs-built_in">$effect</span><span class="language-typescript">(<span class="hljs-function">() =&gt;</span> {
    <span class="hljs-variable language_">console</span>.<span class="hljs-title function_">log</span>(name, </span><span class="hljs-built_in">$state.snapshot</span><span class="language-typescript">(raw));
  });
  <span class="hljs-comment">// $state(0) in a comment, and a store: $count</span>
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
""");
    }

    [Fact]
    public void ModuleScriptAndSpecialElements()
    {
        AssertHighlighter("svelte",
"""
<script context="module">
  export const prerender = true;
</script>
<script module>
  export function f() {}
</script>
<svelte:head><title>{title}</title></svelte:head>
<svelte:window on:keydown={onKey} />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">script</span> <span class="hljs-attr">context</span>=<span class="hljs-string">&quot;module&quot;</span>&gt;</span><span class="language-javascript">
  <span class="hljs-keyword">export</span> <span class="hljs-keyword">const</span> prerender = <span class="hljs-literal">true</span>;
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">script</span> <span class="hljs-attr">module</span>&gt;</span><span class="language-javascript">
  <span class="hljs-keyword">export</span> <span class="hljs-keyword">function</span> <span class="hljs-title function_">f</span>(<span class="hljs-params"></span>) {}
</span><span class="hljs-tag">&lt;/<span class="hljs-name">script</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">svelte:head</span>&gt;</span><span class="hljs-tag">&lt;<span class="hljs-name">title</span>&gt;</span><span class="hljs-template-variable">{<span class="language-javascript">title</span>}</span><span class="hljs-tag">&lt;/<span class="hljs-name">title</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">svelte:head</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">svelte:window</span> <span class="hljs-attr">on:keydown</span>=<span class="hljs-template-variable">{<span class="language-javascript">onKey</span>}</span> /&gt;</span>
""");
    }

    [Fact]
    public void Blocks()
    {
        AssertHighlighter("svelte",
"""
{#if user.loggedIn}
  <button on:click={toggle}>Log out</button>
{:else if user.pending}
  <p>Wait…</p>
{:else}
  <button on:click={toggle}>Log in</button>
{/if}

{#each items as item, i (item.id)}
  <li>{i}: {item.name}</li>
{:else}
  <p>No items</p>
{/each}

{#await promise}
  <p>loading</p>
{:then value}
  <p>{value}</p>
{:catch error}
  <p style="color: red">{error.message}</p>
{/await}

{#await fetch(url).then((r) => r.json()) then data}{data}{/await}

{#key value}<div transition:fade>{value}</div>{/key}
""",
"""
<span class="hljs-template-tag">{#<span class="hljs-keyword">if</span><span class="language-javascript"> user.<span class="hljs-property">loggedIn</span></span>}</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">button</span> <span class="hljs-attr">on:click</span>=<span class="hljs-template-variable">{<span class="language-javascript">toggle</span>}</span>&gt;</span>Log out<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span>
<span class="hljs-template-tag">{:<span class="hljs-keyword">else</span> <span class="hljs-keyword">if</span><span class="language-javascript"> user.<span class="hljs-property">pending</span></span>}</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>Wait…<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
<span class="hljs-template-tag">{:<span class="hljs-keyword">else</span>}</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">button</span> <span class="hljs-attr">on:click</span>=<span class="hljs-template-variable">{<span class="language-javascript">toggle</span>}</span>&gt;</span>Log in<span class="hljs-tag">&lt;/<span class="hljs-name">button</span>&gt;</span>
<span class="hljs-template-tag">{/<span class="hljs-keyword">if</span>}</span>

<span class="hljs-template-tag">{#<span class="hljs-keyword">each</span><span class="language-javascript"> items </span><span class="hljs-keyword">as</span><span class="language-javascript"> item, <span class="hljs-title function_">i</span> (item.<span class="hljs-property">id</span>)</span>}</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">li</span>&gt;</span><span class="hljs-template-variable">{<span class="language-javascript">i</span>}</span>: <span class="hljs-template-variable">{<span class="language-javascript">item.<span class="hljs-property">name</span></span>}</span><span class="hljs-tag">&lt;/<span class="hljs-name">li</span>&gt;</span>
<span class="hljs-template-tag">{:<span class="hljs-keyword">else</span>}</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>No items<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
<span class="hljs-template-tag">{/<span class="hljs-keyword">each</span>}</span>

<span class="hljs-template-tag">{#<span class="hljs-keyword">await</span><span class="language-javascript"> promise</span>}</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span>loading<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
<span class="hljs-template-tag">{:<span class="hljs-keyword">then</span><span class="language-javascript"> value</span>}</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-template-variable">{<span class="language-javascript">value</span>}</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
<span class="hljs-template-tag">{:<span class="hljs-keyword">catch</span><span class="language-javascript"> error</span>}</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span> <span class="hljs-attr">style</span>=<span class="hljs-string">&quot;color: red&quot;</span>&gt;</span><span class="hljs-template-variable">{<span class="language-javascript">error.<span class="hljs-property">message</span></span>}</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
<span class="hljs-template-tag">{/<span class="hljs-keyword">await</span>}</span>

<span class="hljs-template-tag">{#<span class="hljs-keyword">await</span><span class="language-javascript"> <span class="hljs-title function_">fetch</span>(url).<span class="hljs-title function_">then</span>(<span class="hljs-function">(<span class="hljs-params">r</span>) =&gt;</span> r.<span class="hljs-title function_">json</span>()) </span><span class="hljs-keyword">then</span><span class="language-javascript"> data</span>}</span><span class="hljs-template-variable">{<span class="language-javascript">data</span>}</span><span class="hljs-template-tag">{/<span class="hljs-keyword">await</span>}</span>

<span class="hljs-template-tag">{#<span class="hljs-keyword">key</span><span class="language-javascript"> value</span>}</span><span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">transition:fade</span>&gt;</span><span class="hljs-template-variable">{<span class="language-javascript">value</span>}</span><span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span><span class="hljs-template-tag">{/<span class="hljs-keyword">key</span>}</span>
""");
    }

    [Fact]
    public void Tags()
    {
        AssertHighlighter("svelte",
"""
{@html post.content}
{@const area = box.width * box.height}
{@debug user, count}
{#snippet row(item)}
  <td>{item.name}</td>
{/snippet}
{@render row({ name: 'x' })}
""",
"""
<span class="hljs-template-tag">{@<span class="hljs-keyword">html</span><span class="language-javascript"> post.<span class="hljs-property">content</span></span>}</span>
<span class="hljs-template-tag">{@<span class="hljs-keyword">const</span><span class="language-javascript"> area = box.<span class="hljs-property">width</span> * box.<span class="hljs-property">height</span></span>}</span>
<span class="hljs-template-tag">{@<span class="hljs-keyword">debug</span><span class="language-javascript"> user, count</span>}</span>
<span class="hljs-template-tag">{#<span class="hljs-keyword">snippet</span><span class="language-javascript"> <span class="hljs-title function_">row</span>(item)</span>}</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">td</span>&gt;</span><span class="hljs-template-variable">{<span class="language-javascript">item.<span class="hljs-property">name</span></span>}</span><span class="hljs-tag">&lt;/<span class="hljs-name">td</span>&gt;</span>
<span class="hljs-template-tag">{/<span class="hljs-keyword">snippet</span>}</span>
<span class="hljs-template-tag">{@<span class="hljs-keyword">render</span><span class="language-javascript"> <span class="hljs-title function_">row</span>({ <span class="hljs-attr">name</span>: <span class="hljs-string">&#x27;x&#x27;</span> })</span>}</span>
""");
    }

    [Fact]
    public void Directives()
    {
        AssertHighlighter("svelte",
"""
<input bind:value={name} on:input|preventDefault={handle} class:active={isActive} use:tooltip={{ text: 'hi' }} />
<div transition:fly={{ y: 200, duration: 2000 }} in:fade out:slide|local animate:flip style:color={color}></div>
<Comp {...props} {name} bind:this={el} let:item />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">input</span> <span class="hljs-attr">bind:value</span>=<span class="hljs-template-variable">{<span class="language-javascript">name</span>}</span> <span class="hljs-attr">on:input|preventDefault</span>=<span class="hljs-template-variable">{<span class="language-javascript">handle</span>}</span> <span class="hljs-attr">class:active</span>=<span class="hljs-template-variable">{<span class="language-javascript">isActive</span>}</span> <span class="hljs-attr">use:tooltip</span>=<span class="hljs-template-variable">{<span class="language-javascript">{ <span class="hljs-attr">text</span>: <span class="hljs-string">&#x27;hi&#x27;</span> }</span>}</span> /&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">transition:fly</span>=<span class="hljs-template-variable">{<span class="language-javascript">{ <span class="hljs-attr">y</span>: <span class="hljs-number">200</span>, <span class="hljs-attr">duration</span>: <span class="hljs-number">2000</span> }</span>}</span> <span class="hljs-attr">in:fade</span> <span class="hljs-attr">out:slide|local</span> <span class="hljs-attr">animate:flip</span> <span class="hljs-attr">style:color</span>=<span class="hljs-template-variable">{<span class="language-javascript">color</span>}</span>&gt;</span><span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">Comp</span> <span class="hljs-template-variable">{<span class="language-javascript">...props</span>}</span> <span class="hljs-template-variable">{<span class="language-javascript">name</span>}</span> <span class="hljs-attr">bind:this</span>=<span class="hljs-template-variable">{<span class="language-javascript">el</span>}</span> <span class="hljs-attr">let:item</span> /&gt;</span>
""");
    }

    [Fact]
    public void AttributeValues()
    {
        AssertHighlighter("svelte",
"""
<a href="/users/{user.id}" class='btn {active ? "on" : ""}' title={title}>x</a>
<img src={src} alt="{name} dances." />
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">a</span> <span class="hljs-attr">href</span>=<span class="hljs-string">&quot;/users/<span class="hljs-template-variable">{<span class="language-javascript">user.<span class="hljs-property">id</span></span>}</span>&quot;</span> <span class="hljs-attr">class</span>=<span class="hljs-string">&#x27;btn <span class="hljs-template-variable">{<span class="language-javascript">active ? <span class="hljs-string">&quot;on&quot;</span> : <span class="hljs-string">&quot;&quot;</span></span>}</span>&#x27;</span> <span class="hljs-attr">title</span>=<span class="hljs-template-variable">{<span class="language-javascript">title</span>}</span>&gt;</span>x<span class="hljs-tag">&lt;/<span class="hljs-name">a</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">img</span> <span class="hljs-attr">src</span>=<span class="hljs-template-variable">{<span class="language-javascript">src</span>}</span> <span class="hljs-attr">alt</span>=<span class="hljs-string">&quot;<span class="hljs-template-variable">{<span class="language-javascript">name</span>}</span> dances.&quot;</span> /&gt;</span>
""");
    }

    [Fact]
    public void NestedBracesAndStrings()
    {
        AssertHighlighter("svelte",
"""
<p>{JSON.stringify({ a: { b: "}" } })}</p>
<p>{`template ${x} }`}</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-template-variable">{<span class="language-javascript"><span class="hljs-title class_">JSON</span>.<span class="hljs-title function_">stringify</span>({ <span class="hljs-attr">a</span>: { <span class="hljs-attr">b</span>: <span class="hljs-string">&quot;}&quot;</span> } })</span>}</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-template-variable">{<span class="language-javascript"><span class="hljs-string">`template <span class="hljs-subst">${x}</span> }`</span></span>}</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }

    [Fact]
    public void UnterminatedExpression()
    {
        AssertHighlighter("svelte",
"""
<div>
  <p>{oops</p>
  <p>{ok}</p>
</div>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">div</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-template-variable">{<span class="language-javascript">oops</span></span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
  <span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-template-variable">{<span class="language-javascript">ok</span>}</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
<span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
""");
    }

    [Fact]
    public void EmptyExpressionAndBlocks()
    {
        AssertHighlighter("svelte",
"""
<p>{}</p>{:else}{/if}
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span>&gt;</span><span class="hljs-template-variable">{}</span><span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span><span class="hljs-template-tag">{:<span class="hljs-keyword">else</span>}</span><span class="hljs-template-tag">{/<span class="hljs-keyword">if</span>}</span>
""");
    }

    [Fact]
    public void StyleScss()
    {
        AssertHighlighter("svelte",
"""
<style lang="scss">
  $c: red;
  :global(body) { .a { color: $c; } }
</style>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">style</span> <span class="hljs-attr">lang</span>=<span class="hljs-string">&quot;scss&quot;</span>&gt;</span><span class="language-scss">
  <span class="hljs-variable">$c</span>: red;
  :<span class="hljs-built_in">global</span>(body) { <span class="hljs-selector-class">.a</span> { <span class="hljs-attribute">color</span>: <span class="hljs-variable">$c</span>; } }
</span><span class="hljs-tag">&lt;/<span class="hljs-name">style</span>&gt;</span>
""");
    }

    [Fact]
    public void Comments()
    {
        AssertHighlighter("svelte",
"""
<!-- svelte-ignore a11y-click-events-have-key-events -->
<div on:click={() => (open = !open)}>&times;</div>
""",
"""
<span class="hljs-comment">&lt;!-- svelte-ignore a11y-click-events-have-key-events --&gt;</span>
<span class="hljs-tag">&lt;<span class="hljs-name">div</span> <span class="hljs-attr">on:click</span>=<span class="hljs-template-variable">{<span class="language-javascript">() =&gt; (open = !open)</span>}</span>&gt;</span><span class="hljs-symbol">&amp;times;</span><span class="hljs-tag">&lt;/<span class="hljs-name">div</span>&gt;</span>
""");
    }

    [Fact]
    public void Unicode()
    {
        AssertHighlighter("svelte",
"""
<p title="héllo {名前}">{名前} — ünïcode</p>
""",
"""
<span class="hljs-tag">&lt;<span class="hljs-name">p</span> <span class="hljs-attr">title</span>=<span class="hljs-string">&quot;héllo <span class="hljs-template-variable">{<span class="language-javascript">名前</span>}</span>&quot;</span>&gt;</span><span class="hljs-template-variable">{<span class="language-javascript">名前</span>}</span> — ünïcode<span class="hljs-tag">&lt;/<span class="hljs-name">p</span>&gt;</span>
""");
    }
}
