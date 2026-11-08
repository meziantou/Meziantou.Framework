# Meziantou.Framework.Geo.Maps.Data

The map of a region, the world, a continent or a country, embedded in an assembly for
[Meziantou.Framework.Geo.Maps](https://www.nuget.org/packages/Meziantou.Framework.Geo.Maps/): its borders, states and
provinces, cities, rivers, lakes, coastline, land, ocean, seas and physical regions, in the FlatGeobuf format.

Each package is named after the key of its region, such as `Meziantou.Framework.Geo.Maps.Data.France`, and opens its map
with a static property of `Map` named after the same key, `Map.Region_France`, in the `Meziantou.Framework.Geo.Maps.Data`
namespace. It is a C# 14 extension member: the project needs C# 14, the default of .NET 10. The layers are read from the
memory of the loaded assembly: no file is copied next to the application, so the map works in a single-file or a Native
AOT application.

A new version of a package, `1.0.1` after `1.0.0`, is published only when the map of its region changes. The metadata of
the map gives the date of its data (`Map.Metadata.LastModified`).

The borders and subdivisions are derived from OpenStreetMap: the maps are under the
[Open Database License](https://opendatacommons.org/licenses/odbl/), and the maps made from them must credit
"© OpenStreetMap contributors", the text of `Map.Attribution`.

The packages are built by the [tool of the repository](https://github.com/meziantou/Meziantou.Framework/tree/main/tools/Meziantou.Framework.Geo.Maps.Tool),
which can also build the maps with fewer layers, in other formats, or as packages for your own feed.
