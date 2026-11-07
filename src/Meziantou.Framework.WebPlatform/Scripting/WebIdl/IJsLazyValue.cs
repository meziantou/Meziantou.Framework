namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// Creates the value of a lazy data property (<see cref="IJsAdapter{TValue}.DefineLazyDataProperty(TValue, TValue, IJsLazyValue{TValue}, bool, bool, bool)"/>):
/// one object per property, which the adapter can give to the engine as the state of a shared callback, instead of a
/// closure and its delegate.
/// </summary>
/// <typeparam name="TValue">The type of the JavaScript values of the engine.</typeparam>
public interface IJsLazyValue<TValue>
    where TValue : class
{
    /// <summary>Creates the value of the property.</summary>
    TValue CreateValue();
}
