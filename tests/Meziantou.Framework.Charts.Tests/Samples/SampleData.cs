namespace Meziantou.Framework.Charts.Tests;

/// <summary>Deterministic data sets shared by the chart tests.</summary>
internal static class SampleData
{
    public static Sale[] Sales { get; } =
    [
        new("North", 2022, 120), new("North", 2023, 135), new("North", 2024, 150),
        new("South", 2022, 90), new("South", 2023, 110), new("South", 2024, 95),
        new("East", 2022, 60), new("East", 2023, 80), new("East", 2024, 105),
        new("West", 2022, 140), new("West", 2023, 125), new("West", 2024, 160),
    ];

    public static Reading[] Readings { get; } = CreateReadings();

    public static Car[] Cars { get; } =
    [
        new("Aster", 1200, 42, 90, "Europe"), new("Breeze", 1350, 38, 110, "Europe"), new("Cobalt", 1600, 31, 150, "USA"),
        new("Dune", 1900, 24, 220, "USA"), new("Ember", 1100, 45, 75, "Japan"), new("Fjord", 1450, 35, 130, "Europe"),
        new("Granite", 2100, 20, 260, "USA"), new("Harbor", 1250, 40, 95, "Japan"), new("Iris", 1500, 33, 140, "Japan"),
        new("Juniper", 1750, 27, 190, "USA"), new("Kestrel", 1300, 39, 105, "Europe"), new("Lumen", 1550, 30, 160, "Japan"),
    ];

    public static Score[] Scores { get; } =
    [
        new("Ada", "Speed", 80), new("Ada", "Strength", 55), new("Ada", "Agility", 90), new("Ada", "Stamina", 70), new("Ada", "Skill", 85),
        new("Bob", "Speed", 60), new("Bob", "Strength", 85), new("Bob", "Agility", 50), new("Bob", "Stamina", 90), new("Bob", "Skill", 65),
    ];

    public static Candle[] Candles { get; } = CreateCandles();

    public static Measurement[] Measurements { get; } = CreateMeasurements();

    public static FileEntry[] Files { get; } =
    [
        new("src/app/main.cs", 120), new("src/app/view.cs", 80), new("src/app/model.cs", 60),
        new("src/core/parser.cs", 140), new("src/core/lexer.cs", 90), new("src/core/ast.cs", 45),
        new("src/util/strings.cs", 30), new("src/util/math.cs", 25),
        new("tests/app.tests.cs", 70), new("tests/core.tests.cs", 85),
        new("docs/readme.md", 40), new("docs/guide.md", 55),
    ];

    /// <summary>Normally distributed values from a seeded generator (Box–Muller).</summary>
    public static double[] Normal(int count, double mean, double deviation, int seed)
    {
#pragma warning disable CA5394 // Do not use insecure randomness: the values must be reproducible
        var random = new Random(seed);
        var values = new double[count];
        for (var i = 0; i < count; i++)
        {
            var u1 = 1 - random.NextDouble();
            var u2 = random.NextDouble();
            values[i] = mean + (deviation * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
        }
#pragma warning restore CA5394

        return values;
    }

    private static Reading[] CreateReadings()
    {
        var result = new List<Reading>();
        var start = new DateOnly(2024, 1, 1);
        for (var month = 0; month < 12; month++)
        {
            var date = start.AddMonths(month);
            var season = Math.Cos((month - 6.5) * Math.PI / 6);
            result.Add(new Reading(date, "Paris", Math.Round(12 - (8 * season), 1, MidpointRounding.AwayFromZero)));
            result.Add(new Reading(date, "Oslo", Math.Round(5 - (11 * season), 1, MidpointRounding.AwayFromZero)));
            result.Add(new Reading(date, "Madrid", Math.Round(16 - (10 * season), 1, MidpointRounding.AwayFromZero)));
        }

        return [.. result];
    }

    private static Candle[] CreateCandles()
    {
        var result = new List<Candle>();
        var price = 100.0;
        var start = new DateOnly(2024, 3, 1);
        for (var i = 0; i < 20; i++)
        {
            var change = Math.Sin(i * 1.7) * 4;
            var open = price;
            var close = Math.Round(price + change, 2, MidpointRounding.AwayFromZero);
            var high = Math.Round(Math.Max(open, close) + 1.5 + Math.Abs(Math.Cos(i)), 2, MidpointRounding.AwayFromZero);
            var low = Math.Round(Math.Min(open, close) - 1.5 - Math.Abs(Math.Sin(i * 0.7)), 2, MidpointRounding.AwayFromZero);
            result.Add(new Candle(start.AddDays(i), open, high, low, close));
            price = close;
        }

        return [.. result];
    }

    private static Measurement[] CreateMeasurements()
    {
        var result = new List<Measurement>();
        result.AddRange(Normal(60, 50, 10, seed: 1).Select(value => new Measurement("A", Math.Round(value, 2, MidpointRounding.AwayFromZero))));
        result.AddRange(Normal(60, 65, 6, seed: 2).Select(value => new Measurement("B", Math.Round(value, 2, MidpointRounding.AwayFromZero))));
        result.AddRange(Normal(60, 40, 15, seed: 3).Select(value => new Measurement("C", Math.Round(value, 2, MidpointRounding.AwayFromZero))));
        result.Add(new Measurement("B", 95));
        result.Add(new Measurement("B", 30));
        return [.. result];
    }
}
