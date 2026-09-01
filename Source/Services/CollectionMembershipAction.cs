using System.Collections.Generic;
using MagpieTrove.Data;

namespace MagpieTrove.Services;

public sealed class CollectionMembershipAction : IUndoableAction
{
	private readonly long _collectionId;

	private readonly IReadOnlyList<long> _imageIds;

	private readonly bool _wasAdded;

	public string Description { get; }

	public CollectionMembershipAction(long collectionId, string name, IReadOnlyList<long> imageIds, bool wasAdded)
	{
		_collectionId = collectionId;
		_imageIds = imageIds;
		_wasAdded = wasAdded;
		Description = (wasAdded ? $"add {imageIds.Count:N0} image{((imageIds.Count == 1) ? "" : "s")} to \"{name}\"" : $"remove {imageIds.Count:N0} image{((imageIds.Count == 1) ? "" : "s")} from \"{name}\"");
	}

	public void Undo()
	{
		if (_wasAdded)
		{
			CollectionRepository.RemoveImages(_collectionId, _imageIds);
		}
		else
		{
			CollectionRepository.AddImages(_collectionId, _imageIds);
		}
	}

	public void Redo()
	{
		if (_wasAdded)
		{
			CollectionRepository.AddImages(_collectionId, _imageIds);
		}
		else
		{
			CollectionRepository.RemoveImages(_collectionId, _imageIds);
		}
	}
}
