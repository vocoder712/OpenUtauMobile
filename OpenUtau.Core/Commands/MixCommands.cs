using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core
{
    /// <summary>轨道混音命令；不向工程添加总线参数。</summary>
    public abstract class MixCommand : UCommand
    {
        public UProject Project { get; }
        public UTrack Track { get; }
        public override bool Silent => true; // 避免刷日志
        public virtual bool HasChanges => true;
        public override ValidateOptions ValidateOptions => new() { SkipTiming = true, SkipPhonemizer = true, SkipPhoneme = true };

        protected MixCommand(UProject project, UTrack track)
        {
            Project = project;
            Track = track ?? throw new ArgumentNullException(nameof(track));
        }

        protected void UpdateMuteStates()
        {
            foreach (UTrack track in Project.tracks) track.Muted = !track.Solo && (track.Mute || Project.SoloTrackExist);
        }

        protected bool SameTarget(UCommand command) => command.GetType() == GetType()
            && command is MixCommand other && other.Project == Project && other.Track == Track;
    }

    /// <summary>同类参数在一次拖动中只保留初值与最终值，布尔参数不通过数值编码。</summary>
    public abstract class MixValueCommand<T> : MixCommand
    {
        private readonly T before;
        private readonly T after;
        public override bool HasChanges => !EqualityComparer<T>.Default.Equals(before, after);

        protected MixValueCommand(UProject project, UTrack track, T before, T after) : base(project, track)
        {
            this.before = before;
            this.after = after;
        }

        protected abstract void SetValue(T value);
        protected abstract MixValueCommand<T> WithValues(T before, T after);
        public override void Execute() => SetValue(after);
        public override void Unexecute() => SetValue(before);
        public override bool CanMerge(IList<UCommand> commands) => commands.Count > 0 && commands.All(SameTarget);
        public override UCommand Merge(IList<UCommand> commands) => WithValues(((MixValueCommand<T>)commands[0]).before, after);
    }

    public sealed class ChangeMixVolumeCommand : MixValueCommand<double>
    {
        public ChangeMixVolumeCommand(UProject project, UTrack track, double value)
            : this(project, track, track.Volume, value) { }
        private ChangeMixVolumeCommand(UProject project, UTrack track, double before, double after)
            : base(project, track, before, after)
        {
            if (!double.IsFinite(after)) throw new ArgumentOutOfRangeException(nameof(after));
        }
        protected override void SetValue(double value)
        {
            Track.Volume = value;
        }
        protected override MixValueCommand<double> WithValues(double before, double after) => new ChangeMixVolumeCommand(Project, Track, before, after);
        public override string ToString() => "调整混音音量";
    }

    public sealed class ChangeMixPanCommand : MixValueCommand<double>
    {
        public ChangeMixPanCommand(UProject project, UTrack track, double value)
            : this(project, track, track.Pan, value) { }
        private ChangeMixPanCommand(UProject project, UTrack track, double before, double after)
            : base(project, track, before, after)
        {
            if (!double.IsFinite(after)) throw new ArgumentOutOfRangeException(nameof(after));
        }
        protected override void SetValue(double value)
        {
            Track.Pan = value;
        }
        protected override MixValueCommand<double> WithValues(double before, double after) => new ChangeMixPanCommand(Project, Track, before, after);
        public override string ToString() => "调整混音声像";
    }

    public sealed class ChangeMixMuteCommand : MixValueCommand<bool>
    {
        public ChangeMixMuteCommand(UProject project, UTrack track, bool value)
            : this(project, track, track.Mute, value) { }
        private ChangeMixMuteCommand(UProject project, UTrack track, bool before, bool after)
            : base(project, track, before, after) { }
        protected override void SetValue(bool value)
        {
            Track.Mute = value;
            UpdateMuteStates();
        }
        protected override MixValueCommand<bool> WithValues(bool before, bool after) => new ChangeMixMuteCommand(Project, Track, before, after);
        public override string ToString() => "切换混音静音";
    }

    public sealed class ChangeMixSoloCommand : MixValueCommand<bool>
    {
        public ChangeMixSoloCommand(UProject project, UTrack track, bool value)
            : this(project, track, track.Solo, value) { }
        private ChangeMixSoloCommand(UProject project, UTrack track, bool before, bool after)
            : base(project, track, before, after) { }
        protected override void SetValue(bool value)
        {
            Track!.Solo = value;
            UpdateMuteStates();
        }
        protected override MixValueCommand<bool> WithValues(bool before, bool after) => new ChangeMixSoloCommand(Project, Track!, before, after);
        public override string ToString() => "切换轨道独奏";
    }

    /// <summary>效果器配置使用独立快照，撤销首次编辑时能够还原空配置。</summary>
    public sealed class ChangeMixFxCommand : MixCommand
    {
        private readonly UMixFx? before;
        private readonly UMixFx? after;
        public ChangeMixFxCommand(UProject project, UTrack track, UMixFx? value)
            : this(project, track, track.MixFx, value) { }
        private ChangeMixFxCommand(UProject project, UTrack track, UMixFx? before, UMixFx? after) : base(project, track)
        {
            this.before = before?.Clone();
            this.after = after?.Clone();
        }
        private void SetValue(UMixFx? value)
        {
            Track.MixFx = value?.Clone();
        }
        public override void Execute() => SetValue(after);
        public override void Unexecute() => SetValue(before);
        public override bool CanMerge(IList<UCommand> commands) => commands.Count > 0 && commands.All(SameTarget);
        public override UCommand Merge(IList<UCommand> commands) => new ChangeMixFxCommand(Project, Track, ((ChangeMixFxCommand)commands[0]).before, after);
        public override string ToString() => "调整混音效果器";
    }
}
