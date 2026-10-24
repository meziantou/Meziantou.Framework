// js-beautify: formatting of JavaScript, CSS and HTML with options
import beautify from "js-beautify";

const js = "var a=function(b,c){if(b){return c?{x:1,y:[1,2,3]}:null}else{for(var i=0;i<10;i++){c+=i}}return `t${b}`};const f=async(x)=>{await x;label:while(true){break label}};class A extends B{static #p=1;get x(){return this.#p}}";
const css = ".a{color:red;background:url(x.png)}@media (max-width:100px){.b>.c{margin:0 auto}}.d:hover::after{content:'x'}";
const html = "<!DOCTYPE html><html><head><title>T</title><style>a{color:red}</style></head><body><div class=a><p>Text <b>bold</b></p><ul><li>1<li>2</ul><script>if(a){b()}</script><pre>  keep   this </pre></div></body></html>";

export default () => [
  beautify.js(js),
  beautify.js(js, { indent_size: 2, brace_style: "expand", space_in_paren: true, end_with_newline: true }),
  beautify.css(css),
  beautify.css(css, { indent_char: "\t", indent_size: 1, newline_between_rules: false }),
  beautify.html(html),
  beautify.html(html, { indent_inner_html: true, wrap_line_length: 40, extra_liners: [] }),
].join("\n----\n");
