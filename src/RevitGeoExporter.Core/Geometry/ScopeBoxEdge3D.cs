namespace RevitGeoExporter.Core.Geometry;

public readonly struct ScopeBoxEdge3D
{
    public ScopeBoxEdge3D(
        double startX,
        double startY,
        double startZ,
        double endX,
        double endY,
        double endZ)
    {
        StartX = startX;
        StartY = startY;
        StartZ = startZ;
        EndX = endX;
        EndY = endY;
        EndZ = endZ;
    }

    public double StartX { get; }
    public double StartY { get; }
    public double StartZ { get; }
    public double EndX { get; }
    public double EndY { get; }
    public double EndZ { get; }
}
