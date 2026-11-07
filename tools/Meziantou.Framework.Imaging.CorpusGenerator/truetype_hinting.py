"""Writes the TrueType hinting suite of the Imaging corpus (font-suites/truetype-hinting).

Three fonts of the Google Fonts project under the Apache License 2.0, hinted with TrueType instructions by hand, are
downloaded at pinned commits, checked against their SHA-256, and subset to Basic Latin with hb-subset, which keeps
the instructions. FreeType then hints every glyph of each subset at several sizes with its version 40 interpreter
for black and white pixels (FT_LOAD_TARGET_MONO): the hinted points and advances are the expectations the library
is compared with (HintingConformanceTests). FreeType is the oracle only: nothing of it is in the library.

Usage, from the repository root, in a virtual environment with freetype-py 2.5.1 (FreeType 2.13.2):

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
from freetype import raw

FONTS = [
    ("DroidSans", "google/fonts", "bfd28d000f58ed495f01bf5a3c07197413ea84bf", "apache/droidsans/DroidSans.ttf", "90f0a053d61ba5042e1c2db00e49584bc8b0d0b3e07fb1609c391b541b0ab4fa"),
    ("OpenSans-Regular", "google/fonts", "092ac9e068723bb83f1e288075b7775c32ce0543", "apache/opensans/OpenSans-Regular.ttf", "13c03e22a633919beb2847c58c8285fb8a735ee97097d7c48fd403f8294b05f8"),
    ("Roboto-Regular", "google/fonts", "7b64519fb0fd8b03b563b32ff55d2362db2c90bf", "apache/roboto/Roboto-Regular.ttf", "79e851404657dac2106b3d22ad256d47824a9a5765458edb72c9102a45816d95"),
]

SIZES = [9, 10, 11, 12, 13, 14, 15, 16, 20, 24]
PINNED_FREETYPE = (2, 13, 2)

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


def dump(path, out):
    face = freetype.Face(path)
    out.write(f"font {os.path.basename(path)} glyphs {face.num_glyphs} upem {face.units_per_EM}\n")
    for ppem in SIZES:
        face.set_pixel_sizes(0, ppem)
        out.write(f"size {ppem}\n")
        for glyph in range(face.num_glyphs):
            face.load_glyph(glyph, FT_LOAD_NO_BITMAP | FT_LOAD_NO_AUTOHINT | FT_LOAD_TARGET_MONO)
            outline = face.glyph.outline
            out.write(f"g {glyph} {face.glyph.advance.x} {len(outline.points)} {' '.join(str(c) for c in outline.contours)}\n")
            out.write(" ".join(f"{x},{y},{t & 1}" for (x, y), t in zip(outline.points, outline.tags)) + "\n")


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


if __name__ == "__main__":
    main()
