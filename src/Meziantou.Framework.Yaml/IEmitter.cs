using Meziantou.Framework.Yaml.Events;

namespace Meziantou.Framework.Yaml;

/// <summary>Represents a YAML stream emitter.</summary>
public interface IEmitter
{
    /// <summary>Emits an event.</summary>
    void Emit(ParsingEvent @event);
}