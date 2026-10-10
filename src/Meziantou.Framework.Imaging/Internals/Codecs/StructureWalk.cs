namespace Meziantou.Framework.Imaging.Internals;

/// <summary>How far a container structure parser walks.</summary>
internal enum StructureWalk
{
    /// <summary>Stops before the first pixel payload (<see cref="ImageIdentifyMode.Header"/>), without consuming it.</summary>
    Header,

    /// <summary>Walks the whole structure, validating it and charging each frame, without delivering or decoding pixel payloads (<see cref="ImageIdentifyMode.FullScan"/>).</summary>
    FullScan,

    /// <summary>Walks the structure for a decoder: pixel payloads are delivered to the decode observer, which charges frames and may stop early (frame limit).</summary>
    Decode,
}
