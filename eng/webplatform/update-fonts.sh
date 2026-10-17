#!/usr/bin/env bash
# Downloads the bundled fonts of src/Meziantou.Framework.WebPlatform/Css/Fonts/Bundled from their official sources (pinned
# commits) and subsets the generic family fallbacks to WOFF2 with fontTools (see the README.md of that folder).
#
# Usage: eng/webplatform/update-fonts.sh
#   Requires python3 (a virtual environment with the pinned fontTools and Brotli is created in a temporary folder).
set -euo pipefail

FONTTOOLS_VERSION=4.60.1
BROTLI_VERSION=1.1.0
# https://github.com/google/fonts: Arimo (sans-serif), Tinos (serif) and Cousine (monospace), SIL Open Font License 1.1
GOOGLE_FONTS_COMMIT=9710da1eacb3be272583c3224dcb70f9da6eadbb
# https://github.com/googlefonts/tinos: the license of Tinos (the google/fonts folder of Tinos has no OFL.txt)
TINOS_COMMIT=3b4482a99b80ea5fc75f187b1be3120a3f5905b3
# https://github.com/web-platform-tests/wpt: the Ahem font (public domain, CC0)
WPT_COMMIT=1d99362b6bca46cc06e731ece8e022f0f1c21695

# Basic Latin, Latin-1 Supplement, Latin Extended-A, the spacing modifier letters of Latin, Greek, Cyrillic, the general
# punctuation (spaces, dashes, quotes, bullets...), the euro sign, the trade mark sign, arrows, the minus sign, the BOM and
# the replacement character
# The characters of the math font (the generic family math, used by the MathML elements): the fallback characters, the
# combining marks, the letterlike symbols, the arrows, the mathematical operators, the miscellaneous technical symbols (the
# parts of the stretchy brackets), the box drawing characters, the geometric shapes, the miscellaneous mathematical symbols,
# the supplemental arrows and operators, and the mathematical alphanumeric symbols (set below)
UNICODES="U+0000-017F,U+0192,U+0218-021B,U+02C6-02C7,U+02C9,U+02D8-02DD,U+0370-03FF,U+0400-04FF,U+2000-206F,U+20AC,U+2122,U+2190-2195,U+2212,U+2215,U+FEFF,U+FFFD"

MATH_UNICODES="$UNICODES,U+0300-036F,U+2070-209F,U+20D0-20FF,U+2100-214F,U+2190-21FF,U+2200-22FF,U+2300-23FF,U+2500-257F,U+25A0-25FF,U+27C0-27FF,U+2900-2AFF,U+2B00-2BFF,U+1D400-1D7FF,U+1EE00-1EEFF"

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="$root/src/Meziantou.Framework.WebPlatform/Css/Fonts/Bundled"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

python3 -m venv "$work/venv"
"$work/venv/bin/pip" install --quiet "fonttools==$FONTTOOLS_VERSION" "brotli==$BROTLI_VERSION"

download() {
  curl -fsSL -o "$2" "$1"
}

google="https://raw.githubusercontent.com/google/fonts/$GOOGLE_FONTS_COMMIT"
download "$google/ofl/arimo/Arimo%5Bwght%5D.ttf" "$work/Arimo-VF.ttf"
download "$google/ofl/arimo/Arimo-Italic%5Bwght%5D.ttf" "$work/Arimo-Italic-VF.ttf"
download "$google/ofl/arimo/OFL.txt" "$out/Arimo-OFL.txt"
for style in Regular Bold Italic BoldItalic; do
  download "$google/ofl/tinos/Tinos-$style.ttf" "$work/Tinos-$style.ttf"
  download "$google/ofl/cousine/Cousine-$style.ttf" "$work/Cousine-$style.ttf"
done
download "https://raw.githubusercontent.com/googlefonts/tinos/$TINOS_COMMIT/OFL.txt" "$out/Tinos-OFL.txt"
download "$google/ofl/cousine/OFL.txt" "$out/Cousine-OFL.txt"
# STIX Two Math (math): its MATH table gives the layout constants, the italic corrections and the stretchy glyphs of MathML
download "$google/ofl/stixtwomath/STIXTwoMath-Regular.ttf" "$work/STIXTwoMath-Regular.math"
download "$google/ofl/stixtwomath/OFL.txt" "$out/STIXTwoMath-OFL.txt"
download "https://raw.githubusercontent.com/web-platform-tests/wpt/$WPT_COMMIT/fonts/Ahem.ttf" "$out/Ahem.ttf"
# The license of Ahem, referenced by the license URL of its name table
download "https://www.w3.org/Style/CSS/Test/Fonts/Ahem/COPYING" "$out/Ahem-COPYING.txt"

# The subsets keep the kerning (the only layout feature of the shaper), drop the hinting (the glyphs are not hinted by the
# rasterizer) and keep the names (the copyright and license strings)
for font in "$work"/*.ttf; do
  name="$(basename "$font" .ttf)"
  "$work/venv/bin/pyftsubset" "$font" --unicodes="$UNICODES" --layout-features=kern --no-hinting --name-IDs='*' \
    --name-languages='*' --drop-tables+=Debg --flavor=woff2 --output-file="$out/$name.woff2"
done

# The math font keeps its MATH table (the subsetter keeps the size variants and the assembly parts of the kept glyphs) and
# the features of the math layout: the script style alternates (ssty), the right-to-left mirrored forms (rtlm), the
# dotless forms (dtls) and the flattened accents (flac)
"$work/venv/bin/pyftsubset" "$work/STIXTwoMath-Regular.math" --unicodes="$MATH_UNICODES" --layout-features=kern,ssty,rtlm,dtls,flac \
  --no-hinting --name-IDs='*' --name-languages='*' --drop-tables+=Debg --flavor=woff2 --output-file="$out/STIXTwoMath-Regular.woff2"

ls -l "$out"
