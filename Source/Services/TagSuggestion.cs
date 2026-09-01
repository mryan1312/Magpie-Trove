namespace MagpieTrove.Services;

public sealed record TagSuggestion(long TagId, string Name, double Score, string Reason);
