namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>A named instance of a variable font: a name and a value per axis.</summary>
internal sealed record FontNamedInstanceDefinition(string Name, double[] Coordinates, string? PostScriptName = null);
