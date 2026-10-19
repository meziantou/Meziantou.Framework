# Meziantou.Framework.Fonts

The fully managed OpenType font reader and text shaper of [`Meziantou.Framework.Imaging`](https://www.nuget.org/packages/Meziantou.Framework.Imaging) and [`Meziantou.Framework.WebPlatform`](https://www.nuget.org/packages/Meziantou.Framework.WebPlatform).

This package has no public API: install `Meziantou.Framework.Imaging` to load fonts and draw text. The packages that use it depend on its exact version, so update them together.

What it covers:

- TrueType and OpenType fonts and font collections, WOFF 1.0 and WOFF 2.0
- TrueType, CFF and CFF2 outlines, variable fonts (`fvar`, `avar` 1 and 2, `gvar`, `HVAR`, `VVAR`, `MVAR`)
- Color fonts: `COLR` versions 0 and 1, `CPAL`, `sbix`, `CBDT`
- Horizontal and vertical metrics, `BASE` baselines
- Text shaping with `GSUB` and `GPOS`, and the shaping models of complex scripts (Arabic, Hebrew, Indic, Khmer, Myanmar, Thai and Lao, Hangul, and the universal shaping engine)

It is written from the specifications and checked against HarfBuzz and FreeType.
