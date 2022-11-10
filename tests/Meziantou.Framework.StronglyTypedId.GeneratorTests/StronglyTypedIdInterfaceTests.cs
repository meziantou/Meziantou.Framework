namespace Meziantou.Framework.StronglyTypedId.GeneratorTests;

using System.Reflection;
using FluentAssertions;
using global::Xunit;

public sealed partial class StronglyTypedIdInterfaceTests
{
    private const string IStronglyTypedIdInterfaceName = "IStronglyTypedId`1";

    [Fact]
    public void PropertyShould_Be_StronglyTypedId()
    {
        var notAStronglyTypedId = new EntityWithGuidStronglyTypedId().GetType()
            .GetProperties()
            .FirstOrDefault(_ => _.Name == "BookId");

        notAStronglyTypedId.Should()
            .NotBeNull();

        notAStronglyTypedId.IsStronglyTypedId()
            .Should()
            .BeTrue();

        notAStronglyTypedId.GetTypeOfStronglyTypedId()
            .Should()
            .Be<Guid>();
    }

    [Fact]
    public void PropertyShould_BeStronglyTypedId()
    {
        var firstProperty = new EntityWithGuidStronglyTypedId().GetType()
            .GetProperties()
            .FirstOrDefault(_ => _.IsStronglyTypedId());

        firstProperty.Should()
            .NotBeNull();

        firstProperty.IsStronglyTypedId()
            .Should()
            .BeTrue();
    }

    [Fact]
    public void PropertyShould_Not_Be_StronglyTypedId()
    {
        var notAStronglyTypedId = new EntityWithoutStronglyTypedId().GetType()
            .GetProperties()
            .FirstOrDefault(_ => _.PropertyType == typeof(Guid));

        notAStronglyTypedId.Should()
            .NotBeNull();

        notAStronglyTypedId.IsStronglyTypedId()
            .Should()
            .BeFalse();

        var shouldThrow = () =>
        {
            var result = notAStronglyTypedId.GetTypeOfStronglyTypedId();
        };

        shouldThrow.Should()
            .Throw<InvalidDataException>();
    }

    [Fact]
    public void PropertyShould_Not_BeStronglyTypedId()
    {
        var firstProperty = new EntityWithoutStronglyTypedId().GetType()
            .GetProperties()
            .First();

        firstProperty.IsStronglyTypedId()
            .Should()
            .BeFalse();
    }


    [Fact]
    public void ShouldBe_GuidType_Of_StronglyTypedId()
    {
        var typeOfStronglyTypedId = new EntityWithGuidStronglyTypedId().BookId.GetType()
            .GetTypeOfStronglyTypedId();

        typeOfStronglyTypedId.Should()
            .Be<Guid>();
    }

