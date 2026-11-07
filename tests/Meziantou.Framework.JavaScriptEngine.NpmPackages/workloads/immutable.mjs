// Immutable.js: persistent List, Map, OrderedMap, Set, Record, Seq (lazy), deep operations, equality and hashing
import * as Immutable from "immutable";

const { List, Map, OrderedMap, Set, Record, Seq, Range, fromJS, is } = Immutable;

export default () => {
  const Person = Record({ name: "", age: 0, tags: List() }, "Person");
  const people = List([new Person({ name: "Ada", age: 36, tags: List(["math"]) }), new Person({ name: "Alan", age: 41 }), new Person({ name: "Grace", age: 85, tags: List(["navy", "cobol"]) })]);
  const nested = fromJS({ a: { b: [1, 2, { c: 3 }] }, d: "x" });
  const big = Range(0, 20000).toList();
  const updated = big.set(10000, -1).push(20000).delete(0);
  const lazy = [];
  const sequence = Seq(Range(1, Infinity)).filter(value => { lazy.push(value); return value % 3 === 0; }).map(value => value * value).take(4);
  return JSON.stringify({
    people: people.sortBy(person => -person.age).map(person => person.name + ":" + person.tags.join("/")).toArray(),
    grouped: people.groupBy(person => person.age > 40).map(group => group.map(person => person.name)).toJS(),
    nested: [nested.getIn(["a", "b", 2, "c"]), nested.setIn(["a", "b", 0], 9).toJS(), nested.updateIn(["a", "b"], list => list.push(4)).getIn(["a", "b"]).size, nested.mergeDeep({ a: { e: 1 } }).toJS(), nested.deleteIn(["a", "b", 1]).toJS()],
    list: [updated.size, updated.get(9999), updated.last(), updated.first(), big.size, big.get(10000), List([3, 1, 2]).sort().toArray(), List([1, 2, 3]).interpose(0).toArray(), List.of(1, 2).concat([3], List([4])).reverse().toArray()],
    map: [Map({ a: 1, b: 2 }).merge({ b: 3, c: 4 }).toObject(), Map([[{ key: 1 }, "object key"]]).size, OrderedMap({ z: 1, a: 2 }).set("m", 3).keySeq().toArray(), Map({ a: 1 }).withMutations(map => map.set("b", 2).set("c", 3)).toJS()],
    set: [Set([1, 2, 3]).union([3, 4]).toArray(), Set([1, 2, 3]).intersect([2, 3, 4]).toArray(), Set([1, 2, 3]).subtract([1]).toArray(), Set.of(List([1]), List([1])).size],
    equality: [is(Map({ a: List([1]) }), Map({ a: List([1]) })), is(nested, fromJS(nested.toJS())), Map({ a: 1 }).hashCode() === Map({ a: 1 }).hashCode(), Map({ a: 1 }).equals(Map({ a: 2 }))],
    sequence: [sequence.toArray(), lazy.length],
    string: [String(people.get(0)), String(Map({ a: List([1]) })), String(Range(1, 10, 3))],
  });
};
