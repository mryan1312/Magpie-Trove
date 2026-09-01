using System.Collections.Generic;
using System.Linq;
using MagpieTrove.Models;

namespace MagpieTrove.Services;

public sealed class DuplicateGroup
{
	public required DuplicateKind Kind { get; init; }

	public required List<ImageItem> Images { get; init; }

	public int Spread { get; init; }

	public int Count => Images.Count;

	public ImageItem Best => (from i in Images
		orderby (long)i.Width * (long)i.Height descending, i.FileSize descending
		select i).First();

	public long ReclaimableBytes => Images.Sum((ImageItem i) => i.FileSize) - Best.FileSize;

	public string KindLabel
	{
		get
		{
			if (Kind != DuplicateKind.Identical)
			{
				if (Spread != 0)
				{
					return $"Near match ({Spread}/64 bits differ)";
				}
				return "Visually identical";
			}
			return "Identical files";
		}
	}
}
