namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A reference between items (ISO/IEC 14496-12, section 8.11.12): its type, the item it comes from and the items it points to, in order.</summary>
internal sealed record HeifItemReference(uint Type, uint FromItemId, uint[] ToItemIds);
