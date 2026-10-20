using System.ComponentModel.DataAnnotations;
using Meziantou.Framework.DataAnnotations;

namespace Meziantou.Framework.Tests.DataAnnotations;

public sealed class AbsoluteUriAttributeTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("https://www.meziantou.net/", true)]
    [InlineData("relative/path", false)]
    [InlineData("not a uri", false)]
    public void Validate(string? url, bool expectedIsValid)
    {
        var model = new MyModel { Url = url };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        Assert.Equal(expectedIsValid, isValid);
    }

#pragma warning disable CA1056 // URI-like properties should not be strings: the attribute validates a string property
    #region AbsoluteUriAttribute_Usage
    public sealed class MyModel
    {
        [AbsoluteUri]
        public string? Url { get; set; }
    }
    #endregion
#pragma warning restore CA1056
}
