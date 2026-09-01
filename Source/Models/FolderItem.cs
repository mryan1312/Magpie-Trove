using System.IO;
using MagpieTrove.Common;

namespace MagpieTrove.Models;

public sealed class FolderItem : ObservableObject
{
	private int _count;

	private bool _isOffline;

	public long Id { get; init; }

	public string Path { get; init; } = "";

	public string DisplayName
	{
		get
		{
			string fileName = System.IO.Path.GetFileName(Path.TrimEnd(new char[2] { '\\', '/' }));
			if (fileName == null || fileName.Length <= 0)
			{
				return Path;
			}
			return fileName;
		}
	}

	public int Count
	{
		get
		{
			return _count;
		}
		set
		{
			if (Set(ref _count, value, "Count"))
			{
				OnPropertyChanged("AvailabilityText");
			}
		}
	}

	public bool IsOffline
	{
		get
		{
			return _isOffline;
		}
		set
		{
			if (Set(ref _isOffline, value, "IsOffline"))
			{
				OnPropertyChanged("AvailabilityText");
			}
		}
	}

	public string AvailabilityText
	{
		get
		{
			if (!IsOffline)
			{
				return $"{Count:N0}";
			}
			return "Offline";
		}
	}
}
