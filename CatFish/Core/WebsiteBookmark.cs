using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace CatFish.Core;

public sealed class WebsiteBookmark : INotifyPropertyChanged, IEditableObject
{
    private string _name = "";
    private string _url = "";
    private string _description = "";
    private WebsiteBookmark? _beforeEdit;

    public string Name { get => _name; set => SetField(ref _name, value); }
    public string Url { get => _url; set => SetField(ref _url, value); }
    public string Description { get => _description; set => SetField(ref _description, value); }
    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(Url) && string.IsNullOrWhiteSpace(Description);
    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField(ref string field, string? value, [CallerMemberName] string? property = null)
    {
        value ??= "";
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new(property));
        PropertyChanged?.Invoke(this, new(nameof(IsEmpty)));
    }

    internal WebsiteBookmark Copy() => new() { Name = Name, Url = Url, Description = Description };

    internal static List<WebsiteBookmark> CreateDefaults() =>
    [
        new() { Name = "电子书", Url = "https://www.txt80.cc/" },
        new() { Name = "线上桌游", Url = "https://game.hullqin.cn/" },
        new() { Name = "小霸王", Url = "https://www.yikm.net/" }
    ];

    public void BeginEdit() => _beforeEdit ??= Copy();
    public void EndEdit() => _beforeEdit = null;
    public void CancelEdit()
    {
        if (_beforeEdit is not { } original) return;
        _beforeEdit = null;
        Name = original.Name; Url = original.Url; Description = original.Description;
    }

    internal static bool TryGetVisitUri(string? value, out Uri? uri)
    {
        uri = null;
        var address = value?.Trim();
        if (string.IsNullOrEmpty(address)) return false;
        if (!address.Contains("://", StringComparison.Ordinal)) address = "https://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var candidate)
            || (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(candidate.Host) || candidate.HostNameType == UriHostNameType.Unknown
            || candidate.UserInfo.Length > 0) return false;
        uri = candidate;
        return true;
    }
}
