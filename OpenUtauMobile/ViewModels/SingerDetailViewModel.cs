using System;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Services.Platform;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels;

public class SingerDetailViewModel : NavigateViewModelBase, IDisposable
{
    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenWebCommand { get; }
    public event Action<USinger>? SingerUninstalled;

    private USinger _singer;
    private readonly bool _isProviderSinger;
    private readonly IDisposable _favoriteSubscription;

    // ── Basic Info ──
    /// <summary>外部歌手维护由提供方声明，禁止套用目录递归删除。</summary>
    public bool CanDelete => Services.SingerPlugins.BuiltinSingerMaintenance.CanUninstall(_singer)
        || (Services.SingerPlugins.SingerDeletionService.Find(_singer) is { } candidate
            && Services.SingerPlugins.SingerDeletionService.CanDelete(candidate));

    public string SingerName => _singer.LocalizedName;
    public string SingerId => _singer.Id ?? string.Empty;
    public string SingerType => GetSingerTypeDisplay();
    public string Author => _singer.Author ?? string.Empty;
    public string Voice => _singer.Voice ?? string.Empty;
    public string Web => _singer.Web ?? string.Empty;
    public string Version => _singer.Version ?? string.Empty;
    public string OtherInfo => _singer.OtherInfo ?? string.Empty;
    public string Location => _singer.Location ?? string.Empty;
    public string DefaultPhonemizer => _singer.DefaultPhonemizer ?? string.Empty;

    // ── Display helpers ──
    public bool HasAuthor => !string.IsNullOrWhiteSpace(Author);
    public bool HasVoice => !string.IsNullOrWhiteSpace(Voice);
    public bool HasWeb => !string.IsNullOrWhiteSpace(Web);
    public bool HasVersion => !string.IsNullOrWhiteSpace(Version);
    public bool HasOtherInfo => !string.IsNullOrWhiteSpace(OtherInfo);
    public bool HasLocation => !string.IsNullOrWhiteSpace(Location);
    public bool HasDefaultPhonemizer => !string.IsNullOrWhiteSpace(DefaultPhonemizer);

    // ── Avatar ──
    [Reactive] public Bitmap? AvatarBitmap { get; private set; }
    public bool HasAvatar => AvatarBitmap != null;

    // ── Favorite ──
    [Reactive] public bool IsFavorite { get; set; }

    public SingerDetailViewModel(MainViewModel navigator, USinger singer) : base(navigator)
    {
        _singer = singer;
        _isProviderSinger = Services.SingerPlugins.SingerDeletionService.Find(singer) != null;

        BackCommand = ReactiveCommand.Create(OnBack);
        DeleteCommand = ReactiveCommand.CreateFromTask(OnDeleteAsync,
            this.WhenAnyValue(_ => _.CanDelete));

        // Create OpenWebCommand - enabled only when HasWeb is true
        IObservable<bool> canOpenWeb = this.WhenAnyValue(x => x.HasWeb);
        OpenWebCommand = ReactiveCommand.CreateFromTask(OnOpenWebAsync, canOpenWeb);

        IsFavorite = _singer.IsFavourite;

        // Load avatar
        LoadAvatar();

        // Sync favorite state back to singer
        _favoriteSubscription = this.WhenAnyValue(x => x.IsFavorite)
            .Subscribe(fav => _singer.IsFavourite = fav);
    }

    public void Dispose()
    {
        _favoriteSubscription.Dispose();
        AvatarBitmap?.Dispose();
        AvatarBitmap = null;
        BackCommand.Dispose(); DeleteCommand.Dispose(); OpenWebCommand.Dispose();
    }

    /// <summary>重新进入详情时换用刷新后的实例；已删除的歌手返回管理页。</summary>
    public override void OnNavigatedTo()
    {
        if (!_isProviderSinger) return;
        Services.SingerPlugins.SingerCandidate? candidate = Services.SingerPlugins.SingerDeletionService.Find(_singer);
        if (candidate?.Entry.Singer is not { } current)
        {
            Navigator.NavigateBack(this);
            return;
        }
        if (ReferenceEquals(_singer, current)) return;
        _singer = current;
        IsFavorite = current.IsFavourite;
        AvatarBitmap?.Dispose();
        AvatarBitmap = null;
        LoadAvatar();
        string[] properties = [nameof(SingerName), nameof(SingerId), nameof(SingerType), nameof(Author), nameof(Voice),
            nameof(Web), nameof(Version), nameof(OtherInfo), nameof(Location), nameof(DefaultPhonemizer), nameof(HasAuthor), nameof(HasVoice),
            nameof(HasWeb), nameof(HasVersion), nameof(HasOtherInfo), nameof(HasLocation), nameof(HasDefaultPhonemizer), nameof(HasAvatar), nameof(CanDelete)];
        foreach (string property in properties)
            this.RaisePropertyChanged(property);
    }
    private void LoadAvatar()
    {
        try
        {
            byte[]? avatarData = _singer.AvatarData;
            if (avatarData is { Length: > 0 })
            {
                using MemoryStream stream = new(avatarData);
                AvatarBitmap = new Bitmap(stream);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load singer avatar for {Singer}", _singer.Name);
        }
    }

    private string GetSingerTypeDisplay()
    {
        return Services.SingerPlugins.SingerTypePresentation.Name(_singer.SingerType);
    }

    private void OnBack()
    {
        Navigator.NavigateBack(this);
    }

    private async Task OnDeleteAsync()
    {
        if (await SingerDeletePopupService.ShowAsync(_singer))
        {
            SingerUninstalled?.Invoke(_singer);
            Navigator.NavigateBack(this);
        }
    }

    private async Task OnOpenWebAsync()
    {
        if (string.IsNullOrWhiteSpace(Web)) return;

        try
        {
            ExternalUrlLaunchResult result = await ExternalUrlService.OpenAsync(Web);
            if (!result.Succeeded) ToastService.Enqueue(L.S("About.Toast.OpenLinkFailed"));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open web URL: {Url}", Web);
            ToastService.Enqueue(L.S("About.Toast.OpenLinkFailed"));
        }
    }
}
