using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MagpieTrove.Controls;

public class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
	public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register("ItemWidth", typeof(double), typeof(VirtualizingWrapPanel), (PropertyMetadata)(object)new FrameworkPropertyMetadata((object)140.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

	public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register("ItemHeight", typeof(double), typeof(VirtualizingWrapPanel), (PropertyMetadata)(object)new FrameworkPropertyMetadata((object)160.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

	private Size _extent;

	private Size _viewport;

	private Point _offset;

	private int _itemsPerRow;

	public double ItemWidth
	{
		get
		{
			return (double)((DependencyObject)this).GetValue(ItemWidthProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(ItemWidthProperty, (object)value);
		}
	}

	public double ItemHeight
	{
		get
		{
			return (double)((DependencyObject)this).GetValue(ItemHeightProperty);
		}
		set
		{
			((DependencyObject)this).SetValue(ItemHeightProperty, (object)value);
		}
	}

	public int ItemsPerRow => _itemsPerRow;

	public bool CanVerticallyScroll { get; set; }

	public bool CanHorizontallyScroll { get; set; }

	public double ExtentWidth => _extent.Width;

	public double ExtentHeight => _extent.Height;

	public double ViewportWidth => _viewport.Width;

	public double ViewportHeight => _viewport.Height;

	public double HorizontalOffset => _offset.X;

	public double VerticalOffset => _offset.Y;

	public ScrollViewer? ScrollOwner { get; set; }

	private double LineSize => Math.Max(16.0, ItemHeight / 3.0);

	protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
	{
		switch (args.Action)
		{
		case NotifyCollectionChangedAction.Remove:
		case NotifyCollectionChangedAction.Replace:
			RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
			break;
		case NotifyCollectionChangedAction.Move:
			RemoveInternalChildRange(args.OldPosition.Index, args.ItemUICount);
			break;
		case NotifyCollectionChangedAction.Reset:
			RemoveInternalChildRange(0, base.InternalChildren.Count);
			SetVerticalOffset(0.0);
			break;
		}
		InvalidateMeasure();
	}

	protected override Size MeasureOverride(Size availableSize)
	{
		_ = base.InternalChildren;
		int num = ItemsControl.GetItemsOwner((DependencyObject)(object)this)?.Items.Count ?? 0;
		double num2 = Math.Max(1.0, ItemWidth);
		double num3 = Math.Max(1.0, ItemHeight);
		double num4 = (double.IsInfinity(availableSize.Width) ? num2 : availableSize.Width);
		_itemsPerRow = Math.Max(1, (int)Math.Floor(num4 / num2));
		int num5 = ((num != 0) ? ((int)Math.Ceiling((double)num / (double)_itemsPerRow)) : 0);
		UpdateScrollInfo(availableSize, new Size((double)_itemsPerRow * num2, (double)num5 * num3));
		if (num == 0)
		{
			CleanUpItems(0, -1);
			return FinalMeasureSize(availableSize);
		}
		int num6 = Math.Max(0, (int)Math.Floor(_offset.Y / num3));
		int num7 = (int)Math.Ceiling((_offset.Y + Math.Max(_viewport.Height, 1.0)) / num3) - 1;
		if (num7 < num6)
		{
			num7 = num6;
		}
		int first = Math.Max(0, num6 * _itemsPerRow);
		int last = Math.Min(num - 1, (num7 + 1) * _itemsPerRow - 1);
		Realize(first, last, new Size(num2, num3));
		CleanUpItems(first, last);
		return FinalMeasureSize(availableSize);
	}

	private Size FinalMeasureSize(Size availableSize)
	{
		return new Size(double.IsInfinity(availableSize.Width) ? _extent.Width : availableSize.Width, double.IsInfinity(availableSize.Height) ? _extent.Height : availableSize.Height);
	}

	private void Realize(int first, int last, Size tileSize)
	{
		IItemContainerGenerator itemContainerGenerator = base.ItemContainerGenerator;
		GeneratorPosition position = itemContainerGenerator.GeneratorPositionFromIndex(first);
		int num = ((position.Offset == 0) ? position.Index : (position.Index + 1));
		using (itemContainerGenerator.StartAt(position, GeneratorDirection.Forward, allowStartAtRealizedItem: true))
		{
			int num2 = first;
			bool isNewlyRealized;
			while (num2 <= last && itemContainerGenerator.GenerateNext(out isNewlyRealized) is UIElement uIElement)
			{
				if (isNewlyRealized || !base.InternalChildren.Contains(uIElement))
				{
					if (num >= base.InternalChildren.Count)
					{
						AddInternalChild(uIElement);
					}
					else
					{
						InsertInternalChild(num, uIElement);
					}
					itemContainerGenerator.PrepareItemContainer((DependencyObject)(object)uIElement);
				}
				uIElement.Measure(tileSize);
				num2++;
				num++;
			}
		}
	}

	private void CleanUpItems(int first, int last)
	{
		IItemContainerGenerator itemContainerGenerator = base.ItemContainerGenerator;
		ItemsControl itemsOwner = ItemsControl.GetItemsOwner((DependencyObject)(object)this);
		IRecyclingItemContainerGenerator recyclingItemContainerGenerator = ((itemsOwner != null && VirtualizingPanel.GetVirtualizationMode((DependencyObject)(object)itemsOwner) == VirtualizationMode.Recycling) ? (itemContainerGenerator as IRecyclingItemContainerGenerator) : null);
		for (int num = base.InternalChildren.Count - 1; num >= 0; num--)
		{
			GeneratorPosition position = new GeneratorPosition(num, 0);
			int num2 = itemContainerGenerator.IndexFromGeneratorPosition(position);
			if (num2 < first || num2 > last)
			{
				if (recyclingItemContainerGenerator != null)
				{
					recyclingItemContainerGenerator.Recycle(position, 1);
				}
				else
				{
					itemContainerGenerator.Remove(position, 1);
				}
				RemoveInternalChildRange(num, 1);
			}
		}
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		IItemContainerGenerator itemContainerGenerator = base.ItemContainerGenerator;
		double num = Math.Max(1.0, ItemWidth);
		double num2 = Math.Max(1.0, ItemHeight);
		for (int i = 0; i < base.InternalChildren.Count; i++)
		{
			UIElement uIElement = base.InternalChildren[i];
			int num3 = itemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
			if (num3 >= 0)
			{
				int num4 = num3 / _itemsPerRow;
				int num5 = num3 % _itemsPerRow;
				uIElement.Arrange(new Rect((double)num5 * num - _offset.X, (double)num4 * num2 - _offset.Y, num, num2));
			}
		}
		return finalSize;
	}

	protected override void BringIndexIntoView(int index)
	{
		if (index >= 0)
		{
			double num = Math.Max(1.0, ItemHeight);
			double num2 = (double)(index / _itemsPerRow) * num;
			if (num2 < _offset.Y)
			{
				SetVerticalOffset(num2);
			}
			else if (num2 + num > _offset.Y + _viewport.Height)
			{
				SetVerticalOffset(num2 + num - _viewport.Height);
			}
		}
	}

	private void UpdateScrollInfo(Size availableSize, Size extent)
	{
		Size val = new Size(double.IsInfinity(availableSize.Width) ? extent.Width : availableSize.Width, double.IsInfinity(availableSize.Height) ? extent.Height : availableSize.Height);
		bool flag = extent != _extent || val != _viewport;
		_extent = extent;
		_viewport = val;
		double num = Math.Max(0.0, _extent.Height - _viewport.Height);
		if (_offset.Y > num)
		{
			_offset.Y = num;
			flag = true;
		}
		if (flag)
		{
			ScrollOwner?.InvalidateScrollInfo();
		}
	}

	public void LineUp()
	{
		SetVerticalOffset(_offset.Y - LineSize);
	}

	public void LineDown()
	{
		SetVerticalOffset(_offset.Y + LineSize);
	}

	public void PageUp()
	{
		SetVerticalOffset(_offset.Y - _viewport.Height);
	}

	public void PageDown()
	{
		SetVerticalOffset(_offset.Y + _viewport.Height);
	}

	public void MouseWheelUp()
	{
		SetVerticalOffset(_offset.Y - ItemHeight);
	}

	public void MouseWheelDown()
	{
		SetVerticalOffset(_offset.Y + ItemHeight);
	}

	public void LineLeft()
	{
	}

	public void LineRight()
	{
	}

	public void PageLeft()
	{
	}

	public void PageRight()
	{
	}

	public void MouseWheelLeft()
	{
	}

	public void MouseWheelRight()
	{
	}

	public void SetHorizontalOffset(double offset)
	{
	}

	public void SetVerticalOffset(double offset)
	{
		double num = Math.Max(0.0, Math.Min(offset, Math.Max(0.0, _extent.Height - _viewport.Height)));
		if (!(Math.Abs(num - _offset.Y) < 0.5))
		{
			_offset.Y = num;
			ScrollOwner?.InvalidateScrollInfo();
			InvalidateMeasure();
		}
	}

	public Rect MakeVisible(Visual visual, Rect rectangle)
	{
		DependencyObject val = (DependencyObject)(object)visual;
		while (val != null && !base.InternalChildren.Contains(val as UIElement))
		{
			val = VisualTreeHelper.GetParent(val);
		}
		if (val is UIElement element)
		{
			int index = base.InternalChildren.IndexOf(element);
			int num = base.ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(index, 0));
			if (num >= 0)
			{
				BringIndexIntoView(num);
			}
		}
		return rectangle;
	}

	public VirtualizingWrapPanel()
	{
		_extent = new Size(0.0, 0.0);
		_viewport = new Size(0.0, 0.0);
		_itemsPerRow = 1;
		CanVerticallyScroll = true;
	}
}
