namespace MagpieTrove.Services;

public sealed record TagImportResult(int MatchedImages, int UnmatchedImages, int TagsApplied);
