using System;
using System.Collections.Generic;
using System.Linq;

namespace MagpieTrove.Services;

public sealed class AppSettings
{
	public string DefaultLibraryRoot { get; set; } = "";

	public string ModelDirectory { get; set; } = "";

	public Guid CurrentLibraryId { get; set; }

	public List<LibraryDefinition> Libraries { get; set; } = new List<LibraryDefinition>();

	public LibraryDefinition CurrentLibrary => Libraries.FirstOrDefault((LibraryDefinition l) => l.Id == CurrentLibraryId) ?? Libraries[0];
}
