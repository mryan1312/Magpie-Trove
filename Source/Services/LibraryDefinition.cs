using System;

namespace MagpieTrove.Services;

public sealed record LibraryDefinition(Guid Id, string Name, string Directory)
{
	public string DisplayText => Name + "  —  " + Directory;
}
