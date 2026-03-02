using System.Collections.Generic;

namespace RevitGeoExporter.Core.Coordinates;

public static class JapanPlaneRectangular
{
    // EPSG codes for JGD2011 / Japan Plane Rectangular CS zones.
    public static readonly IReadOnlyDictionary<int, string> Zones = new Dictionary<int, string>
    {
        [6669] = "Zone I",
        [6670] = "Zone II",
        [6671] = "Zone III",
        [6672] = "Zone IV",
        [6673] = "Zone V",
        [6674] = "Zone VI",
        [6675] = "Zone VII",
        [6676] = "Zone VIII",
        [6677] = "Zone IX",
        [6678] = "Zone X",
        [6679] = "Zone XI",
        [6680] = "Zone XII",
        [6681] = "Zone XIII",
        [6682] = "Zone XIV",
        [6683] = "Zone XV",
        [6684] = "Zone XVI",
        [6685] = "Zone XVII",
        [6686] = "Zone XVIII",
        [6687] = "Zone XIX",
    };
}
