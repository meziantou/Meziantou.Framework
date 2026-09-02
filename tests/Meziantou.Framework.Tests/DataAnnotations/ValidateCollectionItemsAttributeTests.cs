using System.ComponentModel.DataAnnotations;
using Meziantou.Framework.DataAnnotations;

namespace Meziantou.Framework.Tests.DataAnnotations;

public sealed class ValidateCollectionItemsAttributeTests
{
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
}
