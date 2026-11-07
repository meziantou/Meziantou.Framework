# Generates ComplexShapingTest.ttf, the synthetic font of ComplexScriptShapingTests: box glyphs (each one a rectangle, the
# marks have no advance) without any OpenType layout table, for the complex-script processing that does not depend on the
# features of the font: the Thai SARA AM decomposition and the PUA fallback, the Hebrew presentation forms, the Hangul jamo
# composition, the Khmer split vowels and the Arabic fallback shaping (pip install fonttools==4.60.2;
# python make-complex-shaping-test-font.py). The glyph order is fixed: the tests use the glyph ids.
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

glyphs = [
    # name, code point, advance
    (".notdef", None, 500),
    ("space", 0x20, 250),
    ("dottedcircle", 0x25CC, 500),
    # Thai
    ("ko_kai", 0x0E01, 500),
    ("po_pla", 0x0E1B, 500),
    ("yo_ying", 0x0E0D, 500),
    ("sara_aa", 0x0E32, 400),
    ("sara_am", 0x0E33, 400),
    ("nikhahit", 0x0E4D, 0),
    ("mai_ek", 0x0E48, 0),
    ("sara_u", 0x0E38, 0),
    ("mai_ek.sdl", 0xF705, 0),
    ("yo_ying.rd", 0xF70F, 500),
    # Hebrew
    ("bet", 0x05D1, 500),
    ("dagesh", 0x05BC, 0),
    ("bet_dagesh", 0xFB31, 500),
    ("shin", 0x05E9, 500),
    ("shin_dot", 0x05C1, 0),
    ("shin_shindot", 0xFB2A, 500),
    # Hangul
    ("kiyeok", 0x1100, 500),
    ("a", 0x1161, 500),
    ("kiyeok_final", 0x11A8, 500),
    ("ga", 0xAC00, 1000),
    ("gag", 0xAC01, 1000),
    ("tone", 0x302E, 300),
    # Khmer
    ("ka", 0x1780, 500),
    ("e", 0x17C1, 300),
    ("oe", 0x17BE, 300),
    # Arabic
    ("alef", 0x0627, 300),
    ("beh", 0x0628, 500),
    ("lam", 0x0644, 400),
    ("alef.isol", 0xFE8D, 310),
    ("alef.fina", 0xFE8E, 320),
    ("beh.isol", 0xFE8F, 510),
    ("beh.fina", 0xFE90, 520),
    ("beh.init", 0xFE91, 530),
    ("beh.medi", 0xFE92, 540),
    ("lam.isol", 0xFEDD, 410),
    ("lam.fina", 0xFEDE, 420),
    ("lam.init", 0xFEDF, 430),
    ("lam.medi", 0xFEE0, 440),
    ("lam_alef.isol", 0xFEFB, 600),
    ("lam_alef.fina", 0xFEFC, 610),
]

builder = FontBuilder(1000, isTTF=True)
builder.setupGlyphOrder([name for name, _, _ in glyphs])
builder.setupCharacterMap({code: name for name, code, _ in glyphs if code is not None})
outlines = {}
for name, _, advance in glyphs:
    pen = TTGlyphPen(None)
    if name != "space":
        width = advance if advance > 0 else 200
        left = 50 if advance > 0 else -250
        pen.moveTo((left, 0))
        pen.lineTo((left, 700))
        pen.lineTo((left + width - 100, 700))
        pen.lineTo((left + width - 100, 0))
        pen.closePath()
    outlines[name] = pen.glyph()
builder.setupGlyf(outlines)
builder.setupHorizontalMetrics({name: (advance, 50 if advance > 0 else -250) for name, _, advance in glyphs})
builder.setupHorizontalHeader(ascent=800, descent=-200)
builder.setupNameTable({"familyName": "ComplexShapingTest", "styleName": "Regular", "fullName": "ComplexShapingTest Regular", "psName": "ComplexShapingTest-Regular"})
builder.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
builder.setupPost()
builder.save("ComplexShapingTest.ttf")
