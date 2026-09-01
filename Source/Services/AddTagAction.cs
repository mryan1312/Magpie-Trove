using System.Collections.Generic;
using MagpieTrove.Data;

namespace MagpieTrove.Services;

public sealed class AddTagAction : IUndoableAction
{
	private readonly long _tagId;

	private readonly IReadOnlyList<long> _imageIds;

	public string Description { get; }

	public AddTagAction(long tagId, string tagName, IReadOnlyList<long> imageIds)
	{
		_tagId = tagId;
		_imageIds = imageIds;
		Description = $"tag {imageIds.Count:N0} image{((imageIds.Count == 1) ? "" : "s")} as \"{tagName}\"";
	}

	public void Undo()
	{
		TagRepository.RemoveTagFromImages(_tagId, _imageIds);
	}

	public void Redo()
	{
		TagRepository.RestoreTagOnImages(_tagId, _imageIds);
	}
}
