namespace TheEasyWayForDrivers.Core.Models;

public sealed record OperationProgress(
    string Stage,
    int Percent,
    string Message);
