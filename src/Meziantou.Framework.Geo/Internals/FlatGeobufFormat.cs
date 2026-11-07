namespace Meziantou.Framework.Geo.Internals;

/// <summary>The constants of the FlatGeobuf format: the indexes of the fields of its FlatBuffers tables (header.fbs and feature.fbs) and its enumerations.</summary>
internal static class FlatGeobufFormat
{
    public const byte MajorVersion = 3;
    public const byte PatchVersion = 1;

    public const int HeaderName = 0;
    public const int HeaderEnvelope = 1;
    public const int HeaderGeometryType = 2;
    public const int HeaderHasZ = 3;
    public const int HeaderColumns = 7;
    public const int HeaderFeaturesCount = 8;
    public const int HeaderIndexNodeSize = 9;
    public const int HeaderCrs = 10;
    public const int HeaderTitle = 11;
    public const int HeaderDescription = 12;

    public const int ColumnName = 0;
    public const int ColumnType = 1;

    public const int CrsOrganization = 0;
    public const int CrsCode = 1;
    public const int CrsName = 2;

    public const int GeometryEnds = 0;
    public const int GeometryXY = 1;
    public const int GeometryZ = 2;
    public const int GeometryType = 6;
    public const int GeometryParts = 7;

    public const int FeatureGeometry = 0;
    public const int FeatureProperties = 1;
    public const int FeatureColumns = 2;

    public const byte GeometryUnknown = 0;
    public const byte GeometryPoint = 1;
    public const byte GeometryLineString = 2;
    public const byte GeometryPolygon = 3;
    public const byte GeometryMultiPoint = 4;
    public const byte GeometryMultiLineString = 5;
    public const byte GeometryMultiPolygon = 6;
    public const byte GeometryCollection = 7;

    public const byte ColumnByte = 0;
    public const byte ColumnUByte = 1;
    public const byte ColumnBool = 2;
    public const byte ColumnShort = 3;
    public const byte ColumnUShort = 4;
    public const byte ColumnInt = 5;
    public const byte ColumnUInt = 6;
    public const byte ColumnLong = 7;
    public const byte ColumnULong = 8;
    public const byte ColumnFloat = 9;
    public const byte ColumnDouble = 10;
    public const byte ColumnString = 11;
    public const byte ColumnJson = 12;
    public const byte ColumnDateTime = 13;
    public const byte ColumnBinary = 14;
}
