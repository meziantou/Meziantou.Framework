// Computes the expected values of ColorValueReferenceTests with the sample code of CSS Color Module Level 4, at a pinned
// commit of w3c/csswg-drafts. The sample code is downloaded, not committed.
//
//   node generate-css-color-4.mjs > css-color-4.json
//
// Node 18 or later (fetch). The inputs are those of generate-colour-science.py: change both together.
import vm from "node:vm";

const commit = "700ba7b5e6ea39623b68a2a0575c2bb89c640d54";
const files = ["multiply-matrices.js", "conversions.js", "utilities.js", "better-rgbToHsl.js", "hslToRgb.js", "rgbToHwb.js", "hwbToRgb.js", "deltaEOK.js", "deltaE2000.js"];

let source = "";
for (const file of files) {
    const response = await fetch(`https://raw.githubusercontent.com/w3c/csswg-drafts/${commit}/css-color-4/${file}`);
    if (!response.ok) {
        throw new Error(`${file}: ${response.status}`);
    }

    // utilities.js has an older hslToRgb (hue in sextants): the one of hslToRgb.js, loaded after it, replaces it
    source += (await response.text()) + "\n";
}

const context = {};
vm.createContext(context);
vm.runInContext(source + "\nthis.api = { lin_sRGB, gam_sRGB, lin_sRGB_to_XYZ, XYZ_to_lin_sRGB, lin_P3, lin_P3_to_XYZ, D65_to_D50, D50_to_D65, XYZ_to_Lab, Lab_to_XYZ, Lab_to_LCH, LCH_to_Lab, XYZ_to_OKLab, OKLab_to_XYZ, OKLab_to_OKLCH, OKLCH_to_OKLab, rgbToHsl, hslToRgb, rgbToHwb, hwbToRgb, naive_sRGB_to_CMYK, naive_CMYK_to_sRGB, deltaEOK, deltaE2000 };", context);
const css = context.api;

// The inputs: the corners of the RGB cube, grays, then colors of a linear congruential generator
const rgb8 = [];
for (const r of [0, 255]) for (const g of [0, 255]) for (const b of [0, 255]) rgb8.push([r, g, b]);
for (const v of [1, 2, 10, 11, 17, 50, 127, 128, 200, 254]) rgb8.push([v, v, v]);
let seed = 20261027;
// MINSTD: the products stay below 2^53, so JavaScript and Python compute the same numbers
const next = () => {
    seed = (seed * 48271) % 2147483647;
    return seed;
};
for (let i = 0; i < 200; i++) rgb8.push([next() % 256, next() % 256, next() % 256]);
const rgb16 = [];
for (let i = 0; i < 50; i++) rgb16.push([next() % 65536, next() % 65536, next() % 65536]);

const labs = [[0, 0, 0], [100, 0, 0], [50, 0, 0], [5, 3, -2], [7.9, 0, 0], [8.1, 0, 0], [0.5, 1, 1], [53.24, 80.09, 67.2], [87.7, -86.18, 83.18], [32.3, 79.19, -107.86], [50, 125, -125], [75, -60, 90], [30, 20, 30], [62.5, -10, -40], [95, 2, -3]];
const lchs = [[50, 0, 0], [50, 30, 0], [50, 30, 90], [50, 30, 180], [50, 30, 270], [52.2345, 72.2, 56.2], [70, 50, 359.9], [20, 100, 300], [90, 10, 45]];
const oklabs = [[0, 0, 0], [1, 0, 0], [0.5, 0, 0], [0.62796, 0.22486, 0.12585], [0.86644, -0.23389, 0.1795], [0.45201, -0.03246, -0.31153], [0.7, 0.1, -0.1], [0.3, -0.05, 0.08], [0.95, 0.4, 0.4]];
const oklchs = [[0.5, 0, 0], [0.5, 0.1, 0], [0.62796, 0.25768, 29.23], [0.7, 0.15, 145], [0.4, 0.2, 264], [0.9, 0.05, 90], [0.6, 0.37, 330]];
const hsls = [[0, 1, 0.5], [120, 1, 0.5], [240, 1, 0.5], [60, 0.5, 0.25], [300, 0.25, 0.75], [210, 0.8, 0.4], [33.3, 0.66, 0.66], [359, 1, 0.5], [180, 0, 0.3]];
const hwbs = [[0, 0, 0], [120, 0.2, 0.3], [240, 0.5, 0.5], [60, 0.7, 0.6], [300, 0.1, 0], [15.5, 0.05, 0.9], [200, 0.33, 0.12]];
const cmyks = [[0, 0, 0, 0], [1, 0, 0, 0], [0, 1, 0, 0.5], [0.2, 0.4, 0.6, 0.1], [0.5, 0.5, 0.5, 1], [0.9, 0.1, 0.3, 0.25]];

