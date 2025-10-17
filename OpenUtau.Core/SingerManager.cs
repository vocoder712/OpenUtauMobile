using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core {
    public class SingerManager : SingletonBase<SingerManager> {
        public Dictionary<string, USinger> Singers { get; private set; } = new Dictionary<string, USinger>();
        public Dictionary<USingerType, List<USinger>> SingerGroups { get; private set; } = new Dictionary<USingerType, List<USinger>>();

        private readonly ConcurrentQueue<USinger> reloadQueue = new ConcurrentQueue<USinger>();
        private CancellationTokenSource reloadCancellation;

        private HashSet<USinger> singersUsed = new HashSet<USinger>();

        public void Initialize() {
            SearchAllSingers();
        }

        public void SearchAllSingers() {
            Log.Information("Searching singers.");
            Directory.CreateDirectory(PathManager.Inst.SingersPath);
            var stopWatch = Stopwatch.StartNew();
            var singers = ClassicSingerLoader.FindAllSingers()
                .Concat(Vogen.VogenSingerLoader.FindAllSingers())
                .Distinct();
            Singers = singers
                .ToLookup(s => s.Id)
                .ToDictionary(g => g.Key, g => g.First());
            SingerGroups = singers
                .GroupBy(s => s.SingerType)
                .ToDictionary(s => s.Key, s => s.LocalizedOrderBy(singer => singer.LocalizedName).ToList());
            stopWatch.Stop();
            Log.Information($"Search all singers: {stopWatch.Elapsed}");
        }

        public USinger GetSinger(string name) {
            Log.Information($"Attach singer to track: {name}");
            name = name.Replace("%VOICE%", "");
            if (Singers.ContainsKey(name)) {
                return Singers[name];
            }
            return null;
        }

        public void ScheduleReload(USinger singer) {
            reloadQueue.Enqueue(singer);
            ScheduleReload();
        }

        private void ScheduleReload() {
            var newCancellation = new CancellationTokenSource();
            var oldCancellation = Interlocked.Exchange(ref reloadCancellation, newCancellation);
            if (oldCancellation != null) {
                oldCancellation.Cancel();
                oldCancellation.Dispose();
            }
            Task.Run(() => {
                Thread.Sleep(200);
                if (newCancellation.IsCancellationRequested) {
                    return;
                }
                Refresh();
            });
        }

        private void Refresh() {
            var singers = new HashSet<USinger>();
            while (reloadQueue.TryDequeue(out USinger singer)) {
                singers.Add(singer);
            }
            foreach (var singer in singers) {
                Log.Information($"Reloading {singer.Id}");
                new Task(() => {
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Reloading {singer.Id}"));
                }).Start(DocManager.Inst.MainScheduler);
                int retries = 5;
                while (retries > 0) {
                    retries--;
                    try {
                        singer.Reload();
                        break;
                    } catch (Exception e) {
                        if (retries == 0) {
                            Log.Error(e, $"Failed to reload {singer.Id}");
                        } else {
                            Log.Error(e, $"Retrying reload {singer.Id}");
                            Thread.Sleep(200);
                        }
                    }
                }
                Log.Information($"Reloaded {singer.Id}");
                new Task(() => {
                    DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Reloaded {singer.Id}"));
                    DocManager.Inst.ExecuteCmd(new OtoChangedNotification(external: true));
                }).Start(DocManager.Inst.MainScheduler);
            }
        }

        //Check which singers are in use and free memory for those that are not
        public void ReleaseSingersNotInUse(UProject project) {
            //Check which singers are in use
            var singersInUse = new HashSet<USinger>();
            foreach (var track in project.tracks) {
                var singer = track.Singer;
                if (singer != null && singer.Found && !singersInUse.Contains(singer)) {
                    singersInUse.Add(singer);
                }
            }
            //Release singers that are no longer in use
            foreach (var singer in singersUsed) {
                if (!singersInUse.Contains(singer)) {
                    singer.FreeMemory();
                }
            }
            //Update singers used
            singersUsed = singersInUse;
        }

        /// <summary>
        /// 卸载指定歌手
        /// </summary>
        /// <param name="singer">要卸载的歌手</param>
        /// <returns>是否成功卸载</returns>
        public async Task<bool> UninstallSingerAsync(USinger singer)
        {
            if (singer == null || !Singers.ContainsKey(singer.Id))
            {
                return false;
            }

            // 保存状态用于回滚
            bool wasInSingers = Singers.ContainsKey(singer.Id);
            bool wasInGroup = SingerGroups.TryGetValue(singer.SingerType, out var group) && group.Contains(singer);
            bool wasInUsed = singersUsed.Contains(singer);

            // 回滚标识
            bool canRollback = true; // 当进入到删除文件资源阶段后，设置为false，不得回滚

            try
            {
                // 1。 先修改内存状态
                Singers.Remove(singer.Id); // 删歌手字典
                if (SingerGroups.TryGetValue(singer.SingerType, out var singerGroup)) // 删分组
                {
                    singerGroup.Remove(singer);
                    if (singerGroup.Count == 0)
                    {
                        SingerGroups.Remove(singer.SingerType);
                    }
                }
                singersUsed.Remove(singer); // 从最近使用列表中移除
                singer.FreeMemory(); // 释放内存

                // 2. 再删除文件系统资源
                canRollback = false;
                await Task.Run(() =>
                {
                    if (File.Exists(singer.Location)) // vogen单文件声库
                    {
                        File.Delete(singer.Location);
                        Log.Information($"已删除声库文件: {singer.Location}");
                    }
                    else if (Directory.Exists(singer.Location)) // 传统文件夹声库
                    {
                        Directory.Delete(singer.Location, recursive: true);
                        Log.Information($"已删除声库目录: {singer.Location}");
                    }
                    else
                    {
                        Log.Warning($"声库位置不存在: {singer.Location}");
                    }
                });

                Log.Information($"已卸载声库 {singer.Id}");
                return true;
            }
            catch (Exception e)
            {
                Log.Error(e, $"未能卸载声库 {singer.Id}");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification($"未能卸载声库 {singer.Id}", e));
                if (!canRollback)
                {
                    Log.Warning("声库文件删除失败，无法回滚内存状态");
                    return false;
                }

                // 回滚内存状态
                if (wasInSingers && !Singers.ContainsKey(singer.Id))
                {
                    Singers[singer.Id] = singer;
                }
                if (wasInGroup && SingerGroups.TryGetValue(singer.SingerType, out var singerGroup))
                {
                    if (!singerGroup.Contains(singer))
                    {
                        singerGroup.Add(singer);
                    }
                }
                else if (wasInGroup && !SingerGroups.ContainsKey(singer.SingerType))
                {
                    SingerGroups[singer.SingerType] = new List<USinger> { singer };
                }
                if (wasInUsed && !singersUsed.Contains(singer))
                {
                    singersUsed.Add(singer);
                }

                Log.Information($"已回滚声库 {singer.Id} 的删除操作");

                return false;
            }
        }
    }
}
