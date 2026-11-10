namespace Meziantou.Framework.Diagrams;

/// <summary>A domain of the Cynefin framework: how cause and effect relate in a situation, and so how to respond to it.</summary>
public enum CynefinDomain
{
    /// <summary>Cause and effect can only be understood in retrospect: probe, sense, respond (top left).</summary>
    Complex,

    /// <summary>Cause and effect need analysis or expertise: sense, analyse, respond (top right).</summary>
    Complicated,

    /// <summary>Cause and effect are obvious: sense, categorise, respond (bottom right).</summary>
    Clear,

    /// <summary>No cause and effect can be perceived: act, sense, respond (bottom left).</summary>
    Chaotic,

    /// <summary>The domain is not known yet, also known as disorder (center).</summary>
    Confusion,
}
