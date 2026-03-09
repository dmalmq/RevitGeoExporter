namespace RevitGeoExporter.Core.Models;

public static class OpeningSnapPolicy
{
    public const double DefaultOpeningSnapDistanceMeters = 0.20d;
    public const double ElevatorOpeningSnapDistanceMeters = 0.50d;

    public static double ResolveMaxSnapDistance(
        bool isAcceptedElevatorDoorFamily,
        bool isNearElevatorBoundary)
    {
        return isAcceptedElevatorDoorFamily || isNearElevatorBoundary
            ? ElevatorOpeningSnapDistanceMeters
            : DefaultOpeningSnapDistanceMeters;
    }
}
