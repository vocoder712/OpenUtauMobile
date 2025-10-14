using CommunityToolkit.Mvvm.Messaging;
using DynamicData.Binding;
using OpenUtau.Classic;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SharpCompress.Archives;
using OpenUtauMobile.ViewModels.Messages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using SharpCompress.Common;
using SharpCompress.Readers;
using OpenUtau.Core;
using Serilog;
using ExCSS;
using System.Diagnostics;

namespace OpenUtauMobile.ViewModels
{
    public class InstallSingerViewModel : ReactiveObject
    {
        [Reactive] public bool Busy { get; set; } = true;
        [Reactive] public bool MissingInfo { get; set; } = true;

        [Reactive] public string InstallPackagePath { get; set; } = string.Empty;
        [Reactive] public VoicebankConfig? VoicebankConfig { get; set; } = new VoicebankConfig(); // 声库配置
        [Reactive] public string SingerType { get; set; } = "diffsinger"; // 歌手类型，默认为diffsinger
        public List<string> SingerTypes { get; set; } = new List<string> { "diffsinger", "utau", "enunu" }; // 支持的歌手类型
        [Reactive] public string InstallPath { get; set; } = PathManager.Inst.SingersInstallPath; // 安装路径
        [Reactive] public string InstallSize { get; set; } = "未知"; // 安装包大小

        public Encoding[] Encodings { get; set; } = new Encoding[] { // 可能的编码
            Encoding.GetEncoding("shift_jis"),
            Encoding.UTF8,
            Encoding.GetEncoding("gb2312"),
            Encoding.GetEncoding("big5"),
            Encoding.GetEncoding("ks_c_5601-1987"),
            Encoding.GetEncoding("Windows-1252"),
            Encoding.GetEncoding("macintosh"),
        };
        [Reactive] public Encoding ArchiveEncoding { get; set; } = Encoding.UTF8; // 压缩包编码方式，默认UTF-8
        [Reactive] public Encoding TextEncoding { get; set; } = Encoding.UTF8; // 文本编码方式，默认UTF-8
        [Reactive] public ObservableCollectionExtended<string> ArchiveEntryItems { get; set; } = []; // 压缩包条目样本
        [Reactive] public ObservableCollectionExtended<string> TextItems { get; set; } = []; // 文本样本

        [Reactive] public string InstallProgressText { get; set; } = "进度："; // 安装进度
        [Reactive] public string InstallProgressDetail { get; set; } = ""; // 安装进度消息
        [Reactive] public double InstallProgress { get; set; } = 0.0; // 安装进度, 0.0-1.0



        public void Init()
        {
            Busy = true;
            VoicebankConfig = LoadCharacterYaml(InstallPackagePath); // 从压缩包解出character.yaml以获取歌手信息
            Debug.WriteLine($"Name: {VoicebankConfig?.Name}");
            MissingInfo = string.IsNullOrEmpty(VoicebankConfig?.SingerType); // 判断是否缺少信息
            RefreshArchiveItems(); // 刷新压缩包编码样本
            RefreshTextItems(); // 刷新文本编码样本
            using var archive = ArchiveFactory.Open(InstallPackagePath);
            long totalUncompressSize = archive.Entries
                .Where(entry => !entry.IsDirectory)
                .Sum(entry => entry.Size);
            InstallSize = Utils.FormatTools.FormatSize(totalUncompressSize);
            Log.Information($"准备安装声库。安装包路径：{InstallPackagePath}");
            Busy = false;
        }

        private static VoicebankConfig? LoadCharacterYaml(string installPackagePath)
        {
            using (var archive = ArchiveFactory.Open(installPackagePath))
            {
                var entry = archive.Entries.FirstOrDefault(e => Path.GetFileName(e.Key) == "character.yaml");
                if (entry == null)
                {
                    return null;
                }
                //打开文件流，并获取配置信息
                using (var stream = entry.OpenEntryStream())
                {
                    return VoicebankConfig.Load(stream);
                }
            }
        }

        /// <summary>
        /// 刷新压缩包编码样本
        /// </summary>
        public void RefreshArchiveItems()
        {
            Busy = true; // 忙碌
            if (string.IsNullOrEmpty(InstallPackagePath))
            {
                ArchiveEntryItems.Clear();
                Busy = false; // 空闲
                return;
            }
            var readerOptions = new ReaderOptions
            {
                ArchiveEncoding = new ArchiveEncoding { Forced = ArchiveEncoding },
            };
            using (var archive = ArchiveFactory.Open(InstallPackagePath, readerOptions))
            {
                ArchiveEntryItems.Clear();
                ArchiveEntryItems.AddRange(
                    archive.Entries
                        .Select(entry => entry.Key)
                        .Where(key => key != null)
                        .Select(key => key!)
                        .ToArray()
                );
                Busy = false; // 空闲
            }
        }

        /// <summary>
        /// 刷新文本编码样本
        /// </summary>
        public void RefreshTextItems()
        {
            Busy = true; // 忙碌
            if (string.IsNullOrEmpty(InstallPackagePath))
            {
                TextItems.Clear();
                Busy = false; // 空闲
                return;
            }
            var readerOptions = new ReaderOptions
            {
                ArchiveEncoding = new ArchiveEncoding { Forced = ArchiveEncoding },
            };
            using (var archive = ArchiveFactory.Open(InstallPackagePath, readerOptions))
            {
                try
                {
                    TextItems.Clear();
                    foreach (var entry in archive.Entries
                        .Where(entry => entry.Key != null && (entry.Key.EndsWith("character.txt") || entry.Key.EndsWith("oto.ini"))))
                    {
                        using (var stream = entry.OpenEntryStream())
                        {
                            using var reader = new StreamReader(stream, TextEncoding);
                            TextItems.Add($"------ {entry.Key} ------");
                            int count = 0;
                            while (count < 256 && !reader.EndOfStream)
                            {
                                string? line = reader.ReadLine();
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    TextItems.Add(line);
                                    count++;
                                }
                            }
                            if (!reader.EndOfStream)
                            {
                                TextItems.Add($"...");
                            }
                        }
                    }
                    Busy = false; // 空闲
                }
                catch (Exception ex)
                {
                    Busy = false; // 空闲
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
                }
            }
        }

        /// <summary>
        /// 更新安装进度信息，作为回调委托给安装器
        /// </summary>
        /// <param name="progress"></param>
        /// <param name="detail"></param>
        public void UpdateInstallProgress(double progress, string detail)
        {
            InstallProgress = progress / 100;
            InstallProgressText = $"进度：{progress:0.##}%";
            InstallProgressDetail = $"解压条目：{detail}";
        }

        public void Install()
        {
            //初始化安装器
            var installer = new VoicebankInstaller(InstallPath, 
                //产生进度条通知，将一个可以发送进度通知的命令执行器传递给安装器，以便安装器更新进度条
                UpdateInstallProgress, ArchiveEncoding, TextEncoding);

            //开始安装
            installer.Install(InstallPackagePath, SingerType);
        }
    }
}
