// culori: color parsing, conversions between color spaces (OKLCH, Lab, P3, Rec. 2020...), interpolation, gamut mapping,
// color difference and contrast
import { parse, formatHex, formatRgb, formatCss, converter, interpolate, samples, toGamut, differenceCiede2000, wcagContrast, clampChroma, blend, average, nearest, colorsNamed } from "culori";

const toOklch = converter("oklch");
const toLab = converter("lab");
const toP3 = converter("p3");
const toRec2020 = converter("rec2020");
const toHsl = converter("hsl");
const round = value => typeof value === "number" ? Math.round(value * 1e6) / 1e6 : value;
const clean = color => color && Object.fromEntries(Object.entries(color).map(([key, value]) => [key, round(value)]));

export default () => JSON.stringify({
  parsed: ["#336699", "rgb(255 0 0 / 50%)", "hsl(120deg 50% 50%)", "oklch(70% 0.15 200)", "lab(50% 40 -20)", "color(display-p3 1 0 0)", "rebeccapurple", "hwb(90 10% 10%)", "transparent"].map(text => clean(parse(text))),
  conversions: ["#336699", "tomato", "color(display-p3 1 0 0)"].map(color => [toOklch(color), toLab(color), toP3(color), toRec2020(color), toHsl(color)].map(clean)),
  formats: [formatHex("oklch(70% 0.15 200)"), formatRgb("lab(50% 40 -20)"), formatCss(toOklch("#336699")), formatCss(clampChroma("oklch(70% 0.4 200)", "oklch"))],
  interpolation: samples(5).map(interpolate(["red", "lime", "blue"], "oklch")).map(formatHex),
  gamut: formatHex(toGamut("rgb", "oklch")("oklch(90% 0.4 30)")),
  difference: round(differenceCiede2000()("#336699", "#336698")),
  contrast: [round(wcagContrast("white", "#336699")), round(wcagContrast("black", "yellow"))],
  blend: formatHex(blend(["#ff000080", "#0000ff80"], "multiply")),
  average: formatHex(average(["red", "blue", "lime"], "oklab")),
  nearest: nearest(Object.keys(colorsNamed), differenceCiede2000())("#3366a0", 3),
}, null, 1);
