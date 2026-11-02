using System.ComponentModel.DataAnnotations;
using Meziantou.Framework.DataAnnotations;

namespace Meziantou.Framework.Tests.DataAnnotations;

public sealed class AbsoluteUriAttributeTests
{
    [Fact]
    public void Documentation_Validate()
    {
        #region AbsoluteUriAttribute_Validate
        var attribute = new AbsoluteUriAttribute();

        Assert.True(attribute.IsValid("https://www.example.com/"));
        Assert.False(attribute.IsValid("images/logo.png"));

        // null and values that are not strings are valid: add [Required] to reject null
        Assert.True(attribute.IsValid(null));

        var result = attribute.GetValidationResult("images/logo.png", new ValidationContext(new object()));
        Assert.NotNull(result);
        Assert.Equal("Uri 'images/logo.png' is not an absolute URI", result.ErrorMessage);
        #endregion
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("https://www.meziantou.net/", true)]
    [InlineData("relative/path", false)]
    [InlineData("not a uri", false)]
    [InlineData("file:///etc/passwd", true)]
    [InlineData("/etc/passwd", false)]
    [InlineData(@"C:\Windows", false)]
    [InlineData(@"\\server\share", false)]
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

    [Fact]
    public void CustomErrorMessage()
    {
        var model = new CustomMessageModel { Url = "relative" };

        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true));

        var result = Assert.Single(results);
        Assert.Equal("Url must be absolute", result.ErrorMessage);
        Assert.Equal([nameof(CustomMessageModel.Url)], result.MemberNames);
    }

    [Fact]
    public void DefaultErrorMessage_HasMemberName()
    {
        var model = new MyModel { Url = "relative" };

        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true));

        var result = Assert.Single(results);
        Assert.Equal("Uri 'relative' is not an absolute URI", result.ErrorMessage);
        Assert.Equal([nameof(MyModel.Url)], result.MemberNames);
    }

#pragma warning disable CA1056 // URI-like properties should not be strings: the attribute validates a string property
    private sealed class CustomMessageModel
    {
        [AbsoluteUri("{0} must be absolute")]
        public string? Url { get; set; }
    }
#pragma warning restore CA1056
}
