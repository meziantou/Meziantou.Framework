// Lodash: collections, objects, strings, templates (compiled with Function), memoization, deep operations and chaining
import _ from "lodash";

const users = [
  { user: "barney", age: 36, active: true, tags: ["a", "b"], address: { city: "Paris" } },
  { user: "fred", age: 40, active: false, tags: ["b"], address: { city: "Lyon" } },
  { user: "pebbles", age: 1, active: true, tags: [], address: { city: "Paris" } },
  { user: "barney", age: 18, active: false, tags: ["c"], address: { city: "Nice" } },
];

export default () => {
  const template = _.template("<% _.forEach(users, function(u) { %><li><%- u.user %> (<%= u.age %>)</li><% }); %> ${ title }", { imports: { _ } });
  const memoized = _.memoize(value => value * 2);
  memoized(2);
  memoized(2);
  return JSON.stringify({
    chain: _.chain(users).filter("active").sortBy(["user", "age"]).map(user => _.pick(user, ["user", "age"])).value(),
    groupBy: _.groupBy(users, "address.city"),
    countBy: _.countBy(users, user => user.age > 20),
    keyBy: _.mapValues(_.keyBy(users, "age"), "user"),
    orderBy: _.orderBy(users, ["user", "age"], ["asc", "desc"]).map(user => user.user + user.age),
    partition: _.partition(users, "active").map(group => group.length),
    uniq: [_.uniqBy(users, "user").length, _.union([1, 2], [2, 3]), _.intersection([1, 2, 3], [2, 3, 4]), _.xor([1, 2], [2, 3]), _.difference([1, 2, 3], [2])],
    arrays: [_.chunk([1, 2, 3, 4, 5], 2), _.flattenDeep([1, [2, [3, [4]]]]), _.zip(["a", "b"], [1, 2], [true]), _.zipObjectDeep(["a.b[0].c", "a.b[1].d"], [1, 2]), _.range(0, 20, 5), _.sortedIndex([10, 20, 30], 25), _.takeRightWhile([1, 2, 3, 4], value => value > 2)],
    objects: [_.merge({ a: [{ b: 1 }], c: { d: 1 } }, { a: [{ e: 2 }], c: { f: 2 } }), _.defaultsDeep({ a: { b: 1 } }, { a: { b: 2, c: 3 } }), _.get(users, "[0].address.city"), _.set({}, "a[0].b.c", 4), _.omitBy({ a: 1, b: null, c: 0 }, _.isNil), _.invert({ a: 1, b: 2 }), _.toPairs({ x: 1 }), _.isEqual({ a: [1, { b: 2 }] }, { a: [1, { b: 2 }] })],
    cloneDeep: _.cloneDeep({ date: new Date(Date.UTC(2020, 0, 1)), regex: /a/g, map: [1, { b: 2 }] }),
    strings: [_.camelCase("Foo Bar-baz_qux"), _.kebabCase("fooBarBaz"), _.snakeCase("Foo Bar"), _.startCase("--foo-bar--"), _.capitalize("FRED"), _.deburr("déjà vu Ærøskøbing"), _.escape("<a href='x'>&</a>"), _.unescape("&lt;b&gt;"), _.pad("abc", 8, "_-"), _.truncate("hi-diddly-ho there, neighborino", { length: 24, separator: /,? +/ }), _.words("fred, barney, & pebbles"), _.template("Hello <%= name %>!")({ name: "<b>" })],
    template: template({ users, title: "users" }),
    numbers: [_.sum([4, 2, 8, 6]), _.meanBy(users, "age"), _.round(4.006, 2), _.clamp(-10, -5, 5), _.inRange(3, 2, 4), _.maxBy(users, "age").user],
    functions: [memoized.cache.size, _.flow([value => value + 1, value => value * 3])(1), _.curry((a, b, c) => [a, b, c])(1)(2)(3), _.partial((a, b) => a + b, "x")("y"), _.overArgs((a, b) => [a, b], [Math.abs, String])(-1, 2)],
  });
};
