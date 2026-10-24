// Vue 3: the SFC compiler (template, script setup, scoped styles, server rendering code), and server rendering of
// components (reactivity, computed values, slots) with vue/server-renderer
import { parse, compileScript, compileTemplate, compileStyle } from "@vue/compiler-sfc";
import { createSSRApp, ref, computed, h } from "vue";
import { renderToString } from "vue/server-renderer";

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

  const app = createSSRApp({
    props: ["start"],
    setup(props) {
      const count = ref(props.start);
      const double = computed(() => count.value * 2);
      return { count, double, items: ["a", "<b>", "c"] };
    },
    render() {
      const child = { props: ["msg"], render() { return h("em", { class: ["x", { y: true }], style: { color: "red" } }, [this.msg, this.$slots.default?.()]); } };
      return h("section", [h("p", `${this.count} x 2 = ${this.double}`), h("ul", this.items.map(item => h("li", { key: item }, item))), h(child, { msg: "hi" }, { default: () => "slot <content>" })]);
    },
  }, { start: 21 });
  results.push(await renderToString(app));
  return results.join("\n----\n");
};
