using System;
using System.Collections.Generic;

namespace MagpieTrove.Services;

public sealed class UndoService
{
	private const int Capacity = 100;

	private readonly LinkedList<IUndoableAction> _undo = new LinkedList<IUndoableAction>();

	private readonly Stack<IUndoableAction> _redo = new Stack<IUndoableAction>();

	public bool CanUndo => _undo.Count > 0;

	public bool CanRedo => _redo.Count > 0;

	public string UndoDescription => _undo.Last?.Value.Description ?? "";

	public string RedoDescription
	{
		get
		{
			if (_redo.Count <= 0)
			{
				return "";
			}
			return _redo.Peek().Description;
		}
	}

	public event Action? Changed;

	public void Push(IUndoableAction action)
	{
		_undo.AddLast(action);
		while (_undo.Count > 100)
		{
			_undo.RemoveFirst();
		}
		_redo.Clear();
		Changed?.Invoke();
	}

	public string? Undo()
	{
		LinkedListNode<IUndoableAction> last = _undo.Last;
		if (last == null)
		{
			return null;
		}
		_undo.RemoveLast();
		last.Value.Undo();
		_redo.Push(last.Value);
		Changed?.Invoke();
		return last.Value.Description;
	}

	public string? Redo()
	{
		if (_redo.Count == 0)
		{
			return null;
		}
		IUndoableAction undoableAction = _redo.Pop();
		undoableAction.Redo();
		_undo.AddLast(undoableAction);
		Changed?.Invoke();
		return undoableAction.Description;
	}

	public void Clear()
	{
		_undo.Clear();
		_redo.Clear();
		Changed?.Invoke();
	}
}
