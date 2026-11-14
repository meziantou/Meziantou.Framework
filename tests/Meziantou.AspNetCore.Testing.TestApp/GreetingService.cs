namespace Meziantou.AspNetCore.Testing.TestApp;

/// <summary>Greets the signed-in user on the profile page; the tests replace it.</summary>
public class GreetingService
{
    public virtual string Greet(string name) => "Hello " + name;
}
