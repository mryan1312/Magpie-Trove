using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MagpieTrove.Services;

public sealed class LibraryWatchService : IDisposable
{
	private readonly Func<IReadOnlyList<LibraryFileChange>, bool, Task> _callback;

	private readonly ConcurrentDictionary<string, LibraryFileChange> _pending = new ConcurrentDictionary<string, LibraryFileChange>(StringComparer.OrdinalIgnoreCase);

	private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();

	private readonly Timer _timer;

	private int _fullRescanRequested;

	private int _flushing;

	private bool _disposed;

	public LibraryWatchService(Func<IReadOnlyList<LibraryFileChange>, bool, Task> callback)
	{
		_callback = callback;
		_timer = new Timer(delegate(object? _)
		{
			_ = FlushAsync();
		}, null, -1, -1);
	}

	public void Configure(IEnumerable<string> roots)
	{
		foreach (FileSystemWatcher watcher in _watchers)
		{
			watcher.Dispose();
		}
		_watchers.Clear();
		foreach (string item in roots.Distinct<string>(StringComparer.OrdinalIgnoreCase))
		{
			if (Directory.Exists(item))
			{
				try
				{
					FileSystemWatcher fileSystemWatcher = new FileSystemWatcher(item)
					{
						IncludeSubdirectories = true,
						NotifyFilter = (NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime),
						InternalBufferSize = 65536
					};
					fileSystemWatcher.Created += OnChanged;
					fileSystemWatcher.Changed += OnChanged;
					fileSystemWatcher.Deleted += OnChanged;
					fileSystemWatcher.Renamed += OnRenamed;
					fileSystemWatcher.Error += OnError;
					fileSystemWatcher.EnableRaisingEvents = true;
					_watchers.Add(fileSystemWatcher);
				}
				catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException) ? 1 : 0) != 0)
				{
					RequestFullRescan();
				}
			}
		}
	}

	private void OnChanged(object sender, FileSystemEventArgs e)
	{
		if (ImageFileInfo.IsSupported(e.FullPath))
		{
			_pending[e.FullPath] = new LibraryFileChange(e.ChangeType, e.FullPath);
		}
		else if (e.ChangeType == WatcherChangeTypes.Deleted)
		{
			RequestFullRescan();
		}
		Schedule();
	}

	private void OnRenamed(object sender, RenamedEventArgs e)
	{
		bool flag = ImageFileInfo.IsSupported(e.OldFullPath);
		bool flag2 = ImageFileInfo.IsSupported(e.FullPath);
		if (flag & flag2)
		{
			_pending[e.FullPath] = new LibraryFileChange(WatcherChangeTypes.Renamed, e.FullPath, e.OldFullPath);
		}
		else if (flag)
		{
			_pending[e.OldFullPath] = new LibraryFileChange(WatcherChangeTypes.Deleted, e.OldFullPath);
		}
		else if (flag2)
		{
			_pending[e.FullPath] = new LibraryFileChange(WatcherChangeTypes.Created, e.FullPath);
		}
		else
		{
			RequestFullRescan();
		}
		Schedule();
	}

	private void OnError(object sender, ErrorEventArgs e)
	{
		RequestFullRescan();
	}

	private void RequestFullRescan()
	{
		Interlocked.Exchange(ref _fullRescanRequested, 1);
		Schedule();
	}

	private void Schedule()
	{
		if (!_disposed)
		{
			_timer.Change(750, -1);
		}
	}

	private async Task FlushAsync()
	{
		if (_disposed || Interlocked.Exchange(ref _flushing, 1) != 0)
		{
			return;
		}
		try
		{
			List<LibraryFileChange> list = _pending.Values.ToList();
			_pending.Clear();
			bool flag = Interlocked.Exchange(ref _fullRescanRequested, 0) != 0;
			if ((list.Count > 0) | flag)
			{
				await _callback(list, flag);
			}
		}
		catch
		{
		}
		finally
		{
			Interlocked.Exchange(ref _flushing, 0);
			if (!_pending.IsEmpty || Volatile.Read(in _fullRescanRequested) != 0)
			{
				Schedule();
			}
		}
	}

	public void Dispose()
	{
		_disposed = true;
		_timer.Dispose();
		foreach (FileSystemWatcher watcher in _watchers)
		{
			watcher.Dispose();
		}
		_watchers.Clear();
	}
}
