// Vue 3: the SFC compiler (template, script setup, scoped styles, server rendering code), and an application mounted in
// the document (the browser build of Vue, with the template compiler: the default ES module of the package reads
// process.env.NODE_ENV, which a bundler replaces)
import { parse, compileScript, compileTemplate, compileStyle } from "@vue/compiler-sfc";
import { createApp, ref, computed, nextTick } from "/node_modules/vue/dist/vue.esm-browser.prod.js";

const sfc = `<template>
  <div :class="{ active: isActive }" @click="toggle">
    <h1>{{ title.toUpperCase() }}</h1>
    <ul v-if="items.length"><li v-for="(item, index) in items" :key="item.id">{{ index }}: {{ item.name }}</li></ul>
    <p v-else>Empty</p>
    <slot name="footer" :count="items.length">Default footer</slot>
    <input v-model="query" />
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from "vue";
const props = defineProps<{ title: string; items: { id: number; name: string }[] }>();
const emit = defineEmits<{ (e: "toggle", value: boolean): void }>();
const isActive = ref(false);
const query = ref("");
const count = computed(() => props.items.length);
function toggle() { isActive.value = !isActive.value; emit("toggle", isActive.value); }
</script>

<style scoped>
.active h1 { color: v-bind(color); }
li + li { margin-top: 4px; }
</style>
`;

export default async () => {
  const results = [];
  const { descriptor, errors } = parse(sfc, { filename: "List.vue" });
  results.push(String(errors.length));
  const script = compileScript(descriptor, { id: "data-v-123", inlineTemplate: false });
  results.push(script.content);
  results.push(compileTemplate({ source: descriptor.template.content, filename: "List.vue", id: "data-v-123", scoped: true, compilerOptions: { bindingMetadata: script.bindings } }).code);
  results.push(compileStyle({ source: descriptor.styles[0].content, filename: "List.vue", id: "data-v-123", scoped: true }).code);
  results.push(compileTemplate({ source: descriptor.template.content, filename: "List.vue", id: "data-v-123", ssr: true, ssrCssVars: [] }).code);

  // An application mounted in the document: the templates are compiled at run time (new Function), and the updates of
  // the reactive state patch the DOM
  const container = document.createElement("div");
  document.body.append(container);
  const Child = { props: ["msg"], template: `<em :class="['x', { y: true }]" :style="{ color: 'red' }">{{ msg }} <slot></slot></em>` };
  const app = createApp({
    components: { Child },
    setup() {
      const count = ref(21);
      const items = ref(["a", "<b>", "c"]);
      const double = computed(() => count.value * 2);
      const filter = ref("");
      const visible = computed(() => items.value.filter(item => item.includes(filter.value)));
      return { count, double, items, filter, visible };
    },
    template: `<section><p :data-count="count">{{ count }} x 2 = {{ double }}</p><input v-model="filter"><ul><li v-for="item in visible" :key="item">{{ item }}</li></ul><Child msg="hi">slot &lt;content&gt;</Child><p v-if="count > 21">big</p></section>`,
  });
  const instance = app.mount(container);
  results.push(container.innerHTML);
  instance.count++;
  instance.items.push("d");
  instance.filter = "d";
  await nextTick();
  results.push(container.innerHTML);
  app.unmount();
  results.push(JSON.stringify(container.innerHTML));
  return results.join("\n----\n");
};
