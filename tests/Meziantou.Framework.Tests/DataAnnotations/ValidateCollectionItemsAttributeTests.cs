using System.ComponentModel.DataAnnotations;
using Meziantou.Framework.DataAnnotations;

namespace Meziantou.Framework.Tests.DataAnnotations;

public sealed class ValidateCollectionItemsAttributeTests
{
    [Fact]
    public void Documentation_TryValidateObject()
    {
        #region ValidateCollectionItemsAttribute_TryValidateObject
        var model = new MyModel
        {
            Items = [new ChildModel { Name = "valid" }, new ChildModel { Name = null }],
        };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        // The result is the first error, and its member name tells which item is invalid
        Assert.False(isValid);
        var error = Assert.Single(results);
        Assert.Equal("The Name field is required.", error.ErrorMessage);
        Assert.Equal(["Items[1].Name"], error.MemberNames);
        #endregion
    }

    [Fact]
    public void ReportsItemsViolatingNonRequiredAttributes()
    {
        var model = new Model { Items = [new Item { Name = "way too long", Age = 9999 }] };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void ReportsItemsViolatingRequiredAttributes()
    {
        var model = new Model { Items = [new Item { Name = null, Age = 1 }] };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        Assert.False(isValid);
    }

    [Fact]
    public void AcceptsValidItems()
    {
        var model = new Model { Items = [new Item { Name = "ok", Age = 5 }] };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        Assert.True(isValid);
        Assert.Empty(results);
    }

    [Fact]
    public void AcceptsNullCollection()
    {
        var model = new Model { Items = null };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        Assert.True(isValid);
    }

    [Fact]
    public void ValidateCollectionItemsAttribute_Example()
    {
        var model = new MyModel { Items = [new ChildModel { Name = "valid" }, new ChildModel { Name = null }] };

        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.NotEmpty(results);
    }

#pragma warning disable CA1002 // Do not expose generic lists: the example shows a typical model
    #region ValidateCollectionItemsAttribute_Usage
    public sealed class MyModel
    {
        // Validates each item using the data annotations of ChildModel
        [ValidateCollectionItems]
        public List<ChildModel>? Items { get; set; }
    }

    public sealed class ChildModel
    {
        [Required]
        public string? Name { get; set; }
    }
    #endregion
#pragma warning restore CA1002

    private sealed class Model
    {
        [ValidateCollectionItems]
        public List<Item>? Items { get; set; }
    }

    private sealed class Item
    {
        [Required]
        [StringLength(3)]
        public string? Name { get; set; }

        [Range(1, 10)]
        public int Age { get; set; }
    }

    [Fact]
    public void NullItemsAreSkipped()
    {
        var model = new Model { Items = [null!, new Item { Name = "ok", Age = 5 }] };

        var results = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true));
    }

    [Fact]
    public void ReportsAllErrorsWithItemPaths()
    {
        var model = new Model { Items = [new Item { Name = "ok", Age = 5 }, new Item { Name = null, Age = 99 }, new Item { Name = "toolong", Age = 1 }] };

        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true));

        var result = Assert.IsType<CollectionItemsValidationResult>(Assert.Single(results));
        Assert.Equal(["Items[1].Name"], result.MemberNames);
        Assert.Equal(
            ["Items[1].Name", "Items[1].Age", "Items[2].Name"],
            result.Errors.SelectMany(error => error.MemberNames).ToArray());
    }

    [Fact]
    public void CustomErrorMessage()
    {
        var model = new CustomMessageModel { Items = [new Item { Name = null, Age = 99 }] };

        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true));

        var result = Assert.Single(results);
        Assert.Equal("Items contains invalid values", result.ErrorMessage);
        Assert.Equal(["Items[0].Name", "Items[0].Age"], result.MemberNames);
    }

    [Fact]
    public void FlowsServiceProviderAndItems()
    {
        var model = new ServiceModel { Items = [new ServiceItem()] };
        var services = new ServiceProviderStub();
        var context = new ValidationContext(model, services, new Dictionary<object, object?> { ["key"] = "value" });

        var results = new List<ValidationResult>();
        Assert.True(Validator.TryValidateObject(model, context, results, validateAllProperties: true));
        Assert.Same(services, ServiceItem.LastServiceProvider);
        Assert.Equal("value", ServiceItem.LastItemValue);
    }

    private sealed class CustomMessageModel
    {
        [ValidateCollectionItems(ErrorMessage = "{0} contains invalid values")]
        public List<Item>? Items { get; set; }
    }

    private sealed class ServiceModel
    {
        [ValidateCollectionItems]
        public List<ServiceItem>? Items { get; set; }
    }

    private sealed class ServiceItem : IValidatableObject
    {
        public static object? LastServiceProvider { get; private set; }

        public static object? LastItemValue { get; private set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            LastServiceProvider = validationContext.GetService(typeof(ServiceProviderStub));
            LastItemValue = validationContext.Items["key"];
            return [];
        }
    }

    private sealed class ServiceProviderStub : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(ServiceProviderStub) ? this : null;
    }
}
