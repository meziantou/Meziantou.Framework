# Computes the expected values of ColorValueReferenceTests with colour-science, an independent implementation of the
# CIE equations.
#
#   python3 -m venv venv && venv/bin/pip install colour-science==0.4.4
#   venv/bin/python generate-colour-science.py > colour-science.json
#
# The inputs are those of generate-css-color-4.mjs: change both together.
import json
import sys

import colour
import numpy as np

assert colour.__version__ == "0.4.4", colour.__version__

D50 = np.array([0.3457, 0.3585])
D65 = np.array([0.3127, 0.3290])

# The matrices derived from the chromaticities of the primaries and of the white, as CSS Color 4 and the library compute
# them, not the four-digit matrix IEC 61966-2-1 publishes, which colour-science uses by default for sRGB
SRGB = colour.RGB_COLOURSPACES["sRGB"].copy()
SRGB.use_derived_matrix_RGB_to_XYZ = True
SRGB.use_derived_matrix_XYZ_to_RGB = True
P3 = colour.RGB_COLOURSPACES["Display P3"].copy()
P3.use_derived_matrix_RGB_to_XYZ = True
P3.use_derived_matrix_XYZ_to_RGB = True


def inputs():
    rgb8 = [[r, g, b] for r in (0, 255) for g in (0, 255) for b in (0, 255)]
    rgb8 += [[v, v, v] for v in (1, 2, 10, 11, 17, 50, 127, 128, 200, 254)]
    seed = 20261027

    def next_value():
        nonlocal seed
        seed = (seed * 48271) % 2147483647
        return seed

    for _ in range(200):
        rgb8.append([next_value() % 256, next_value() % 256, next_value() % 256])

    rgb16 = [[next_value() % 65536, next_value() % 65536, next_value() % 65536] for _ in range(50)]
    return rgb8, rgb16


def to_d50(xyz):
    return colour.chromatic_adaptation(xyz, colour.xy_to_XYZ(D65), colour.xy_to_XYZ(D50), method="Von Kries", transform="Bradford")


def describe(rgb, values, colourspace=SRGB):
    xyz_d65 = colour.RGB_to_XYZ(values, colourspace, apply_cctf_decoding=True)
    xyz_d50 = to_d50(xyz_d65)
    lab = colour.XYZ_to_Lab(xyz_d50, D50)
    hsv = colour.RGB_to_HSV(values)
    hsl = colour.RGB_to_HSL(values)
    return {
        "rgb": rgb,
        "xyzD65": xyz_d65.tolist(),
        "xyzD50": xyz_d50.tolist(),
        "lab": lab.tolist(),
        "lch": colour.Lab_to_LCHab(lab).tolist(),
        "luv": colour.XYZ_to_Luv(xyz_d50, D50).tolist(),
        "oklab": colour.XYZ_to_Oklab(xyz_d65).tolist(),
        # colour-science gives the hue as a fraction of a turn
        "hsv": [hsv[0] * 360, hsv[1], hsv[2]],
        "hsl": [hsl[0] * 360, hsl[1], hsl[2]],
        "cmyk": colour.CMY_to_CMYK(colour.RGB_to_CMY(values)).tolist(),
    }


def main():
    rgb8, rgb16 = inputs()
    luvs = [[0, 0, 0], [50, 0, 0], [5, 3, -2], [53.24, 175, 37.75], [87.73, -83.07, 107.4], [32.3, -9.4, -130.34], [70, 20, -30], [7.9, 1, 1]]
    hsvs = [[0, 1, 1], [120, 0.5, 0.5], [240, 0.25, 0.75], [60, 1, 0.2], [300, 0.8, 0.9], [33.3, 0.66, 0.66], [359, 1, 1], [180, 0, 0.3]]
    result = {
        "source": f"colour-science {colour.__version__}, numpy {np.__version__}",
        "srgb8": [describe(rgb, np.array(rgb) / 255) for rgb in rgb8],
        "srgb16": [describe(rgb, np.array(rgb) / 65535) for rgb in rgb16],
        "displayP3": [{"rgb": rgb, "xyzD65": colour.RGB_to_XYZ(np.array(rgb) / 255, P3, apply_cctf_decoding=True).tolist()} for rgb in rgb8[:40]],
        "luv": [{"luv": luv, "xyzD50": colour.Luv_to_XYZ(np.array(luv), D50).tolist()} for luv in luvs],
        "hsv": [{"hsv": hsv, "srgb": colour.HSV_to_RGB(np.array([hsv[0] / 360, hsv[1], hsv[2]])).tolist()} for hsv in hsvs],
        "deltaE": [],
    }

    for i in range(0, len(rgb8) - 1, 2):
        lab1 = colour.XYZ_to_Lab(to_d50(colour.RGB_to_XYZ(np.array(rgb8[i]) / 255, SRGB, apply_cctf_decoding=True)), D50)
        lab2 = colour.XYZ_to_Lab(to_d50(colour.RGB_to_XYZ(np.array(rgb8[i + 1]) / 255, SRGB, apply_cctf_decoding=True)), D50)
        result["deltaE"].append({
            "lab1": lab1.tolist(),
            "lab2": lab2.tolist(),
            "deltaE76": float(colour.delta_E(lab1, lab2, method="CIE 1976")),
            "deltaE94": float(colour.delta_E(lab1, lab2, method="CIE 1994")),
            "deltaE94Textiles": float(colour.delta_E(lab1, lab2, method="CIE 1994", textiles=True)),
            "deltaE2000": float(colour.delta_E(lab1, lab2, method="CIE 2000")),
        })

    # One entry per line
    sections = []
    for key, value in result.items():
        if isinstance(value, list):
            items = ",\n".join("    " + json.dumps(item, separators=(",", ":")) for item in value)
            sections.append(f"  {json.dumps(key)}: [\n{items}\n  ]")
        else:
            sections.append(f"  {json.dumps(key)}: {json.dumps(value)}")

    sys.stdout.write("{\n" + ",\n".join(sections) + "\n}\n")


main()
