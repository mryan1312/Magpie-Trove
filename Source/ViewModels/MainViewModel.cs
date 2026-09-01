using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using MagpieTrove.Common;
using MagpieTrove.Data;
using MagpieTrove.Models;
using MagpieTrove.Services;
using MagpieTrove.Views;

namespace MagpieTrove.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
	private readonly ThumbnailService _thumbnails;

	private readonly DispatcherTimer _searchDebounce;

	private readonly Dispatcher _dispatcher;

	private int _refreshToken;

	private bool _suspendRefresh;

	private CancellationTokenSource? _scanCts;

	private CancellationTokenSource? _embedCts;

	private readonly VectorIndex _index;

	private readonly SuggestionService _suggestions;

	private readonly EmbeddingPipeline _embeddings;

	private readonly UndoService _undo;

	private readonly DuplicateService _duplicateFinder;

	private readonly LibraryWatchService _watcher;

	private readonly Dictionary<string, LibraryFileChange> _deferredWatcherChanges;

	private bool _deferredFullRescan;

	private bool _startupReconciliationStarted;

	private bool _startupReconciliationRequested;

	private long? _similarTo;

	private string _similarToName;

	private IReadOnlyList<ImageItem> _images;

	private IReadOnlyList<ImageItem> _selectedImages;

	private ImageItem? _currentImage;

	private CollectionItem? _selectedCollection;

	private FolderItem? _selectedFolder;

	private string _searchText;

	private string _tagSearchText;

	private string _newTagText;

	private string _statusText;

	private string _progressText;

	private double _progressValue;

	private bool _isBusy;

	private bool _matchAll;

	private bool _untaggedOnly;

	private bool _importEmbeddedKeywords;

	private int _minRating;

	private SortField _sortBy;

	private bool _sortDescending;

	private double _tileSize;

	private DateTime? _dateFrom;

	private DateTime? _dateTo;

	private string? _cameraMake;

	private string? _cameraModel;

	private string? _lens;

	private int? _isoMin;

	private int? _isoMax;

	private double? _apertureMin;

	private double? _apertureMax;

	private double? _shutterMin;

	private double? _shutterMax;

	private double? _focalMin;

	private double? _focalMax;

	private bool _isAnalyzing;

	private string _analyzeStatus;

	private double _analyzeProgress;

	private string _coverageText;

	private readonly List<string> _recent;

	private List<string> _tagClipboard;

	private bool _autoAdvance;

	private bool _implyAncestors;

	private FlagFilter _flagFilter;

	public ObservableCollection<TagItem> Tags { get; }

	public ObservableCollection<TagItem> VisibleTags { get; }

	public ObservableCollection<CollectionItem> Collections { get; }

	public ObservableCollection<FolderItem> Folders { get; }

	public ObservableCollection<TagChip> SelectionTags { get; }

	public ObservableCollection<string> TagSuggestions { get; }

	public ObservableCollection<string> CameraMakes { get; }

	public ObservableCollection<string> CameraModels { get; }

	public ObservableCollection<string> Lenses { get; }

	public IReadOnlyList<ImageItem> Images
	{
		get
		{
			return _images;
		}
		private set
		{
			_images = value;
			OnPropertyChanged("Images");
			OnPropertyChanged("ResultSummary");
		}
	}

	public IReadOnlyList<ImageItem> SelectedImages
	{
		get
		{
			return _selectedImages;
		}
		private set
		{
			_selectedImages = value;
			OnPropertyChanged("SelectedImages");
			OnPropertyChanged("HasSelection");
			OnPropertyChanged("SelectionSummary");
		}
	}

	public bool HasSelection => _selectedImages.Count > 0;

	public ImageItem? CurrentImage
	{
		get
		{
			return _currentImage;
		}
		private set
		{
			Set(ref _currentImage, value, "CurrentImage");
		}
	}

	public string SearchText
	{
		get
		{
			return _searchText;
		}
		set
		{
			if (Set(ref _searchText, value, "SearchText"))
			{
				_searchDebounce.Stop();
				_searchDebounce.Start();
			}
		}
	}

	public string TagSearchText
	{
		get
		{
			return _tagSearchText;
		}
		set
		{
			if (Set(ref _tagSearchText, value, "TagSearchText"))
			{
				ApplyTagSearch();
			}
		}
	}

	public string NewTagText
	{
		get
		{
			return _newTagText;
		}
		set
		{
			if (Set(ref _newTagText, value, "NewTagText"))
			{
				UpdateTagSuggestions();
			}
		}
	}

	public bool MatchAll
	{
		get
		{
			return _matchAll;
		}
		set
		{
			if (Set(ref _matchAll, value, "MatchAll"))
			{
				Refresh();
			}
		}
	}

	public bool UntaggedOnly
	{
		get
		{
			return _untaggedOnly;
		}
		set
		{
			if (Set(ref _untaggedOnly, value, "UntaggedOnly"))
			{
				Refresh();
			}
		}
	}

	public int MinRating
	{
		get
		{
			return _minRating;
		}
		set
		{
			if (Set(ref _minRating, value, "MinRating"))
			{
				Refresh();
			}
		}
	}

	public SortField SortBy
	{
		get
		{
			return _sortBy;
		}
		set
		{
			if (Set(ref _sortBy, value, "SortBy"))
			{
				Refresh();
			}
		}
	}

	public bool SortDescending
	{
		get
		{
			return _sortDescending;
		}
		set
		{
			if (Set(ref _sortDescending, value, "SortDescending"))
			{
				Refresh();
			}
		}
	}

	public CollectionItem? SelectedCollection
	{
		get
		{
			return _selectedCollection;
		}
		set
		{
			if (Set(ref _selectedCollection, value, "SelectedCollection"))
			{
				if (value != null && value.IsSmart && value.Rule != null)
				{
					ApplySavedRule(value.Rule);
				}
				else
				{
					Refresh();
				}
			}
		}
	}

	public FolderItem? SelectedFolder
	{
		get
		{
			return _selectedFolder;
		}
		set
		{
			if (Set(ref _selectedFolder, value, "SelectedFolder"))
			{
				Refresh();
			}
		}
	}

	public double TileSize
	{
		get
		{
			return _tileSize;
		}
		set
		{
			if (Set(ref _tileSize, value, "TileSize"))
			{
				OnPropertyChanged("TileWidth");
				OnPropertyChanged("TileHeight");
			}
		}
	}

	public double TileWidth => Math.Round(_tileSize) + 12.0;

	public double TileHeight => Math.Round(_tileSize) + 34.0;

	public DateTime? DateFrom
	{
		get
		{
			return _dateFrom;
		}
		set
		{
			if (Set(ref _dateFrom, value, "DateFrom"))
			{
				Refresh();
			}
		}
	}

	public DateTime? DateTo
	{
		get
		{
			return _dateTo;
		}
		set
		{
			if (Set(ref _dateTo, value, "DateTo"))
			{
				Refresh();
			}
		}
	}

	public bool ImportEmbeddedKeywords
	{
		get
		{
			return _importEmbeddedKeywords;
		}
		set
		{
			if (Set(ref _importEmbeddedKeywords, value, "ImportEmbeddedKeywords") && !_suspendRefresh)
			{
				Database.SetMeta("import_embedded_keywords", value ? "1" : "0");
				StatusText = (value ? "Embedded keyword import enabled. Rescan to backfill existing images." : "Embedded keyword import disabled.");
			}
		}
	}

	public string? CameraMake
	{
		get
		{
			return _cameraMake;
		}
		set
		{
			if (Set(ref _cameraMake, value, "CameraMake"))
			{
				Refresh();
			}
		}
	}

	public string? CameraModel
	{
		get
		{
			return _cameraModel;
		}
		set
		{
			if (Set(ref _cameraModel, value, "CameraModel"))
			{
				Refresh();
			}
		}
	}

	public string? Lens
	{
		get
		{
			return _lens;
		}
		set
		{
			if (Set(ref _lens, value, "Lens"))
			{
				Refresh();
			}
		}
	}

	public int? IsoMin
	{
		get
		{
			return _isoMin;
		}
		set
		{
			if (Set(ref _isoMin, value, "IsoMin"))
			{
				Refresh();
			}
		}
	}

	public int? IsoMax
	{
		get
		{
			return _isoMax;
		}
		set
		{
			if (Set(ref _isoMax, value, "IsoMax"))
			{
				Refresh();
			}
		}
	}

	public double? ApertureMin
	{
		get
		{
			return _apertureMin;
		}
		set
		{
			if (Set(ref _apertureMin, value, "ApertureMin"))
			{
				Refresh();
			}
		}
	}

	public double? ApertureMax
	{
		get
		{
			return _apertureMax;
		}
		set
		{
			if (Set(ref _apertureMax, value, "ApertureMax"))
			{
				Refresh();
			}
		}
	}

	public double? ShutterSpeedMin
	{
		get
		{
			return _shutterMin;
		}
		set
		{
			if (Set(ref _shutterMin, value, "ShutterSpeedMin"))
			{
				Refresh();
			}
		}
	}

	public double? ShutterSpeedMax
	{
		get
		{
			return _shutterMax;
		}
		set
		{
			if (Set(ref _shutterMax, value, "ShutterSpeedMax"))
			{
				Refresh();
			}
		}
	}

	public double? FocalLengthMin
	{
		get
		{
			return _focalMin;
		}
		set
		{
			if (Set(ref _focalMin, value, "FocalLengthMin"))
			{
				Refresh();
			}
		}
	}

	public double? FocalLengthMax
	{
		get
		{
			return _focalMax;
		}
		set
		{
			if (Set(ref _focalMax, value, "FocalLengthMax"))
			{
				Refresh();
			}
		}
	}

	public IReadOnlyList<SortOption> SortOptions { get; }

	public string StatusText
	{
		get
		{
			return _statusText;
		}
		private set
		{
			Set(ref _statusText, value, "StatusText");
		}
	}

	public string ProgressText
	{
		get
		{
			return _progressText;
		}
		private set
		{
			Set(ref _progressText, value, "ProgressText");
		}
	}

	public double ProgressValue
	{
		get
		{
			return _progressValue;
		}
		private set
		{
			Set(ref _progressValue, value, "ProgressValue");
		}
	}

	public bool IsBusy
	{
		get
		{
			return _isBusy;
		}
		private set
		{
			Set(ref _isBusy, value, "IsBusy");
		}
	}

	public string ResultSummary
	{
		get
		{
			if (_images.Count != 1)
			{
				return $"{_images.Count:N0} images";
			}
			return "1 image";
		}
	}

	public string SelectionSummary
	{
		get
		{
			int count = _selectedImages.Count;
			return count switch
			{
				0 => "No selection", 
				1 => _selectedImages[0].FileName, 
				_ => $"{count:N0} images selected", 
			};
		}
	}

	public string ActiveFilterSummary
	{
		get
		{
			List<string> list = new List<string>();
			List<string> list2 = (from t in Tags
				where t.IsIncluded
				select t.Name).ToList();
			List<string> list3 = (from t in Tags
				where t.IsExcluded
				select t.Name).ToList();
			if (list2.Count > 0)
			{
				list.Add(string.Join(MatchAll ? " AND " : " OR ", list2));
			}
			if (list3.Count > 0)
			{
				list.Add("NOT " + string.Join(", ", list3));
			}
			if (UntaggedOnly)
			{
				list.Add("untagged");
			}
			if (MinRating > 0)
			{
				list.Add($"{MinRating}+ stars");
			}
			if (!string.IsNullOrWhiteSpace(SearchText))
			{
				list.Add("\"" + SearchText.Trim() + "\"");
			}
			if (SelectedFolder != null)
			{
				list.Add(SelectedFolder.DisplayName);
			}
			if (SelectedCollection != null)
			{
				list.Add(SelectedCollection.Name);
			}
			if (DateFrom.HasValue || DateTo.HasValue)
			{
				list.Add((DateFrom?.ToString("d") ?? "…") + "–" + (DateTo?.ToString("d") ?? "…"));
			}
			if (CameraMake != null)
			{
				list.Add(CameraMake);
			}
			if (CameraModel != null)
			{
				list.Add(CameraModel);
			}
			if (Lens != null)
			{
				list.Add(Lens);
			}
			if (list.Count != 0)
			{
				return string.Join("  •  ", list);
			}
			return "All images";
		}
	}

	public RelayCommand AddFolderCommand { get; }

	public RelayCommand RescanCommand { get; }

	public RelayCommand CancelScanCommand { get; }

	public RelayCommand RemoveFolderCommand { get; }

	public RelayCommand ClearFilterCommand { get; }

	public RelayCommand ToggleTagFilterCommand { get; }

	public RelayCommand AddTagCommand { get; }

	public RelayCommand RemoveTagCommand { get; }

	public RelayCommand RenameTagCommand { get; }

	public RelayCommand DeleteTagCommand { get; }

	public RelayCommand SetRatingCommand { get; }

	public RelayCommand NewCollectionCommand { get; }

	public RelayCommand NewSmartCollectionCommand { get; }

	public RelayCommand AddToCollectionCommand { get; }

	public RelayCommand RemoveFromCollectionCommand { get; }

	public RelayCommand RenameCollectionCommand { get; }

	public RelayCommand DeleteCollectionCommand { get; }

	public RelayCommand RevealInExplorerCommand { get; }

	public RelayCommand CopyPathCommand { get; }

	public RelayCommand RemoveFromLibraryCommand { get; }

	public RelayCommand ClearThumbnailCacheCommand { get; }

	public RelayCommand ToggleSortDirectionCommand { get; }

	public RelayCommand ClearFolderFilterCommand { get; }

	public RelayCommand FocusSearchCommand { get; }

	public RelayCommand TagSelectionWithCommand { get; }

	public RelayCommand AnalyzeImagesCommand { get; }

	public RelayCommand CancelAnalyzeCommand { get; }

	public RelayCommand FindSimilarCommand { get; }

	public RelayCommand ClearSimilarCommand { get; }

	public RelayCommand ApplySuggestionCommand { get; }

	public RelayCommand TrainProbeCommand { get; }

	public RelayCommand ShowProbeCandidatesCommand { get; }

	public RelayCommand AddChildTagCommand { get; }

	public RelayCommand PromoteTagCommand { get; }

	public RelayCommand UndoCommand { get; }

	public RelayCommand RedoCommand { get; }

	public RelayCommand PinTagCommand { get; }

	public RelayCommand ManageAliasesCommand { get; }

	public RelayCommand ChangeTagColorCommand { get; }

	public RelayCommand CopyTagsCommand { get; }

	public RelayCommand PasteTagsCommand { get; }

	public RelayCommand PickCommand { get; }

	public RelayCommand RejectCommand { get; }

	public RelayCommand UnflagCommand { get; }

	public RelayCommand ApplyRecentTagCommand { get; }

	public bool CanUndo => _undo.CanUndo;

	public bool CanRedo => _undo.CanRedo;

	public string UndoTooltip
	{
		get
		{
			if (!_undo.CanUndo)
			{
				return "Nothing to undo";
			}
			return "Undo " + _undo.UndoDescription + " (Ctrl+Z)";
		}
	}

	public string RedoTooltip
	{
		get
		{
			if (!_undo.CanRedo)
			{
				return "Nothing to redo";
			}
			return "Redo " + _undo.RedoDescription + " (Ctrl+Y)";
		}
	}

	public ObservableCollection<TagItem> TagTree { get; }

	public ObservableCollection<TagSuggestion> SuggestedTags { get; }

	public bool IsAnalyzing
	{
		get
		{
			return _isAnalyzing;
		}
		private set
		{
			if (Set(ref _isAnalyzing, value, "IsAnalyzing"))
			{
				OnPropertyChanged("CanAnalyze");
			}
		}
	}

	public string AnalyzeStatus
	{
		get
		{
			return _analyzeStatus;
		}
		private set
		{
			Set(ref _analyzeStatus, value, "AnalyzeStatus");
		}
	}

	public double AnalyzeProgress
	{
		get
		{
			return _analyzeProgress;
		}
		private set
		{
			Set(ref _analyzeProgress, value, "AnalyzeProgress");
		}
	}

	public bool VisualFeaturesAvailable => ClipEmbedder.IsModelAvailable;

	public bool CanAnalyze
	{
		get
		{
			if (VisualFeaturesAvailable)
			{
				return !IsAnalyzing;
			}
			return false;
		}
	}

	public bool HasEmbeddings => _index.Count > 0;

	public bool IsSimilarMode
	{
		get
		{
			long? similarTo = _similarTo;
			return similarTo.HasValue;
		}
	}

	public string SimilarModeText
	{
		get
		{
			long? similarTo = _similarTo;
			if (similarTo.HasValue)
			{
				return "Visually similar to " + _similarToName;
			}
			return "";
		}
	}

	public string CoverageText
	{
		get
		{
			return _coverageText;
		}
		private set
		{
			Set(ref _coverageText, value, "CoverageText");
		}
	}

	public string LibraryStatsText
	{
		get
		{
			int indexed = ImageRepository.CountAll();
			int missing = ImageRepository.CountMissing();
			string cacheSize = ImageItem.FormatSize(_thumbnails.GetCacheSizeBytes());
			// Read rather than cache: the current library can be renamed from the
			// library manager without the view model being rebuilt.
			string library = AppSettingsService.Load().CurrentLibrary.Name;
			return $"{library}  •  {indexed:N0} indexed  •  {Tags.Count:N0} tags  •  {missing:N0} missing  •  cache {cacheSize}";
		}
	}

	/// <summary>The library's folder on disk, shown as the status bar's tooltip.</summary>
	public string LibraryDirectory => Database.DataDirectory;

	public ObservableCollection<TagItem> PinnedTags { get; }

	public ObservableCollection<string> RecentTags { get; }

	public bool ImplyAncestors
	{
		get
		{
			return _implyAncestors;
		}
		set
		{
			Set(ref _implyAncestors, value, "ImplyAncestors");
		}
	}

	public bool AutoAdvance
	{
		get
		{
			return _autoAdvance;
		}
		set
		{
			Set(ref _autoAdvance, value, "AutoAdvance");
		}
	}

	public FlagFilter FlagFilterMode
	{
		get
		{
			return _flagFilter;
		}
		set
		{
			if (Set(ref _flagFilter, value, "FlagFilterMode"))
			{
				Refresh();
			}
		}
	}

	public IReadOnlyList<FlagOption> FlagOptions { get; }

	public string TagClipboardSummary
	{
		get
		{
			if (_tagClipboard.Count != 0)
			{
				return "Copied: " + string.Join(", ", _tagClipboard);
			}
			return "Nothing copied";
		}
	}

	public event Action? FocusSearchRequested;

	public event Action? AdvanceRequested;

	public MainViewModel(ThumbnailService thumbnails)
	{
		_undo = new UndoService();
		_deferredWatcherChanges = new Dictionary<string, LibraryFileChange>(StringComparer.OrdinalIgnoreCase);
		_similarToName = "";
		_images = Array.Empty<ImageItem>();
		_selectedImages = Array.Empty<ImageItem>();
		_searchText = "";
		_tagSearchText = "";
		_newTagText = "";
		_statusText = "Ready";
		_progressText = "";
		_matchAll = true;
		_sortBy = SortField.DateTaken;
		_sortDescending = true;
		_tileSize = 160.0;
		Tags = new ObservableCollection<TagItem>();
		VisibleTags = new ObservableCollection<TagItem>();
		Collections = new ObservableCollection<CollectionItem>();
		Folders = new ObservableCollection<FolderItem>();
		SelectionTags = new ObservableCollection<TagChip>();
		TagSuggestions = new ObservableCollection<string>();
		CameraMakes = new ObservableCollection<string>();
		CameraModels = new ObservableCollection<string>();
		Lenses = new ObservableCollection<string>();
		SortOptions =
		[
			new SortOption(SortField.DateTaken, "Date taken"),
			new SortOption(SortField.DateAdded, "Date added"),
			new SortOption(SortField.DateModified, "Date modified"),
			new SortOption(SortField.FileName, "File name"),
			new SortOption(SortField.Folder, "Folder"),
			new SortOption(SortField.FileSize, "File size"),
			new SortOption(SortField.Rating, "Rating"),
			new SortOption(SortField.Random, "Random"),
			new SortOption(SortField.CameraMake, "Camera make"),
			new SortOption(SortField.CameraModel, "Camera model"),
			new SortOption(SortField.Lens, "Lens"),
			new SortOption(SortField.Iso, "ISO"),
			new SortOption(SortField.Aperture, "Aperture"),
			new SortOption(SortField.ShutterSpeed, "Shutter speed"),
			new SortOption(SortField.FocalLength, "Focal length")
		];
		TagTree = new ObservableCollection<TagItem>();
		SuggestedTags = new ObservableCollection<TagSuggestion>();
		_analyzeStatus = "";
		_coverageText = "";
		PinnedTags = new ObservableCollection<TagItem>();
		RecentTags = new ObservableCollection<string>();
		_recent = new List<string>();
		_tagClipboard = new List<string>();
		_autoAdvance = true;
		FlagOptions =
		[
			new FlagOption(FlagFilter.All, "All"),
			new FlagOption(FlagFilter.HideRejected, "Hide rejected"),
			new FlagOption(FlagFilter.Picked, "Picks only"),
			new FlagOption(FlagFilter.Rejected, "Rejects only"),
			new FlagOption(FlagFilter.Unflagged, "Unflagged")
		];
		_thumbnails = thumbnails;
		_dispatcher = Dispatcher.CurrentDispatcher;
		_index = new VectorIndex("clip-vit-b32-vision");
		_suggestions = new SuggestionService(_index);
		_embeddings = new EmbeddingPipeline(thumbnails);
		_duplicateFinder = new DuplicateService(thumbnails);
		_watcher = new LibraryWatchService((IReadOnlyList<LibraryFileChange> changes, bool fullRescan) => _dispatcher.InvokeAsync<Task>((Func<Task>)(() => ApplyWatchedChangesAsync(changes, fullRescan))).Task.Unwrap());
		_undo.Changed += delegate
		{
			OnPropertyChanged("CanUndo");
			OnPropertyChanged("CanRedo");
			OnPropertyChanged("UndoTooltip");
			OnPropertyChanged("RedoTooltip");
		};
		_searchDebounce = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(250L, 0L)
		};
		_searchDebounce.Tick += delegate
		{
			_searchDebounce.Stop();
			Refresh();
		};
		AddFolderCommand = new RelayCommand(AddFolder);
		RescanCommand = new RelayCommand(async delegate
		{
			await RescanAsync();
		}, () => !IsBusy);
		CancelScanCommand = new RelayCommand(CancelScan, () => IsBusy);
		RemoveFolderCommand = new RelayCommand(delegate(object? p)
		{
			RemoveFolder(p as FolderItem);
		});
		ClearFilterCommand = new RelayCommand(ClearFilter);
		ToggleTagFilterCommand = new RelayCommand(delegate(object? p)
		{
			ToggleTagFilter(p as TagItem);
		});
		AddTagCommand = new RelayCommand(AddTagToSelection);
		RemoveTagCommand = new RelayCommand(delegate(object? p)
		{
			RemoveTagFromSelection(p as TagChip);
		});
		RenameTagCommand = new RelayCommand(delegate(object? p)
		{
			RenameTag(p as TagItem);
		});
		DeleteTagCommand = new RelayCommand(delegate(object? p)
		{
			DeleteTag(p as TagItem);
		});
		SetRatingCommand = new RelayCommand(delegate(object? p)
		{
			SetRating(p);
		});
		NewCollectionCommand = new RelayCommand(NewCollectionFromSelection);
		NewSmartCollectionCommand = new RelayCommand(NewSmartCollectionFromFilter);
		AddToCollectionCommand = new RelayCommand(delegate(object? p)
		{
			AddSelectionToCollection(p as CollectionItem);
		});
		RemoveFromCollectionCommand = new RelayCommand(RemoveSelectionFromCollection);
		RenameCollectionCommand = new RelayCommand(delegate(object? p)
		{
			RenameCollection(p as CollectionItem);
		});
		DeleteCollectionCommand = new RelayCommand(delegate(object? p)
		{
			DeleteCollection(p as CollectionItem);
		});
		RevealInExplorerCommand = new RelayCommand(RevealInExplorer);
		CopyPathCommand = new RelayCommand(CopyPath);
		RemoveFromLibraryCommand = new RelayCommand(RemoveSelectionFromLibrary);
		ClearThumbnailCacheCommand = new RelayCommand(ClearThumbnailCache);
		ToggleSortDirectionCommand = new RelayCommand((Action)delegate
		{
			SortDescending = !SortDescending;
		}, (Func<bool>?)null);
		ClearFolderFilterCommand = new RelayCommand((Action)delegate
		{
			SelectedFolder = null;
		}, (Func<bool>?)null);
		FocusSearchCommand = new RelayCommand((Action)delegate
		{
			FocusSearchRequested?.Invoke();
		}, (Func<bool>?)null);
		TagSelectionWithCommand = new RelayCommand(delegate(object? p)
		{
			if (p is TagItem tagItem)
			{
				ApplyTagText(tagItem.Name);
			}
		});
		AnalyzeImagesCommand = new RelayCommand(async delegate
		{
			await AnalyzeAsync();
		}, () => !IsAnalyzing);
		CancelAnalyzeCommand = new RelayCommand((Action)delegate
		{
			_embedCts?.Cancel();
		}, (Func<bool>?)null);
		FindSimilarCommand = new RelayCommand(FindSimilarToSelection);
		ClearSimilarCommand = new RelayCommand((Action)delegate
		{
			SetSimilarTo(null);
		}, (Func<bool>?)null);
		ApplySuggestionCommand = new RelayCommand(delegate(object? p)
		{
			if (p is TagSuggestion tagSuggestion)
			{
				ApplyTagText(tagSuggestion.Name);
			}
		});
		TrainProbeCommand = new RelayCommand(delegate(object? p)
		{
			TrainProbe(p as TagItem);
		});
		AddChildTagCommand = new RelayCommand(delegate(object? p)
		{
			AddChildTag(p as TagItem);
		});
		UndoCommand = new RelayCommand(PerformUndo, () => _undo.CanUndo);
		RedoCommand = new RelayCommand(PerformRedo, () => _undo.CanRedo);
		PinTagCommand = new RelayCommand(delegate(object? p)
		{
			PinTag(p as TagItem);
		});
		ManageAliasesCommand = new RelayCommand(delegate(object? p)
		{
			ManageAliases(p as TagItem);
		});
		ChangeTagColorCommand = new RelayCommand(delegate(object? p)
		{
			ChangeTagColor(p as TagItem);
		});
		CopyTagsCommand = new RelayCommand(CopyTags);
		PasteTagsCommand = new RelayCommand(PasteTags);
		PickCommand = new RelayCommand((Action)delegate
		{
			SetFlag(1);
		}, (Func<bool>?)null);
		RejectCommand = new RelayCommand((Action)delegate
		{
			SetFlag(-1);
		}, (Func<bool>?)null);
		UnflagCommand = new RelayCommand((Action)delegate
		{
			SetFlag(0);
		}, (Func<bool>?)null);
		ApplyRecentTagCommand = new RelayCommand(delegate(object? p)
		{
			if (p is string text)
			{
				ApplyTagText(text);
			}
		});
		PromoteTagCommand = new RelayCommand(delegate(object? p)
		{
			if (p is TagItem tag)
			{
				ReparentTag(tag, null);
			}
		});
		ShowProbeCandidatesCommand = new RelayCommand(delegate(object? p)
		{
			ShowProbeCandidates(p as TagItem);
		});
	}

	private void PerformUndo()
	{
		string text = _undo.Undo();
		if (text != null)
		{
			AfterHistoryChange("Undid " + text + ".");
		}
	}

	private void PerformRedo()
	{
		string text = _undo.Redo();
		if (text != null)
		{
			AfterHistoryChange("Redid " + text + ".");
		}
	}

	private void AfterHistoryChange(string message)
	{
		ReloadTags();
		ReloadCollections();
		RefreshSelectionTags();
		Refresh();
		StatusText = message;
	}

	public void Load()
	{
		RestoreSettings();
		RestoreRecent();
		ReloadTags();
		ReloadPinned();
		ReloadCollections();
		ReloadFolders();
		ReloadExifOptions();
		RestartWatchers();
		_index.Reload();
		RefreshCoverage();
		Refresh();
		if (Folders.Count == 0)
		{
			StatusText = "No folders yet — use Add Folder to index some images.";
		}
	}

	public async Task ReconcileAtStartupAsync()
	{
		if (!_startupReconciliationStarted && Folders.Count != 0)
		{
			_startupReconciliationRequested = true;
			if (ShouldStartStartupReconciliation(_startupReconciliationStarted, IsBusy, Folders.Count))
			{
				_startupReconciliationStarted = true;
				_startupReconciliationRequested = false;
				StatusText = "Checking watched folders for changes…";
				await RescanAsync(isStartupReconciliation: true);
			}
		}
	}

	internal static bool ShouldStartStartupReconciliation(bool alreadyStarted, bool isBusy, int folderCount)
	{
		if (!alreadyStarted && !isBusy)
		{
			return folderCount > 0;
		}
		return false;
	}

	public void SaveSettings()
	{
		Database.SetMeta("tile_size", ((int)TileSize).ToString());
		Database.SetMeta("sort_by", SortBy.ToString());
		Database.SetMeta("sort_desc", SortDescending ? "1" : "0");
		Database.SetMeta("match_all", MatchAll ? "1" : "0");
		Database.SetMeta("auto_advance", AutoAdvance ? "1" : "0");
		Database.SetMeta("import_embedded_keywords", ImportEmbeddedKeywords ? "1" : "0");
	}

	public void Dispose()
	{
		_watcher.Dispose();
	}

	private void RestoreSettings()
	{
		_suspendRefresh = true;
		try
		{
			if (int.TryParse(Database.GetMeta("tile_size"), out var result))
			{
				TileSize = Math.Clamp(result, 80, 320);
			}
			if (Enum.TryParse<SortField>(Database.GetMeta("sort_by"), out var result2))
			{
				SortBy = result2;
			}
			SortDescending = Database.GetMeta("sort_desc") != "0";
			MatchAll = Database.GetMeta("match_all") != "0";
			AutoAdvance = Database.GetMeta("auto_advance") != "0";
			ImportEmbeddedKeywords = Database.GetMeta("import_embedded_keywords") == "1";
		}
		finally
		{
			_suspendRefresh = false;
		}
	}

	public FilterQuery BuildFilter()
	{
		FilterQuery obj = new FilterQuery
		{
			IncludeTagIds = (from t in Tags
				where t.IsIncluded
				select t.Id).ToList(),
			ExcludeTagIds = (from t in Tags
				where t.IsExcluded
				select t.Id).ToList(),
			MatchAll = MatchAll,
			UntaggedOnly = UntaggedOnly,
			Search = (string.IsNullOrWhiteSpace(SearchText) ? null : SearchText),
			FolderPrefix = SelectedFolder?.Path,
			MinRating = MinRating,
			Flags = FlagFilterMode,
			DateFrom = DateFrom,
			DateTo = DateTo,
			CameraMake = CameraMake,
			CameraModel = CameraModel,
			Lens = Lens,
			IsoMin = IsoMin,
			IsoMax = IsoMax,
			ApertureMin = ApertureMin,
			ApertureMax = ApertureMax,
			ShutterSpeedMin = ShutterSpeedMin,
			ShutterSpeedMax = ShutterSpeedMax,
			FocalLengthMin = FocalLengthMin,
			FocalLengthMax = FocalLengthMax
		};
		CollectionItem selectedCollection = SelectedCollection;
		obj.CollectionId = ((selectedCollection != null && !selectedCollection.IsSmart) ? new long?(selectedCollection.Id) : ((long?)null));
		obj.SortBy = SortBy;
		obj.SortDescending = SortDescending;
		return obj;
	}

	public async void Refresh()
	{
		if (_suspendRefresh)
		{
			return;
		}
		int token = ++_refreshToken;
		FilterQuery filter = BuildFilter();
		OnPropertyChanged("ActiveFilterSummary");
		_thumbnails.ClearQueue();
		List<ImageItem> results;
		try
		{
			results = await Task.Run(() => ImageRepository.Query(filter)).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Exception ex3 = ex2;
			_dispatcher.Invoke<string>((Func<string>)(() => StatusText = "Query failed: " + ex3.Message));
			return;
		}
		List<ImageItem> ranked = ApplyVisualRanking(results);
		_dispatcher.Invoke((Action)delegate
		{
			if (token == _refreshToken)
			{
				Images = ranked;
				UpdateSelection(Array.Empty<ImageItem>());
				StatusText = ((ranked.Count == 0) ? "No images match the current filter." : (IsSimilarMode ? SimilarModeText : ActiveFilterSummary));
			}
		});
	}

	public void ReloadTags()
	{
		Dictionary<long, TagFilterState> dictionary = Tags.ToDictionary((TagItem t) => t.Id, (TagItem t) => t.State);
		HashSet<long> hashSet = (from t in Tags
			where t.IsExpanded
			select t.Id).ToHashSet();
		List<TagItem> allWithCounts = TagRepository.GetAllWithCounts();
		Dictionary<long, TagItem> dictionary2 = allWithCounts.ToDictionary((TagItem t) => t.Id);
		foreach (TagItem item in allWithCounts)
		{
			if (dictionary.TryGetValue(item.Id, out var value))
			{
				item.State = value;
			}
			item.IsExpanded = hashSet.Count == 0 || hashSet.Contains(item.Id);
		}
		TagTree.Clear();
		foreach (TagItem item2 in allWithCounts)
		{
			long? parentId = item2.ParentId;
			if (parentId.HasValue)
			{
				long valueOrDefault = parentId.GetValueOrDefault();
				if (dictionary2.TryGetValue(valueOrDefault, out var value2))
				{
					item2.Parent = value2;
					value2.Children.Add(item2);
					continue;
				}
			}
			item2.Parent = null;
			TagTree.Add(item2);
		}
		foreach (TagItem item3 in allWithCounts)
		{
			item3.OnChildrenChanged();
		}
		Tags.Clear();
		foreach (TagItem item4 in allWithCounts)
		{
			Tags.Add(item4);
		}
		ApplyTagSearch();
		UpdateTagSuggestions();
		ReloadPinned();
		OnPropertyChanged("LibraryStatsText");
	}

	private void ApplyTagSearch()
	{
		string text = TagSearchText.Trim();
		VisibleTags.Clear();
		if (text.Length == 0)
		{
			foreach (TagItem tag in Tags)
			{
				tag.IsVisible = true;
			}
			{
				foreach (TagItem tag2 in Tags)
				{
					VisibleTags.Add(tag2);
				}
				return;
			}
		}
		foreach (TagItem tag3 in Tags)
		{
			tag3.IsVisible = false;
		}
		foreach (TagItem tag4 in Tags)
		{
			if (!tag4.Name.Contains(text, StringComparison.OrdinalIgnoreCase) && tag4.State == TagFilterState.Neutral)
			{
				continue;
			}
			for (TagItem tagItem = tag4; tagItem != null; tagItem = tagItem.Parent)
			{
				tagItem.IsVisible = true;
				if (tagItem != tag4)
				{
					tagItem.IsExpanded = true;
				}
			}
			VisibleTags.Add(tag4);
		}
	}

	public void ReloadCollections()
	{
		long? num = SelectedCollection?.Id;
		Collections.Clear();
		foreach (CollectionItem item in CollectionRepository.GetAll())
		{
			Collections.Add(item);
		}
		if (num.HasValue)
		{
			long id = num.GetValueOrDefault();
			_selectedCollection = Collections.FirstOrDefault((CollectionItem c) => c.Id == id);
			OnPropertyChanged("SelectedCollection");
		}
	}

	public void ReloadFolders()
	{
		Folders.Clear();
		foreach (FolderItem item in FolderRepository.GetAll())
		{
			item.Count = FolderRepository.CountUnder(item.Path);
			Folders.Add(item);
		}
		OnPropertyChanged("LibraryStatsText");
	}

	private void RestartWatchers()
	{
		_watcher.Configure(Folders.Select((FolderItem f) => f.Path));
	}

	private void ReloadExifOptions()
	{
		CameraMakes.Clear();
		foreach (string distinctValue in ImageRepository.GetDistinctValues("camera_make"))
		{
			CameraMakes.Add(distinctValue);
		}
		CameraModels.Clear();
		foreach (string distinctValue2 in ImageRepository.GetDistinctValues("camera_model"))
		{
			CameraModels.Add(distinctValue2);
		}
		Lenses.Clear();
		foreach (string distinctValue3 in ImageRepository.GetDistinctValues("lens"))
		{
			Lenses.Add(distinctValue3);
		}
	}

	public void UpdateSelection(IReadOnlyList<ImageItem> selection)
	{
		SelectedImages = selection;
		CurrentImage = ((selection.Count > 0) ? selection[0] : null);
		SelectionTags.Clear();
		SuggestedTags.Clear();
		if (selection.Count == 0)
		{
			return;
		}
		foreach (TagChip item in TagRepository.GetTagsForSelection(selection.Select((ImageItem i) => i.Id).ToList()))
		{
			SelectionTags.Add(item);
		}
		RefreshSuggestions();
	}

	private void RefreshSelectionTags()
	{
		UpdateSelection(SelectedImages);
	}

	private void UpdateTagSuggestions()
	{
		TagSuggestions.Clear();
		string needle = NewTagText.Trim();
		if (needle.Length == 0)
		{
			return;
		}
		foreach (string item in (from t in Tags
			where t.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
			orderby t.Name.StartsWith(needle, StringComparison.OrdinalIgnoreCase) descending, t.Count descending
			select t.Name).Take(12))
		{
			TagSuggestions.Add(item);
		}
	}

	private void AddTagToSelection()
	{
		if (SelectedImages.Count != 0 && !string.IsNullOrWhiteSpace(NewTagText))
		{
			ApplyTagText(NewTagText);
			NewTagText = "";
		}
	}

	public void ApplyTagText(string text)
	{
		if (SelectedImages.Count == 0)
		{
			return;
		}
		List<long> list = SelectedImages.Select((ImageItem i) => i.Id).ToList();
		string[] array = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		int num = 0;
		string[] array2 = array;
		foreach (string text2 in array2)
		{
			long num3 = TagRepository.AddTagToImages(text2, list, out List<long> newlyTagged);
			if (num3 == 0L)
			{
				continue;
			}
			num++;
			RememberRecent(text2.Trim());
			if (newlyTagged.Count > 0)
			{
				_undo.Push(new AddTagAction(num3, text2.Trim(), newlyTagged));
			}
			if (!ImplyAncestors)
			{
				continue;
			}
			foreach (long ancestorId in TagRepository.GetAncestors(num3))
			{
				TagItem tagItem = Tags.FirstOrDefault((TagItem t) => t.Id == ancestorId);
				if (tagItem != null)
				{
					TagRepository.AddTagToImages(tagItem.FullPath, list, out List<long> newlyTagged2);
					if (newlyTagged2.Count > 0)
					{
						_undo.Push(new AddTagAction(ancestorId, tagItem.Name, newlyTagged2));
					}
				}
			}
		}
		if (num != 0)
		{
			ReloadTags();
			RefreshSelectionTags();
			StatusText = $"Tagged {list.Count:N0} image{((list.Count == 1) ? "" : "s")}.";
		}
	}

	private void RemoveTagFromSelection(TagChip? chip)
	{
		if (chip != null && SelectedImages.Count != 0)
		{
			TagRepository.RemoveTagFromImages(chip.Id, SelectedImages.Select((ImageItem i) => i.Id), out List<long> actuallyRemoved);
			if (actuallyRemoved.Count > 0)
			{
				_undo.Push(new RemoveTagAction(chip.Id, chip.Name, actuallyRemoved));
			}
			ReloadTags();
			RefreshSelectionTags();
		}
	}

	public void ReparentTag(TagItem tag, TagItem? newParent)
	{
		if ((newParent == null || tag.Id != newParent.Id) && tag.ParentId != newParent?.Id)
		{
			if (!TagRepository.Reparent(tag.Id, newParent?.Id))
			{
				StatusText = ((newParent == null) ? ("Could not move \"" + tag.Name + "\" — a top-level tag with that name already exists.") : ($"Could not move \"{tag.Name}\" under \"{newParent.Name}\" — that would either " + "nest it inside itself or clash with an existing child."));
			}
			else
			{
				ReloadTags();
				Refresh();
				StatusText = ((newParent == null) ? ("Moved \"" + tag.Name + "\" to the top level.") : $"Moved \"{tag.Name}\" under \"{newParent.Name}\".");
			}
		}
	}

	public void ReviewDuplicates(Func<DuplicateService, (bool Confirmed, List<long> Ids)> showDialog)
	{
		var (flag, list) = showDialog(_duplicateFinder);
		if (flag && list.Count != 0)
		{
			LibrarySnapshot snapshot = ImageRepository.CaptureSnapshot(list);
			_undo.Push(new RemoveFromLibraryAction(snapshot));
			ImageRepository.Remove(list);
			ReloadTags();
			ReloadCollections();
			ReloadFolders();
			_index.Reload();
			RefreshCoverage();
			Refresh();
			StatusText = $"Removed {list.Count:N0} duplicate(s) from the library. Ctrl+Z to undo.";
		}
	}

	private void ManageAliases(TagItem? tag)
	{
		if (tag == null)
		{
			return;
		}
		List<string> aliases = TagRepository.GetAliases(tag.Id);
		string prompt = ((aliases.Count > 0) ? ("Aliases for \"" + tag.Name + "\" (comma separated). Currently: " + string.Join(", ", aliases)) : ("Alternative spellings for \"" + tag.Name + "\", comma separated:"));
		string text = InputDialog.Show("Tag aliases", prompt, string.Join(", ", aliases));
		if (text == null)
		{
			return;
		}
		foreach (string item in aliases)
		{
			TagRepository.RemoveAlias(item);
		}
		int num = 0;
		List<string> list = new List<string>();
		string[] array = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string text2 in array)
		{
			if (TagRepository.AddAlias(tag.Id, text2))
			{
				num++;
			}
			else
			{
				list.Add(text2);
			}
		}
		StatusText = ((list.Count == 0) ? $"\"{tag.Name}\" now answers to {num} alias(es)." : $"Set {num} alias(es). Skipped {string.Join(", ", list)} — already a real tag name.");
	}

	private void ChangeTagColor(TagItem? tag)
	{
		if (tag != null)
		{
			TagColorDialog tagColorDialog = new TagColorDialog(tag.Color)
			{
				Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault((Window w) => w.IsActive)
			};
			if (tagColorDialog.ShowDialog() == true)
			{
				TagRepository.SetColor(tag.Id, tagColorDialog.ColorValue);
				tag.Color = tagColorDialog.ColorValue;
				RefreshSelectionTags();
				Refresh();
				StatusText = "Changed the colour of \"" + tag.Name + "\".";
			}
		}
	}

	private void AddChildTag(TagItem? parent)
	{
		if (parent != null)
		{
			string value = InputDialog.Show("New child tag", "Name of the tag to nest under \"" + parent.Name + "\":");
			if (!string.IsNullOrWhiteSpace(value))
			{
				TagRepository.GetOrCreate($"{parent.FullPath}{'/'}{value}");
				parent.IsExpanded = true;
				ReloadTags();
				StatusText = $"Added \"{value}\" under \"{parent.Name}\".";
			}
		}
	}

	private void ToggleTagFilter(TagItem? tag)
	{
		if (tag != null)
		{
			tag.CycleState();
			Refresh();
		}
	}

	private void RenameTag(TagItem? tag)
	{
		if (tag != null)
		{
			string text = InputDialog.Show("Rename tag", "New name:", tag.Name);
			if (!string.IsNullOrWhiteSpace(text) && !(text == tag.Name))
			{
				TagRepository.Rename(tag.Id, text);
				ReloadTags();
				RefreshSelectionTags();
				Refresh();
			}
		}
	}

	private void DeleteTag(TagItem? tag)
	{
		if (tag != null && MessageBox.Show($"Delete the tag \"{tag.Name}\"?\n\nIt will be removed from {tag.Count:N0} image(s). " + "The image files themselves are not touched.", "Magpie Trove", MessageBoxButton.OKCancel, MessageBoxImage.Exclamation) == MessageBoxResult.OK)
		{
			TagRepository.Delete(tag.Id);
			ReloadTags();
			RefreshSelectionTags();
			Refresh();
		}
	}

	private void SetRating(object? parameter)
	{
		if (SelectedImages.Count == 0 || parameter == null || !int.TryParse(parameter.ToString(), out var rating))
		{
			return;
		}
		rating = Math.Clamp(rating, 0, 5);
		List<(long, int)> list = SelectedImages.Select((ImageItem i) => (Id: i.Id, Rating: i.Rating)).ToList();
		if (list.All<(long, int)>(((long Id, int Rating) p) => p.Rating == rating))
		{
			return;
		}
		_undo.Push(new RatingAction(list, rating));
		ImageRepository.SetRating(SelectedImages.Select((ImageItem i) => i.Id), rating);
		foreach (ImageItem selectedImage in SelectedImages)
		{
			selectedImage.Rating = rating;
		}
		if (MinRating > 0 || SortBy == SortField.Rating)
		{
			Refresh();
		}
	}

	private void AddFolder()
	{
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = "Add a folder to the Magpie Trove library",
			Multiselect = true
		};
		if (openFolderDialog.ShowDialog() == true)
		{
			string[] folderNames = openFolderDialog.FolderNames;
			for (int i = 0; i < folderNames.Length; i++)
			{
				FolderRepository.Add(folderNames[i]);
			}
			ReloadFolders();
			RestartWatchers();
			RescanAsync();
		}
	}

	private void RemoveFolder(FolderItem? folder)
	{
		if (folder != null && MessageBox.Show($"Stop watching \"{folder.Path}\"?\n\nIts {folder.Count:N0} indexed image(s) and their tags will be removed from the " + "library. No files on disk are deleted.", "Magpie Trove", MessageBoxButton.OKCancel, MessageBoxImage.Exclamation) == MessageBoxResult.OK)
		{
			FolderRepository.Remove(folder.Id, removeImages: true);
			if (SelectedFolder?.Id == folder.Id)
			{
				_selectedFolder = null;
			}
			ReloadFolders();
			RestartWatchers();
			ReloadTags();
			ReloadCollections();
			Refresh();
		}
	}

	public async Task RescanAsync(bool isStartupReconciliation = false)
	{
		if (IsBusy)
		{
			return;
		}
		List<string> list = Folders.Select((FolderItem f) => f.Path).ToList();
		if (list.Count == 0)
		{
			StatusText = "Add a folder first.";
			return;
		}
		IsBusy = true;
		_scanCts = new CancellationTokenSource();
		Progress<ScanProgress> progress = new Progress<ScanProgress>(delegate(ScanProgress p)
		{
			ProgressText = ((p.Total > 0) ? $"{p.Message}  ({p.Processed:N0}/{p.Total:N0})" : p.Message);
			ProgressValue = p.Percent;
		});
		try
		{
			ScanResult scanResult = await LibraryScanner.ScanAsync(list, progress, _scanCts.Token);
			string value = (isStartupReconciliation ? "Startup check complete" : "Scan complete");
			StatusText = $"{value} — {scanResult.Added:N0} added, {scanResult.Updated:N0} updated, {scanResult.Skipped:N0} unchanged" + ((scanResult.Moved > 0) ? $", {scanResult.Moved:N0} moved (tags kept)" : "") + ((scanResult.Unreadable > 0) ? $", {scanResult.Unreadable:N0} unreadable" : "") + ((scanResult.MarkedMissing > 0) ? $", {scanResult.MarkedMissing:N0} missing" : "") + ".";
			if (scanResult.OfflineRoots > 0)
			{
				StatusText += $" {scanResult.OfflineRoots:N0} folder root(s) offline; existing images were preserved.";
			}
		}
		catch (OperationCanceledException)
		{
			StatusText = "Scan cancelled.";
		}
		catch (Exception ex2)
		{
			StatusText = "Scan failed: " + ex2.Message;
		}
		finally
		{
			IsBusy = false;
			ProgressText = "";
			ProgressValue = 0.0;
			_scanCts?.Dispose();
			_scanCts = null;
		}
		_dispatcher.Invoke((Action)delegate
		{
			ReloadFolders();
			RestartWatchers();
			ReloadTags();
			ReloadExifOptions();
			RefreshCoverage();
			Refresh();
		});
		if (_startupReconciliationRequested)
		{
			_startupReconciliationRequested = false;
			_startupReconciliationStarted = true;
		}
		await DrainDeferredWatcherChangesAsync();
	}

	public void CancelScan()
	{
		_scanCts?.Cancel();
	}

	private async Task ApplyWatchedChangesAsync(IReadOnlyList<LibraryFileChange> changes, bool fullRescan)
	{
		if (IsBusy)
		{
			_deferredFullRescan |= fullRescan;
			{
				foreach (LibraryFileChange change in changes)
				{
					_deferredWatcherChanges[change.Path] = change;
				}
				return;
			}
		}
		if (fullRescan)
		{
			StatusText = "File watcher lost events; running a safety rescan.";
			await RescanAsync();
			return;
		}
		IsBusy = true;
		ProgressText = $"Updating {changes.Count:N0} file change(s)…";
		try
		{
			IncrementalScanResult incrementalScanResult = await LibraryScanner.ApplyChangesAsync(changes);
			StatusText = $"Library updated — {incrementalScanResult.Updated:N0} changed, {incrementalScanResult.Missing:N0} missing" + ((incrementalScanResult.Unreadable > 0) ? $", {incrementalScanResult.Unreadable:N0} unreadable" : "") + ".";
			ReloadFolders();
			ReloadTags();
			ReloadCollections();
			ReloadExifOptions();
			RefreshCoverage();
			Refresh();
		}
		finally
		{
			IsBusy = false;
			ProgressText = "";
			ProgressValue = 0.0;
		}
		await DrainDeferredWatcherChangesAsync();
		if (_startupReconciliationRequested)
		{
			await ReconcileAtStartupAsync();
		}
	}

	private async Task DrainDeferredWatcherChangesAsync()
	{
		if (!IsBusy && (_deferredFullRescan || _deferredWatcherChanges.Count != 0))
		{
			bool deferredFullRescan = _deferredFullRescan;
			List<LibraryFileChange> changes = _deferredWatcherChanges.Values.ToList();
			_deferredFullRescan = false;
			_deferredWatcherChanges.Clear();
			if (deferredFullRescan)
			{
				StatusText = "File watcher lost events; running a safety rescan.";
				await RescanAsync();
			}
			else
			{
				await ApplyWatchedChangesAsync(changes, fullRescan: false);
			}
		}
	}

	private void NewCollectionFromSelection()
	{
		string text = InputDialog.Show("New collection", "Collection name:", "Untitled collection");
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		long collectionId = CollectionRepository.Create(text, CollectionKind.Manual, null);
		if (SelectedImages.Count > 0)
		{
			CollectionRepository.AddImages(collectionId, SelectedImages.Select((ImageItem i) => i.Id));
		}
		ReloadCollections();
		StatusText = $"Created collection \"{text}\" with {SelectedImages.Count:N0} image(s).";
	}

	private void NewSmartCollectionFromFilter()
	{
		string text = InputDialog.Show("New smart collection", "Name — the current filter becomes its rule:", "Untitled smart collection");
		if (!string.IsNullOrWhiteSpace(text))
		{
			FilterQuery filterQuery = BuildFilter();
			filterQuery.CollectionId = null;
			CollectionRepository.Create(text, CollectionKind.Smart, filterQuery);
			ReloadCollections();
			StatusText = "Saved the current filter as \"" + text + "\".";
		}
	}

	private void ApplySavedRule(FilterQuery rule)
	{
		_suspendRefresh = true;
		try
		{
			foreach (TagItem tag in Tags)
			{
				tag.State = (rule.IncludeTagIds.Contains(tag.Id) ? TagFilterState.Include : (rule.ExcludeTagIds.Contains(tag.Id) ? TagFilterState.Exclude : TagFilterState.Neutral));
			}
			MatchAll = rule.MatchAll;
			UntaggedOnly = rule.UntaggedOnly;
			MinRating = rule.MinRating;
			FlagFilterMode = rule.Flags;
			DateFrom = rule.DateFrom;
			DateTo = rule.DateTo;
			SortBy = rule.SortBy;
			SortDescending = rule.SortDescending;
			CameraMake = rule.CameraMake;
			CameraModel = rule.CameraModel;
			Lens = rule.Lens;
			IsoMin = rule.IsoMin;
			IsoMax = rule.IsoMax;
			ApertureMin = rule.ApertureMin;
			ApertureMax = rule.ApertureMax;
			ShutterSpeedMin = rule.ShutterSpeedMin;
			ShutterSpeedMax = rule.ShutterSpeedMax;
			FocalLengthMin = rule.FocalLengthMin;
			FocalLengthMax = rule.FocalLengthMax;
			_searchText = rule.Search ?? "";
			OnPropertyChanged("SearchText");
			_selectedFolder = Folders.FirstOrDefault((FolderItem f) => string.Equals(f.Path, rule.FolderPrefix, StringComparison.OrdinalIgnoreCase));
			OnPropertyChanged("SelectedFolder");
		}
		finally
		{
			_suspendRefresh = false;
		}
		ApplyTagSearch();
		Refresh();
	}

	private void AddSelectionToCollection(CollectionItem? collection)
	{
		if (collection == null || SelectedImages.Count == 0)
		{
			return;
		}
		if (collection.IsSmart)
		{
			StatusText = "Smart collections are defined by their rule — edit the filter instead.";
			return;
		}
		List<long> imageIds = SelectedImages.Select((ImageItem i) => i.Id).ToList();
		CollectionRepository.AddImages(collection.Id, imageIds);
		_undo.Push(new CollectionMembershipAction(collection.Id, collection.Name, imageIds, wasAdded: true));
		ReloadCollections();
		StatusText = $"Added {SelectedImages.Count:N0} image(s) to \"{collection.Name}\".";
	}

	private void RemoveSelectionFromCollection()
	{
		CollectionItem selectedCollection = SelectedCollection;
		if (selectedCollection != null && !selectedCollection.IsSmart && SelectedImages.Count != 0)
		{
			List<long> imageIds = SelectedImages.Select((ImageItem i) => i.Id).ToList();
			CollectionRepository.RemoveImages(selectedCollection.Id, imageIds);
			_undo.Push(new CollectionMembershipAction(selectedCollection.Id, selectedCollection.Name, imageIds, wasAdded: false));
			ReloadCollections();
			Refresh();
		}
	}

	private void RenameCollection(CollectionItem? collection)
	{
		if (collection != null)
		{
			string text = InputDialog.Show("Rename collection", "New name:", collection.Name);
			if (!string.IsNullOrWhiteSpace(text))
			{
				CollectionRepository.Rename(collection.Id, text);
				collection.Name = text;
			}
		}
	}

	private void DeleteCollection(CollectionItem? collection)
	{
		if (collection != null && MessageBox.Show("Delete the collection \"" + collection.Name + "\"?\n\nImages and tags are not affected.", "Magpie Trove", MessageBoxButton.OKCancel, MessageBoxImage.Exclamation) == MessageBoxResult.OK)
		{
			CollectionRepository.Delete(collection.Id);
			if (SelectedCollection?.Id == collection.Id)
			{
				_selectedCollection = null;
			}
			ReloadCollections();
			OnPropertyChanged("SelectedCollection");
			Refresh();
		}
	}

	private void RevealInExplorer()
	{
		if (CurrentImage == null)
		{
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + CurrentImage.Path + "\"")
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			StatusText = "Could not open Explorer: " + ex.Message;
		}
	}

	private void CopyPath()
	{
		if (SelectedImages.Count == 0)
		{
			return;
		}
		try
		{
			Clipboard.SetText(string.Join(Environment.NewLine, SelectedImages.Select((ImageItem i) => i.Path)));
			StatusText = $"Copied {SelectedImages.Count:N0} path(s).";
		}
		catch (Exception ex)
		{
			StatusText = "Clipboard unavailable: " + ex.Message;
		}
	}

	private void RemoveSelectionFromLibrary()
	{
		if (SelectedImages.Count != 0 && MessageBox.Show($"Remove {SelectedImages.Count:N0} image(s) from the Magpie Trove library?\n\n" + "Their tags are discarded. The files on disk are not deleted, and a rescan will index them again as untagged.\n\nThis can be undone with Ctrl+Z.", "Magpie Trove", MessageBoxButton.OKCancel, MessageBoxImage.Exclamation) == MessageBoxResult.OK)
		{
			LibrarySnapshot snapshot = ImageRepository.CaptureSnapshot(SelectedImages.Select((ImageItem i) => i.Id));
			_undo.Push(new RemoveFromLibraryAction(snapshot));
			ImageRepository.Remove(SelectedImages.Select((ImageItem i) => i.Id));
			ReloadTags();
			ReloadCollections();
			ReloadFolders();
			Refresh();
		}
	}

	private void ClearFilter()
	{
		_suspendRefresh = true;
		try
		{
			foreach (TagItem tag in Tags)
			{
				tag.State = TagFilterState.Neutral;
			}
			UntaggedOnly = false;
			MinRating = 0;
			FlagFilterMode = FlagFilter.All;
			DateFrom = null;
			DateTo = null;
			CameraMake = null;
			CameraModel = null;
			Lens = null;
			IsoMin = null;
			IsoMax = null;
			ApertureMin = null;
			ApertureMax = null;
			ShutterSpeedMin = null;
			ShutterSpeedMax = null;
			FocalLengthMin = null;
			FocalLengthMax = null;
			_similarTo = null;
			_similarToName = "";
			OnPropertyChanged("IsSimilarMode");
			OnPropertyChanged("SimilarModeText");
			_searchText = "";
			OnPropertyChanged("SearchText");
			_selectedFolder = null;
			OnPropertyChanged("SelectedFolder");
			_selectedCollection = null;
			OnPropertyChanged("SelectedCollection");
		}
		finally
		{
			_suspendRefresh = false;
		}
		ApplyTagSearch();
		Refresh();
	}

	private void ClearThumbnailCache()
	{
		string text = ImageItem.FormatSize(_thumbnails.GetCacheSizeBytes());
		if (MessageBox.Show("Delete the thumbnail cache (" + text + ")?\n\nThumbnails are rebuilt as you browse.", "Magpie Trove", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
		{
			_thumbnails.ClearDiskCache();
			StatusText = "Thumbnail cache cleared.";
			OnPropertyChanged("LibraryStatsText");
		}
	}

	public void RefreshModelAvailability()
	{
		OnPropertyChanged("VisualFeaturesAvailable");
		OnPropertyChanged("CanAnalyze");
		RefreshCoverage();
	}

	private void RefreshCoverage()
	{
		if (!VisualFeaturesAvailable)
		{
			CoverageText = "Visual search model not installed.";
			return;
		}
		(int Embedded, int Pending) coverage = EmbeddingRepository.GetCoverage("clip-vit-b32-vision");
		int item = coverage.Embedded;
		int item2 = coverage.Pending;
		CoverageText = ((item2 == 0) ? $"{item:N0} images analysed" : $"{item:N0} analysed, {item2:N0} pending");
		OnPropertyChanged("HasEmbeddings");
	}

	public async Task AnalyzeAsync()
	{
		if (!CanAnalyze)
		{
			return;
		}
		IsAnalyzing = true;
		_embedCts = new CancellationTokenSource();
		Progress<EmbedProgress> progress = new Progress<EmbedProgress>(delegate(EmbedProgress p)
		{
			AnalyzeProgress = p.Percent;
			AnalyzeStatus = ((p.Remaining > TimeSpan.Zero) ? $"Analysing {p.Processed:N0}/{p.Total:N0}  ({p.ImagesPerSecond:0}/s, {p.Remaining:mm\\:ss} left)" : $"Analysing {p.Processed:N0}/{p.Total:N0}");
		});
		try
		{
			EmbedResult embedResult = await _embeddings.RunAsync(useGpu: true, progress, _embedCts.Token);
			StatusText = ((embedResult.Embedded == 0) ? "Everything is already analysed." : ($"Analysed {embedResult.Embedded:N0} images in {embedResult.Elapsed.TotalSeconds:0}s" + ((embedResult.Failed > 0) ? $" ({embedResult.Failed:N0} could not be read)" : "") + "."));
		}
		catch (OperationCanceledException)
		{
			StatusText = "Analysis stopped — progress is saved, run it again to continue.";
		}
		catch (Exception ex2)
		{
			StatusText = "Analysis failed: " + ex2.Message;
		}
		finally
		{
			IsAnalyzing = false;
			AnalyzeStatus = "";
			AnalyzeProgress = 0.0;
			_embedCts?.Dispose();
			_embedCts = null;
		}
		await Task.Run(delegate
		{
			_index.Reload();
		});
		RefreshCoverage();
		RefreshSuggestions();
	}

	private void FindSimilarToSelection()
	{
		if (CurrentImage != null)
		{
			if (!HasEmbeddings)
			{
				StatusText = "Run Analyse images first so Magpie Trove knows what your photos look like.";
			}
			else
			{
				SetSimilarTo(CurrentImage);
			}
		}
	}

	private void SetSimilarTo(ImageItem? anchor)
	{
		_similarTo = anchor?.Id;
		_similarToName = anchor?.FileName ?? "";
		OnPropertyChanged("IsSimilarMode");
		OnPropertyChanged("SimilarModeText");
		Refresh();
	}

	private List<ImageItem> ApplyVisualRanking(List<ImageItem> results)
	{
		long? similarTo = _similarTo;
		if (similarTo.HasValue)
		{
			long valueOrDefault = similarTo.GetValueOrDefault();
			if (results.Count != 0)
			{
				HashSet<long> candidates = results.Select((ImageItem r) => r.Id).ToHashSet();
				List<ScoredImage> list = _index.SearchSimilarTo(valueOrDefault, Math.Min(results.Count, 2000), candidates);
				if (list.Count == 0)
				{
					return results;
				}
				Dictionary<long, int> order = new Dictionary<long, int>(list.Count);
				for (int num = 0; num < list.Count; num++)
				{
					order[list[num].ImageId] = num;
				}
				return (from r in results
					where order.ContainsKey(r.Id)
					orderby order[r.Id]
					select r).ToList();
			}
		}
		return results;
	}

	private void RefreshSuggestions()
	{
		SuggestedTags.Clear();
		if (!HasEmbeddings || SelectedImages.Count == 0)
		{
			return;
		}
		try
		{
			foreach (TagSuggestion item in _suggestions.SuggestFromNeighbours(SelectedImages.Select((ImageItem i) => i.Id).ToList()))
			{
				SuggestedTags.Add(item);
			}
		}
		catch (Exception)
		{
		}
	}

	private void TrainProbe(TagItem? tag)
	{
		if (tag == null)
		{
			return;
		}
		if (!HasEmbeddings)
		{
			StatusText = "Run Analyse images first.";
			return;
		}
		ProbeResult probeResult = _suggestions.TrainProbe(tag.Id, tag.Name);
		StatusText = probeResult.Message;
		if (probeResult.Trained)
		{
			MessageBox.Show(probeResult.Message + "\n\nUse \"Show images it suggests\" on the tag to review what it found. Tagging a wider variety of images — especially ones it gets wrong — is what sharpens it.", "Magpie Trove", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		else
		{
			MessageBox.Show(probeResult.Message, "Magpie Trove", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void ShowProbeCandidates(TagItem? tag)
	{
		if (tag == null)
		{
			return;
		}
		if (!_suggestions.HasProbe(tag.Id))
		{
			StatusText = "No probe trained for \"" + tag.Name + "\" yet — use \"Learn this tag\" first.";
			return;
		}
		List<ScoredImage> list = _suggestions.FindCandidates(tag.Id);
		if (list.Count == 0)
		{
			StatusText = "The probe for \"" + tag.Name + "\" found no new candidates.";
			return;
		}
		HashSet<long> byId = list.Select((ScoredImage c) => c.ImageId).ToHashSet();
		Dictionary<long, int> ordered = new Dictionary<long, int>();
		for (int num = 0; num < list.Count; num++)
		{
			ordered[list[num].ImageId] = num;
		}
		List<ImageItem> source = ImageRepository.Query(new FilterQuery());
		Images = (from i in source
			where byId.Contains(i.Id)
			orderby ordered[i.Id]
			select i).ToList();
		UpdateSelection(Array.Empty<ImageItem>());
		StatusText = $"{list.Count:N0} images that look like \"{tag.Name}\" but aren't tagged with it. " + "Select the correct ones and apply the tag.";
	}

	private void ReloadPinned()
	{
		PinnedTags.Clear();
		foreach (TagItem item in from t in Tags
			where t.IsPinned
			orderby t.PinnedSlot
			select t)
		{
			PinnedTags.Add(item);
		}
	}

	public bool ApplyPinnedTag(int slot)
	{
		TagItem tagItem = PinnedTags.FirstOrDefault((TagItem t) => t.PinnedSlot == slot);
		if (tagItem == null)
		{
			StatusText = $"No tag pinned to {slot}. Right-click a tag to pin it.";
			return false;
		}
		if (SelectedImages.Count == 0)
		{
			return false;
		}
		ApplyTagText(tagItem.FullPath);
		if (AutoAdvance)
		{
			AdvanceRequested?.Invoke();
		}
		return true;
	}

	private void PinTag(TagItem? tag)
	{
		if (tag == null)
		{
			return;
		}
		if (tag.IsPinned)
		{
			TagRepository.SetPinnedSlot(tag.Id, null);
			StatusText = "Unpinned \"" + tag.Name + "\".";
		}
		else
		{
			int? num = TagRepository.NextFreeSlot();
			if (!num.HasValue)
			{
				StatusText = "All nine number keys are taken — unpin one first.";
				return;
			}
			TagRepository.SetPinnedSlot(tag.Id, num.Value);
			StatusText = $"\"{tag.Name}\" is now on key {num.Value}.";
		}
		ReloadTags();
		ReloadPinned();
	}

	public void SetFlag(int flag)
	{
		if (SelectedImages.Count == 0)
		{
			return;
		}
		List<(long, int)> list = SelectedImages.Select((ImageItem i) => (Id: i.Id, Flag: i.Flag)).ToList();
		if (list.All<(long, int)>(((long Id, int Flag) p) => p.Flag == flag))
		{
			return;
		}
		_undo.Push(new FlagAction(list, flag));
		ImageRepository.SetFlag(SelectedImages.Select((ImageItem i) => i.Id), flag);
		foreach (ImageItem selectedImage in SelectedImages)
		{
			selectedImage.Flag = flag;
		}
		if (AutoAdvance)
		{
			AdvanceRequested?.Invoke();
		}
		if (FlagFilterMode != FlagFilter.All)
		{
			Refresh();
		}
	}

	public void CopyTags()
	{
		if (CurrentImage != null)
		{
			_tagClipboard = TagRepository.GetTagNames(CurrentImage.Id);
			OnPropertyChanged("TagClipboardSummary");
			StatusText = ((_tagClipboard.Count == 0) ? "That image has no tags to copy." : $"Copied {_tagClipboard.Count} tag(s) — Ctrl+Shift+V to apply to a selection.");
		}
	}

	public void PasteTags()
	{
		if (_tagClipboard.Count == 0 || SelectedImages.Count == 0)
		{
			return;
		}
		foreach (string item in _tagClipboard)
		{
			ApplyTagText(item);
		}
		StatusText = $"Applied {_tagClipboard.Count} tag(s) to {SelectedImages.Count:N0} image(s).";
	}

	private void RememberRecent(string name)
	{
		_recent.RemoveAll((string n) => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
		_recent.Insert(0, name);
		while (_recent.Count > 12)
		{
			_recent.RemoveAt(_recent.Count - 1);
		}
		RecentTags.Clear();
		foreach (string item in _recent)
		{
			RecentTags.Add(item);
		}
		Database.SetMeta("recent_tags", string.Join("\n", _recent));
	}

	private void RestoreRecent()
	{
		string meta = Database.GetMeta("recent_tags");
		if (string.IsNullOrEmpty(meta))
		{
			return;
		}
		_recent.Clear();
		_recent.AddRange(meta.Split('\n', StringSplitOptions.RemoveEmptyEntries));
		RecentTags.Clear();
		foreach (string item in _recent)
		{
			RecentTags.Add(item);
		}
	}
}
