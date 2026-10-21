namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// Creates the functions of a lazy accessor property (<see cref="IJsAdapter{TValue}.DefineLazyAccessorProperty(TValue, TValue, IJsLazyAccessor{TValue}, bool, bool, bool)"/>):
/// one object per property, which the adapter can give to the engine as the state of shared callbacks, instead of
/// closures and their delegates.
/// </summary>
/// <typeparam name="TValue">The type of the JavaScript values of the engine.</typeparam>
public interface IJsLazyAccessor<TValue>
    where TValue : class
{
    /// <summary>Creates the getter of the property.</summary>
    TValue CreateGetter();

    /// <summary>Creates the setter of the property (only called when the property has a setter).</summary>
    TValue CreateSetter();
}
