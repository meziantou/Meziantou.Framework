// whatwg-url: the URL Standard in JavaScript (the URL parser state machine, IDNA with tr46 and its Unicode tables,
// percent-encoding sets, the host parser, URLSearchParams). The harness gives it to the packages as the URL global.
import { URL, URLSearchParams, parseURL, serializeURL, percentDecodeString } from "whatwg-url";

const inputs = [
  ["https://user:pa%20ss@EXAMPLE.com:443/a/./b/../c?q=1&r=a b#frag ment"],
  ["HTTP://[::FFFF:192.168.0.1]:80/"],
  ["http://0x7f.1/"],
  ["https://münchen.de/straße?ä=ö"],
  ["https://xn--mnchen-3ya.de/"],
  ["https://日本語.jp/パス"],
  ["file:///C:/Users/../test.txt"],
  ["file://server/share/file"],
  ["mailto:someone@example.com?subject=Hi%20there"],
  ["javascript:alert(1)"],
  ["blob:https://example.com/uuid"],
  ["../../a?b#c", "http://host/x/y/z"],
  ["//other.example/path", "https://base.example/"],
  ["?query", "http://host/path/file"],
  ["#hash", "http://host/path?q"],
  ["http://ex ample.com/"],
  ["https://exa%41mple.com/"],
  ["http://1.2.3.4.5/"],
  ["http://[1::2::3]/"],
  ["https://a.b.c.d.e.f.g.h.i.j.k.l.m.n.o.p/" + "x".repeat(10)],
  ["  \t https://trim.example/\n  "],
  ["https://example.com/%zz%41"],
  ["non-special://host:8080/path"],
  ["https://faß.de/"],
  ["https://xn--a.example/"],
];

export default () => {
  const results = inputs.map(([input, base]) => {
    try {
      const url = new URL(input, base);
      return [input, url.href, url.origin, url.protocol, url.username, url.password, url.host, url.hostname, url.port, url.pathname, url.search, url.hash];
    }
    catch (error) {
      return [input, error.name + ": " + error.message];
    }
  });

  const url = new URL("https://example.com/path?b=2&a=1&a=3");
  url.searchParams.append("c", "x y&z");
  url.searchParams.sort();
  url.pathname = "/new path/ü";
  url.hash = "section 1";
  url.port = "8443";
  url.username = "user@name";
  results.push(["mutated", url.href, [...url.searchParams].join(";")]);
  const params = new URLSearchParams({ q: "a+b c", emoji: "😀" });
  params.set("list", ["1", "2"]);
  results.push(["params", params.toString(), params.get("q"), params.has("emoji"), [...new URLSearchParams("?a=1&a=2&b").getAll("a")]]);
  results.push(["record", serializeURL(parseURL("HTTPS://Example.COM/./a/%2e%2E/b")), percentDecodeString("%E2%9C%93%zz").toString()]);
  results.push(["canParse", URL.canParse("https://x"), URL.canParse("nope")]);
  return JSON.stringify(results, null, 1);
};
