using System.Collections.Generic;
using System.Linq;
using MagpieTrove.Data;

namespace MagpieTrove.Services;

public sealed class RatingAction : IUndoableAction
{
	private readonly IReadOnlyList<(long ImageId, int OldRating)> _previous;

	private readonly int _newRating;

	public string Description { get; }

	public RatingAction(IReadOnlyList<(long, int)> previous, int newRating)
	{
		_previous = previous;
		_newRating = newRating;
		Description = $"rate {previous.Count:N0} image{((previous.Count == 1) ? "" : "s")}";
	}

	public void Undo()
	{
		foreach (IGrouping<int, (long, int)> item in from p in _previous
			group p by p.OldRating)
		{
			ImageRepository.SetRating(item.Select<(long, int), long>(((long ImageId, int OldRating) g) => g.ImageId), item.Key);
		}
	}

	public void Redo()
	{
		ImageRepository.SetRating(_previous.Select(((long ImageId, int OldRating) p) => p.ImageId), _newRating);
	}
}
