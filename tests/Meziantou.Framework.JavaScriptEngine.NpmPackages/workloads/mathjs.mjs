// math.js: the expression parser, matrices, complex numbers, units, fractions, big numbers, symbolic derivatives and
// simplification
import { create, all } from "mathjs";

const math = create(all, { number: "number", precision: 64 });
const big = create(all, { number: "BigNumber", precision: 40 });

export default () => {
  const scope = { x: 3, f: undefined };
  const results = [
    math.evaluate("sqrt(-4) + 2i * (1 - i)"),
    math.evaluate("det([1, 2; 3, 4]) + inv([1, 2; 3, 4])"),
    math.evaluate("[1, 2, 3] .* [4, 5, 6]"),
    math.evaluate("5 cm + 2 inch to mm"),
    math.evaluate("90 km/h to m/s"),
    math.evaluate("sin(45 deg)^2 + cos(pi/4)^2"),
    math.evaluate("f(x) = x^2 + 1; f(4)", scope),
    math.evaluate("x * 2 + y", { ...scope, y: 1 }),
    math.evaluate("fraction(1, 3) + fraction(1, 6)"),
    math.evaluate("eigs([2, 0; 0, 3]).values"),
    math.evaluate("lusolve([1, 2; 3, 4], [5; 6])"),
    math.evaluate("std([2, 4, 4, 4, 5, 5, 7, 9]) + median([3, 1, 2]) + mode([1, 2, 2])"),
    math.evaluate("combinations(10, 3) + factorial(10) + gcd(48, 18) + lcm(4, 6) + isPrime(97)"),
    math.evaluate("bin(255) + \" \" + hex(255) + \" \" + format(1/3, 5)"),
    math.derivative("x^3 + sin(x) * exp(2x)", "x"),
    math.simplify("2x + 3x - (x + 1)^2 + 1"),
    math.rationalize("(x^2 - 1) / (x - 1)"),
    math.parse("a * (b + c) / 2").transform(node => node.isSymbolNode && node.name === "a" ? new math.ConstantNode(10) : node),
    math.multiply(math.matrix([[1, 2], [3, 4]]), math.matrix([[5], [6]])),
    math.kron([[1, 2]], [[1], [1]]),
    math.evaluate("5 kg * 9.81 m / s^2 to N"),
    big.evaluate("1 / 3 + 2 ^ 100"),
    big.evaluate("sqrt(2)"),
    math.format(math.pi, { notation: "engineering", precision: 8 }),
    math.parse("sqrt(x^2 + y^2) / 2").toTex(),
  ];

  try {
    math.evaluate("1 +");
  }
  catch (error) {
    results.push(error.message);
  }

  return results.map(value => math.format(value, { precision: 14 })).join("\n");
};
