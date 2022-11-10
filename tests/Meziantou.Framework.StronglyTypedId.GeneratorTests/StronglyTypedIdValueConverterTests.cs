namespace Meziantou.Framework.StronglyTypedId.GeneratorTests;

internal partial class StronglyTypedIdValueConverterTests
{
    // Not Implemented
    public void ShouldConvertToGuid_UsingExtensions()
    {
        /*var expectedGuid = Guid.NewGuid();
        var targetEntity = new EntityWithGuidStronglyTypedId(expectedGuid);
        var stronglyTypedIdProperty = targetEntity.GetType()
            .GetProperties()
            .FirstOrDefault(_ => _.IsStronglyTypedId());

        stronglyTypedIdProperty!.Should()!
            .NotBeNull();

        stronglyTypedIdProperty!.PropertyType!.GetTypeOfStronglyTypedId()!
            .Should()!
            .Be<Guid>();*/
    }
#nullable enable

    [StronglyTypedId(typeof(Guid))]
    private partial struct StronglyTypedGuid
    {
    }


    private class EntityWithGuidStronglyTypedId
    {
        public EntityWithGuidStronglyTypedId(Guid guidValue)
        {
            this.BookId = StronglyTypedGuid.FromGuid(guidValue);
            this.BookName = Guid.NewGuid()
                .ToString();
        }

        public StronglyTypedGuid BookId { get; }

        public string BookName { get; }
    }
}