namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>
/// Identifies the metadata entity a symbol was read from. A metadata token is only meaningful within the module that owns it,
/// so it is always paired with the module version ID (MVID). Tokens are not stable across builds.
/// </summary>
/// <param name="ModuleVersionId">The MVID of the module that defines the entity.</param>
/// <param name="ModuleName">The name of the module that defines the entity (e.g. <c>MyLibrary.dll</c>).</param>
/// <param name="MetadataToken">The metadata token of the entity (e.g. a <c>TypeDef</c>, <c>MethodDef</c>, <c>Field</c>, <c>Property</c> or <c>Event</c> token).</param>
public sealed record PublicApiMetadataOrigin(Guid ModuleVersionId, string ModuleName, int MetadataToken);
