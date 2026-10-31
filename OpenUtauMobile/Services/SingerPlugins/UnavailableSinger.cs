using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Services.SingerPlugins
{
    /// <summary>保留无法解析歌手的工程身份，保存工程时不会丢失原 ID。</summary>
    internal sealed class UnavailableSinger(string id, string name, USingerType type) : USinger
    {
        public override string Id => id;
        public override string Name => name;
        public override USingerType SingerType => type;

        /// <summary>修正 Core 缺失占位对象的空身份，不替换任何已找到歌手。</summary>
        internal static void PreserveReferences(UProject project)
        {
            foreach (UTrack track in project.tracks)
                if (track.Singer is { Found: false, Id: null } singer)
                {
                    string? identity = track.singer ?? singer.Name;
                    if (!string.IsNullOrWhiteSpace(identity)) track.Singer = new UnavailableSinger(identity, singer.Name, singer.SingerType);
                }
        }
    }
}