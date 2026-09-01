using System;

namespace MagpieTrove.Data;

public sealed class VectorSet
{
	public required long[] ImageIds { get; init; }

	public required float[] Data { get; init; }

	public required int Dimensions { get; init; }

	public int Count => ImageIds.Length;

	public ReadOnlySpan<float> this[int index] => Data.AsSpan(index * Dimensions, Dimensions);

	public static VectorSet Empty(int dimensions)
	{
		return new VectorSet
		{
			ImageIds = Array.Empty<long>(),
			Data = Array.Empty<float>(),
			Dimensions = dimensions
		};
	}
}
