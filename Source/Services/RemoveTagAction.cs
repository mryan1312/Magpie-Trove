using System.Collections.Generic;
using MagpieTrove.Data;

namespace MagpieTrove.Services;

public sealed class RemoveTagAction : IUndoableAction
{
	private readonly long _tagId;

	private readonly IReadOnlyList<long> _imageIds;

	public string Description { get; }

	public RemoveTagAction(long tagId, string tagName, IReadOnlyList<long> imageIds)
	{
		_tagId = tagId;
		_imageIds = imageIds;
		Description = $"remove \"{tagName}\" from {imageIds.Count:N0} image{((imageIds.Count == 1) ? "" : "s")}";
	}

	public void Undo()
	{
		TagRepository.RestoreTagOnImages(_tagId, _imageIds);
	}

	public void Redo()
	{
		TagRepository.RemoveTagFromImages(_tagId, _imageIds);
	}
}
