using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using MagpieTrove.Common;
using MagpieTrove.Data;
using MagpieTrove.Models;
using MagpieTrove.Services;

namespace MagpieTrove.Views;

public partial class DuplicatesWindow : Window, INotifyPropertyChanged
{
	private sealed class GroupRow
	{
		public DuplicateGroup Group { get; }

		public List<EntryRow> Entries { get; }

		public string GroupName { get; }

		public string ReclaimText { get; }

		public GroupRow(DuplicateGroup group)
		{
			GroupRow groupRow = this;
			Group = group;
			GroupName = "grp" + Guid.NewGuid().ToString("N");
			ImageItem best = group.Best;
			Entries = group.Images.Select((ImageItem image) => new EntryRow(image, image == best, groupRow.GroupName)).ToList();
			ReclaimText = ((group.ReclaimableBytes > 0) ? (ImageItem.FormatSize(group.ReclaimableBytes) + " recoverable") : "");
		}
	}

	private sealed class EntryRow : ObservableObject
	{
		private bool _keep;

		public ImageItem Image { get; }

		public bool IsBest { get; }

		public string GroupName { get; }

		public string Detail { get; }

		public string TagSummary { get; }

		public bool HasTags { get; }

		public bool Keep
		{
			get
			{
				return _keep;
			}
			set
			{
				Set(ref _keep, value, "Keep");
			}
		}

		public EntryRow(ImageItem image, bool isBest, string groupName)
		{
			Image = image;
			IsBest = isBest;
			GroupName = groupName;
			_keep = isBest;
			Detail = $"{image.Dimensions}  •  {image.FileSizeDisplay}\n{image.Folder}";
			List<string> tagNames = TagRepository.GetTagNames(image.Id);
			HasTags = tagNames.Count > 0;
			TagSummary = ((tagNames.Count > 0) ? string.Join(", ", tagNames) : "");
		}
	}

	private readonly DuplicateService _duplicates;

	private readonly List<GroupRow> _rows = new List<GroupRow>();

	private CancellationTokenSource? _cts;

	private bool _isBusy;






	public List<long> RemovedImageIds { get; } = new List<long>();

	public bool IsBusy
	{
		get
		{
			return _isBusy;
		}
		private set
		{
			if (_isBusy != value)
			{
				_isBusy = value;
				OnPropertyChanged("IsBusy");
				OnPropertyChanged("IsIdle");
			}
		}
	}

	public bool IsIdle => !_isBusy;

	public event PropertyChangedEventHandler? PropertyChanged;

	public DuplicatesWindow(DuplicateService duplicates)
	{
		_duplicates = duplicates;
		InitializeComponent();
		UpdateEmptyState();
	}

	private void OnPropertyChanged(string name)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}

	private async void OnScan(object sender, RoutedEventArgs e)
	{
		await RunScanAsync((int)Sensitivity.Value);
	}

	public async Task RunScanAsync(int maxDistance)
	{
		if (IsBusy)
		{
			return;
		}
		IsBusy = true;
		_cts = new CancellationTokenSource();
		Progress<DuplicateProgress> progress = new Progress<DuplicateProgress>(delegate(DuplicateProgress p)
		{
			((DispatcherObject)this).Dispatcher.Invoke((Action)delegate
			{
				Progress.Value = p.Percent;
				StatusText.Text = ((p.Total > 0) ? $"{p.Message}  {p.Processed:N0}/{p.Total:N0}" : p.Message);
			});
		});
		try
		{
			DuplicateScan scan = await _duplicates.ScanAsync(maxDistance, progress, _cts.Token).ConfigureAwait(continueOnCapturedContext: false);
			await ((DispatcherObject)this).Dispatcher.InvokeAsync((Action)delegate
			{
				Populate(scan);
			});
		}
		catch (OperationCanceledException)
		{
			await ((DispatcherObject)this).Dispatcher.InvokeAsync<string>((Func<string>)(() => StatusText.Text = "Scan stopped."));
		}
		catch (Exception ex2)
		{
			Exception ex3 = ex2;
			await ((DispatcherObject)this).Dispatcher.InvokeAsync<string>((Func<string>)(() => StatusText.Text = "Scan failed: " + ex3.Message));
		}
		finally
		{
			await ((DispatcherObject)this).Dispatcher.InvokeAsync((Action)delegate
			{
				IsBusy = false;
				_cts?.Dispose();
				_cts = null;
			});
		}
	}

	private void Populate(DuplicateScan scan)
	{
		_rows.Clear();
		foreach (DuplicateGroup group in scan.Groups)
		{
			_rows.Add(new GroupRow(group));
		}
		GroupList.ItemsSource = null;
		GroupList.ItemsSource = _rows;
		SummaryText.Text = ((scan.Groups.Count == 0) ? "No duplicates found." : $"{scan.Groups.Count:N0} group(s), {scan.TotalImages:N0} images, {ImageItem.FormatSize(scan.ReclaimableBytes)} recoverable");
		StatusText.Text = ((scan.Unreadable > 0) ? $"{scan.Unreadable:N0} image(s) could not be fingerprinted." : "");
		UpdateEmptyState();
	}

	private void UpdateEmptyState()
	{
		EmptyText.Visibility = ((_rows.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		if (_rows.Count == 0 && SummaryText.Text.Length > 0)
		{
			EmptyText.Text = "No duplicates at this sensitivity. Try raising it.";
		}
	}

	private void OnCancel(object sender, RoutedEventArgs e)
	{
		_cts?.Cancel();
	}

	private void OnSelectBest(object sender, RoutedEventArgs e)
	{
		foreach (GroupRow row in _rows)
		{
			foreach (EntryRow entry in row.Entries)
			{
				entry.Keep = entry.IsBest;
			}
		}
	}

	private void OnRemoveOthers(object sender, RoutedEventArgs e)
	{
		List<long> list = (from entry in _rows.SelectMany((GroupRow r) => r.Entries.Where((EntryRow entry) => !entry.Keep))
			select entry.Image.Id).Distinct().ToList();
		if (list.Count == 0)
		{
			StatusText.Text = "Nothing marked for removal — every group has all copies kept.";
			return;
		}
		int num = _rows.Count((GroupRow r) => r.Entries.All((EntryRow entry) => !entry.Keep));
		string text = ((num > 0) ? ($"\n\n{num} group(s) have no copy marked Keep — every copy in " + "those groups would be removed.") : "");
		if (MessageBox.Show($"Remove {list.Count:N0} image(s) from the Magpie Trove library?\n\n" + "The files stay on disk; only the library entries and their tags go. This can be undone with Ctrl+Z." + text, "Magpie Trove", MessageBoxButton.OKCancel, (num > 0) ? MessageBoxImage.Exclamation : MessageBoxImage.Question) == MessageBoxResult.OK)
		{
			RemovedImageIds.Clear();
			RemovedImageIds.AddRange(list);
			base.DialogResult = true;
		}
	}

	private void OnClose(object sender, RoutedEventArgs e)
	{
		Close();
	}

}
