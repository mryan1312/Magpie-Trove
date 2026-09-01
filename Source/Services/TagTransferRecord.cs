using System.Collections.Generic;

namespace MagpieTrove.Services;

public sealed class TagTransferRecord
{
	public string Path { get; set; } = "";

	public string? ContentHash { get; set; }

	public List<string> Tags { get; set; } = new List<string>();
}
