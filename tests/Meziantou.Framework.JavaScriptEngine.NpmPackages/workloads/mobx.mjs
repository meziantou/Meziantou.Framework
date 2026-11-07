// MobX (its production build: the default ES module reads process.env.NODE_ENV, which a bundler replaces): observables (proxies), computed values, reactions, autoruns, actions, transactions, observable collections,
// interception and spying
import { makeAutoObservable, observable, computed, autorun, reaction, when, runInAction, action, configure, toJS, intercept, observe, spy } from "/node_modules/mobx/dist/mobx.esm.production.min.js";

configure({ enforceActions: "never" });

class Store {
  todos = [];
  filter = "all";
  constructor() { makeAutoObservable(this); }
  get visible() { return this.todos.filter(todo => this.filter === "all" || todo.done === (this.filter === "done")); }
  get summary() { return `${this.todos.filter(todo => todo.done).length}/${this.todos.length}`; }
  add(title) { this.todos.push({ title, done: false }); }
  toggle(index) { this.todos[index].done = !this.todos[index].done; }
}

export default async () => {
  const log = [];
  const events = [];
  const stopSpy = spy(event => { if (event.type === "action") events.push("action " + event.name); });
  const store = new Store();
  const stopAutorun = autorun(() => log.push("summary " + store.summary));
  const stopReaction = reaction(() => store.visible.map(todo => todo.title), titles => log.push("visible " + titles.join(",")), { equals: (a, b) => a.join() === b.join() });
  store.add("a");
  store.add("b");
  store.toggle(0);
  store.filter = "done";
  runInAction(() => { store.add("c"); store.toggle(2); store.toggle(1); });
  store.filter = "all";

  const map = observable.map({ x: 1 });
  const set = observable.set([1]);
  const box = observable.box(10);
  const doubled = computed(() => box.get() * 2);
  observe(map, change => log.push(`map ${change.type} ${change.name} ${change.oldValue} -> ${change.newValue}`));
  intercept(box, change => { if (change.newValue < 0) return null; change.newValue = Math.round(change.newValue); return change; });
  autorun(() => log.push(`box ${box.get()} doubled ${doubled.get()} set ${[...set].join("|")} map ${map.get("x")}`));
  map.set("x", 2);
  map.set("y", 3);
  set.add(2);
  box.set(4.6);
  box.set(-1);
  action("batch", () => { box.set(7); set.delete(1); })();

  const ready = when(() => store.todos.length > 3);
  store.add("d");
  await ready;
  log.push("when resolved");
  stopAutorun();
  stopReaction();
  stopSpy();
  store.add("ignored");
  log.push(JSON.stringify(toJS(store.todos)));
  return log.concat(events).join("\n");
};
