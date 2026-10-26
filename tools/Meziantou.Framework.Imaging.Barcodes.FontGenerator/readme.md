# Meziantou.Framework.Imaging.Barcodes.FontGenerator

Generates `src/Meziantou.Framework.Imaging.Barcodes/Fonts/OcrA.ttf` and `OcrB.ttf`, the fonts of `BarcodeFonts`, from the
character shapes of the OCR standards. The tests of `Meziantou.Framework.Imaging.Barcodes` fail when the committed fonts
are not what the generator builds.

```bash
dotnet run --project tools/Meziantou.Framework.Imaging.Barcodes.FontGenerator -- generate
dotnet run --project tools/Meziantou.Framework.Imaging.Barcodes.FontGenerator -- specimen <directory>
```

## Sources

Both standards are published by NIST as Federal Information Processing Standards Publication 32-1:

- OCR-A: ANSI X3.17-1981, https://nvlpubs.nist.gov/nistpubs/Legacy/FIPS/fipspub32-1-1981.pdf
- OCR-B: ANSI X3.49-1975, https://nvlpubs.nist.gov/nistpubs/Legacy/FIPS/fipspub32-1-1975.pdf

The glyphs come from these documents only: no other OCR font, nor the source of another font generator, was used.

## OCR-A

`OcrA*.cs` transcribe the drawings of Fig. 1 to 94 (one comment per figure): the stroke centerlines, in the fractions of
the nominal centerline height `H` and width `W` the drawings give (the small letters in thousandths of an inch), the
radii of Table 1, and the character displacements `ΔX`. A corner the drawings mark with a radius of 1/2 T is a round
join of the pen; a corner they draw square is a mitered join.

## OCR-B

The drawings of OCR-B are not dimensioned. `OcrB.Centerlines.cs` holds the centerlines traced from the 4:1
illustrations of the standard (Fig. 141 and 142, pages 54 and 55 of the PDF), which are not committed. To trace them
again:

```bash
pdftoppm -r 600 -png -f 54 -l 55 fipspub32-1-1975.pdf page
dotnet run --project tools/Meziantou.Framework.Imaging.Barcodes.FontGenerator -- digitize page-54.png page-55.png tools/Meziantou.Framework.Imaging.Barcodes.FontGenerator/OcrB.Centerlines.cs
dotnet run --project tools/Meziantou.Framework.Imaging.Barcodes.FontGenerator -- review page-54.png page-55.png 0 review.png
```

`OcrBDigitizer` finds the rows of the illustration and the cells of the 2.54 mm pitch, thins each character to its
skeleton and traces it into polylines (`Skeleton`). The dots and the characters that are not strokes of a constant width
(`,` `;` `'` `_`) are traced as outlines instead. `review` draws the outline of each glyph over the illustration, in red.

## Fonts

`OutlineBuilder` strokes the centerlines with a pen of the nominal stroke width (0.35 mm; 0.31 mm for the small letters
of OCR-B) and merges the strokes into contours that do not overlap, with `VectorPath.Widen` and `VectorPath.Combine` of
Meziantou.Framework.Imaging. The TrueType files are written by the `FontBuilder` of the corpus generator. The advance is
600 units of a 1000-unit em: the 0.1 inch pitch.
