// CSSO: structural minification (merging, restructuring, usage data) and its AST
// The ES module build of the package: its lib directory imports source-map-js, a CommonJS module
import { minify, syntax } from "csso/dist/csso.esm";

const css = `
.header { color: #ff0000; margin: 10px 10px 10px 10px; }
.footer { color: red; margin-top: 0px; margin-right: 0; margin-bottom: 0; margin-left: 0; }
.a { font-weight: bold; } .b { font-weight: bold; }
.c { background: url("image.png") no-repeat 0 0; background-color: #FFFFFF; }
@media screen and (max-width: 600px) { .a { color: rgb(255, 255, 255); } .b { color: #fff; } }
.unused { display: none; }
div > p:first-child::before { content: "\\201C"; }
.d { transition: opacity 0.30s ease-in-out, transform 0.30s ease-in-out; }
.e { width: calc( 100% - 2 * 10px ); }
@font-face { font-family: "X"; src: url(x.woff2) format("woff2"); }
.f { color: transparent; color: rgba(0,0,0,0); }
`;

export default () => JSON.stringify({
  default: minify(css).css,
  noRestructure: minify(css, { restructure: false }).css,
  usage: minify(css, { usage: { classes: ["a", "b", "header"], tags: ["div", "p"] } }).css,
  comments: minify("/*! keep */ /* drop */ .a{color:red}", { comments: "exclamation" }).css,
  ast: syntax.generate(syntax.compress(syntax.parse(".x { color: #ff0000 } .y { color: red }")).ast),
}, null, 2);
