// Immer: structural sharing with proxies (produce), patches, inverse patches, Map and Set drafts, frozen results,
// current and original
import { produce, produceWithPatches, applyPatches, enablePatches, enableMapSet, current, original, freeze, isDraft } from "immer";

enablePatches();
enableMapSet();

const base = {
  todos: [{ id: 1, title: "Learn", done: false, tags: ["a"] }, { id: 2, title: "Write", done: true, tags: [] }],
  users: new Map([["alice", { age: 30 }]]),
  labels: new Set(["x"]),
  meta: { version: 1, nested: { deep: { value: 1 } } },
};

export default () => {
  const log = [];
  const next = produce(base, draft => {
    draft.todos[0].done = true;
    draft.todos[0].tags.push("b");
    draft.todos.push({ id: 3, title: "Ship", done: false, tags: [] });
    draft.todos.splice(1, 1);
    draft.users.get("alice").age++;
    draft.users.set("bob", { age: 25 });
    draft.labels.add("y");
    draft.labels.delete("x");
    draft.meta.version = 2;
    log.push(isDraft(draft.todos), JSON.stringify(current(draft.todos).map(todo => todo.id)), original(draft.todos).length);
  });

  const [patched, patches, inversePatches] = produceWithPatches(base, draft => {
    draft.meta.nested.deep.value = 42;
    draft.todos[1].title = "Rewrite";
    delete draft.meta.version;
    draft.todos.unshift({ id: 0 });
  });

  const replaced = produce(base, () => ({ replaced: true }));
  const unchanged = produce(base, draft => { draft.meta.version = 1; });
  const curried = produce((draft, amount) => { draft.count += amount; });
  return JSON.stringify({
    next: { ...next, users: [...next.users], labels: [...next.labels] },
    sharing: [next.meta.nested === base.meta.nested, next.todos[0] === base.todos[0], unchanged === base, Object.isFrozen(next.todos[0]), Object.isFrozen(base.todos)],
    log,
    patches,
    inversePatches,
    roundTrip: JSON.stringify(applyPatches(patched, inversePatches).todos) === JSON.stringify(base.todos),
    replaced,
    curried: curried({ count: 1 }, 41),
    frozen: Object.isFrozen(freeze({ a: { b: 1 } }, true).a),
  });
};
