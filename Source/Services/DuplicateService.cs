using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using MagpieTrove.Data;
using MagpieTrove.Models;

namespace MagpieTrove.Services;

public sealed class DuplicateService
{
	private sealed class UnionFind
	{
		private readonly int[] _parent;

		private readonly int[] _rank;

		public UnionFind(int size)
		{
			_parent = new int[size];
			_rank = new int[size];
			for (int i = 0; i < size; i++)
			{
				_parent[i] = i;
			}
		}

		public int Find(int x)
		{
			while (_parent[x] != x)
			{
				_parent[x] = _parent[_parent[x]];
				x = _parent[x];
			}
			return x;
		}

		public void Union(int a, int b)
		{
			int num = Find(a);
			int num2 = Find(b);
			if (num != num2)
			{
				if (_rank[num] < _rank[num2])
				{
					int num3 = num2;
					num2 = num;
					num = num3;
				}
				_parent[num2] = num;
				if (_rank[num] == _rank[num2])
				{
					_rank[num]++;
				}
			}
		}
	}

	private readonly ThumbnailService _thumbnails;

	public DuplicateService(ThumbnailService thumbnails)
	{
		_thumbnails = thumbnails;
	}

	public Task<DuplicateScan> ScanAsync(int maxDistance, IProgress<DuplicateProgress>? progress, CancellationToken token)
	{
		return Task.Run(() => Scan(maxDistance, progress, token), token);
	}

