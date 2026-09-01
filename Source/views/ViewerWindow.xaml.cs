using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MagpieTrove.Data;
using MagpieTrove.Models;
using MagpieTrove.Services;
using MagpieTrove.ViewModels;

namespace MagpieTrove.Views;

public partial class ViewerWindow : Window
{
	private const int MaxDecodeEdge = 6000;

	private readonly MainViewModel _vm;

	private readonly IReadOnlyList<ImageItem> _images;

	private readonly IReadOnlyList<ImageItem> _comparePair;

	private readonly DispatcherTimer _slideshowTimer;

	private int _index;

	private int _loadToken;

	private BitmapSource? _bitmap;

	private bool _isPanning;

	private Point _panOrigin;

	private double _viewScale;

	private double _viewLeft;

	private double _viewTop;

	private double _leftAtPanStart;

	private double _topAtPanStart;

	private bool _viewAdjusted;

	private bool _syncingFilmstrip;

	private bool _isComparing;











	public int CurrentIndex => _index;

	private ImageItem? Current
	{
		get
		{
			if (_index < 0 || _index >= _images.Count)
			{
				return null;
			}
			return _images[_index];
		}
	}

	public ViewerWindow(MainViewModel viewModel, IReadOnlyList<ImageItem> images, int startIndex, IReadOnlyList<ImageItem>? compareSelection = null)
	{
		_viewScale = 1.0;
		_vm = viewModel;
		_images = images;
		IReadOnlyList<ImageItem>? comparePair;
		if (compareSelection == null || compareSelection.Count != 2)
		{
			IReadOnlyList<ImageItem> readOnlyList = Array.Empty<ImageItem>();
			comparePair = readOnlyList;
		}
		else
		{
			comparePair = compareSelection;
		}
		_comparePair = comparePair;
		InitializeComponent();
		base.DataContext = _vm;
		_index = Math.Clamp(startIndex, 0, Math.Max(0, images.Count - 1));
		Filmstrip.ItemsSource = images;
		CompareButton.IsEnabled = _comparePair.Count == 2;
		_slideshowTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(3L)
		};
		_slideshowTimer.Tick += delegate
		{
			Step(1);
		};
		base.Loaded += delegate
		{
			ShowCurrent();
		};
		base.PreviewKeyDown += OnPreviewKeyDown;
		base.Closed += delegate
		{
			_slideshowTimer.Stop();
		};
	}

	private void ShowCurrent()
	{
		ImageItem current = Current;
		if (current == null)
		{
			Close();
			return;
		}
		_vm.UpdateSelection([current]);
		_syncingFilmstrip = true;
		Filmstrip.SelectedIndex = _index;
		Filmstrip.ScrollIntoView(current);
		_syncingFilmstrip = false;
		TitleText.Text = current.FileName;
		SubtitleText.Text = $"{_index + 1:N0} of {_images.Count:N0}   •   {current.Dimensions}   •   {current.FileSizeDisplay}   •   {current.EffectiveDate:g}   •   {current.Folder}";
		LoadAsync(current);
	}

	private async void LoadAsync(ImageItem item)
	{
		int token = ++_loadToken;
		ImageView.Source = null;
		_bitmap = null;
		LoadingText.Visibility = Visibility.Visible;
		ZoomText.Text = "";
		BitmapSource bitmapSource = await Task.Run(() => Decode(item.Path, item.RotationOverride));
		if (token == _loadToken)
		{
			LoadingText.Visibility = Visibility.Collapsed;
			if (bitmapSource == null)
			{
				LoadingText.Text = (File.Exists(item.Path) ? "This file could not be decoded." : "This file is no longer on disk.");
				LoadingText.Visibility = Visibility.Visible;
				return;
			}
			LoadingText.Text = "Loading…";
			_bitmap = bitmapSource;
			ImageView.Source = bitmapSource;
			FitToWindow();
		}
	}

	private static BitmapSource? Decode(string path, int rotationOverride)
	{
		try
		{
			using FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16384, FileOptions.SequentialScan);
			BitmapDecoder bitmapDecoder = BitmapDecoder.Create(fileStream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
			if (bitmapDecoder.Frames.Count == 0)
			{
				return null;
			}
			BitmapFrame bitmapFrame = bitmapDecoder.Frames[0];
			int degrees = ((bitmapFrame.Metadata is BitmapMetadata meta) ? ImageFileInfo.ReadOrientation(meta) : 0);
			int num = Math.Max(bitmapFrame.PixelWidth, bitmapFrame.PixelHeight);
			fileStream.Position = 0L;
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
			bitmapImage.StreamSource = fileStream;
			if (num > 6000)
			{
				if (bitmapFrame.PixelWidth >= bitmapFrame.PixelHeight)
				{
					bitmapImage.DecodePixelWidth = 6000;
				}
				else
				{
					bitmapImage.DecodePixelHeight = 6000;
				}
			}
			bitmapImage.EndInit();
			((Freezable)bitmapImage).Freeze();
			BitmapSource bitmapSource = ImageFileInfo.ApplyOrientation(bitmapImage, degrees);
			if (rotationOverride == 0)
			{
				return bitmapSource;
			}
			TransformedBitmap transformedBitmap = new TransformedBitmap(bitmapSource, new RotateTransform(rotationOverride));
			((Freezable)transformedBitmap).Freeze();
			return transformedBitmap;
		}
		catch (Exception ex) when (((ex is IOException || ex is NotSupportedException || ex is FileFormatException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is OutOfMemoryException || ex is OverflowException) ? 1 : 0) != 0)
		{
			return null;
		}
	}

	private void Step(int delta)
	{
		if (_images.Count != 0)
		{
			if (_isComparing)
			{
				SetCompareMode(enabled: false);
			}
			_index = (_index + delta + _images.Count) % _images.Count;
			ShowCurrent();
		}
	}

	private void SetView(ViewerPlacement placement)
	{
		if (_bitmap != null)
		{
			_viewScale = placement.Scale;
			_viewLeft = placement.Left;
			_viewTop = placement.Top;
			ImageView.Width = (double)_bitmap.PixelWidth * _viewScale;
			ImageView.Height = (double)_bitmap.PixelHeight * _viewScale;
			Canvas.SetLeft(ImageView, _viewLeft);
			Canvas.SetTop(ImageView, _viewTop);
			ZoomText.Text = $"{_viewScale * 100.0:0}%";
		}
	}

	private void FitToWindow()
	{
		if (_bitmap != null)
		{
			double actualWidth = Viewport.ActualWidth;
			double actualHeight = Viewport.ActualHeight;
			if (!(actualWidth <= 0.0) && !(actualHeight <= 0.0))
			{
				SetView(ViewerLayout.Fit(_bitmap.PixelWidth, _bitmap.PixelHeight, actualWidth, actualHeight));
				_viewAdjusted = false;
			}
		}
	}

	private void ZoomTo(double scale, Point center)
	{
		if (_bitmap != null)
		{
			SetView(ViewerLayout.ZoomAround(_viewScale, _viewLeft, _viewTop, scale, center.X, center.Y));
			_viewAdjusted = true;
		}
	}

	private void ActualSize()
	{
		if (_bitmap != null)
		{
			ZoomTo(1.0, new Point(Viewport.ActualWidth / 2.0, Viewport.ActualHeight / 2.0));
		}
	}

	private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (_bitmap != null)
		{
			double num = ((e.Delta > 0) ? 1.15 : 0.8695652173913044);
			double scale = Math.Clamp(_viewScale * num, 0.02, 40.0);
			ZoomTo(scale, e.GetPosition(Viewport));
			e.Handled = true;
		}
	}

	private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ClickCount == 2)
		{
			if (Math.Abs(_viewScale - 1.0) < 0.01)
			{
				FitToWindow();
			}
			else
			{
				ZoomTo(1.0, e.GetPosition(Viewport));
			}
		}
		else if (_bitmap != null)
		{
			_isPanning = true;
			_panOrigin = e.GetPosition(Viewport);
			_leftAtPanStart = _viewLeft;
			_topAtPanStart = _viewTop;
			Viewport.CaptureMouse();
			Viewport.Cursor = Cursors.SizeAll;
		}
	}

	private void OnViewportMouseMove(object sender, MouseEventArgs e)
	{
		if (_isPanning)
		{
			Point position = e.GetPosition(Viewport);
			SetView(new ViewerPlacement(_viewScale, _leftAtPanStart + position.X - _panOrigin.X, _topAtPanStart + position.Y - _panOrigin.Y));
			_viewAdjusted = true;
		}
	}

	private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
	{
		if (_isPanning)
		{
			_isPanning = false;
			Viewport.ReleaseMouseCapture();
			Viewport.Cursor = Cursors.Arrow;
		}
	}

	private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
	{
		if (_bitmap != null && !_viewAdjusted)
		{
			FitToWindow();
		}
	}

	private void OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		bool flag = TagEntry.IsKeyboardFocusWithin;
		Key key;
		if (flag)
		{
			key = e.Key;
			bool flag2 = (((int)key == 13 || (int)key == 94) ? true : false);
			flag = !flag2;
		}
		if (flag)
		{
			return;
		}
		key = e.Key;
		int num;
		if ((int)key <= 39)
		{
			if ((int)key >= 35)
			{
				if ((int)key == 35)
				{
					num = 0;
					goto IL_01bb;
				}
				num = 3;
				goto IL_02dc;
			}
			switch ((int)key - 13)
			{
			case 0:
				goto IL_013a;
			case 6:
			case 10:
				goto IL_015c;
			case 5:
			case 7:
			case 12:
				goto IL_0168;
			case 9:
				goto IL_0174;
			case 8:
				goto IL_0186;
			case 1:
			case 2:
			case 3:
			case 4:
			case 11:
				return;
			}
			if ((int)key != 34)
			{
				return;
			}
		}
		else
		{
			if ((int)key <= 43)
			{
				goto IL_0313;
			}
			if ((int)key > 75)
			{
				if ((int)key <= 141)
				{
					if ((int)key != 85)
					{
						if ((int)key == 87)
						{
							goto IL_0232;
						}
						if ((int)key != 141)
						{
							return;
						}
					}
					ZoomTo(Math.Min(40.0, _viewScale * 1.25), new Point(Viewport.ActualWidth / 2.0, Viewport.ActualHeight / 2.0));
				}
				else
				{
					if ((int)key == 143)
					{
						goto IL_0232;
					}
					if ((int)key != 149)
					{
						if ((int)key != 151)
						{
							return;
						}
						RotateCurrent(90);
					}
					else
					{
						RotateCurrent(-90);
					}
				}
				goto IL_036c;
			}
			if ((int)key <= 49)
			{
				if ((int)key == 46)
				{
					ToggleCompare();
					goto IL_036c;
				}
				if ((int)key != 49)
				{
					return;
				}
			}
			else
			{
				switch ((int)key - 59)
				{
				case 4:
					goto IL_028a;
				case 3:
					goto IL_02a6;
				case 0:
					goto IL_032c;
				case 8:
					goto IL_0341;
				case 5:
					goto IL_0356;
				case 1:
				case 2:
				case 6:
				case 7:
					return;
				}
				if ((int)key != 74)
				{
					if ((int)key != 75)
					{
						return;
					}
					num = 2;
					goto IL_01bb;
				}
			}
		}
		FitToWindow();
		goto IL_036c;
		IL_0341:
		if ((int)Keyboard.Modifiers == 0)
		{
			_vm.SetFlag(-1);
			goto IL_036c;
		}
		return;
		IL_028a:
		TagEntry.Focus();
		TagEntry.SelectAll();
		goto IL_036c;
		IL_0356:
		if ((int)Keyboard.Modifiers == 0)
		{
			_vm.SetFlag(0);
			goto IL_036c;
		}
		return;
		IL_01bb:
		if ((int)Keyboard.Modifiers != 0)
		{
			if (num == 0)
			{
				num = 1;
				goto IL_02dc;
			}
			if (num == 2)
			{
				return;
			}
		}
		ActualSize();
		goto IL_036c;
		IL_0232:
		ZoomTo(Math.Max(0.02, _viewScale / 1.25), new Point(Viewport.ActualWidth / 2.0, Viewport.ActualHeight / 2.0));
		goto IL_036c;
		IL_015c:
		Step(-1);
		goto IL_036c;
		IL_0168:
		Step(1);
		goto IL_036c;
		IL_02a6:
		ToggleSlideshow();
		goto IL_036c;
		IL_0186:
		_index = Math.Max(0, _images.Count - 1);
		ShowCurrent();
		goto IL_036c;
		IL_013a:
		if (TagEntry.IsKeyboardFocusWithin)
		{
			Keyboard.ClearFocus();
		}
		else
		{
			Close();
		}
		goto IL_036c;
		IL_032c:
		if ((int)Keyboard.Modifiers == 0)
		{
			_vm.SetFlag(1);
			goto IL_036c;
		}
		return;
		IL_02dc:
		if ((int)Keyboard.Modifiers != 2)
		{
			if (num == 1)
			{
				return;
			}
			if (num == 3)
			{
				goto IL_0313;
			}
		}
		_vm.SetRatingCommand.Execute((e.Key - Key.D0).ToString());
		goto IL_036c;
		IL_036c:
		e.Handled = true;
		return;
		IL_0313:
		if ((int)Keyboard.Modifiers != 0 || ApplyPinned(e.Key - Key.D0))
		{
			return;
		}
		goto IL_036c;
		IL_0174:
		_index = 0;
		ShowCurrent();
		goto IL_036c;
	}

	private bool ApplyPinned(int slot)
	{
		TagItem tagItem = _vm.PinnedTags.FirstOrDefault((TagItem t) => t.PinnedSlot == slot);
		if (tagItem == null)
		{
			return false;
		}
		_vm.ApplyTagText(tagItem.FullPath);
		if (_vm.AutoAdvance)
		{
			Step(1);
		}
		return true;
	}

	private void OnTagEntryKeyDown(object sender, KeyEventArgs e)
	{
		if ((int)e.Key == 6)
		{
			_vm.AddTagCommand.Execute(null);
			e.Handled = true;
		}
	}

	private void OnPreviousClick(object sender, RoutedEventArgs e)
	{
		Step(-1);
	}

	private void OnNextClick(object sender, RoutedEventArgs e)
	{
		Step(1);
	}

	private void OnFitClick(object sender, RoutedEventArgs e)
	{
		FitToWindow();
	}

	private void OnActualSizeClick(object sender, RoutedEventArgs e)
	{
		ActualSize();
	}

	private void OnRotateLeftClick(object sender, RoutedEventArgs e)
	{
		RotateCurrent(-90);
	}

	private void OnRotateRightClick(object sender, RoutedEventArgs e)
	{
		RotateCurrent(90);
	}

	private void OnSlideshowClick(object sender, RoutedEventArgs e)
	{
		ToggleSlideshow();
	}

	private void OnCompareClick(object sender, RoutedEventArgs e)
	{
		ToggleCompare();
	}

	private void OnCloseClick(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void RotateCurrent(int delta)
	{
		ImageItem current = Current;
		if (current != null)
		{
			current.RotationOverride = ((current.RotationOverride + delta) % 360 + 360) % 360;
			ImageRepository.SetRotationOverride(current.Id, current.RotationOverride);
			LoadAsync(current);
		}
	}

	private void OnFilmstripSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_syncingFilmstrip && Filmstrip.SelectedIndex >= 0 && Filmstrip.SelectedIndex != _index)
		{
			_index = Filmstrip.SelectedIndex;
			if (_isComparing)
			{
				SetCompareMode(enabled: false);
			}
			ShowCurrent();
		}
	}

	private void ToggleSlideshow()
	{
		if (_slideshowTimer.IsEnabled)
		{
			_slideshowTimer.Stop();
			SlideshowButton.Content = "Play";
			return;
		}
		int num = (int.TryParse((SlideshowInterval.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var result) ? result : 3);
		_slideshowTimer.Interval = TimeSpan.FromSeconds(num);
		if (_isComparing)
		{
			SetCompareMode(enabled: false);
		}
		_slideshowTimer.Start();
		SlideshowButton.Content = "Stop";
	}

	private async void ToggleCompare()
	{
		if (_isComparing)
		{
			SetCompareMode(enabled: false);
		}
		else if (_comparePair.Count == 2)
		{
			_slideshowTimer.Stop();
			SlideshowButton.Content = "Play";
			LoadingText.Visibility = Visibility.Visible;
			ImageItem leftItem = _comparePair[0];
			ImageItem rightItem = _comparePair[1];
			(BitmapSource, BitmapSource) tuple = await Task.Run(() => (Decode(leftItem.Path, leftItem.RotationOverride), Decode(rightItem.Path, rightItem.RotationOverride)));
			LoadingText.Visibility = Visibility.Collapsed;
			if (tuple.Item1 == null || tuple.Item2 == null)
			{
				MessageBox.Show(this, "One of the comparison images could not be decoded.", "Magpie Trove", MessageBoxButton.OK, MessageBoxImage.Asterisk);
				return;
			}
			CompareLeft.Source = tuple.Item1;
			CompareRight.Source = tuple.Item2;
			TitleText.Text = leftItem.FileName + "  ↔  " + rightItem.FileName;
			SubtitleText.Text = "Side-by-side comparison";
			SetCompareMode(enabled: true);
		}
	}

	private void SetCompareMode(bool enabled)
	{
		_isComparing = enabled;
		CompareSurface.Visibility = ((!enabled) ? Visibility.Collapsed : Visibility.Visible);
		ImageView.Visibility = (enabled ? Visibility.Collapsed : Visibility.Visible);
		CompareButton.Content = (enabled ? "Single" : "Compare");
		if (!enabled)
		{
			ShowCurrent();
		}
	}

}