    [Fact]
    public void ShouldBe_Have_StronglyTypedId_Property()
    {
        var hasStronglyTypedId = new EntityWithGuidStronglyTypedId();

        hasStronglyTypedId.GetType()
            .HasStronglyTypedIdProperty()
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ShouldBe_Not_Have_StronglyTypedId_Proeprty()
    {
        var notAStronglyTypedId = new EntityWithoutStronglyTypedId();

        notAStronglyTypedId.GetType()
            .HasStronglyTypedIdProperty()
            .Should()
            .BeFalse();
    }


    [Fact]
    public void ShouldImplement_Interface_IStronglyTypedId()
    {
        var newStronglyTypedIdValueObject = IdStringWithInterface.FromString(Guid.NewGuid()
            .ToString());
        var type = newStronglyTypedIdValueObject.GetType();
        var interfaces = type.GetInterfaces();

        interfaces.Should()
            .Contain(_ => _.Name == IStronglyTypedIdInterfaceName);
    }

    [Fact]
    public void ShouldImplement_Interface_StronglyTypedId_AsGuid()
    {
        var someEntity = new EntityWithGuidStronglyTypedId();

        someEntity.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Should()
            .Contain(_ => _.IsStronglyTypedId());
    }

    [Fact]
    public void ShouldThrowIfNotStronglyTypedId()
    {
        var notAStronglyTypedId = new EntityWithoutStronglyTypedId();
        var getTypeOfStronglyTypedId = () =>
        {
            notAStronglyTypedId.BookId.GetType()
                .GetTypeOfStronglyTypedId();
        };

        getTypeOfStronglyTypedId.Should()
            .Throw<InvalidDataException>();
    }


    [Fact]
    public void TestClassesShouldBeValid()
    {
        var notAStronglyTypedId = new EntityWithoutStronglyTypedId();
        var hasStronglyTypedId = new EntityWithGuidStronglyTypedId();

        notAStronglyTypedId.GetType()
            .GetProperties()
            .First(_ => _.Name == "BookId")
            .PropertyType.Should()
            .Be<Guid>();

        notAStronglyTypedId.GetType()
            .GetProperties()
            .First(_ => _.Name == "BookName")
            .PropertyType.Should()
            .Be<string>();

        hasStronglyTypedId.GetType()
            .GetProperties()
            .First(_ => _.Name == "BookId")
            .PropertyType.Should()
            .NotBe<Guid>();

        hasStronglyTypedId.GetType()
            .GetProperties()
            .First(_ => _.Name == "BookName")
            .PropertyType.Should()
            .Be<string>();
    }
#nullable enable
    [StronglyTypedId(typeof(bool))]
    private partial struct IdBooleanWithInterface
    {
    }

    [StronglyTypedId(typeof(byte))]
    private partial struct IdByteWithInterface
    {
    }

    [StronglyTypedId(typeof(DateTime))]
    private partial struct IdDateTimeWithInterface
    {
    }

    [StronglyTypedId(typeof(DateTimeOffset))]
    private partial struct IdDateTimeOffsetWithInterface
    {
    }

    [StronglyTypedId(typeof(decimal))]
    private partial struct IdDecimalWithInterface
    {
    }

    [StronglyTypedId(typeof(double))]
    private partial struct IdDoubleWithInterface
    {
    }

    [StronglyTypedId(typeof(Guid))]
    private partial struct IdGuidWithInterface
    {
    }

    [StronglyTypedId(typeof(short))]
    private partial struct IdInt16WithInterface
    {
    }

    [StronglyTypedId(typeof(int))]
    private partial struct IdInt32WithInterface
    {
    }

    [StronglyTypedId(typeof(long))]
    private partial struct IdInt64WithInterface
    {
    }

    [StronglyTypedId(typeof(sbyte))]
    private partial struct IdSByteWithInterface
    {
    }

    [StronglyTypedId(typeof(float))]
    private partial struct IdSingleWithInterface
    {
    }

    [StronglyTypedId(typeof(string))]
    private partial struct IdStringWithInterface
    {
    }

    [StronglyTypedId(typeof(ushort))]
    private partial struct IdUInt16WithInterface
    {
    }

    [StronglyTypedId(typeof(uint))]
    private partial struct IdUInt32WithInterface
    {
    }

    [StronglyTypedId(typeof(ulong))]
    private partial struct IdUInt64WithInterface
    {
    }

    private class EntityWithoutStronglyTypedId
    {
        public EntityWithoutStronglyTypedId()
        {
            this.BookId = Guid.NewGuid();

            this.BookName = Guid.NewGuid()
                .ToString();
        }

        public Guid BookId { get; }

        public string BookName { get; }
    }

    private class EntityWithGuidStronglyTypedId
    {
        public EntityWithGuidStronglyTypedId()
        {
            this.BookId = IdGuidWithInterface.FromGuid(Guid.NewGuid());
            this.BookName = Guid.NewGuid()
                .ToString();
        }

        public IdGuidWithInterface BookId { get; }

        public string BookName { get; }
    }
}