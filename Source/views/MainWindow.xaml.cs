using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using MagpieTrove.Models;
using MagpieTrove.Services;
using MagpieTrove.ViewModels;

namespace MagpieTrove.Views;

public partial class MainWindow : Window
{
	private readonly MainViewModel _vm;

	private bool _saveSettingsOnClose = true;

	private Point _tagPressPosition;

	private TagItem? _tagPressed;

	private bool _tagDragging;




	public MainWindow(MainViewModel viewModel)
	{
		_vm = viewModel;
		InitializeComponent();
		base.DataContext = _vm;
		_vm.FocusSearchRequested += delegate
		{
			SearchBox.Focus();
			SearchBox.SelectAll();
		};
		_vm.AdvanceRequested += AdvanceSelection;
		base.Loaded += delegate
		{
			_vm.Load();
		};
		base.ContentRendered += async delegate
		{
			await _vm.ReconcileAtStartupAsync();
		};
		base.Closing += delegate
		{
			if (_saveSettingsOnClose)
			{
				_vm.SaveSettings();
			}
			_vm.Dispose();
		};
	}

	public void PrepareForLibrarySwitch()
	{
		_vm.SaveSettings();
		_saveSettingsOnClose = false;
	}

	public void CancelLibrarySwitch()
	{
		_saveSettingsOnClose = true;
	}

	public void CloseForLibrarySwitch()
	{
		Close();
	}