	private DuplicateScan Scan(int maxDistance, IProgress<DuplicateProgress>? progress, CancellationToken token)
	{
		(int, int) tuple = EnsureHashes(progress, token);
		progress?.Report(new DuplicateProgress("Comparing…", 0, 0));
		List<ImageRepository.HashEntry> entries = ImageRepository.GetHashIndex();
		if (entries.Count < 2)
		{
			return new DuplicateScan(new List<DuplicateGroup>(), tuple.Item1, tuple.Item2);
		}
		UnionFind unionFind = new UnionFind(entries.Count);
		Dictionary<string, int> dictionary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < entries.Count; i++)
		{
			string quickHash = entries[i].QuickHash;
			if (quickHash != null && quickHash.Length > 0)
			{
				if (dictionary.TryGetValue(quickHash, out var value))
				{
					unionFind.Union(value, i);
				}
				else
				{
					dictionary[quickHash] = i;
				}
			}
		}
		int count = entries.Count;
		for (int j = 0; j < count; j++)
		{
			if ((j & 0x3FF) == 0)
			{
				token.ThrowIfCancellationRequested();
				progress?.Report(new DuplicateProgress("Comparing…", j, count));
			}
			ulong? perceptualHash = entries[j].PerceptualHash;
			if (!perceptualHash.HasValue)
			{
				continue;
			}
			ulong valueOrDefault = perceptualHash.GetValueOrDefault();
			for (int k = j + 1; k < count; k++)
			{
				perceptualHash = entries[k].PerceptualHash;
				if (perceptualHash.HasValue)
				{
					ulong valueOrDefault2 = perceptualHash.GetValueOrDefault();
					if (PerceptualHash.Distance(valueOrDefault, valueOrDefault2) <= maxDistance)
					{
						unionFind.Union(j, k);
					}
				}
			}
		}
		Dictionary<int, List<int>> dictionary2 = new Dictionary<int, List<int>>();
		for (int l = 0; l < count; l++)
		{
			int key = unionFind.Find(l);
			if (!dictionary2.TryGetValue(key, out var value2))
			{
				value2 = (dictionary2[key] = new List<int>());
			}
			value2.Add(l);
		}
		List<long> imageIds = dictionary2.Values.Where((List<int> c) => c.Count > 1).SelectMany((List<int> c) => c.Select((int index) => entries[index].Id)).ToList();
		Dictionary<long, ImageItem> images = ImageRepository.GetByIds(imageIds).ToDictionary((ImageItem imageItem) => imageItem.Id);
		List<DuplicateGroup> list2 = new List<DuplicateGroup>();
		foreach (List<int> item in dictionary2.Values.Where((List<int> c) => c.Count > 1))
		{
			List<ImageItem> list3 = item.Select((int index) => images.GetValueOrDefault(entries[index].Id)).OfType<ImageItem>().ToList();
			if (list3.Count < 2)
			{
				continue;
			}
			int num = (from index in item
				select entries[index].QuickHash into h
				where h != null && h.Length > 0
				select h).Distinct<string>(StringComparer.OrdinalIgnoreCase).Count();
			int num2 = 0;
			for (int num3 = 0; num3 < item.Count; num3++)
			{
				for (int num4 = num3 + 1; num4 < item.Count; num4++)
				{
					ulong? perceptualHash = entries[item[num3]].PerceptualHash;
					if (perceptualHash.HasValue)
					{
						ulong valueOrDefault3 = perceptualHash.GetValueOrDefault();
						perceptualHash = entries[item[num4]].PerceptualHash;
						if (perceptualHash.HasValue)
						{
							ulong valueOrDefault4 = perceptualHash.GetValueOrDefault();
							num2 = Math.Max(num2, PerceptualHash.Distance(valueOrDefault3, valueOrDefault4));
						}
					}
				}
			}
			list2.Add(new DuplicateGroup
			{
				Kind = ((num != 1 || list3.Count <= 1) ? DuplicateKind.NearIdentical : DuplicateKind.Identical),
				Images = (from imageItem in list3
					orderby (long)imageItem.Width * (long)imageItem.Height descending, imageItem.FileSize descending
					select imageItem).ToList(),
				Spread = num2
			});
		}
		list2 = (from g in list2
			orderby g.Kind == DuplicateKind.Identical descending, g.ReclaimableBytes descending
			select g).ToList();
		progress?.Report(new DuplicateProgress("Done", count, count));
		return new DuplicateScan(list2, tuple.Item1, tuple.Item2);
	}

	private (int Hashed, int Unreadable) EnsureHashes(IProgress<DuplicateProgress>? progress, CancellationToken token)
	{
		List<ImageItem> pending = ImageRepository.GetImagesWithoutPerceptualHash();
		if (pending.Count == 0)
		{
			return (Hashed: 0, Unreadable: 0);
		}
		int total = pending.Count;
		int done = 0;
		int num = 0;
		(long Id, ulong? Hash)[] results = new(long, ulong?)[pending.Count];
		Parallel.For(0, pending.Count, new ParallelOptions
		{
			MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2),
			CancellationToken = token
		}, delegate(int i)
		{
			BitmapSource bitmapSource = Load(pending[i]);
			results[i] = (Id: pending[i].Id, Hash: (bitmapSource == null) ? ((ulong?)null) : new ulong?(PerceptualHash.Compute(bitmapSource)));
			int num2 = Interlocked.Increment(ref done);
			if ((num2 & 0x3F) == 0)
			{
				progress?.Report(new DuplicateProgress("Fingerprinting…", num2, total));
			}
		});
		num = results.Count(delegate((long Id, ulong? Hash) r)
		{
			ulong? item = r.Hash;
			return !item.HasValue;
		});
		ImageRepository.SetPerceptualHashes(from r in results.Where(delegate((long Id, ulong? Hash) r)
			{
				ulong? item = r.Hash;
				return item.HasValue;
			})
			select (Id: r.Id, Value: r.Hash.Value));
		return (Hashed: results.Length - num, Unreadable: num);
	}

	private BitmapSource? Load(ImageItem item)
	{
		BitmapSource bitmapSource = _thumbnails.TryLoadCached(item);
		if (bitmapSource != null)
		{
			return bitmapSource;
		}
		try
		{
			using FileStream streamSource = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan);
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
			bitmapImage.StreamSource = streamSource;
			bitmapImage.DecodePixelWidth = 64;
			bitmapImage.EndInit();
			((Freezable)bitmapImage).Freeze();
			return bitmapImage;
		}
		catch (Exception ex) when (((ex is IOException || ex is NotSupportedException || ex is FileFormatException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is OutOfMemoryException || ex is OverflowException) ? 1 : 0) != 0)
		{
			return null;
		}
	}
}
