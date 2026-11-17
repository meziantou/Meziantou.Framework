"""Writes the TrueType hinting suite of the Imaging corpus (font-suites/truetype-hinting).

Three fonts of the Google Fonts project under the Apache License 2.0, hinted with TrueType instructions by hand, are
downloaded at pinned commits, checked against their SHA-256, and subset to Basic Latin with hb-subset, which keeps
the instructions. FreeType then hints every glyph of each subset at several sizes with its version 40 interpreter
for black and white pixels (FT_LOAD_TARGET_MONO): the hinted points and advances are the expectations the library
is compared with (HintingConformanceTests). FreeType is the oracle only: nothing of it is in the library.

Variable fonts are then made of the subsets of Droid Sans and Roboto with fontTools: two axes (fvar), deltas for the points of
every glyph (gvar, some of them inferred) and for the control values (cvar), and no HVAR table, so that the advances
vary with the phantom points. FreeType hints them the same way at a few instances.

Usage, from the repository root, in a virtual environment with freetype-py 2.5.1 (FreeType 2.13.2) and fontTools:

    python -I tools/Meziantou.Framework.Imaging.CorpusGenerator/truetype_hinting.py <work directory> <hb-subset> <output directory>

The corpus generator (the 'fonts' command) then copies the output directory into the corpus and pins its hash.
"""

import ctypes
import hashlib
import os
import subprocess
import sys
import urllib.request

import freetype
from fontTools.ttLib import TTFont, newTable
from fontTools.ttLib.tables._f_v_a_r import Axis
from fontTools.ttLib.tables.TupleVariation import TupleVariation
from freetype import raw

FONTS = [
    ("DroidSans", "google/fonts", "bfd28d000f58ed495f01bf5a3c07197413ea84bf", "apache/droidsans/DroidSans.ttf", "90f0a053d61ba5042e1c2db00e49584bc8b0d0b3e07fb1609c391b541b0ab4fa"),
    ("OpenSans-Regular", "google/fonts", "092ac9e068723bb83f1e288075b7775c32ce0543", "apache/opensans/OpenSans-Regular.ttf", "13c03e22a633919beb2847c58c8285fb8a735ee97097d7c48fd403f8294b05f8"),
    ("Roboto-Regular", "google/fonts", "7b64519fb0fd8b03b563b32ff55d2362db2c90bf", "apache/roboto/Roboto-Regular.ttf", "79e851404657dac2106b3d22ad256d47824a9a5765458edb72c9102a45816d95"),
]

SIZES = [9, 10, 11, 12, 13, 14, 15, 16, 20, 24]
PINNED_FREETYPE = (2, 13, 2)

# The variable font: its axes (tag, minimum, default, maximum), the instances it is hinted at (user values) and the sizes.
# The normalized coordinates of the instances are exact in 2.14, which the specification rounds them to: FreeType keeps
# them in 16.16.
VARIABLE_AXES = [("wght", 100, 400, 900), ("wdth", 68, 100, 132)]
VARIABLE_INSTANCES = [(900, 100), (100, 68), (650, 108), (325, 132)]
VARIABLE_SIZES = [9, 11, 12, 16, 20]

FT_LOAD_NO_BITMAP = 0x8
FT_LOAD_NO_AUTOHINT = 0x8000
FT_LOAD_TARGET_MONO = 0x20000


def download(work, name, repository, commit, path, sha256):
    target = os.path.join(work, name + ".ttf")
    if not os.path.exists(target):
        url = f"https://github.com/{repository}/raw/{commit}/{path}"
        with urllib.request.urlopen(url) as response, open(target, "wb") as file:
            file.write(response.read())

    with open(target, "rb") as file:
        actual = hashlib.sha256(file.read()).hexdigest()
    if actual != sha256:
        raise SystemExit(f"{target} has the SHA-256 {actual}, {sha256} is pinned")

    return target


def dump(path, out, sizes=SIZES, instances=None):
    face = freetype.Face(path)
    out.write(f"font {os.path.basename(path)} glyphs {face.num_glyphs} upem {face.units_per_EM}\n")
    for instance in instances or [None]:
        if instance is not None:
            # The values of the axes, as 16.16 numbers.
            coordinates = (freetype.FT_Fixed * len(instance))(*(value << 16 for value in instance))
            if raw.FT_Set_Var_Design_Coordinates(face._FT_Face, len(instance), coordinates):
                raise SystemExit("FreeType cannot set the coordinates of an instance")
            out.write(f"instance {' '.join(str(value) for value in instance)}\n")

        for ppem in sizes:
            face.set_pixel_sizes(0, ppem)
            out.write(f"size {ppem}\n")
            for glyph in range(face.num_glyphs):
                face.load_glyph(glyph, FT_LOAD_NO_BITMAP | FT_LOAD_NO_AUTOHINT | FT_LOAD_TARGET_MONO)
                outline = face.glyph.outline
                out.write(f"g {glyph} {face.glyph.advance.x} {len(outline.points)} {' '.join(str(c) for c in outline.contours)}\n")
                out.write(" ".join(f"{x},{y},{t & 1}" for (x, y), t in zip(outline.points, outline.tags)) + "\n")