const toLab = rgb => css.XYZ_to_Lab(css.D65_to_D50(css.lin_sRGB_to_XYZ(css.lin_sRGB(rgb))));
const toOklab = rgb => css.XYZ_to_OKLab(css.lin_sRGB_to_XYZ(css.lin_sRGB(rgb)));
const fromXyzD65 = xyz => css.gam_sRGB(css.XYZ_to_lin_sRGB(xyz));
const fraction = (rgb, max) => rgb.map(v => v / max);

const result = {
    source: `CSS Color Module Level 4 sample code, w3c/csswg-drafts@${commit}`,
    srgb8: rgb8.map(rgb => describeRgb(rgb, fraction(rgb, 255))),
    srgb16: rgb16.map(rgb => describeRgb(rgb, fraction(rgb, 65535))),
    displayP3: rgb8.slice(0, 40).map(rgb => ({ rgb, xyzD65: css.lin_P3_to_XYZ(css.lin_P3(fraction(rgb, 255))) })),
    lab: labs.map(lab => {
        const xyzD50 = css.Lab_to_XYZ(lab);
        return { lab, xyzD50, srgb: fromXyzD65(css.D50_to_D65(xyzD50)) };
    }),
    lch: lchs.map(lch => ({ lch, lab: css.LCH_to_Lab(lch) })),
    oklab: oklabs.map(oklab => {
        const xyzD65 = css.OKLab_to_XYZ(oklab);
        return { oklab, xyzD65, srgb: fromXyzD65(xyzD65) };
    }),
    oklch: oklchs.map(oklch => ({ oklch, oklab: css.OKLCH_to_OKLab(oklch) })),
    hsl: hsls.map(hsl => ({ hsl, srgb: css.hslToRgb(hsl[0], hsl[1] * 100, hsl[2] * 100) })),
    hwb: hwbs.map(hwb => ({ hwb, srgb: css.hwbToRgb(hwb[0], hwb[1] * 100, hwb[2] * 100) })),
    cmyk: cmyks.map(cmyk => ({ cmyk, srgb: css.naive_CMYK_to_sRGB(cmyk) })),
    deltaE: [],
};

for (let i = 0; i + 1 < rgb8.length; i += 2) {
    const lab1 = toLab(fraction(rgb8[i], 255));
    const lab2 = toLab(fraction(rgb8[i + 1], 255));
    const ok1 = toOklab(fraction(rgb8[i], 255));
    const ok2 = toOklab(fraction(rgb8[i + 1], 255));
    result.deltaE.push({ lab1, lab2, deltaE2000: css.deltaE2000(lab1, lab2), oklab1: ok1, oklab2: ok2, deltaEOK: css.deltaEOK(ok1, ok2) });
}

// One entry per line; a hue CSS makes missing (NaN, powerless) is null
const replacer = (key, value) => typeof value === "number" && Number.isNaN(value) ? null : value;
const sections = Object.entries(result).map(([key, value]) => Array.isArray(value)
    ? `  ${JSON.stringify(key)}: [\n${value.map(item => "    " + JSON.stringify(item, replacer)).join(",\n")}\n  ]`
    : `  ${JSON.stringify(key)}: ${JSON.stringify(value)}`);
process.stdout.write(`{\n${sections.join(",\n")}\n}\n`);

function describeRgb(rgb, values) {
    const xyzD65 = css.lin_sRGB_to_XYZ(css.lin_sRGB(values));
    const xyzD50 = css.D65_to_D50(xyzD65);
    const lab = css.XYZ_to_Lab(xyzD50);
    const oklab = css.XYZ_to_OKLab(xyzD65);
    const hsl = css.rgbToHsl(...values);
    const hwb = css.rgbToHwb(...values);
    return {
        rgb,
        xyzD65,
        xyzD50,
        lab,
        lch: css.Lab_to_LCH(lab),
        oklab,
        oklch: css.OKLab_to_OKLCH(oklab),
        hsl: [hsl[0], hsl[1] / 100, hsl[2] / 100],
        hwb: [hwb[0], hwb[1] / 100, hwb[2] / 100],
        cmyk: css.naive_sRGB_to_CMYK(values),
    };
}
