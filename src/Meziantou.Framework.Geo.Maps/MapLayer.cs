namespace Meziantou.Framework.Geo.Maps;

/// <summary>A layer of the <see cref="Map"/> of a region.</summary>
public enum MapLayer
{
    /// <summary>The borders of the countries, as polygons, with their <c>name</c> and their ISO 3166-1 alpha-2 code, <c>iso</c>.</summary>
    Borders,

    /// <summary>
    /// The states, provinces and other subdivisions of the countries, as polygons, with their <c>name</c>, their ISO 3166-2
    /// code, <c>iso</c>, the ISO 3166-1 alpha-2 code of their country, <c>country</c>, and their OpenStreetMap
    /// <c>admin_level</c>: a country can have subdivisions of several levels, the smaller level being the larger one.
    /// </summary>
    StatesProvinces,

    /// <summary>
    /// The populated places, as points, with their <c>name</c>, the ISO 3166-1 alpha-2 code of their country,
    /// <c>country</c>, their <c>population</c>, their <c>rank</c> (from 0 to 14, larger for larger places, to choose the
    /// places to label) and whether they are the capital of their country, <c>capital</c>.
    /// </summary>
    Cities,

    /// <summary>The rivers, as lines, with their <c>name</c>.</summary>
    Rivers,

    /// <summary>The lakes and reservoirs, as polygons, with their <c>name</c>.</summary>
    Lakes,

    /// <summary>The coastlines, as lines.</summary>
    Coastline,

    /// <summary>The land, as polygons cut to the bounds of the region.</summary>
    Land,

    /// <summary>The oceans and seas, as polygons cut to the bounds of the region.</summary>
    Ocean,

    /// <summary>
    /// The named areas of the oceans, such as seas, gulfs, bays and straits, as polygons cut to the bounds of the region,
    /// to label the water: their <c>name</c>, their <c>kind</c> (<c>sea</c>, <c>gulf</c>, <c>bay</c>, <c>strait</c>,
    /// <c>sound</c>, <c>channel</c>, <c>lagoon</c>, <c>fjord</c>, <c>ocean</c>...) and their <c>scale_rank</c> (from 0,
    /// the most important, to 9, to choose the areas to label).
    /// </summary>
    MarineAreas,

    /// <summary>
    /// The named physical regions, such as mountain ranges, deserts, plateaus, islands and peninsulas, as polygons cut to
    /// the bounds of the region, and as points for the features too small for a polygon, such as capes and waterfalls:
    /// their <c>name</c>, as Natural Earth writes it (the largest regions are in capitals), their <c>kind</c> (the class
    /// of Natural Earth in lowercase, such as <c>range/mtn</c>, <c>desert</c>, <c>plateau</c>, <c>island</c> or
    /// <c>cape</c>) and their <c>scale_rank</c> (from 0, the most important, to 7).
    /// </summary>
    PhysicalRegions,
}
