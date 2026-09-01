namespace MagpieTrove.Services;

public sealed record PhotoExportOptions(string DestinationDirectory, int MaxLongEdge = 0, string FileNamePattern = "{name}", ExportImageFormat Format = ExportImageFormat.Original);
