// Dart Sass compiled to JavaScript: variables, nesting, mixins, functions, maps, loops, the module system, custom
// importers, source maps, the messages of the logger and the errors
import * as sass from "sass";

// The messages of Sass contain paths relative to Uri.base: the browser build of Dart computes it from self.location
globalThis.location = { href: "file:///" };

const modules = {
  "theme": `
        @use "sass:color";
        @use "sass:map";
        $colors: (primary: #3366cc, danger: #d9534f, muted: #777);
        @function color($name) { @return map.get($colors, $name); }
        @mixin button($name) {
            background: color($name);
            border: 1px solid color.adjust(color($name), $lightness: -10%);
            &:hover { background: color.scale(color($name), $lightness: 5%); }
        }`,
};

const importer = {
  canonicalize: url => url.startsWith("theme") ? new URL("memory:/" + url) : null,
  load: url => ({ contents: modules[url.pathname.substring(1)], syntax: "scss" }),
};

const scss = `
    @use "sass:math";
    @use "sass:list";
    @use "sass:string";
    @use "sass:color";
    @use "theme" as t;

    $breakpoints: (small: 576px, medium: 768px, large: 992px);
    %reset { margin: 0; padding: 0; }

    .button { @include t.button(primary); @extend %reset; }
    .button-danger { @include t.button(danger); }
    ul.nav { @extend %reset; li { display: inline-block; & + li { margin-left: math.div(16px, 2); } } }

    @each $name, $width in $breakpoints {
        @media (min-width: $width) { .container-#{$name} { max-width: $width - 16px; } }
    }

    @for $i from 1 through 4 { .col-#{$i} { width: math.percentage(math.div($i, 4)); } }

    $i: 6;
    @while $i > 0 { .item-#{$i} { width: 2em * $i; } $i: $i - 2; }

    .info {
        content: string.quote(string.to-upper-case("sass"));
        font: list.join(12px, (bold, serif), comma);
        width: calc(100% - #{math.round(math.$pi * 10px)});
        color: rgba(t.color(muted), 0.5);
        mix: color.mix(#f00, #00f, 25%);
        channel: color.channel(#3366cc, "hue", $space: hsl);
    }
`;

// The messages of @warn and @debug
const messages = [];
const logger = {
  warn: (message, options) => messages.push("warn: " + message + (options.deprecation ? " (deprecation)" : "")),
  debug: message => messages.push("debug: " + message),
};

const results = [];
results.push(sass.compileString(scss + "\n@warn 'a warning';\n@debug 1 + 2;", { importers: [importer], logger }).css);
results.push(sass.compileString(scss, { importers: [importer], style: "compressed", logger }).css);
results.push(sass.compileString(".a\n  color: red\n  .b\n    color: blue\n", { syntax: "indented" }).css);

const withMap = sass.compileString(".a { .b { color: red; } }", { sourceMap: true });
results.push(JSON.stringify(withMap.sourceMap.mappings));

results.push(messages.join("\n"));

for (const invalid of [".a { color: $undefined; }", ".a { width: 1px + 1s; }", "@use 'missing';", ".a { color: darken(red, 10%); }"]) {
  try {
    results.push(sass.compileString(invalid, { logger: { warn: message => results.push("warn: " + message.split("\n")[0]) } }).css);
  }
  catch (error) {
    results.push(error.sassMessage + " at " + error.span?.start.line + ":" + error.span?.start.column);
  }
}

export default () => results.join("\n----\n");
