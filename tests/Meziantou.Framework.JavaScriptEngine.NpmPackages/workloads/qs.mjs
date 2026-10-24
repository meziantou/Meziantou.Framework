// qs: parsing and stringifying nested query strings (brackets, dots, arrays, encoding, charsets, limits)
import qs from "qs";

export default () => JSON.stringify([
  qs.parse("a[b][c]=1&a[d][]=2&a[d][]=3&e=%C3%A9&f=x+y&g[0]=a&g[2]=b&h"),
  qs.parse("a.b.c=1&a.d=2", { allowDots: true }),
  qs.parse("?a=1&b=2;c=3", { ignoreQueryPrefix: true, delimiter: /[&;]/ }),
  qs.parse("a[1]=b&a[15]=c&a[21]=d", { arrayLimit: 20 }),
  qs.parse("a=%A7", { charset: "iso-8859-1" }),
  qs.parse("utf8=%E2%9C%93&a=%C3%B8", { charsetSentinel: true }),
  qs.parse("a[b][c][d][e][f][g]=x", { depth: 3 }),
  qs.parse("a=true&b=1&c=", { decoder: (value, decoder, charset, type) => type === "value" && value === "true" ? true : decoder(value) }),
  qs.stringify({ a: { b: { c: "d e" } }, f: [1, 2], g: null, h: undefined, i: "ü&=" }),
  qs.stringify({ a: [1, 2] }, { arrayFormat: "brackets" }),
  qs.stringify({ a: [1, 2] }, { arrayFormat: "comma" }),
  qs.stringify({ a: [1, 2], b: { c: 1 } }, { encodeValuesOnly: true, allowDots: true, arrayFormat: "indices" }),
  qs.stringify({ a: "b", c: "d" }, { addQueryPrefix: true, sort: (left, right) => right.localeCompare(left) }),
  qs.stringify({ date: new Date(Date.UTC(2020, 0, 1)) }, { serializeDate: date => String(date.getTime()) }),
], null, 1);
