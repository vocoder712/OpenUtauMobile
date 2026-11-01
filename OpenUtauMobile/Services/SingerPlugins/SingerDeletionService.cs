using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Plugin.Abstractions;
using Serilog;

namespace OpenUtauMobile.Services.SingerPlugins
{
    /// <summary>提供方控制删除范围，宿主只校验归属并协调发现刷新。</summary>
    public static class SingerDeletionService
    {
        /// <summary>按实例或稳定身份及位置找到当前条目。</summary>
        public static SingerCandidate? Find(USinger singer) => SingerDiscoveryService.Instance.Candidates.FirstOrDefault(item =>
            ReferenceEquals(item.Entry.Singer, singer) || (item.Entry.Singer?.Id == singer.Id
                && item.Entry.Singer?.SingerType == singer.SingerType && item.Entry.Location == singer.Location));

        /// <summary>取得刷新后的同一位置，不能把重复 ID 的另一个位置当作删除目标。</summary>
        public static SingerCandidate? Current(SingerCandidate candidate) => SingerDiscoveryService.Instance.Candidates.FirstOrDefault(item =>
            item.ProviderId == candidate.ProviderId && item.Entry.Location == candidate.Entry.Location);

        /// <summary>绑定阶段隔离提供方错误，不因能力查询失败使详情页面崩溃。</summary>
        public static bool CanDelete(SingerCandidate candidate)
        {
            try
            {
                return SingerPluginService.Registry.Snapshot.Providers.TryGetValue(candidate.ProviderId, out RegisteredSingerProvider? provider)
                    && provider.Instance.CanDelete(candidate.Entry);
            }
            catch (Exception exception) { Log.Warning(exception, "Failed to query singer deletion capability {Provider}", candidate.ProviderId); return false; }
        }

        /// <summary>执行前重新查询条目和权限，成功、失败或取消后均刷新目录。</summary>
        public static async Task DeleteAsync(SingerCandidate candidate, CancellationToken cancellationToken = default)
        {
            SingerCandidate current = Current(candidate) ?? throw new InvalidOperationException("歌手条目已改变，请刷新列表。");
            if (!CanDelete(current) || !SingerPluginService.Registry.Snapshot.Providers.TryGetValue(current.ProviderId, out RegisteredSingerProvider? provider))
                throw new InvalidOperationException("此歌手不允许删除。");
            await SingerDiscoveryService.Instance.MaintainAsync(() => Task.Run(async () =>
                await provider.Instance.DeleteAsync(current.Entry, new SingerDeletionContext(SingerPluginRuntime.Roots(),
                    new SingerPluginRuntime.PluginLog(provider.PluginId)), cancellationToken), cancellationToken));
        }
    }
}
