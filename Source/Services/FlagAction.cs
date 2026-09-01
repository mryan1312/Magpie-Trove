using System.Collections.Generic;
using System.Linq;
using MagpieTrove.Data;

namespace MagpieTrove.Services;

public sealed class FlagAction : IUndoableAction
{
	private readonly IReadOnlyList<(long ImageId, int OldFlag)> _previous;

	private readonly int _newFlag;

	public string Description { get; }

	public FlagAction(IReadOnlyList<(long, int)> previous, int newFlag)
	{
		_previous = previous;
		_newFlag = newFlag;
		string value = ((newFlag > 0) ? "pick" : ((newFlag >= 0) ? "unflag" : "reject"));
		Description = $"{value} {previous.Count:N0} image{((previous.Count == 1) ? "" : "s")}";
	}

	public void Undo()
	{
		foreach (IGrouping<int, (long, int)> item in from p in _previous
			group p by p.OldFlag)
		{
			ImageRepository.SetFlag(item.Select<(long, int), long>(((long ImageId, int OldFlag) g) => g.ImageId), item.Key);
		}
	}

	public void Redo()
	{
		ImageRepository.SetFlag(_previous.Select(((long ImageId, int OldFlag) p) => p.ImageId), _newFlag);
	}
}
