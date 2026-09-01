namespace MagpieTrove.Services;

public sealed record IncrementalScanResult(int Updated, int Missing, int Unreadable);
