using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using MagpieTrove.Data;
using MagpieTrove.Models;

namespace MagpieTrove.Services;

public sealed class EmbeddingPipeline
{
	private const int BatchSize = 16;

	private readonly ThumbnailService _thumbnails;

	public EmbeddingPipeline(ThumbnailService thumbnails)
	{
		_thumbnails = thumbnails;
	}

	public Task<EmbedResult> RunAsync(bool useGpu, IProgress<EmbedProgress>? progress, CancellationToken token)
	{
		return Task.Run(() => Run(useGpu, progress, token), token);
	}

	private EmbedResult Run(bool useGpu, IProgress<EmbedProgress>? progress, CancellationToken token)
	{
		int item = EmbeddingRepository.GetCoverage("clip-vit-b32-vision").Pending;
		if (item == 0)
		{
			return new EmbedResult(0, 0, TimeSpan.Zero);
		}
		using ClipEmbedder clipEmbedder = new ClipEmbedder(null, useGpu);
		DateTime utcNow = DateTime.UtcNow;
		int num = 0;
		int num2 = 0;
		using SqliteConnection sqliteConnection = Database.Open();
		while (!token.IsCancellationRequested)
		{
			List<ImageItem> batch = EmbeddingRepository.GetPending("clip-vit-b32-vision", 16);
			if (batch.Count == 0)
			{
				break;
			}
			BitmapSource?[] decoded = new BitmapSource[batch.Count];
			Parallel.For(0, batch.Count, new ParallelOptions
			{
				MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2),
				CancellationToken = token
			}, delegate(int i)
			{
				decoded[i] = LoadForEmbedding(batch[i]);
			});
			List<BitmapSource> list = new List<BitmapSource>(batch.Count);
			List<ImageItem> list2 = new List<ImageItem>(batch.Count);
			for (int num3 = 0; num3 < batch.Count; num3++)
			{
				BitmapSource bitmapSource = decoded[num3];
				if (bitmapSource == null)
				{
					EmbeddingRepository.Upsert(sqliteConnection, batch[num3].Id, "clip-vit-b32-vision", new float[clipEmbedder.Dimensions]);
					num2++;
				}
				else
				{
					list.Add(bitmapSource);
					list2.Add(batch[num3]);
				}
			}
			if (list.Count > 0)
			{
				float[][] array;
				try
				{
					array = clipEmbedder.Embed(list);
				}
				catch (Exception)
				{
					num2 += list.Count;
					foreach (ImageItem item2 in list2)
					{
						EmbeddingRepository.Upsert(sqliteConnection, item2.Id, "clip-vit-b32-vision", new float[clipEmbedder.Dimensions]);
					}
					continue;
				}
				using SqliteTransaction sqliteTransaction = sqliteConnection.BeginTransaction();
				for (int num4 = 0; num4 < list2.Count; num4++)
				{
					EmbeddingRepository.Upsert(sqliteConnection, list2[num4].Id, "clip-vit-b32-vision", array[num4]);
				}
				sqliteTransaction.Commit();
				num += list2.Count;
			}
			double totalSeconds = (DateTime.UtcNow - utcNow).TotalSeconds;
			int num5 = num + num2;
			progress?.Report(new EmbedProgress(num5, item, (totalSeconds > 0.0) ? ((double)num5 / totalSeconds) : 0.0));
		}
		return new EmbedResult(num, num2, DateTime.UtcNow - utcNow);
	}

	private BitmapSource? LoadForEmbedding(ImageItem item)
	{
		BitmapSource bitmapSource = _thumbnails.TryLoadCached(item);
		if (bitmapSource != null && Math.Min(bitmapSource.PixelWidth, bitmapSource.PixelHeight) >= 224)
		{
			return bitmapSource;
		}
		return DecodeAt(item.Path, 336);
	}

	private static BitmapSource? DecodeAt(string path, int maxEdge)
	{
		try
		{
			using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan);
			BitmapDecoder bitmapDecoder = BitmapDecoder.Create(fileStream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
			if (bitmapDecoder.Frames.Count == 0)
			{
				return null;
			}
			BitmapFrame bitmapFrame = bitmapDecoder.Frames[0];
			int degrees = ((bitmapFrame.Metadata is BitmapMetadata meta) ? ImageFileInfo.ReadOrientation(meta) : 0);
			int pixelWidth = bitmapFrame.PixelWidth;
			int pixelHeight = bitmapFrame.PixelHeight;
			int num = pixelWidth;
			if (num <= 0 || pixelHeight <= 0)
			{
				return null;
			}
			fileStream.Position = 0L;
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
			bitmapImage.StreamSource = fileStream;
			if (num <= pixelHeight)
			{
				bitmapImage.DecodePixelWidth = Math.Min(maxEdge, num);
			}
			else
			{
				bitmapImage.DecodePixelHeight = Math.Min(maxEdge, pixelHeight);
			}
			bitmapImage.EndInit();
			((Freezable)bitmapImage).Freeze();
			return ImageFileInfo.ApplyOrientation(bitmapImage, degrees);
		}
		catch (Exception ex) when (((ex is IOException || ex is NotSupportedException || ex is FileFormatException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is OutOfMemoryException || ex is OverflowException) ? 1 : 0) != 0)
		{
			return null;
		}
	}
}
