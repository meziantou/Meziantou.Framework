using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml.Tests.Serialization;
public class YamlRuntimeDerivedTypeTests
{
    // ---- Model types: no [YamlDerivedType] attributes on base ----

    private abstract class Vehicle
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class Car : Vehicle
    {
        public int Doors { get; set; }
    }

    private sealed class Truck : Vehicle
    {
        public double PayloadTons { get; set; }
    }

    private sealed class Motorcycle : Vehicle
    {
        public bool HasSidecar { get; set; }
    }

    // Base class with [YamlPolymorphic] but no [YamlDerivedType]
    [YamlPolymorphic(DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag)]
    private abstract class Sensor
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class TemperatureSensor : Sensor
    {
        public double MaxTemp { get; set; }
    }

    private sealed class PressureSensor : Sensor
    {
        public double MaxPsi { get; set; }
    }

    // Base with attribute-based derived types AND runtime derived types
    [YamlPolymorphic]
    [YamlDerivedType(typeof(Circle), "circle")]
    private abstract class Shape
    {
        public string Color { get; set; } = string.Empty;
    }

    private sealed class Circle : Shape
    {
        public double Radius { get; set; }
    }

    private sealed class Square : Shape
    {
        public double Side { get; set; }
    }

    [YamlPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [YamlDerivedType(typeof(JsonCircle), "circle")]
    private abstract class JsonShape
    {
        public string Color { get; set; } = string.Empty;
    }

    private sealed class JsonCircle : JsonShape
    {
        public double Radius { get; set; }
    }

    private sealed class JsonSquare : JsonShape
    {
        public double Side { get; set; }
    }

    // Interface-based polymorphism
    private interface IPlugin
    {
        string Name { get; set; }
    }

    [SuppressMessage("Design", "MA0182: Internal type is apparently never used", Justification = "Used in tests")]
    private sealed class AudioPlugin : IPlugin
    {
        public string Name { get; set; } = string.Empty;
        public int Channels { get; set; }
    }

    [SuppressMessage("Design", "MA0182: Internal type is apparently never used", Justification = "Used in tests")]
    private sealed class VideoPlugin : IPlugin
    {
        public string Name { get; set; } = string.Empty;
        public int Width { get; set; }
    }

    // Default derived type (no discriminator)
    private abstract class Notification
    {
        public string Message { get; set; } = string.Empty;
    }

    private sealed class EmailNotification : Notification
    {
        public string To { get; set; } = string.Empty;
    }

    private sealed class DefaultNotification : Notification
    {
    }

    // Non-assignable type for validation test
    private sealed class Unrelated
    {
    }

    // ---- Property Discriminator Tests ----

    [Fact]
    public void RuntimeDerivedTypesDeserializeWithPropertyDiscriminator()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Car), "car"),
                        new YamlDerivedType(typeof(Truck), "truck"),
                    },
                },
            },
        };

        var yaml = "$type: car\nName: Sedan\nDoors: 4\n";
        var value = YamlSerializer.Deserialize<Vehicle>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<Car>(value);
        Assert.Equal("Sedan", value.Name);
        Assert.Equal(4, ((Car)value).Doors);
    }

    [Fact]
    public void RuntimeDerivedTypesSerializeWithPropertyDiscriminator()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Car), "car"),
                        new YamlDerivedType(typeof(Truck), "truck"),
                    },
                },
            },
        };

        Vehicle vehicle = new Truck { Name = "Semi", PayloadTons = 20.5 };
        var yaml = YamlSerializer.Serialize(vehicle, typeof(Vehicle), options);

        Assert.Contains("$type: truck", yaml);
        Assert.Contains("Name: Semi", yaml);
        Assert.Contains("PayloadTons: 20.5", yaml);
    }

    [Fact]
    public void RuntimeDerivedTypesDeserializeWithDiscriminatorNotFirst()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Car), "car"),
                    },
                },
            },
        };

        var yaml = "Name: Coupe\nDoors: 2\n$type: car\n";
        var value = YamlSerializer.Deserialize<Vehicle>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<Car>(value);
        Assert.Equal("Coupe", value.Name);
        Assert.Equal(2, ((Car)value).Doors);
    }

    // ---- Tag Discriminator Tests ----

    [Fact]
    public void RuntimeDerivedTypesDeserializeWithTagDiscriminator()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Car), "car") { Tag = "!car" },
                        new YamlDerivedType(typeof(Truck), "truck") { Tag = "!truck" },
                    },
                },
            },
        };

        var yaml = "!car\nName: Roadster\nDoors: 2\n";
        var value = YamlSerializer.Deserialize<Vehicle>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<Car>(value);
        Assert.Equal("Roadster", value.Name);
        Assert.Equal(2, ((Car)value).Doors);
    }

    [Fact]
    public void RuntimeDerivedTypesSerializeWithTagDiscriminator()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Car), "car") { Tag = "!car" },
                        new YamlDerivedType(typeof(Motorcycle), "moto") { Tag = "!moto" },
                    },
                },
            },
        };

        Vehicle vehicle = new Motorcycle { Name = "Harley", HasSidecar = true };
        var yaml = YamlSerializer.Serialize(vehicle, typeof(Vehicle), options);

        Assert.Contains("!moto", yaml);
        Assert.DoesNotContain("$type:", yaml);
        Assert.Contains("Name: Harley", yaml);
        Assert.Contains("HasSidecar: true", yaml);
    }

    [Fact]
    public void RuntimeDerivedTypesDeserializeWithBothTagAndProperty()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DiscriminatorStyle = YamlTypeDiscriminatorStyle.Both,
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Car), "car") { Tag = "!car" },
                        new YamlDerivedType(typeof(Truck), "truck") { Tag = "!truck" },
                    },
                },
            },
        };

        var yaml = "!truck\nName: Pickup\nPayloadTons: 1.5\n";
        var value = YamlSerializer.Deserialize<Vehicle>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<Truck>(value);
        Assert.Equal("Pickup", value.Name);
        Assert.Equal(1.5, ((Truck)value).PayloadTons);
    }

    // ---- Base type has [YamlPolymorphic] but no [YamlDerivedType] ----

    [Fact]
    public void BaseWithYamlPolymorphicAttributeUsesRuntimeDerivedTypes()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Sensor)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(TemperatureSensor)) { Tag = "!temp" },
                        new YamlDerivedType(typeof(PressureSensor)) { Tag = "!pressure" },
                    },
                },
            },
        };

        var yaml = "!temp\nId: S1\nMaxTemp: 100.5\n";
        var value = YamlSerializer.Deserialize<Sensor>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<TemperatureSensor>(value);
        Assert.Equal("S1", value.Id);
        Assert.Equal(100.5, ((TemperatureSensor)value).MaxTemp);
    }

    [Fact]
    public void BaseWithYamlPolymorphicAttributeSerializesWithRuntimeDerivedTypes()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Sensor)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(TemperatureSensor)) { Tag = "!temp" },
                        new YamlDerivedType(typeof(PressureSensor)) { Tag = "!pressure" },
                    },
                },
            },
        };

        Sensor sensor = new PressureSensor { Id = "P1", MaxPsi = 300.0 };
        var yaml = YamlSerializer.Serialize(sensor, typeof(Sensor), options);

        Assert.Contains("!pressure", yaml);
        Assert.Contains("Id: P1", yaml);
        Assert.Contains("MaxPsi: 300", yaml);
    }

    // ---- Mixed: Attribute-based + Runtime-based ----

    [Fact]
    public void RuntimeDerivedTypeMergesWithAttributeDerivedTypes()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Shape)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Square), "square"),
                    },
                },
            },
        };

        // Attribute-registered type still works
        var yaml1 = "$type: circle\nColor: red\nRadius: 5\n";
        var circle = YamlSerializer.Deserialize<Shape>(yaml1, options);
        Assert.NotNull(circle);
        Assert.IsType<Circle>(circle);
        Assert.Equal("red", circle.Color);
        Assert.Equal(5.0, ((Circle)circle).Radius);

        // Runtime-registered type also works
        var yaml2 = "$type: square\nColor: blue\nSide: 3\n";
        var square = YamlSerializer.Deserialize<Shape>(yaml2, options);
        Assert.NotNull(square);
        Assert.IsType<Square>(square);
        Assert.Equal("blue", square.Color);
        Assert.Equal(3.0, ((Square)square).Side);
    }

    [Fact]
    public void RuntimeDerivedTypeSerializesMixedAttributeAndRuntime()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Shape)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Square), "square"),
                    },
                },
            },
        };

        Shape shape = new Square { Color = "green", Side = 7 };
        var yaml = YamlSerializer.Serialize(shape, typeof(Shape), options);

        Assert.Contains("$type: square", yaml);
        Assert.Contains("Color: green", yaml);
        Assert.Contains("Side: 7", yaml);
    }

    [Fact]
    public void AttributeDerivedTypeTakesPrecedenceOverRuntime()
    {
        // Both attribute and runtime register Circle, attribute should win
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Shape)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Circle), "circle"), // same as attribute
                        new YamlDerivedType(typeof(Square), "square"),
                    },
                },
            },
        };

        var yaml = "$type: circle\nColor: yellow\nRadius: 2\n";
        var value = YamlSerializer.Deserialize<Shape>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<Circle>(value);
        Assert.Equal(2.0, ((Circle)value).Radius);
    }

    [Fact]
    public void ConflictingRuntimeDiscriminatorIsSkippedWhenAttributeAlreadyOwnsIt()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Shape)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Square), "circle"),
                    },
                },
            },
        };

        var value = YamlSerializer.Deserialize<Shape>("$type: circle\nColor: red\nRadius: 5\n", options);
        Assert.NotNull(value);
        Assert.IsType<Circle>(value);

        var exception = Assert.Throws<NotSupportedException>(
            () => YamlSerializer.Serialize<Shape>(new Square { Color = "blue", Side = 3 }, options));
        Assert.Contains(typeof(Square).ToString(), exception.Message);
    }

    [Fact]
    public void JsonAttributeDerivedTypeTakesPrecedenceOverRuntime()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(JsonShape)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(JsonSquare), "circle"),
                    },
                },
            },
        };

        var value = YamlSerializer.Deserialize<JsonShape>("kind: circle\nColor: red\nRadius: 5\n", options);
        Assert.NotNull(value);
        Assert.IsType<JsonCircle>(value);

        var exception = Assert.Throws<NotSupportedException>(
            () => YamlSerializer.Serialize<JsonShape>(new JsonSquare { Color = "blue", Side = 3 }, options));
        Assert.Contains(typeof(JsonSquare).ToString(), exception.Message);
    }

    // ---- Integer Discriminator ----

    [Fact]
    public void RuntimeDerivedTypesWorkWithIntegerDiscriminator()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Car), 1),
                        new YamlDerivedType(typeof(Truck), 2),
                    },
                },
            },
        };

        var yaml = "$type: 1\nName: Compact\nDoors: 4\n";
        var value = YamlSerializer.Deserialize<Vehicle>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<Car>(value);
        Assert.Equal("Compact", value.Name);
    }

    [Fact]
    public void RuntimeDerivedTypesSerializeWithIntegerDiscriminator()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Car), 1),
                        new YamlDerivedType(typeof(Truck), 2),
                    },
                },
            },
        };

        Vehicle vehicle = new Truck { Name = "Hauler", PayloadTons = 10 };
        var yaml = YamlSerializer.Serialize(vehicle, typeof(Vehicle), options);

        Assert.Contains("$type: 2", yaml);
        Assert.Contains("Name: Hauler", yaml);
    }

    // ---- Default Derived Type (no discriminator) ----

    [Fact]
    public void RuntimeDefaultDerivedTypeDeserializesWhenNoDiscriminator()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Notification)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(EmailNotification), "email"),
                        new YamlDerivedType(typeof(DefaultNotification)),
                    },
                },
            },
        };

        var yaml = "Message: Hello\n";
        var value = YamlSerializer.Deserialize<Notification>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<DefaultNotification>(value);
        Assert.Equal("Hello", value.Message);
    }

    [Fact]
    public void RuntimeDefaultDerivedTypeSerializesWithoutDiscriminator()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Notification)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(EmailNotification), "email"),
                        new YamlDerivedType(typeof(DefaultNotification)),
                    },
                },
            },
        };

        Notification notification = new DefaultNotification { Message = "Test" };
        var yaml = YamlSerializer.Serialize(notification, typeof(Notification), options);

        Assert.DoesNotContain("$type:", yaml);
        Assert.Contains("Message: Test", yaml);
    }

    [Fact]
    public void RuntimeDefaultDerivedTypeDeserializesWithMatchingDiscriminator()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Notification)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(EmailNotification), "email"),
                        new YamlDerivedType(typeof(DefaultNotification)),
                    },
                },
            },
        };

        var yaml = "$type: email\nMessage: Hi\nTo: test@example.com\n";
        var value = YamlSerializer.Deserialize<Notification>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<EmailNotification>(value);
        Assert.Equal("Hi", value.Message);
        Assert.Equal("test@example.com", ((EmailNotification)value).To);
    }

    // ---- Unknown Discriminator Handling ----

    [Fact]
    public void RuntimeDerivedTypesUnknownDiscriminatorFailsByDefault()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Car), "car"),
                    },
                },
            },
        };

        var yaml = "$type: spaceship\nName: USS Enterprise\n";
        Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Vehicle>(yaml, options));
    }

    [Fact]
    public void RuntimeDerivedTypesUnknownDiscriminatorCanFallBackToBase()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                UnknownDerivedTypeHandling = YamlUnknownDerivedTypeHandling.FallBackToBase,
                DerivedTypeMappings =
                {
                    [typeof(Notification)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(EmailNotification), "email"),
                        new YamlDerivedType(typeof(DefaultNotification)),
                    },
                },
            },
        };

        var yaml = "$type: sms\nMessage: Unknown\n";
        var value = YamlSerializer.Deserialize<Notification>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<DefaultNotification>(value);
        Assert.Equal("Unknown", value.Message);
    }

    // ---- Custom Discriminator Property Name ----

    [Fact]
    public void RuntimeDerivedTypesWithCustomDiscriminatorPropertyName()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "kind",
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Car), "car"),
                        new YamlDerivedType(typeof(Truck), "truck"),
                    },
                },
            },
        };

        var yaml = "kind: truck\nName: BigRig\nPayloadTons: 30\n";
        var value = YamlSerializer.Deserialize<Vehicle>(yaml, options);

        Assert.NotNull(value);
        Assert.IsType<Truck>(value);
        Assert.Equal("BigRig", value.Name);
    }

    [Fact]
    public void RuntimeDerivedTypesSerializeWithCustomDiscriminatorPropertyName()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                TypeDiscriminatorPropertyName = "kind",
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Car), "car"),
                    },
                },
            },
        };

        Vehicle vehicle = new Car { Name = "Mini", Doors = 3 };
        var yaml = YamlSerializer.Serialize(vehicle, typeof(Vehicle), options);

        Assert.Contains("kind: car", yaml);
        Assert.DoesNotContain("$type:", yaml);
    }

    // ---- Roundtrip Tests ----

    [Fact]
    public void RuntimeDerivedTypesPropertyDiscriminatorRoundtrip()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Car), "car"),
                        new YamlDerivedType(typeof(Truck), "truck"),
                        new YamlDerivedType(typeof(Motorcycle), "moto"),
                    },
                },
            },
        };

        Vehicle original = new Car { Name = "Tesla", Doors = 4 };
        var yaml = YamlSerializer.Serialize(original, typeof(Vehicle), options);
        var deserialized = YamlSerializer.Deserialize<Vehicle>(yaml, options);

        Assert.NotNull(deserialized);
        Assert.IsType<Car>(deserialized);
        Assert.Equal("Tesla", deserialized.Name);
        Assert.Equal(4, ((Car)deserialized).Doors);
    }

    [Fact]
    public void RuntimeDerivedTypesTagDiscriminatorRoundtrip()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Car), "car") { Tag = "!car" },
                        new YamlDerivedType(typeof(Motorcycle), "moto") { Tag = "!moto" },
                    },
                },
            },
        };

        Vehicle original = new Motorcycle { Name = "Ducati", HasSidecar = false };
        var yaml = YamlSerializer.Serialize(original, typeof(Vehicle), options);
        var deserialized = YamlSerializer.Deserialize<Vehicle>(yaml, options);

        Assert.NotNull(deserialized);
        Assert.IsType<Motorcycle>(deserialized);
        Assert.Equal("Ducati", deserialized.Name);
        Assert.Equal(false, ((Motorcycle)deserialized).HasSidecar);
    }

    // ---- Dictionary with Polymorphic Values ----

    [Fact]
    public void RuntimeDerivedTypesInDictionaryValues()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DiscriminatorStyle = YamlTypeDiscriminatorStyle.Tag,
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Car), "car") { Tag = "!car" },
                        new YamlDerivedType(typeof(Truck), "truck") { Tag = "!truck" },
                    },
                },
            },
        };

        var yaml = "fleet1: !car\n  Name: Sedan\n  Doors: 4\nfleet2: !truck\n  Name: Hauler\n  PayloadTons: 15\n";
        var value = YamlSerializer.Deserialize<Dictionary<string, Vehicle>>(yaml, options);

        Assert.NotNull(value);
        Assert.HasCount(2, value);
        Assert.IsType<Car>(value["fleet1"]);
        Assert.Equal(4, ((Car)value["fleet1"]).Doors);
        Assert.IsType<Truck>(value["fleet2"]);
        Assert.Equal(15.0, ((Truck)value["fleet2"]).PayloadTons);
    }

    // ---- Validation Tests ----

    [Fact]
    public void RuntimeDerivedTypeThrowsWhenTypeNotAssignable()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Unrelated), "unrelated"),
                    },
                },
            },
        };

        var yaml = "$type: unrelated\n";
        var ex = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Vehicle>(yaml, options));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("not assignable", ex.InnerException!.Message);
    }

    [Fact]
    public void YamlDerivedTypeConstructorThrowsOnNullType()
    {
        Assert.Throws<ArgumentNullException>(() => new YamlDerivedType(null!));
        Assert.Throws<ArgumentNullException>(() => new YamlDerivedType(null!, "disc"));
        Assert.Throws<ArgumentNullException>(() => new YamlDerivedType(null!, 1));
    }

    [Fact]
    public void YamlDerivedTypeConstructorThrowsOnNullDiscriminator()
    {
        Assert.Throws<ArgumentNullException>(() => new YamlDerivedType(typeof(Car), (string)null!));
    }

    [Fact]
    public void YamlDerivedTypePropertiesAreSetCorrectly()
    {
        var dt1 = new YamlDerivedType(typeof(Car));
        Assert.Equal(typeof(Car), dt1.DerivedType);
        Assert.Null(dt1.Discriminator);
        Assert.Null(dt1.Tag);

        var dt2 = new YamlDerivedType(typeof(Truck), "truck") { Tag = "!truck" };
        Assert.Equal(typeof(Truck), dt2.DerivedType);
        Assert.Equal("truck", dt2.Discriminator);
        Assert.Equal("!truck", dt2.Tag);

        var dt3 = new YamlDerivedType(typeof(Motorcycle), 42);
        Assert.Equal(typeof(Motorcycle), dt3.DerivedType);
        Assert.Equal("42", dt3.Discriminator);
    }

    // ---- Empty/No Runtime Mappings ----

    [Fact]
    public void EmptyRuntimeMappingsDoNotAffectExistingBehavior()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>(),
                },
            },
        };

        // Empty list should not enable polymorphism — deserializing abstract type with
        // a discriminator should fail because there are no registered derived types.
        Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Vehicle>("$type: car\nName: X\n", options));
    }

    [Fact]
    public void NoRuntimeMappingsDefaultsAreEmpty()
    {
        var options = new YamlPolymorphismOptions();
        Assert.NotNull(options.DerivedTypeMappings);
        Assert.Empty(options.DerivedTypeMappings);
    }

    // ---- Cross-Project Architecture Pattern Test ----

    [Fact]
    public void CrossProjectPolymorphismPatternWorks()
    {
        // This test simulates the cross-project architecture:
        // - Sensor (base) in Core project — has [YamlPolymorphic] for tag style, but no [YamlDerivedType]
        // - TemperatureSensor, PressureSensor in Network project — registered at runtime
        // - Composition happens here (Application project)

        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Sensor)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(TemperatureSensor)) { Tag = "!temp" },
                        new YamlDerivedType(typeof(PressureSensor)) { Tag = "!pressure" },
                    },
                },
            },
        };

        // Deserialize a dictionary of sensors (typical YAML config pattern)
        var yaml = "sensor1: !temp\n  Id: T1\n  MaxTemp: 200\nsensor2: !pressure\n  Id: P1\n  MaxPsi: 500\n";
        var sensors = YamlSerializer.Deserialize<Dictionary<string, Sensor>>(yaml, options);

        Assert.NotNull(sensors);
        Assert.HasCount(2, sensors);

        Assert.IsType<TemperatureSensor>(sensors["sensor1"]);
        Assert.Equal("T1", sensors["sensor1"].Id);
        Assert.Equal(200.0, ((TemperatureSensor)sensors["sensor1"]).MaxTemp);

        Assert.IsType<PressureSensor>(sensors["sensor2"]);
        Assert.Equal("P1", sensors["sensor2"].Id);
        Assert.Equal(500.0, ((PressureSensor)sensors["sensor2"]).MaxPsi);

        // Serialize back
        var outputYaml = YamlSerializer.Serialize(sensors, options);
        Assert.Contains("!temp", outputYaml);
        Assert.Contains("!pressure", outputYaml);
        Assert.Contains("Id: T1", outputYaml);
        Assert.Contains("Id: P1", outputYaml);
    }

    // ---- Multiple Base Types ----

    [Fact]
    public void MultipleBaseTypesCanHaveRuntimeMappings()
    {
        var options = new YamlSerializerOptions
        {
            PolymorphismOptions = new YamlPolymorphismOptions
            {
                DerivedTypeMappings =
                {
                    [typeof(Vehicle)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(Car), "car"),
                    },
                    [typeof(Notification)] = new List<YamlDerivedType>
                    {
                        new YamlDerivedType(typeof(EmailNotification), "email"),
                    },
                },
            },
        };

        var vehicleYaml = "$type: car\nName: Test\nDoors: 2\n";
        var vehicle = YamlSerializer.Deserialize<Vehicle>(vehicleYaml, options);
        Assert.IsType<Car>(vehicle);

        var notifYaml = "$type: email\nMessage: Hello\nTo: user@test.com\n";
        var notif = YamlSerializer.Deserialize<Notification>(notifYaml, options);
        Assert.IsType<EmailNotification>(notif);
    }
}
