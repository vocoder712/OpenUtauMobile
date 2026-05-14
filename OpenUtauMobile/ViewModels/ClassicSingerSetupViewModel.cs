using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using DynamicData.Binding;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// Classic/Enunu/DiffSinger 歌手安装向导的 ViewModel。
/// 四步流程：Step 0 压缩包编码 → Step 1 文本编码 → Step 2 歌手类型 → Step 3 安装摘要
/// </summary>
public class ClassicSingerSetupViewModel : NavigateViewModelBase, ICmdSubscriber
{
    [Reactive] public int Step { get; set; }
    public string StepText => string.Format(L.S("SingerSetup.StepFormat"), Step + 1);

    [Reactive] public string ArchiveFilePath { get; set; } = string.Empty;

    public Encoding[] Encodings { get; set; } =
    [
        Encoding.GetEncoding("shift_jis"),
        Encoding.UTF8,
        Encoding.GetEncoding("gb2312"),
        Encoding.GetEncoding("big5"),
        Encoding.GetEncoding("ks_c_5601-1987"),
        Encoding.GetEncoding("Windows-1252"),
        Encoding.GetEncoding("macintosh")
    ];

    [Reactive] public Encoding ArchiveEncoding { get; set; }
    [Reactive] public Encoding TextEncoding { get; set; }
    [Reactive] public bool MissingInfo { get; set; }

    public string[] SingerTypes { get; set; } = ["utau", "enunu", "diffsinger"];
    [Reactive] public string SingerType { get; set; }

    public ObservableCollection<string> TextItems => _textItems;
    private readonly ObservableCollectionExtended<string> _textItems;

    // 安装进度相关
    [Reactive] public bool IsInstalling { get; set; }
    [Reactive] public double InstallProgress { get; set; }
    [Reactive] public string InstallMessage { get; set; } = string.Empty;
    [Reactive] public bool InstallSuccess { get; set; }
    [Reactive] public string ErrorMessage { get; set; } = string.Empty;
    [Reactive] public bool ShowErrorState { get; set; }
    [Reactive] public bool ShowSuccessState { get; set; }
    [Reactive] public bool ShowProgressState { get; set; }

