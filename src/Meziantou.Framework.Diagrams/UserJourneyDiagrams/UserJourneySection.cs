namespace Meziantou.Framework.Diagrams;

/// <summary>A section of a user journey: a part of the goal, above its tasks.</summary>
public sealed class UserJourneySection
{
    /// <summary>Initializes a new instance of the <see cref="UserJourneySection"/> class.</summary>
    /// <param name="name">The name. An empty name is a section without title, as the tasks Mermaid reads before the first section.</param>
    public UserJourneySection(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
    }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; }

    /// <summary>Gets the tasks, in order.</summary>
    public IList<UserJourneyTask> Tasks { get; } = new List<UserJourneyTask>();

    /// <summary>Adds a task.</summary>
    /// <param name="name">The name of the task.</param>
    /// <param name="score">The satisfaction, from 1 (unhappy) to 5 (happy).</param>
    /// <param name="actors">The actors who take part.</param>
    /// <returns>The task.</returns>
    public UserJourneyTask AddTask(string name, double score, params IEnumerable<string> actors)
    {
        var task = new UserJourneyTask(name, score);
        foreach (var actor in actors)
        {
            task.Actors.Add(actor);
        }

        Tasks.Add(task);
        return task;
    }

    /// <summary>Returns the name of the section.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name;
}
