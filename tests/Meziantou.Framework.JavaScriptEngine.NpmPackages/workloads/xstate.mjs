// XState 5: state machines with nested and parallel states, guards, actions, context assignment, delayed transitions
// (the harness timers), invoked promises, spawned actors and the snapshot API
import { setup, createActor, assign, fromPromise, sendTo, fromCallback } from "xstate";

const machine = setup({
  types: {},
  guards: { hasItems: ({ context }) => context.items.length > 0, isValid: ({ event }) => event.value?.length > 2 },
  actions: { log: ({ context, event }, params) => context.log.push(params.message + " " + event.type) },
  actors: {
    fetchData: fromPromise(async ({ input }) => { await Promise.resolve(); if (input.fail) throw new Error("failed"); return ["a", "b", "c"].slice(0, input.count); }),
    ticker: fromCallback(({ sendBack }) => { const id = setInterval(() => sendBack({ type: "TICK" }), 100); return () => clearInterval(id); }),
  },
  delays: { timeout: 500 },
}).createMachine({
  id: "app",
  initial: "idle",
  context: { items: [], log: [], ticks: 0, input: "" },
  states: {
    idle: { on: { LOAD: { target: "loading", actions: { type: "log", params: { message: "loading on" } } } } },
    loading: {
      invoke: {
        src: "fetchData",
        input: ({ event }) => ({ count: event.count, fail: event.fail }),
        onDone: { target: "ready", actions: assign({ items: ({ event }) => event.output }) },
        onError: { target: "failed", actions: assign({ log: ({ context, event }) => [...context.log, "error " + event.error.message] }) },
      },
    },
    failed: { after: { timeout: "idle" } },
    ready: {
      type: "parallel",
      invoke: { src: "ticker" },
      on: { TICK: { actions: assign({ ticks: ({ context }) => context.ticks + 1 }) }, RESET: "#app.idle" },
      states: {
        list: { initial: "unknown", states: { unknown: { always: [{ guard: "hasItems", target: "filled" }, { target: "empty" }] }, filled: {}, empty: {} } },
        form: {
          initial: "editing",
          states: {
            editing: { on: { INPUT: { actions: assign({ input: ({ event }) => event.value }) }, SUBMIT: [{ guard: "isValid", target: "submitted" }, { actions: { type: "log", params: { message: "invalid" } } }] } },
            submitted: { type: "final" },
          },
        },
      },
    },
  },
});

export default async () => {
  const snapshots = [];
  const actor = createActor(machine);
  actor.subscribe(snapshot => snapshots.push(JSON.stringify(snapshot.value) + " ticks=" + snapshot.context.ticks));
  actor.start();
  actor.send({ type: "LOAD", count: 2 });
  await new Promise(resolve => setTimeout(resolve, 10));
  actor.send({ type: "INPUT", value: "ab" });
  actor.send({ type: "SUBMIT" });
  actor.send({ type: "INPUT", value: "abc" });
  actor.send({ type: "SUBMIT" });
  await new Promise(resolve => setTimeout(resolve, 350));
  actor.send({ type: "RESET" });
  actor.send({ type: "LOAD", count: 1, fail: true });
  await new Promise(resolve => setTimeout(resolve, 10));
  const failed = actor.getSnapshot().value;
  await new Promise(resolve => setTimeout(resolve, 600));
  const snapshot = actor.getSnapshot();
  actor.stop();
  return JSON.stringify({ snapshots, failed, final: snapshot.value, context: snapshot.context, status: actor.getSnapshot().status, can: machine.resolveState({ value: "idle", context: snapshot.context }).can({ type: "LOAD" }) }, null, 1);
};
