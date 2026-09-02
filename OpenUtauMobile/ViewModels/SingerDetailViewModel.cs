using System;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels;

public class SingerDetailViewModel : NavigateViewModelBase
{
    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenWebCommand { get; }

    private readonly USinger _singer;

    // ── Basic Info ──
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

        BackCommand = ReactiveCommand.Create(OnBack);
        DeleteCommand = ReactiveCommand.CreateFromTask(OnDeleteAsync);

        // Create OpenWebCommand - enabled only when HasWeb is true
        IObservable<bool> canOpenWeb = this.WhenAnyValue(x => x.HasWeb);
        OpenWebCommand = ReactiveCommand.Create(OnOpenWeb, canOpenWeb);

        IsFavorite = _singer.IsFavourite;

        // Load avatar
        LoadAvatar();

        // Sync favorite state back to singer
        this.WhenAnyValue(x => x.IsFavorite)
            .Subscribe(fav => _singer.IsFavourite = fav);
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
        return _singer.SingerType switch
        {
            USingerType.Classic => "UTAU",
            USingerType.Enunu => "ENUNU",
            USingerType.DiffSinger => "DiffSinger",
            USingerType.Voicevox => "VOICEVOX",
            USingerType.Vogen => "Vogen",
            USingerType.Neutrino => "NEUTRINO v3",
            _ => "Unknown"
        };
    }

    private void OnBack()
    {
        Navigator.NavigateBack(this);
    }

    private async Task OnDeleteAsync()
    {
        try
        {
            await LoadingPopupService.RunAsync(
                L.S("SingerDetail.Uninstalling"),
                _ => Task.Run(() => SingerManager.Inst.UninstallSinger(_singer)));
            ToastService.Enqueue(string.Format(
                L.S("SingerDetail.UninstallSucceeded"),
                SingerName));
            Navigator.NavigateBack(this);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to uninstall singer {Singer}", _singer.Name);
            ErrorMessageNotification notification = new(
                string.Format(L.S("SingerDetail.UninstallFailed"), SingerName),
                exception);
            ErrorDialogService.Show(new ErrorDialogViewModel(notification));
        }
    }

    private void OnOpenWeb()
    {
        if (string.IsNullOrWhiteSpace(Web)) return;

        try
        {
            // TODO: 实现一个全局的URI跳转服务
            ToastService.Enqueue("TODO: 打开链接");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open web URL: {Url}", Web);
        }
    }
}
