namespace MagpieTrove.Models;

public sealed class TagChip
{
	public long Id { get; init; }

	public string Name { get; init; } = "";

	public string Color { get; init; } = "#4FA3E3";

	public int AppliedCount { get; init; }

	public int SelectionCount { get; init; }

	public bool IsPartial => AppliedCount < SelectionCount;

	public string Display
	{
		get
		{
			if (!IsPartial)
			{
				return Name;
			}
			return $"{Name} ({AppliedCount}/{SelectionCount})";
		}
	}
}
