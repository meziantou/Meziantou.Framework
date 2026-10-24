// validator.js: string validators (regular expressions and algorithms such as Luhn and IBAN) and sanitizers
import validator from "validator";

const checks = [
  ["isEmail", ["foo@bar.com", "foo@bar", "\"quoted\"@example.com", "a@b.co.uk", "ünicode@exämple.com"]],
  ["isURL", ["https://example.com/a?b=c#d", "ftp://x.y", "example", "http://localhost:3000", "http://[::1]/"]],
  ["isIP", ["192.168.0.1", "256.1.1.1", "::1", "2001:db8::ff00:42:8329", "1.2.3"]],
  ["isCreditCard", ["4111111111111111", "4111111111111112", "5500 0000 0000 0004"]],
  ["isIBAN", ["FR1420041010050500013M02606", "GB82WEST12345698765432", "FR142004101005"]],
  ["isISBN", ["978-3-16-148410-0", "0-306-40615-2", "123"]],
  ["isUUID", ["123e4567-e89b-12d3-a456-426614174000", "not-a-uuid"]],
  ["isJSON", ["{\"a\":1}", "{a:1}"]],
  ["isJWT", ["eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.abc", "a.b"]],
  ["isISO8601", ["2020-01-01T00:00:00Z", "2020-13-01", "2020-W01-1"]],
  ["isMobilePhone", ["+33612345678", "0612345678", "123"]],
  ["isPostalCode", ["75001", "SW1A 1AA", "1234567"]],
  ["isHexColor", ["#fff", "#abcdef", "#gggggg"]],
  ["isSemVer", ["1.2.3", "1.2", "1.2.3-alpha+001"]],
  ["isStrongPassword", ["Abcdef1!", "password"]],
  ["isBase64", ["aGVsbG8=", "aGVsbG8", "not base64!"]],
  ["isMACAddress", ["01:23:45:67:89:ab", "01-23-45-67-89-AB", "0123.4567.89ab"]],
  ["isAlpha", ["abc", "abc1", "élan"]],
  ["isNumeric", ["123", "-1.5", "1e5", "abc"]],
  ["isSlug", ["my-slug", "My Slug"]],
];

export default () => JSON.stringify({
  checks: checks.map(([name, values]) => [name, values.map(value => validator[name](value, ...(name === "isMobilePhone" ? ["fr-FR"] : name === "isPostalCode" ? ["any"] : [])))]),
  sanitizers: [
    validator.escape("<a href=\"x\">'&'</a>"),
    validator.unescape("&lt;b&gt;&amp;&#x27;"),
    validator.normalizeEmail("Foo.Bar+tag@GoogleMail.com"),
    validator.trim("  x  "),
    validator.blacklist("abcabc", "b"),
    validator.toInt("42px"),
    validator.toFloat("3.14"),
    validator.toBoolean("yes"),
    validator.stripLow("a\u0000b\nc", true),
    validator.toDate("2020-01-02T03:04:05Z")?.toISOString(),
  ],
}, null, 1);