    public ReactiveCommand<Unit, Unit> NextCommand { get; }
    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfirmInstallCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }

    public ClassicSingerSetupViewModel(MainViewModel navigator) : base(navigator)
    {
        SingerType = SingerTypes[0];
        ArchiveEncoding = Encodings[0];
        TextEncoding = Encodings[0];

        _textItems = [];

        // 初始化命令
        NextCommand = ReactiveCommand.Create(OnNext);
        BackCommand = ReactiveCommand.Create(OnBack);
        ConfirmInstallCommand = ReactiveCommand.Create(OnConfirmInstall);
        ExitCommand = ReactiveCommand.Create(OnExit);

        // 订阅 ArchiveFilePath 变化
        this.WhenAnyValue(vm => vm.Step)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(StepText)));

        this.WhenAnyValue(vm => vm.ArchiveFilePath)
            .Subscribe(_ =>
            {
                if (!string.IsNullOrEmpty(ArchiveFilePath))
                {
                    try
                    {
                        if (IsEncrypted(ArchiveFilePath))
                        {
                            ShowErrorState = true;
                            ErrorMessage = L.S("SingerSetup.Error.Encrypted");
                            return;
                        }

                        VoicebankConfig? config = LoadCharacterYaml(ArchiveFilePath);
                        MissingInfo = config == null || string.IsNullOrEmpty(config.SingerType);

                        if (!string.IsNullOrEmpty(config?.TextFileEncoding))
                        {
                            try
                            {
                                TextEncoding = Encoding.GetEncoding(config.TextFileEncoding);
                            }
                            catch
                            {
                                // 如果编码无效，保持默认
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowErrorState = true;
                        ErrorMessage = ex.Message;
                    }
                }
            });

        // 订阅 Step、ArchiveEncoding、ArchiveFilePath 的变化以刷新存档列表
        this.WhenAnyValue(vm => vm.Step, vm => vm.ArchiveEncoding, vm => vm.ArchiveFilePath)
            .Subscribe(_ => RefreshArchiveItems());

        // 订阅 Step、TextEncoding 的变化以刷新文本列表
        this.WhenAnyValue(vm => vm.Step, vm => vm.TextEncoding)
            .Subscribe(_ => RefreshTextItems());

        // 注册为 DocManager 订阅者以接收进度通知
        DocManager.Inst.AddSubscriber(this);
    }

    private void OnNext()
    {
        if (Step < 3)
        {
            Step++;
        }
    }

    private void OnBack()
    {
        if (Step > 0)
        {
            Step--;
        }
        else
        {
            Navigator.NavigateBack(this);
        }
    }

    private async void OnConfirmInstall()
    {
        await InstallAsync();
    }

    private void OnExit()
    {
        Navigator.NavigateBack(this);
    }

    private void RefreshArchiveItems()
    {
        if (Step != 0)
        {
            return;
        }

        if (string.IsNullOrEmpty(ArchiveFilePath))
        {
            _textItems.Clear();
            return;
        }

        try
        {
            ReaderOptions readerOptions = new ReaderOptions
            {
                ArchiveEncoding = new ArchiveEncoding { Forced = ArchiveEncoding },
            };
            using (IArchive archive = ArchiveFactory.Open(ArchiveFilePath, readerOptions))
            {
                _textItems.Clear();
                _textItems.AddRange(archive.Entries
                    .Select(entry => entry.Key!)
                    .ToArray());
            }
        }
        catch (Exception ex)
        {
            _textItems.Clear();
            _textItems.Add($"[错误] {ex.Message}");
        }
    }

    private void RefreshTextItems()
    {
        if (Step != 1)
        {
            return;
        }

        if (string.IsNullOrEmpty(ArchiveFilePath))
        {
            _textItems.Clear();
            return;
        }

        try
        {
            ReaderOptions readerOptions = new ReaderOptions
            {
                ArchiveEncoding = new ArchiveEncoding { Forced = ArchiveEncoding },
            };
            using (IArchive archive = ArchiveFactory.Open(ArchiveFilePath, readerOptions))
            {
                _textItems.Clear();
                foreach (IArchiveEntry entry in archive.Entries.Where(entry =>
                             entry.Key!.EndsWith("character.txt", StringComparison.OrdinalIgnoreCase) ||
                             entry.Key.EndsWith("oto.ini", StringComparison.OrdinalIgnoreCase)))
                {
                    using (Stream stream = entry.OpenEntryStream())
                    {
                        using StreamReader reader = new StreamReader(stream, TextEncoding);
                        _textItems.Add($"------ {entry.Key} ------");
                        int count = 0;
                        while (count < 256 && !reader.EndOfStream)
                        {
                            string? line = reader.ReadLine();
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                _textItems.Add(line);
                                count++;
                            }
                        }

                        if (!reader.EndOfStream)
                        {
                            _textItems.Add("...");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _textItems.Clear();
            _textItems.Add($"[错误] {ex.Message}");
        }
    }

    private bool IsEncrypted(string archiveFilePath)
    {
        try
        {
            using (IArchive archive = ArchiveFactory.Open(archiveFilePath))
            {
                return archive.Entries.Any(e => e.IsEncrypted);
            }
        }
        catch
        {
            return false;
        }
    }

    private VoicebankConfig? LoadCharacterYaml(string archiveFilePath)
    {
        try
        {
            using (IArchive archive = ArchiveFactory.Open(archiveFilePath))
            {
                IArchiveEntry? entry = archive.Entries.FirstOrDefault(e =>
                    Path.GetFileName(e.Key) == "character.yaml");
                if (entry == null)
                {
                    return null;
                }

                using (Stream stream = entry.OpenEntryStream())
                {
                    return VoicebankConfig.Load(stream);
                }
            }
        }
        catch
        {
            return null;
        }
    }

    private async Task InstallAsync()
    {
        string archiveFilePath = ArchiveFilePath;
        Encoding archiveEncoding = ArchiveEncoding;
        Encoding textEncoding = TextEncoding;
        string singerType = SingerType;

        // 进入安装状态
        IsInstalling = true;
        ShowProgressState = true;
        InstallProgress = 0;
        InstallMessage = L.S("SingerSetup.Installing");
        ShowErrorState = false;
        ShowSuccessState = false;

        try
        {
            await Task.Run(() =>
            {
                try
                {
                    string basePath = PathManager.Inst.SingersInstallPath;
                    VoicebankInstaller installer = new VoicebankInstaller(basePath, (progress, info) =>
                    {
                        // 这会发送 ProgressBarNotification，我们在 OnNext 中处理
                        DocManager.Inst.ExecuteCmd(new ProgressBarNotification(progress, info));
                    }, archiveEncoding, textEncoding);
                    installer.Install(archiveFilePath, singerType);
                }
                catch (Exception ex)
                {
                    throw new Exception(string.Format(L.S("SingerSetup.InstallFailed"), ex.Message), ex);
                }
            });

            // 安装成功
            IsInstalling = false;
            ShowProgressState = false;
            ShowSuccessState = true;
            InstallSuccess = true;
            InstallMessage = L.S("SingerSetup.InstallComplete");

            // 刷新歌手列表
            DocManager.Inst.ExecuteCmd(new SingersChangedNotification());
        }
        catch (Exception ex)
        {
            // 安装失败
            IsInstalling = false;
            ShowProgressState = false;
            ShowErrorState = true;
            ShowSuccessState = false;
            InstallSuccess = false;
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    /// ICmdSubscriber 实现 - 订阅 ProgressBarNotification
    /// </summary>
    public void OnNext(UCommand cmd, bool isUndo)
    {
        if (cmd is ProgressBarNotification progressNotif)
        {
            // 更新进度条和消息
            InstallProgress = progressNotif.Progress;
            InstallMessage = progressNotif.Info;
        }
    }

    public override void OnNavigatedTo()
    {
        // 初始化时清空错误状态
        ShowErrorState = false;
        ErrorMessage = string.Empty;
    }
}