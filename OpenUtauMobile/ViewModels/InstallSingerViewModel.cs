using CommunityToolkit.Mvvm.Messaging;
using DynamicData.Binding;
using OpenUtau.Classic;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using SharpCompress.Archives;
using OpenUtauMobile.ViewModels.Messages;
using OpenUtauMobile.Resources.Strings;
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
        [Reactive] public string InstallSize { get; set; } = AppResources.Unknown; // 安装包大小

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

        [Reactive] public string InstallProgressText { get; set; } = AppResources.Progress; // 安装进度
        [Reactive] public string InstallProgressDetail { get; set; } = ""; // 安装进度消息
        [Reactive] public double InstallProgress { get; set; } = 0.0; // 安装进度, 0.0-1.0



        public void Init()
        {
            Busy = true;
            VoicebankConfig = LoadCharacterYaml(InstallPackagePath); // 从压缩包解出character.yaml以获取歌手信息
            //Debug.WriteLine($"Name: {VoicebankConfig?.Name}");
            MissingInfo = string.IsNullOrEmpty(VoicebankConfig?.SingerType); // 判断是否缺少信息
            RefreshArchiveItems(); // 刷新压缩包编码样本
            RefreshTextItems(); // 刷新文本编码样本
            using var archive = ArchiveFactory.Open(InstallPackagePath);
            long totalUncompressSize = archive.Entries // 计算解压后总大小
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
            ArchiveEntryItems.Clear();
            if (string.IsNullOrEmpty(InstallPackagePath)) // 路径为空
            {
                Busy = false; // 空闲
                return;
            }
            try
            {
                ReaderOptions readerOptions = new()
                {
                    ArchiveEncoding = new ArchiveEncoding { Forced = ArchiveEncoding }, // 使用指定的编码方式
                };
                using IArchive archive = ArchiveFactory.Open(InstallPackagePath, readerOptions);

                // 只取前50个有效条目作为样本，避免加载所有文件
                const int MaxSampleSize = 50;
                int count = 0;

                foreach (IArchiveEntry entry in archive.Entries)
                {
                    if (entry.Key != null)
                    {
                        ArchiveEntryItems.Add(entry.Key);
                        count++;

                        if (count >= MaxSampleSize)
                        {
                            break;
                        }
                    }
                }

            // 如果文件数量超过样本大小，添加提示信息
            if (count >= MaxSampleSize)
            {
                ArchiveEntryItems.Add(string.Format(AppResources.ShowingFirstNEntries, MaxSampleSize));
            }

                Busy = false; // 空闲
            }
            catch (Exception ex)
            {
                Busy = false; // 空闲
                Log.Error(ex, "刷新压缩包编码样本时出错");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }

        /// <summary>
        /// 刷新文本编码样本
        /// </summary>
        public void RefreshTextItems()
        {
            Busy = true; // 忙碌
            TextItems.Clear();
            if (string.IsNullOrEmpty(InstallPackagePath)) // 路径为空
            {
                Busy = false; // 空闲
                return;
            }
            try
            {
                ReaderOptions readerOptions = new()
                {
                    ArchiveEncoding = new ArchiveEncoding { Forced = ArchiveEncoding }, // 使用指定的编码方式
                };
                using IArchive archive = ArchiveFactory.Open(InstallPackagePath, readerOptions);

                // 限制读取的文本文件数量
                const int MaxTextFiles = 5; // 最多读取5个文本文件作为样本
                int processedFiles = 0;

                foreach (IArchiveEntry entry in archive.Entries)
                {
                    // 达到最大文件数后停止
                    if (processedFiles >= MaxTextFiles)
                    {
                        TextItems.Add(string.Format(AppResources.ShowingFirstNTextFiles, MaxTextFiles));
                        break;
                    }
                    if (entry.Key == null)
                    {
                        continue;
                    }
                    // 后缀筛选
                    if (!entry.Key.EndsWith("character.txt") && !entry.Key.EndsWith("oto.ini"))
                    {
                        continue;
                    }
                    using (Stream stream = entry.OpenEntryStream())
                    {
                        using var reader = new StreamReader(stream, TextEncoding);
                        TextItems.Add($"------ {entry.Key} ------");
                        int count = 0;
                        const int MaxLinesPerFile = 64; // 每个文件读取的行数

                        while (count < MaxLinesPerFile && !reader.EndOfStream)
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
                    processedFiles++;
                }
                Busy = false; // 空闲
            }
            catch (Exception ex)
            {
                Busy = false; // 空闲
                Log.Error(ex, "刷新文本编码样本时出错");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
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
            InstallProgressText = $"{AppResources.Progress}{progress:0.##}%";
            InstallProgressDetail = detail;
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
