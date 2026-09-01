namespace MagpieTrove.Services;

public interface IUndoableAction
{
	string Description { get; }

	void Undo();

	void Redo();
}
