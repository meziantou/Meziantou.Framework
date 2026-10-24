// Redux Toolkit: configureStore with the default middleware (immutability and serializability checks are off in
// production), createSlice with Immer, createAsyncThunk, createEntityAdapter, createSelector (reselect) and a custom
// middleware. The listener middleware is not used: it needs AbortController.
import { configureStore, createSlice, createAsyncThunk, createEntityAdapter, createSelector, combineSlices, nanoid } from "@reduxjs/toolkit";

const todosAdapter = createEntityAdapter({ sortComparer: (a, b) => a.title.localeCompare(b.title) });

const fetchTodos = createAsyncThunk("todos/fetch", async (count, { rejectWithValue }) => {
  if (count < 0)
    return rejectWithValue("negative count");
  return Array.from({ length: count }, (_, index) => ({ id: "t" + index, title: "Fetched " + (count - index), done: index % 2 === 0 }));
});

const todos = createSlice({
  name: "todos",
  initialState: todosAdapter.getInitialState({ status: "idle", error: null }),
  reducers: {
    added: { reducer: todosAdapter.addOne, prepare: title => ({ payload: { id: title.toLowerCase(), title, done: false } }) },
    toggled(state, action) { const todo = state.entities[action.payload]; todo.done = !todo.done; },
    removed: todosAdapter.removeOne,
  },
  extraReducers: builder => builder
    .addCase(fetchTodos.pending, state => { state.status = "loading"; })
    .addCase(fetchTodos.fulfilled, (state, action) => { state.status = "done"; todosAdapter.upsertMany(state, action.payload); })
    .addCase(fetchTodos.rejected, (state, action) => { state.status = "failed"; state.error = action.payload; }),
  selectors: { selectStatus: state => state.status },
});

const counter = createSlice({ name: "counter", initialState: { value: 0 }, reducers: { incrementBy: (state, action) => { state.value += action.payload; } } });

export default async () => {
  const log = [];
  const listener = api => next => action => {
    const result = next(action);
    if (todos.actions.toggled.match(action))
      log.push("toggled " + action.payload + " -> " + api.getState().todos.entities[action.payload].done);
    return result;
  };
  const store = configureStore({ reducer: combineSlices(todos, counter), middleware: getDefault => getDefault().prepend(listener) });
  store.subscribe(() => log.push("state " + JSON.stringify(store.getState().todos.ids)));

  const selectors = todosAdapter.getSelectors(state => state.todos);
  const selectDone = createSelector([selectors.selectAll], all => all.filter(todo => todo.done).map(todo => todo.title));
  store.dispatch(todos.actions.added("Write"));
  store.dispatch(todos.actions.added("Read"));
  store.dispatch(todos.actions.toggled("read"));
  store.dispatch(counter.actions.incrementBy(5));
  const fulfilled = await store.dispatch(fetchTodos(3));
  const rejected = await store.dispatch(fetchTodos(-1));
  store.dispatch(todos.actions.removed("write"));
  const first = selectDone(store.getState());
  return JSON.stringify({
    log,
    state: store.getState(),
    done: first,
    memoized: selectDone(store.getState()) === first,
    total: selectors.selectTotal(store.getState()),
    status: todos.selectors.selectStatus(store.getState()),
    thunks: [fulfilled.type, fulfilled.meta.requestStatus, rejected.type, rejected.payload],
    nanoid: nanoid().length,
  }, null, 1);
};
