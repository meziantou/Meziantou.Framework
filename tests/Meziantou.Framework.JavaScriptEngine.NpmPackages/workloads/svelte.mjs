// The Svelte 5 compiler: components with runes, snippets, each/if/await blocks, scoped styles, compiled for the server
// and the client, with warnings and errors (it uses acorn, magic-string, zimmerframe and locate-character)
import { compile, parse } from "svelte/compiler";

const component = `<script>
    let { items = [], title = "List" } = $props();
    let filter = $state("");
    let visible = $derived(items.filter(item => item.name.includes(filter)));
    $effect(() => { console.log(visible.length); });
    function remove(item) { items = items.filter(other => other !== item); }
</script>

{#snippet row(item, index)}
    <li class:done={item.done} data-index={index}>{index + 1}. {item.name} <button onclick={() => remove(item)}>x</button></li>
{/snippet}

<h1>{title} ({visible.length})</h1>
<input bind:value={filter} placeholder="Filter" />
{#if visible.length === 0}
    <p>Nothing</p>
{:else}
    <ul>
        {#each visible as item, index (item.name)}
            {@render row(item, index)}
        {/each}
    </ul>
{/if}
{#await Promise.resolve(42)}
    <p>waiting</p>
{:then value}
    <p>{value}</p>
{/await}
<img src="a.png">

<style>
    h1 { color: purple; }
    li.done { text-decoration: line-through; }
    .unused { color: red; }
</style>
`;

export default () => {
  const results = [];
  const server = compile(component, { generate: "server", filename: "List.svelte" });
  results.push(server.js.code, server.css?.code ?? "", server.warnings.map(warning => warning.code + ": " + warning.message).join("\n"));
  const client = compile(component, { generate: "client", filename: "List.svelte", dev: false });
  results.push(client.js.code);
  const ast = parse(component, { modern: true });
  results.push(JSON.stringify(ast.fragment.nodes.map(node => node.type)));
  try {
    compile("<div>{#if}</div>", { filename: "Broken.svelte" });
  }
  catch (error) {
    results.push(error.code + ": " + error.message.split("\n")[0]);
  }

  return results.join("\n----\n");
};
