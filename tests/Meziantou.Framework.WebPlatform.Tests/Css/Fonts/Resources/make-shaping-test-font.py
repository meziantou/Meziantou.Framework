# Generates ShapingTest.ttf, the synthetic font of OpenTypeShapingTests: box glyphs (each one a rectangle) with GSUB, GPOS
# and GDEF lookups compiled by fontTools (the small capitals and superscripts of the font synthesis tests are at the end) (pip install fonttools==4.60.2; python make-shaping-test-font.py).
# The glyph order is fixed: the tests use the glyph ids.
from fontTools.feaLib.builder import addOpenTypeFeaturesFromString
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

glyphs = [
    # name, code point, advance
    (".notdef", None, 500),
    ("space", 0x20, 250),
    ("a", 0x61, 500),
    ("b", 0x62, 500),
    ("c", 0x63, 500),
    ("c.alt", None, 450),
    ("d", 0x64, 500),
    ("d.part1", None, 300),
    ("d.part2", None, 300),
    ("f", 0x66, 400),
    ("i", 0x69, 300),
    ("f_i", None, 650),
    ("x", 0x78, 500),
    ("x.locl", None, 600),
    ("acutecomb", 0x301, 0),
    ("alef", 0x627, 300),
    ("alef.fina", None, 320),
    ("beh", 0x628, 500),
    ("beh.init", None, 400),
    ("beh.medi", None, 410),
    ("beh.fina", None, 420),
    ("lam", 0x644, 400),
    ("lam.init", None, 380),
    ("lam_alef", None, 600),
    ("A", 0x41, 600),
    ("a.sc", None, 450),
    ("b.sups", None, 350),
]

features = """
languagesystem DFLT dflt;
languagesystem latn dflt;
languagesystem latn TRK;
languagesystem arab dflt;

feature ccmp { sub d by d.part1 d.part2; } ccmp;
feature locl { script latn; language TRK exclude_dflt; sub x by x.locl; } locl;
feature liga { sub f i by f_i; } liga;
feature calt { sub c' b by c.alt; } calt;
feature init { sub beh by beh.init; sub lam by lam.init; } init;
feature medi { sub beh by beh.medi; } medi;
feature fina { sub beh by beh.fina; sub alef by alef.fina; } fina;
feature rlig { sub lam.init alef.fina by lam_alef; } rlig;
feature kern { pos a b -100; } kern;
feature smcp { sub a by a.sc; } smcp;
feature sups { sub b by b.sups; } sups;
feature mark { markClass acutecomb <anchor 0 0> @TOP; pos base a <anchor 250 700> mark @TOP; } mark;
feature curs {
    pos cursive beh.init <anchor NULL> <anchor 0 100>;
    pos cursive beh.medi <anchor 410 0> <anchor 0 100>;
    pos cursive beh.fina <anchor 420 0> <anchor NULL>;
} curs;

table GDEF {
    GlyphClassDef [space a b c c.alt d d.part1 d.part2 f i x x.locl alef alef.fina beh beh.init beh.medi beh.fina lam lam.init A a.sc b.sups], [f_i lam_alef], [acutecomb], ;
    LigatureCaretByPos f_i 300;
} GDEF;
"""

builder = FontBuilder(1000, isTTF=True)
builder.setupGlyphOrder([name for name, _, _ in glyphs])
builder.setupCharacterMap({code: name for name, code, _ in glyphs if code is not None})
outlines = {}
for name, _, advance in glyphs:
    pen = TTGlyphPen(None)
    if advance > 0 and name != "space":
        pen.moveTo((50, 0))
        pen.lineTo((50, 700))
        pen.lineTo((advance - 50, 700))
        pen.lineTo((advance - 50, 0))
        pen.closePath()
    elif name == "acutecomb":
        pen.moveTo((-200, 750))
        pen.lineTo((-200, 850))
        pen.lineTo((-50, 850))
        pen.lineTo((-50, 750))
        pen.closePath()
    outlines[name] = pen.glyph()
builder.setupGlyf(outlines)
builder.setupHorizontalMetrics({name: (advance, 50 if advance > 0 else -200) for name, _, advance in glyphs})
builder.setupHorizontalHeader(ascent=800, descent=-200)
builder.setupNameTable({"familyName": "ShapingTest", "styleName": "Regular", "fullName": "ShapingTest Regular", "psName": "ShapingTest-Regular"})
builder.setupOS2(sTypoAscender=800, sTypoDescender=-200, usWinAscent=800, usWinDescent=200)
builder.setupPost()
addOpenTypeFeaturesFromString(builder.font, features)
builder.save("ShapingTest.ttf")