def make_variable(source, target):
    """Makes a variable font of a static font: deterministic deltas, some points without delta, cvar deltas."""
    font = TTFont(source)

    # The device widths of hdmx are those of the default instance, which FreeType applies to every instance.
    if "hdmx" in font:
        del font["hdmx"]

    fvar = newTable("fvar")
    for tag, minimum, default, maximum in VARIABLE_AXES:
        axis = Axis()
        axis.axisTag = tag
        axis.minValue, axis.defaultValue, axis.maxValue = minimum, default, maximum
        axis.axisNameID = font["name"].addMultilingualName({"en": tag}, minNameID=256)
        fvar.axes.append(axis)
    font["fvar"] = fvar

    # Regions: each end of the weight, the wide end of the width, and an intermediate region of both.
    regions = [
        ({"wght": (0, 1, 1)}, lambda x, y, cx: (round((x - cx) * 0.06), round(y * 0.03))),
        ({"wght": (-1, -1, 0)}, lambda x, y, cx: (-round((x - cx) * 0.03), -round(y * 0.01))),
        ({"wdth": (0, 1, 1)}, lambda x, y, cx: (round(x * 0.1), 0)),
        ({"wght": (0.2, 0.6, 1), "wdth": (0, 0.5, 1)}, lambda x, y, cx: (round(x * 0.02), -round(y * 0.02))),
    ]

    glyf = font["glyf"]
    gvar = newTable("gvar")
    gvar.version = 1
    gvar.reserved = 0
    gvar.variations = {}
    for index, name in enumerate(font.getGlyphOrder()):
        coordinates, _ = glyf._getCoordinatesAndControls(name, font["hmtx"].metrics)
        points = list(coordinates)
        advance = font["hmtx"].metrics[name][0]
        cx = advance / 2
        variations = []
        for number, (axes, delta) in enumerate(regions):
            deltas = [delta(x, y, cx) for x, y in points]
            # Some points of simple glyphs have no delta in some regions: their deltas are inferred.
            if not glyf[name].isComposite() and number % 2 == 1:
                deltas = [None if (i * 7 + index) % 5 == 0 else d for i, d in enumerate(deltas[:-4])] + deltas[-4:]
            variations.append(TupleVariation(axes, deltas))
        gvar.variations[name] = variations
    font["gvar"] = gvar

    cvt = font["cvt "].values
    cvar = newTable("cvar")
    cvar.version = 1
    cvar.variations = [
        TupleVariation({"wght": (0, 1, 1)}, [round(v * 0.12) if i % 3 != 2 else None for i, v in enumerate(cvt)]),
        TupleVariation({"wght": (-1, -1, 0)}, [-round(v * 0.05) for v in cvt]),
        TupleVariation({"wdth": (0, 1, 1)}, [round(v * 0.08) if i % 4 == 0 else None for i, v in enumerate(cvt)]),
        TupleVariation({"wght": (0.2, 0.6, 1), "wdth": (0, 0.5, 1)}, [7 if i % 5 == 1 else None for i in range(len(cvt))]),
    ]
    font["cvar"] = cvar
    font.save(target)


def main():
    work, hb_subset, output = sys.argv[1:4]
    if freetype.version() != PINNED_FREETYPE:
        raise SystemExit(f"FreeType {freetype.version()} is not the pinned version {PINNED_FREETYPE}")

    # The version 40 interpreter, which is the default of FreeType, set explicitly.
    version = ctypes.c_uint(40)
    if raw.FT_Property_Set(freetype.get_handle(), b"truetype", b"interpreter-version", ctypes.byref(version)):
        raise SystemExit("FreeType does not have the version 40 interpreter")

    os.makedirs(work, exist_ok=True)
    os.makedirs(os.path.join(output, "expected"), exist_ok=True)
    for name, repository, commit, path, sha256 in FONTS:
        source = download(work, name, repository, commit, path, sha256)
        subset = os.path.join(output, name + ".subset.ttf")
        subprocess.run([hb_subset, "--unicodes=20-7E", "--output-file=" + subset, source], check=True)
        with open(os.path.join(output, "expected", name + ".txt"), "w", newline="\n") as file:
            dump(subset, file)

    # Droid Sans has simple glyphs only, Roboto has composite glyphs too.
    for name in ("DroidSans", "Roboto"):
        variable = os.path.join(output, name + "-Variable.ttf")
        make_variable(os.path.join(output, name + ("-Regular" if name == "Roboto" else "") + ".subset.ttf"), variable)
        with open(os.path.join(output, "expected", name + "-Variable.txt"), "w", newline="\n") as file:
            dump(variable, file, VARIABLE_SIZES, VARIABLE_INSTANCES)


if __name__ == "__main__":
    main()
