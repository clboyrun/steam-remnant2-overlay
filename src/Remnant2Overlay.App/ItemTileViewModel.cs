using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Remnant2Overlay.SaveReader;

namespace Remnant2Overlay.App;

public sealed class ItemTileViewModel : INotifyPropertyChanged
{
    private bool _ownedInSave;
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
            var lines = new List<string> { Name, $"{CategoryName} · {Item.WorldName}" };
            if (Item.DropType is not null) lines.Add($"{Item.DropType}: {Item.DropReference}");
            if (Item.Note is not null) lines.Add(Item.Note);
            if (_manualOverride is not null) lines.Add("(set manually)");
            return string.Join(Environment.NewLine, lines);
        }
    }

    public bool OwnedInSave
    {
        get => _ownedInSave;
        set { if (Set(ref _ownedInSave, value)) Changed(nameof(Owned)); }
    }

    /// <summary>Manual mode override. Null means "use the save".</summary>
    public bool? ManualOverride
    {
        get => _manualOverride;
        set
        {
            if (!Set(ref _manualOverride, value)) return;
            Changed(nameof(Owned));
            Changed(nameof(IsManual));
            Changed(nameof(Tooltip));
        }
    }

    public bool IsManual => _manualOverride is not null;
    public bool Owned => _manualOverride ?? _ownedInSave;

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

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Changed(name!);
        return true;
    }
}
