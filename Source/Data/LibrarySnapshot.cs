using System.Collections.Generic;

namespace MagpieTrove.Data;

public sealed class LibrarySnapshot
{
	public required List<long> ImageIds { get; init; }

	public List<object?[]> Rows { get; } = new List<object[]>();

	public List<(long ImageId, long TagId)> TagLinks { get; } = new List<(long, long)>();

	public List<(long CollectionId, long ImageId, long SortOrder)> CollectionLinks { get; } = new List<(long, long, long)>();

	public List<(long ImageId, string Model, int Dim, byte[] Vector)> Embeddings { get; } = new List<(long, string, int, byte[])>();

	public int ImageCount => ImageIds.Count;
}
