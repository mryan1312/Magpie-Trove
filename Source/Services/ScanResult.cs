namespace MagpieTrove.Services;

public sealed record ScanResult(int Added, int Updated, int Skipped, int Unreadable, int MarkedMissing, int Moved, int OfflineRoots);
