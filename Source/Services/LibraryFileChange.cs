using System.IO;

namespace MagpieTrove.Services;

public sealed record LibraryFileChange(WatcherChangeTypes Kind, string Path, string? OldPath = null);
