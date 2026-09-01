using System;

namespace MagpieTrove.Services;

public sealed record EmbedResult(int Embedded, int Failed, TimeSpan Elapsed);
