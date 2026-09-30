namespace TheEasyWayForDrivers.Core.Models;

public sealed record ServiceDiagnosticsInfo(
    string ServiceVersion,
    DateTimeOffset StartedAt,
    int ProcessId,
    string LogDirectory,
    string? CurrentLogFile,
    IReadOnlyList<string> RecentLogLines);
