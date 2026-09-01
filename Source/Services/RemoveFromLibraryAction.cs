using MagpieTrove.Data;

namespace MagpieTrove.Services;

public sealed class RemoveFromLibraryAction : IUndoableAction
{
	private readonly LibrarySnapshot _snapshot;

	public string Description { get; }

	public RemoveFromLibraryAction(LibrarySnapshot snapshot)
	{
		_snapshot = snapshot;
		Description = $"remove {snapshot.ImageCount:N0} image{((snapshot.ImageCount == 1) ? "" : "s")} from the library";
	}

	public void Undo()
	{
		ImageRepository.RestoreSnapshot(_snapshot);
	}

	public void Redo()
	{
		ImageRepository.Remove(_snapshot.ImageIds);
	}
}
