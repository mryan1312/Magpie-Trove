using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using MagpieTrove.Common;

namespace MagpieTrove.Models;

public sealed class ImageItem : ObservableObject
{
	public static IThumbnailSource? ThumbnailSource;

	private BitmapSource? _thumbnail;

	private bool _thumbnailRequested;

	private int _rating;

	private bool _isMissing;

	private int _flag;

	private int _rotationOverride;

	public long Id { get; init; }

	public string Path { get; init; } = "";

	public string FileName { get; init; } = "";

	public string Folder { get; init; } = "";

	public long FileSize { get; init; }

	public int Width { get; init; }

	public int Height { get; init; }

	public DateTime? DateTaken { get; init; }

	public DateTime DateModified { get; init; }

	public DateTime DateAdded { get; init; }

	public string? CameraMake { get; init; }

	public string? CameraModel { get; init; }

	public string? Lens { get; init; }

	public int? Iso { get; init; }

	public double? Aperture { get; init; }

	public double? ShutterSpeed { get; init; }

	public double? FocalLength { get; init; }

	public IReadOnlyList<string> TagColors { get; init; } = Array.Empty<string>();

	public int RotationOverride
	{
		get
		{
			return _rotationOverride;
		}
		set
		{
			Set(ref _rotationOverride, value, "RotationOverride");
		}
	}

	public int Rating
	{
		get
		{
			return _rating;
		}
		set
		{
			Set(ref _rating, value, "Rating");
		}
	}

	public bool IsMissing
	{
		get
		{
			return _isMissing;
		}
		set
		{
			Set(ref _isMissing, value, "IsMissing");
		}
	}

	public int Flag
	{
		get
		{
			return _flag;
		}
		set
		{
			if (Set(ref _flag, value, "Flag"))
			{
				OnPropertyChanged("IsPicked");
				OnPropertyChanged("IsRejected");
			}
		}
	}

	public bool IsPicked => _flag > 0;

	public bool IsRejected => _flag < 0;

	public DateTime EffectiveDate => DateTaken ?? DateModified;

	public string Dimensions
	{
		get
		{
			if (Width <= 0 || Height <= 0)
			{
				return "unknown";
			}
			return $"{Width} x {Height}";
		}
	}

	public string FileSizeDisplay => FormatSize(FileSize);

	public string CameraDisplay => string.Join(" ", new string[2] { CameraMake, CameraModel }.Where((string s) => !string.IsNullOrWhiteSpace(s)).Distinct<string>(StringComparer.OrdinalIgnoreCase));

	public string ApertureDisplay
	{
		get
		{
			double? aperture = Aperture;
			if (aperture.HasValue)
			{
				double valueOrDefault = aperture.GetValueOrDefault();
				return $"f/{valueOrDefault:0.#}";
			}
			return "—";
		}
	}

	public string ShutterDisplay
	{
		get
		{
			double? shutterSpeed = ShutterSpeed;
			if (shutterSpeed.HasValue)
			{
				double valueOrDefault = shutterSpeed.GetValueOrDefault();
				if (valueOrDefault >= 1.0)
				{
					return $"{valueOrDefault:0.##} s";
				}
				return $"1/{Math.Round(1.0 / valueOrDefault):0} s";
			}
			return "—";
		}
	}

	public string FocalLengthDisplay
	{
		get
		{
			double? focalLength = FocalLength;
			if (focalLength.HasValue)
			{
				double valueOrDefault = focalLength.GetValueOrDefault();
				return $"{valueOrDefault:0.#} mm";
			}
			return "—";
		}
	}

	public string IsoDisplay
	{
		get
		{
			int? iso = Iso;
			if (!iso.HasValue)
			{
				return "—";
			}
			return iso.GetValueOrDefault().ToString();
		}
	}

	public string ToolTipText => $"{FileName}\n{Dimensions}  •  {FileSizeDisplay}\n{EffectiveDate:g}\n{Folder}";

	public BitmapSource? Thumbnail
	{
		get
		{
			if (!_thumbnailRequested)
			{
				_thumbnailRequested = true;
				ThumbnailSource?.Request(this);
			}
			return _thumbnail;
		}
	}

	internal bool HasThumbnail => _thumbnail != null;

	internal void SetThumbnail(BitmapSource? value)
	{
		_thumbnail = value;
		if (value == null)
		{
			_thumbnailRequested = false;
		}
		OnPropertyChanged("Thumbnail");
	}

	public static string FormatSize(long bytes)
	{
		string[] array = new string[4] { "B", "KB", "MB", "GB" };
		double num = bytes;
		int num2 = 0;
		while (num >= 1024.0 && num2 < array.Length - 1)
		{
			num /= 1024.0;
			num2++;
		}
		if (num2 == 0)
		{
			return $"{bytes} B";
		}
		return $"{num:0.#} {array[num2]}";
	}

	public bool FileExists()
	{
		return File.Exists(Path);
	}
}
