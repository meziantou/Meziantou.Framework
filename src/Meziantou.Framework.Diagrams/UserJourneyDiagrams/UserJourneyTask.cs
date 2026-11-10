namespace Meziantou.Framework.Diagrams;

/// <summary>A task of a user journey: a step, with the satisfaction of its actors and the actors who take part.</summary>
public sealed class UserJourneyTask
{
    /// <summary>Initializes a new instance of the <see cref="UserJourneyTask"/> class.</summary>
    /// <param name="name">The name.</param>
    /// <param name="score">The satisfaction, from 1 (unhappy) to 5 (happy).</param>
    public UserJourneyTask(string name, double score)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Score = score;
    }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the satisfaction, from 1 (unhappy) to 5 (happy): the higher, the higher the face; above 3 it smiles, below 3 it is sad.</summary>
    public double Score { get; set; }

    /// <summary>Gets the actors who take part, drawn as colored dots on the task.</summary>
    public IList<string> Actors { get; } = new List<string>();

    /// <summary>Returns the name of the task.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
