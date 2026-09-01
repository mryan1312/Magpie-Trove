using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MagpieTrove.Data;
using MagpieTrove.Models;

namespace MagpieTrove.Services;

public sealed class ThumbnailService : IThumbnailSource, IDisposable
{
	public const int ThumbnailSize = 320;

	private const int MemoryCacheLimit = 400;

	private readonly string _cacheDirectory;

	private readonly Dispatcher _dispatcher;

	private readonly ConcurrentStack<ImageItem> _pending = new ConcurrentStack<ImageItem>();

	private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);

	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	private readonly List<Task> _workers = new List<Task>();

	private readonly LinkedList<ImageItem> _lru = new LinkedList<ImageItem>();

	private readonly Dictionary<ImageItem, LinkedListNode<ImageItem>> _lruIndex = new Dictionary<ImageItem, LinkedListNode<ImageItem>>();

	private readonly Lock _lruLock = new Lock();

	public ThumbnailService(Dispatcher dispatcher)
	{
		_dispatcher = dispatcher;
		_cacheDirectory = Path.Combine(Database.DataDirectory, "thumbnails");
		Directory.CreateDirectory(_cacheDirectory);
		int num = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);
		for (int i = 0; i < num; i++)
		{
			_workers.Add(Task.Run(() => WorkerLoop(_cts.Token)));
		}
	}

	public void Request(ImageItem item)
	{
		_pending.Push(item);
		_signal.Release();
	}

	public void ClearQueue()
	{
		_pending.Clear();
	}

	private async Task WorkerLoop(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			try
			{
				await _signal.WaitAsync(token).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (OperationCanceledException)
			{
				break;
			}
			if (!_pending.TryPop(out ImageItem item))
			{
				continue;
			}
			BitmapSource bitmapSource = null;
			try
			{
				bitmapSource = LoadOrCreate(item);
			}
			catch (Exception)
			{
			}
			if (token.IsCancellationRequested)
			{
				break;
			}
			BitmapSource loaded = bitmapSource;
			_dispatcher.BeginInvoke((DispatcherPriority)4, (Delegate)(Action)delegate
			{
				item.SetThumbnail(loaded);
				if (loaded != null)
				{
					Touch(item);
				}
			});
		}
	}

	private BitmapSource? LoadOrCreate(ImageItem item)
	{
		string text = CachePathFor(item);
		if (File.Exists(text))
		{
			BitmapSource bitmapSource = TryDecode(text, 320, applyOrientation: false);
			if (bitmapSource != null)
			{
				return bitmapSource;
			}
			TryDelete(text);
		}
		BitmapSource bitmapSource2 = TryDecode(item.Path, 320, applyOrientation: true);
		if (bitmapSource2 == null)
		{
			return null;
		}
		TrySaveCache(bitmapSource2, text);
		return bitmapSource2;
	}

	private static BitmapSource? TryDecode(string path, int maxEdge, bool applyOrientation)
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
			int degrees = 0;
			if (applyOrientation && bitmapFrame.Metadata is BitmapMetadata meta)
			{
				degrees = ImageFileInfo.ReadOrientation(meta);
			}
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
			if (num >= pixelHeight)
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

	public BitmapSource? TryLoadCached(ImageItem item)
	{
		string path = CachePathFor(item);
		if (!File.Exists(path))
		{
			return null;
		}
		return TryDecode(path, 320, applyOrientation: false);
	}

	private static void TrySaveCache(BitmapSource bitmap, string cachePath)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
			JpegBitmapEncoder jpegBitmapEncoder = new JpegBitmapEncoder
			{
				QualityLevel = 82
			};
			jpegBitmapEncoder.Frames.Add(BitmapFrame.Create(bitmap));
			string text = cachePath + ".tmp";
			using (FileStream stream = new FileStream(text, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				jpegBitmapEncoder.Save(stream);
			}
			File.Move(text, cachePath, overwrite: true);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException) ? 1 : 0) != 0)
		{
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
		}
	}

	private string CachePathFor(ImageItem item)
	{
		string s = $"{item.Path.ToLowerInvariant()}|{item.FileSize}|{item.DateModified.Ticks}|{320}";
		string text = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)));
		return Path.Combine(_cacheDirectory, text.Substring(0, 2), text + ".jpg");
	}

	private void Touch(ImageItem item)
	{
		List<ImageItem> list = null;
		using (_lruLock.EnterScope())
		{
			if (_lruIndex.TryGetValue(item, out LinkedListNode<ImageItem> value))
			{
				_lru.Remove(value);
				_lru.AddLast(value);
			}
			else
			{
				_lruIndex[item] = _lru.AddLast(item);
			}
			while (_lru.Count > 400)
			{
				LinkedListNode<ImageItem> first = _lru.First;
				_lru.RemoveFirst();
				_lruIndex.Remove(first.Value);
				(list ?? (list = new List<ImageItem>())).Add(first.Value);
			}
		}
		if (list == null)
		{
			return;
		}
		foreach (ImageItem item2 in list)
		{
			item2.SetThumbnail(null);
		}
	}

	public long GetCacheSizeBytes()
	{
		try
		{
			return new DirectoryInfo(_cacheDirectory).EnumerateFiles("*", SearchOption.AllDirectories).Sum((FileInfo f) => f.Length);
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			return 0L;
		}
	}

	public void ClearDiskCache()
	{
		try
		{
			foreach (string item in Directory.EnumerateDirectories(_cacheDirectory))
			{
				Directory.Delete(item, recursive: true);
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
		}
	}

	public void Dispose()
	{
		_cts.Cancel();
		_signal.Release(_workers.Count);
		_cts.Dispose();
		_signal.Dispose();
	}
}
