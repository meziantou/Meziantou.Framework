// KaTeX: rendering TeX to HTML and MathML, macros, environments and errors
import katex from "katex";

const formulas = [
  "c = \\pm\\sqrt{a^2 + b^2}",
  "\\int_0^\\infty e^{-x^2}\\,dx = \\frac{\\sqrt{\\pi}}{2}",
  "\\sum_{n=1}^{\\infty} \\frac{1}{n^2} = \\frac{\\pi^2}{6}",
  "\\begin{pmatrix} a & b \\\\ c & d \\end{pmatrix}^{-1} = \\frac{1}{ad-bc}\\begin{pmatrix} d & -b \\\\ -c & a \\end{pmatrix}",
  "\\begin{aligned} f(x) &= (x+1)^2 \\\\ &= x^2 + 2x + 1 \\end{aligned}",
  "\\mathbb{R} \\subset \\mathbb{C}, \\quad \\forall \\epsilon > 0\\ \\exists \\delta",
  "\\text{if } x \\in \\{1, 2\\} \\text{ then } \\lim_{h \\to 0} \\frac{f(x+h)-f(x)}{h}",
  "\\color{red}{x} + \\cancel{y} + \\overbrace{a+b}^{n} + \\vec{v} \\cdot \\hat{u}",
  "\\RR^n",
];

export default () => {
  const results = formulas.map(formula => katex.renderToString(formula, { throwOnError: false, macros: { "\\RR": "\\mathbb{R}" } }));
  results.push(katex.renderToString("x^2 + y^2 = z^2", { displayMode: true, output: "mathml" }));
  for (const invalid of ["\\frac{1}", "\\undefinedmacro", "x^"]) {
    try {
      katex.renderToString(invalid);
      results.push("no error");
    }
    catch (error) {
      results.push(error.name + ": " + error.message);
    }
  }

  return results.join("\n");
};
