using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace MagpieTrove.Models;

public sealed class FilterQuery
{
	public List<long> IncludeTagIds { get; set; } = new List<long>();

	public List<long> ExcludeTagIds { get; set; } = new List<long>();

	public bool MatchAll { get; set; } = true;

	public bool UntaggedOnly { get; set; }

	public string? Search { get; set; }

	public string? FolderPrefix { get; set; }

	public int MinRating { get; set; }

	public FlagFilter Flags { get; set; }

	public DateTime? DateFrom { get; set; }

	public DateTime? DateTo { get; set; }

	public string? CameraMake { get; set; }

	public string? CameraModel { get; set; }

	public string? Lens { get; set; }

	public int? IsoMin { get; set; }

	public int? IsoMax { get; set; }

	public double? ApertureMin { get; set; }

	public double? ApertureMax { get; set; }

	public double? ShutterSpeedMin { get; set; }

	public double? ShutterSpeedMax { get; set; }

	public double? FocalLengthMin { get; set; }

	public double? FocalLengthMax { get; set; }

	[JsonIgnore]
	public long? CollectionId { get; set; }

	public SortField SortBy { get; set; } = SortField.DateTaken;

	public bool SortDescending { get; set; } = true;

	public bool IsEmpty
	{
		get
		{
			if (IncludeTagIds.Count == 0 && ExcludeTagIds.Count == 0 && !UntaggedOnly && string.IsNullOrWhiteSpace(Search) && string.IsNullOrEmpty(FolderPrefix) && MinRating == 0 && !CollectionId.HasValue && Flags == FlagFilter.All && !DateFrom.HasValue && !DateTo.HasValue && string.IsNullOrEmpty(CameraMake) && string.IsNullOrEmpty(CameraModel) && string.IsNullOrEmpty(Lens) && !IsoMin.HasValue && !IsoMax.HasValue && !ApertureMin.HasValue && !ApertureMax.HasValue && !ShutterSpeedMin.HasValue && !ShutterSpeedMax.HasValue && !FocalLengthMin.HasValue)
			{
				return !FocalLengthMax.HasValue;
			}
			return false;
		}
	}

	public FilterQuery Clone()
	{
		return new FilterQuery
		{
			IncludeTagIds = IncludeTagIds.ToList(),
			ExcludeTagIds = ExcludeTagIds.ToList(),
			MatchAll = MatchAll,
			UntaggedOnly = UntaggedOnly,
			Search = Search,
			FolderPrefix = FolderPrefix,
			MinRating = MinRating,
			Flags = Flags,
			DateFrom = DateFrom,
			DateTo = DateTo,
			CameraMake = CameraMake,
			CameraModel = CameraModel,
			Lens = Lens,
			IsoMin = IsoMin,
			IsoMax = IsoMax,
			ApertureMin = ApertureMin,
			ApertureMax = ApertureMax,
			ShutterSpeedMin = ShutterSpeedMin,
			ShutterSpeedMax = ShutterSpeedMax,
			FocalLengthMin = FocalLengthMin,
			FocalLengthMax = FocalLengthMax,
			CollectionId = CollectionId,
			SortBy = SortBy,
			SortDescending = SortDescending
		};
	}
}
