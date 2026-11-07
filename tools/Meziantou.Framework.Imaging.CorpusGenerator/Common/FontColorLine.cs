namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>The color line of a gradient: how it continues outside its stops (0 pads, 1 repeats, 2 reflects), and its stops.</summary>
internal sealed record FontColorLine(int Extend, params (double Offset, int PaletteIndex, double Alpha)[] Stops);
