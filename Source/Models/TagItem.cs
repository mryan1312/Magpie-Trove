using System.Collections.Generic;
using System.Collections.ObjectModel;
using MagpieTrove.Common;

namespace MagpieTrove.Models;

public sealed class TagItem : ObservableObject
{
	private string _name = "";

	private int _count;

	private int _totalCount;

	private TagFilterState _state;

	private string _color = "#4FA3E3";

	private bool _isExpanded = true;

	private bool _isVisible = true;

	private int? _pinnedSlot;

	public long Id { get; init; }

	public long? ParentId { get; set; }

	public int? PinnedSlot
	{
		get
		{
			return _pinnedSlot;
		}
		set
		{
			if (Set(ref _pinnedSlot, value, "PinnedSlot"))
			{
				OnPropertyChanged("IsPinned");
				OnPropertyChanged("PinnedSlotDisplay");
			}
		}
	}

	public bool IsPinned
	{
		get
		{
			int? pinnedSlot = _pinnedSlot;
			return pinnedSlot.HasValue;
		}
	}

	public string PinnedSlotDisplay => _pinnedSlot?.ToString() ?? "";

	public TagItem? Parent { get; set; }

	public ObservableCollection<TagItem> Children { get; } = new ObservableCollection<TagItem>();

	public bool HasChildren => Children.Count > 0;

	public int TotalCount
	{
		get
		{
			return _totalCount;
		}
		set
		{
			if (Set(ref _totalCount, value, "TotalCount"))
			{
				OnPropertyChanged("DisplayCount");
			}
		}
	}

	public int DisplayCount
	{
		get
		{
			if (!HasChildren)
			{
				return _count;
			}
			return _totalCount;
		}
	}

	public bool IsExpanded
	{
		get
		{
			return _isExpanded;
		}
		set
		{
			Set(ref _isExpanded, value, "IsExpanded");
		}
	}

	public bool IsVisible
	{
		get
		{
			return _isVisible;
		}
		set
		{
			Set(ref _isVisible, value, "IsVisible");
		}
	}

	public string FullPath
	{
		get
		{
			Stack<string> stack = new Stack<string>();
			for (TagItem tagItem = this; tagItem != null; tagItem = tagItem.Parent)
			{
				stack.Push(tagItem.Name);
			}
			return string.Join("/", stack);
		}
	}

	public string Name
	{
		get
		{
			return _name;
		}
		set
		{
			Set(ref _name, value, "Name");
		}
	}

	public int Count
	{
		get
		{
			return _count;
		}
		set
		{
			if (Set(ref _count, value, "Count"))
			{
				OnPropertyChanged("DisplayCount");
			}
		}
	}

	public string Color
	{
		get
		{
			return _color;
		}
		set
		{
			Set(ref _color, value, "Color");
		}
	}

	public TagFilterState State
	{
		get
		{
			return _state;
		}
		set
		{
			if (Set(ref _state, value, "State"))
			{
				OnPropertyChanged("IsIncluded");
				OnPropertyChanged("IsExcluded");
			}
		}
	}

	public bool IsIncluded => _state == TagFilterState.Include;

	public bool IsExcluded => _state == TagFilterState.Exclude;

	public void OnChildrenChanged()
	{
		OnPropertyChanged("HasChildren");
		OnPropertyChanged("DisplayCount");
	}

	public IEnumerable<TagItem> SelfAndDescendants()
	{
		yield return this;
		foreach (TagItem child in Children)
		{
			foreach (TagItem item in child.SelfAndDescendants())
			{
				yield return item;
			}
		}
	}

	public void CycleState()
	{
		State = _state switch
		{
			TagFilterState.Neutral => TagFilterState.Include, 
			TagFilterState.Include => TagFilterState.Exclude, 
			_ => TagFilterState.Neutral, 
		};
	}
}