	private void OnManageLibraries(object sender, RoutedEventArgs e)
	{
		if (_vm.IsBusy || _vm.IsAnalyzing)
		{
			MessageBox.Show(this, "Stop the current library operation before switching libraries.", "Magpie Trove", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		LibraryManagerWindow libraryManagerWindow = new LibraryManagerWindow(AppSettingsService.Load())
		{
			Owner = this
		};
		if (libraryManagerWindow.ShowDialog() == true)
		{
			((App)Application.Current).ApplyLibrarySettings(libraryManagerWindow.ResultSettings);
			_vm.RefreshModelAvailability();
		}
	}

	private void OnTransfer(object sender, RoutedEventArgs e)
	{
		TransferWindow transferWindow = new TransferWindow(_vm);
		transferWindow.Owner = this;
		transferWindow.ShowDialog();
	}

	private void OnGallerySelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		_vm.UpdateSelection(Gallery.SelectedItems.Cast<ImageItem>().ToList());
	}

	private void OnGalleryDoubleClick(object sender, MouseButtonEventArgs e)
	{
		object originalSource = e.OriginalSource;
		if (FindAncestor<ListBoxItem>((DependencyObject?)((originalSource is DependencyObject) ? originalSource : null)) != null)
		{
			OpenViewer();
		}
	}

	private void OnGalleryKeyDown(object sender, KeyEventArgs e)
	{
		Key key = e.Key;
		if ((int)key >= 75)
		{
			if ((int)key <= 83 && (int)Keyboard.Modifiers == 0)
			{
				_vm.ApplyPinnedTag(e.Key - Key.NumPad0);
				e.Handled = true;
			}
			return;
		}
		if ((int)key <= 39)
		{
			if ((int)key < 34)
			{
				if ((int)key == 6)
				{
					OpenViewer();
					e.Handled = true;
				}
				return;
			}
			if ((int)Keyboard.Modifiers == 2)
			{
				_vm.SetRatingCommand.Execute((e.Key - Key.D0).ToString());
				e.Handled = true;
				return;
			}
			if ((int)key < 35)
			{
				return;
			}
		}
		else if ((int)key > 43)
		{
			if ((int)key != 59)
			{
				if ((int)key != 64)
				{
					if ((int)key == 67 && (int)Keyboard.Modifiers == 0)
					{
						_vm.SetFlag(-1);
						e.Handled = true;
					}
				}
				else if ((int)Keyboard.Modifiers == 0)
				{
					_vm.SetFlag(0);
					e.Handled = true;
				}
			}
			else if ((int)Keyboard.Modifiers == 0)
			{
				_vm.SetFlag(1);
				e.Handled = true;
			}
			return;
		}
		if ((int)Keyboard.Modifiers == 0)
		{
			_vm.ApplyPinnedTag(e.Key - Key.D0);
			e.Handled = true;
		}
	}

	private void AdvanceSelection()
	{
		if (Gallery.Items.Count == 0)
		{
			return;
		}
		int num = Gallery.SelectedIndex + 1;
		if (num < Gallery.Items.Count)
		{
			Gallery.SelectedIndex = num;
			Gallery.ScrollIntoView(Gallery.SelectedItem);
			if (Gallery.ItemContainerGenerator.ContainerFromIndex(num) is ListBoxItem listBoxItem)
			{
				listBoxItem.Focus();
			}
		}
	}

	private void OnOpenViewerMenu(object sender, RoutedEventArgs e)
	{
		OpenViewer();
	}

	private void OnFindDuplicates(object sender, RoutedEventArgs e)
	{
		_vm.ReviewDuplicates(delegate(DuplicateService service)
		{
			DuplicatesWindow duplicatesWindow = new DuplicatesWindow(service)
			{
				Owner = this
			};
			return (Confirmed: duplicatesWindow.ShowDialog() == true, Ids: duplicatesWindow.RemovedImageIds);
		});
	}

	private void OpenViewer()
	{
		if (_vm.Images.Count != 0)
		{
			int startIndex = ((Gallery.SelectedIndex >= 0) ? Gallery.SelectedIndex : 0);
			List<ImageItem> compareSelection = _vm.SelectedImages.ToList();
			ViewerWindow viewerWindow = new ViewerWindow(_vm, _vm.Images, startIndex, compareSelection)
			{
				Owner = this
			};
			viewerWindow.ShowDialog();
			_vm.ReloadTags();
			if (viewerWindow.CurrentIndex >= 0 && viewerWindow.CurrentIndex < Gallery.Items.Count)
			{
				Gallery.SelectedIndex = viewerWindow.CurrentIndex;
				Gallery.ScrollIntoView(Gallery.SelectedItem);
			}
		}
	}

	private void OnTagTreeClick(object sender, MouseButtonEventArgs e)
	{
		object originalSource = e.OriginalSource;
		if (FindAncestor<ToggleButton>((DependencyObject?)((originalSource is DependencyObject) ? originalSource : null)) == null)
		{
			object originalSource2 = e.OriginalSource;
			TreeViewItem treeViewItem = FindAncestor<TreeViewItem>((DependencyObject?)((originalSource2 is DependencyObject) ? originalSource2 : null));
			if (treeViewItem != null && treeViewItem.DataContext is TagItem tagPressed)
			{
				_tagPressed = tagPressed;
				_tagPressPosition = e.GetPosition(TagList);
				_tagDragging = false;
				e.Handled = true;
			}
		}
	}

	private void OnTagTreeMouseMove(object sender, MouseEventArgs e)
	{
		if (_tagPressed != null && e.LeftButton == MouseButtonState.Pressed && !_tagDragging)
		{
			Vector val = e.GetPosition(TagList) - _tagPressPosition;
			if (!(Math.Abs(val.X) < SystemParameters.MinimumHorizontalDragDistance) || !(Math.Abs(val.Y) < SystemParameters.MinimumVerticalDragDistance))
			{
				_tagDragging = true;
				DragDrop.DoDragDrop((DependencyObject)(object)TagList, new DataObject(typeof(TagItem), _tagPressed), DragDropEffects.Move);
				_tagPressed = null;
			}
		}
	}

	private void OnTagTreeMouseUp(object sender, MouseButtonEventArgs e)
	{
		TagItem tagPressed = _tagPressed;
		if (tagPressed != null && !_tagDragging)
		{
			_vm.ToggleTagFilterCommand.Execute(tagPressed);
		}
		_tagPressed = null;
		_tagDragging = false;
	}

	private void OnTagTreeDragOver(object sender, DragEventArgs e)
	{
		e.Effects = (e.Data.GetDataPresent(typeof(TagItem)) ? DragDropEffects.Move : DragDropEffects.None);
		e.Handled = true;
	}

	private void OnTagTreeDrop(object sender, DragEventArgs e)
	{
		if (e.Data.GetData(typeof(TagItem)) is TagItem tag)
		{
			object originalSource = e.OriginalSource;
			TagItem newParent = FindAncestor<TreeViewItem>((DependencyObject?)((originalSource is DependencyObject) ? originalSource : null))?.DataContext as TagItem;
			_vm.ReparentTag(tag, newParent);
			e.Handled = true;
		}
	}

	private void OnTagEntryKeyDown(object sender, KeyEventArgs e)
	{
		if ((int)e.Key == 6)
		{
			_vm.AddTagCommand.Execute(null);
			e.Handled = true;
		}
	}

	private void OnSuggestionClick(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Content: string content })
		{
			_vm.ApplyTagText(content);
			_vm.NewTagText = "";
			TagEntry.Focus();
		}
	}

	private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
	{
		while (node != null)
		{
			T val = (T)(object)((node is T) ? node : null);
			if (val != null)
			{
				return val;
			}
			bool flag = ((node is Visual || node is Visual3D) ? true : false);
			node = (flag ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node));
		}
		return default(T);
	}

}
