using System;
using MagpieTrove.Common;

namespace MagpieTrove.Models;

public sealed class CollectionItem : ObservableObject
{
	private string _name = "";

	private int _count;

	public long Id { get; init; }

	public CollectionKind Kind { get; init; }

	public FilterQuery? Rule { get; set; }

	public DateTime DateCreated { get; init; }

	public string Name
	{
		get
		{
			return _name;
		}
		set
		{
			Set(ref _name, value, "Name");
		}
	}

	public int Count
	{
		get
		{
			return _count;
		}
		set
		{
			Set(ref _count, value, "Count");
		}
	}

	public bool IsSmart => Kind == CollectionKind.Smart;

	public string Glyph
	{
		get
		{
			if (!IsSmart)
			{
				return "▤";
			}
			return "✦";
		}
	}
}
