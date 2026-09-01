using System;

namespace MagpieTrove.Data;

public sealed record ScanRecord(string Path, long FileSize, DateTime DateModified, bool IsMissing, string? QuickHash, bool ExifScanned, bool KeywordsScanned);
