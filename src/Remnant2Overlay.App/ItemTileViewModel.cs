using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Remnant2Overlay.SaveReader;

namespace Remnant2Overlay.App;

public sealed class ItemTileViewModel : INotifyPropertyChanged
{
    private RunItemStatus _status = RunItemStatus.Unknown;
    private string _reason = "";
    private string? _where;
    private bool? _manualOverride;

    public ItemTileViewModel(ItemInfo item, string iconDir)
    {
        Item = item;
        Icon = LoadIcon(Path.Combine(iconDir, item.AssetName + ".png"));
    }

    public ItemInfo Item { get; }
    public ImageSource? Icon { get; }
    public bool HasIcon => Icon is not null;
    public string Name => Item.DisplayName;
    public string Initials => string.Concat(Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w[0]));
    public string CategoryName => App.CategoryLabel(Item.Category);

    public string Tooltip
    {
        get
        {
            var lines = new List<string> { Name, $"{CategoryName} · {Item.WorldName}", "", _reason };
            if (_where is not null) lines.Add(_where);
            if (Item.DropType is not null) lines.Add($"Source: {Item.DropType} – {Item.DropReference}");
            if (Item.Note is not null) lines.Add(Item.Note);
            if (_manualOverride is not null) lines.Add("(marked manually)");
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>Status from the save for the selected run.</summary>
    public RunItemStatus Status => _status;
    public string Reason => _reason;
    public string? Where => _where;

    public void SetStatus(RunItemStatus status, string reason, string? where)
    {
        _status = status;
        _reason = reason;
        _where = where;
        Changed(nameof(Status));
        Changed(nameof(Reason));
        Changed(nameof(Where));
        Changed(nameof(Owned));
        Changed(nameof(DisplayStatus));
        Changed(nameof(Tooltip));
    }

    /// <summary>Manual mode override. Null means "use the save".</summary>
    public bool? ManualOverride
    {
        get => _manualOverride;
        set
        {
            if (_manualOverride == value) return;
            _manualOverride = value;
            Changed(nameof(ManualOverride));
            Changed(nameof(Owned));
            Changed(nameof(IsManual));
            Changed(nameof(DisplayStatus));
            Changed(nameof(Tooltip));
        }
    }

    public bool IsManual => _manualOverride is not null;
    public bool Owned => _manualOverride ?? _status == RunItemStatus.Owned;

    /// <summary>Status used for highlighting, after manual overrides.</summary>
    public RunItemStatus DisplayStatus => _manualOverride switch
    {
        true => RunItemStatus.Owned,
        false when _status == RunItemStatus.Owned => RunItemStatus.Unknown,
        _ => _status,
    };

    private static ImageSource? LoadIcon(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 128;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
