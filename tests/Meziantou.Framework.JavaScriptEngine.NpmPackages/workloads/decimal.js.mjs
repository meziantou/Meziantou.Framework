// decimal.js: arbitrary-precision decimal arithmetic, transcendental functions, rounding modes and conversions
import Decimal from "decimal.js";

export default () => {
  const results = [];
  const D = Decimal.clone({ precision: 50, rounding: Decimal.ROUND_HALF_EVEN });
  results.push(D.sqrt(2).toString(), D.exp(1).toString(), D.ln(10).toString(), D.acos(-1).toString(), D.sin(1).toString(), D.atan2(1, 2).toString());
  results.push(new D("0.1").plus("0.2").toString(), new D(1).div(3).toString(), new D(2).pow(0.5).toString(), new D("123456789.987654321").times("987654321.123456789").toString());
  results.push(new D("1e-7").toString(), new D("123.456").toExponential(2), new D("123.456").toFixed(10), new D("0.000123").toPrecision(2), new D(255).toHexadecimal(), new D("0xff.8").toString(), new D(10).toBinary(), new D("1.5").toFraction().join("/"));
  for (const rounding of [Decimal.ROUND_UP, Decimal.ROUND_DOWN, Decimal.ROUND_CEIL, Decimal.ROUND_FLOOR, Decimal.ROUND_HALF_UP, Decimal.ROUND_HALF_DOWN, Decimal.ROUND_HALF_EVEN, Decimal.ROUND_HALF_CEIL, Decimal.ROUND_HALF_FLOOR]) {
    results.push(["2.5", "-2.5", "1.15", "-1.15"].map(value => new D(value).toDecimalPlaces(1, rounding).toString()).join(" "));
  }

  let sum = new D(0);
  for (let i = 1; i <= 200; i++)
    sum = sum.plus(new D(1).div(new D(i).pow(2)));
  results.push(sum.toString(), sum.times(6).sqrt().toString());
  results.push(new D("1e+9000000000000000").toString(), new D("-0").isNegative(), new D(NaN).isNaN(), D.max(1, "2", new D(3)).toString(), D.sum(1, 2, 3).toString(), new D("9.99").cmp("10"));
  return results.join("\n");
};
