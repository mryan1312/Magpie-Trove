using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MagpieTrove.Services;

public static class ImageFileInfo
{
	public static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".dib", ".tif", ".tiff",
		".webp", ".heic", ".heif", ".avif", ".ico", ".wdp", ".jxr"
	};

	public static bool IsSupported(string path)
	{
		return SupportedExtensions.Contains(Path.GetExtension(path));
	}

	public static ImageMetadata? Read(string path)
	{
		try
		{
			using FileStream bitmapStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
			BitmapDecoder bitmapDecoder = BitmapDecoder.Create(bitmapStream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
			if (bitmapDecoder.Frames.Count == 0)
			{
				return null;
			}
			BitmapFrame bitmapFrame = bitmapDecoder.Frames[0];
			DateTime? dateTaken = null;
			int orientationDegrees = 0;
			string cameraMake = null;
			string cameraModel = null;
			string lens = null;
			int? iso = null;
			double? aperture = null;
			double? shutterSpeed = null;
			double? focalLength = null;
			IReadOnlyList<string> keywords = Array.Empty<string>();
			if (bitmapFrame.Metadata is BitmapMetadata meta)
			{
				dateTaken = ReadDateTaken(meta);
				orientationDegrees = ReadOrientation(meta);
				cameraMake = ReadString(meta, 271);
				cameraModel = ReadString(meta, 272);
				lens = ReadString(meta, 42036);
				double? num = ReadNumber(meta, 34855);
				int? num2;
				if (num.HasValue)
				{
					double valueOrDefault = num.GetValueOrDefault();
					num2 = (int)Math.Round(valueOrDefault);
				}
				else
				{
					num2 = null;
				}
				iso = num2;
				aperture = ReadNumber(meta, 33437);
				shutterSpeed = ReadNumber(meta, 33434);
				focalLength = ReadNumber(meta, 37386);
				keywords = ReadKeywords(meta);
			}
			return new ImageMetadata(bitmapFrame.PixelWidth, bitmapFrame.PixelHeight, dateTaken, orientationDegrees, cameraMake, cameraModel, lens, iso, aperture, shutterSpeed, focalLength, keywords);
		}
		catch (Exception ex) when (((ex is IOException || ex is NotSupportedException || ex is FileFormatException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is OverflowException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	private static IReadOnlyList<string> ReadKeywords(BitmapMetadata meta)
	{
		HashSet<string> values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			ReadOnlyCollection<string> keywords = meta.Keywords;
			if (keywords != null)
			{
				foreach (string item2 in keywords)
				{
					Add(item2);
				}
			}
		}
		catch (Exception ex) when (((ex is NotSupportedException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
		}
		try
		{
			if (meta.GetQuery("/app1/ifd/{ushort=40094}") is byte[] bytes)
			{
				Add(Encoding.Unicode.GetString(bytes).TrimEnd('\0'));
			}
		}
		catch (Exception ex2) when (((ex2 is NotSupportedException || ex2 is ArgumentException) ? 1 : 0) != 0)
		{
		}
		return values.ToArray();
		void Add(string? text)
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				string[] array = text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
				foreach (string item in array)
				{
					values.Add(item);
				}
			}
		}
	}

	private static string? ReadString(BitmapMetadata meta, ushort tag)
	{
		string text = ReadExifValue(meta, tag)?.ToString()?.Trim().TrimEnd('\0');
		if (!string.IsNullOrWhiteSpace(text))
		{
			return text;
		}
		return null;
	}

	private static double? ReadNumber(BitmapMetadata meta, ushort tag)
	{
		object obj = ReadExifValue(meta, tag);
		try
		{
			return (obj == null) ? ((double?)null) : ((obj is ulong packed) ? DecodeUnsignedRational(packed) : ((obj is long num) ? Divide((uint)num, (uint)((ulong)num >> 32)) : ((obj is uint num2) ? new double?(num2) : ((obj is ushort num3) ? new double?((int)num3) : ((obj is int num4) ? new double?(num4) : ((obj is double value) ? new double?(value) : ((!(obj is float num5)) ? new double?(Convert.ToDouble(obj, CultureInfo.InvariantCulture)) : new double?(num5))))))));
		}
		catch (Exception ex) when (((ex is InvalidCastException || ex is FormatException || ex is OverflowException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	private static double? Divide(uint numerator, uint denominator)
	{
		if (denominator != 0)
		{
			return (double)numerator / (double)denominator;
		}
		return null;
	}

	internal static double? DecodeUnsignedRational(ulong packed)
	{
		return Divide((uint)packed, (uint)(packed >> 32));
	}

	private static object? ReadExifValue(BitmapMetadata meta, ushort tag)
	{
		string[] array = new string[4]
		{
			$"/app1/ifd/exif/{{ushort={tag}}}",
			$"/app1/ifd/{{ushort={tag}}}",
			$"/ifd/exif/{{ushort={tag}}}",
			$"/ifd/{{ushort={tag}}}"
		};
		foreach (string query in array)
		{
			try
			{
				if (meta.ContainsQuery(query))
				{
					return meta.GetQuery(query);
				}
			}
			catch (NotSupportedException)
			{
			}
		}
		return null;
	}

	private static DateTime? ReadDateTaken(BitmapMetadata meta)
	{
		try
		{
			string dateTaken = meta.DateTaken;
			if (dateTaken != null && dateTaken.Length > 0 && DateTime.TryParse(dateTaken, out var result))
			{
				return result;
			}
		}
		catch (NotSupportedException)
		{
		}
		return null;
	}

	public static int ReadOrientation(BitmapMetadata meta)
	{
		try
		{
			object obj = null;
			string[] array = new string[2] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" };
			foreach (string query in array)
			{
				if (meta.ContainsQuery(query))
				{
					obj = meta.GetQuery(query);
					if (obj != null)
					{
						break;
					}
				}
			}
			int i;
			switch (Convert.ToInt32(obj ?? ((object)1)))
			{
			case 3:
			case 4:
				i = 180;
				break;
			case 5:
			case 6:
				i = 90;
				break;
			case 7:
			case 8:
				i = 270;
				break;
			default:
				i = 0;
				break;
			}
			return i;
		}
		catch (Exception ex) when (((ex is NotSupportedException || ex is InvalidCastException || ex is FormatException) ? 1 : 0) != 0)
		{
			return 0;
		}
	}

	public static string? ComputeQuickHash(string path, long fileSize)
	{
		try
		{
			using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
			using IncrementalHash incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
			incrementalHash.AppendData(BitConverter.GetBytes(fileSize));
			byte[] array = new byte[65536];
			int count = fileStream.ReadAtLeast(array, Math.Min(65536, (int)Math.Min(fileSize, 65536L)), throwOnEndOfStream: false);
			incrementalHash.AppendData(array, 0, count);
			if (fileSize > 131072)
			{
				fileStream.Seek(-65536L, SeekOrigin.End);
				count = fileStream.ReadAtLeast(array, 65536, throwOnEndOfStream: false);
				incrementalHash.AppendData(array, 0, count);
			}
			return Convert.ToHexString(incrementalHash.GetHashAndReset());
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	public static BitmapSource ApplyOrientation(BitmapSource source, int degrees)
	{
		if (((Freezable)source).CanFreeze && !((Freezable)source).IsFrozen)
		{
			((Freezable)source).Freeze();
		}
		if (degrees == 0)
		{
			return source;
		}
		TransformedBitmap transformedBitmap = new TransformedBitmap(source, new RotateTransform(degrees));
		((Freezable)transformedBitmap).Freeze();
		return transformedBitmap;
	}
}
