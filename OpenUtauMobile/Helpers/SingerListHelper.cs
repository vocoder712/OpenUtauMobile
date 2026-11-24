using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Helpers;

/// <summary>
/// 歌手列表辅助方法。
/// </summary>
public static class SingerListHelper
{
    /// <summary>
    /// 按歌手类型固定顺序展开 <see cref="SingerManager.SingerGroups"/>。
    /// <see cref="SingerManager.SingerGroups"/> 的分组顺序取决于文件系统的目录枚举顺序，
    /// 安装新歌手后目录顺序变化会让 DiffSinger 等分组跑到列表最前面，因此这里按
    /// <see cref="USingerType"/> 的数值顺序展开以保证列表稳定；分组内部保持 Core 已有的本地化排序。
    /// </summary>
    /// <returns>按类型、再按名称排序后的歌手列表。</returns>
    public static List<USinger> GetOrderedSingers()
    {
        Dictionary<USingerType, List<USinger>> groups = SingerManager.Inst.SingerGroups;
        List<USinger> singers = [];
        foreach (KeyValuePair<USingerType, List<USinger>> pair in groups.OrderBy(pair => pair.Key))
        {
            if (pair.Value == null) continue;
            foreach (USinger singer in pair.Value)
            {
                if (singer != null)
                {
                    singers.Add(singer);
                }
            }
        }

        return singers;
    }
}
