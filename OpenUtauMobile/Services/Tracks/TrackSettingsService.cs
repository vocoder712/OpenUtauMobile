using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services.Tracks;

public sealed class TrackSettingsService
{
    public static TrackSettingsService Inst { get; } = new();
    private bool sessionOpen;

    private TrackSettingsService() { }

    public async Task ShowAsync(UTrack track)
    {
        if (sessionOpen || TrackPreparationService.Inst.IsBusy || DocManager.Inst.HasOpenUndoGroup || !DocManager.Inst.Project.tracks.Contains(track)) return;
        sessionOpen = true;
        TrackSettingsPopupViewModel? sessionViewModel = null;
        try
        {
            TrackSettingsPopupViewModel viewModel = sessionViewModel = new(DocManager.Inst.Project, track);
            while (true)
            {
                if (viewModel.IsStale) return;
                TrackSettingsAction action = await PopupService.Show<TrackSettingsAction>(new TrackSettingsPopup(), viewModel);
                switch (action)
                {
                    case TrackSettingsAction.PickSinger:
                    {
                        USinger? singer = await TrackHeaderService.Inst.PickSingerAsync(viewModel.DraftSinger);
                        if (singer != null)
                        {
                            try
                            {
                                await TrackPreparationService.Inst.RunAsync(async loading =>
                                {
                                    Phonemizer? prepared = await TrackPreparationService.PrepareSingerAsync(singer, loading);
                                    if (!viewModel.IsStale && TrackPreparationOwner.IsAvailable(singer)) viewModel.SetSinger(singer, prepared);
                                });
                            }
                            catch (Exception exception)
                            {
                                Serilog.Log.Error(exception, "Failed to prepare track settings singer");
                                viewModel.Error = exception.Message;
                            }
                        }
                        break;
                    }
                    case TrackSettingsAction.PickPhonemizer:
                    {
                        Phonemizer? phonemizer = await TrackHeaderService.Inst.PickPhonemizerAsync(viewModel.DraftPhonemizerIdentifier);
                        if (phonemizer != null) viewModel.SetPhonemizer(phonemizer);
                        break;
                    }
                    case TrackSettingsAction.PickRenderer:
                    {
                        string? renderer = await TrackHeaderService.Inst.PickRendererNameAsync([.. viewModel.Renderers]);
                        if (renderer != null)
                            await TrackPreparationService.Inst.RunAsync(async loading =>
                            {
                                loading.SetIndeterminate(L.S("TrackPreparation.Applying"));
                                await Task.Run(() => OpenUtau.Core.Render.Renderers.GetOrCreate(renderer));
                                if (!viewModel.IsStale) viewModel.SetRenderer(renderer);
                            });
                        break;
                    }
                    case TrackSettingsAction.EditExpressions:
                    {
                        ExpressionsPopupViewModel expressions = new(viewModel.Project, viewModel.Track, true,
                            viewModel.DraftTrackExpressions);
                        TrackExpressionSettingsDraft? result = await PopupService.Show<TrackExpressionSettingsDraft>(
                            new ExpressionsPopup(), expressions);
                        if (result != null) viewModel.SetExpressions(result);
                        break;
                    }
                    case TrackSettingsAction.Apply:
                    {
                        bool singerChanged = false;
                        bool applied = false;
                        try
                        {
                            await TrackPreparationService.Inst.RunAsync(async loading =>
                            {
                                await viewModel.PrepareApplyAsync();
                                loading.SetIndeterminate(L.S("TrackPreparation.Applying"));
                                applied = TryApply(viewModel, out singerChanged);
                            });
                        }
                        catch (Exception exception)
                        {
                            Serilog.Log.Error(exception, "Failed to prepare mobile track settings");
                            viewModel.Error = exception.Message;
                        }
                        if (applied)
                        {
                            if (singerChanged)
                            {
                                try { await VoiceColorMappingService.ValidateAsync(viewModel.Project, viewModel.Track, true); }
                                catch (Exception exception) { Serilog.Log.Error(exception, "Failed to remap track voice colors after applying track settings"); }
                            }
                            return;
                        }
                        break;
                    }
                    default:
                        return;
                }
            }
        }
        finally
        {
            sessionViewModel?.Dispose();
            sessionOpen = false;
        }
    }

    private static bool TryApply(TrackSettingsPopupViewModel viewModel, out bool singerChanged)
    {
        singerChanged = false;
        List<UCommand> commands;
        string error;
        try
        {
            if (!viewModel.TryPrepareApply(out commands, out singerChanged, out error))
            {
                viewModel.Error = error;
                return false;
            }
        }
        catch (Exception exception)
        {
            Serilog.Log.Error(exception, "Failed to prepare mobile track settings");
            viewModel.Error = string.IsNullOrWhiteSpace(exception.Message)
                ? L.S("TrackSettings.Error.Apply") : exception.Message;
            return false;
        }
        if (commands.Count == 0) return true;
        DocManager manager = DocManager.Inst;
        Stopwatch commit = Stopwatch.StartNew();
        bool ownsUndoGroup = false;
        try
        {
            // 复用 Mobile 的帧合并校验；避免全量校验在 UI 释放刚换下的歌手，令撤销时再次同步加载。
            manager.StartUndoGroup("TrackSettings.Apply");
            ownsUndoGroup = true;
            foreach (UCommand command in commands) manager.ExecuteCmd(command);
            // 使用最终轨道设置校验图引用，并与设置修改一起撤销。
            TrackExpressionGraphSelection.Revalidate(viewModel.Project, viewModel.Track);
            manager.EndUndoGroup();
            ownsUndoGroup = false;
        }
        catch (Exception exception)
        {
            if (ownsUndoGroup && manager.HasOpenUndoGroup)
            {
                try { manager.RollBackUndoGroup(); }
                finally
                {
                    if (manager.HasOpenUndoGroup) manager.EndUndoGroup();
                }
            }
            Serilog.Log.Error(exception, "Failed to apply mobile track settings");
            viewModel.Error = string.IsNullOrWhiteSpace(exception.Message)
                ? L.S("TrackSettings.Error.Apply") : exception.Message;
            return false;
        }
        Serilog.Log.Information("轨道提交 Settings：{Milliseconds:F2} ms", commit.Elapsed.TotalMilliseconds);
        try
        {
            SavePreferences(viewModel, singerChanged,
                viewModel.Track.Phonemizer.GetType() != viewModel.OriginalPhonemizerType);
        }
        catch (Exception exception)
        {
            Serilog.Log.Error(exception, "Failed to save preferences after applying mobile track settings");
        }
        return true;
    }

    private static void SavePreferences(TrackSettingsPopupViewModel viewModel, bool singerChanged, bool phonemizerChanged)
    {
        bool changed = false;
        if (singerChanged && !string.IsNullOrEmpty(viewModel.Track.Singer?.Id) && viewModel.Track.Singer.Found &&
            (Preferences.Default.RecentSingers.FirstOrDefault() != viewModel.Track.Singer.Id || Preferences.Default.RecentSingers.Count > 16))
        {
            Preferences.Default.RecentSingers.Remove(viewModel.Track.Singer.Id);
            Preferences.Default.RecentSingers.Insert(0, viewModel.Track.Singer.Id);
            if (Preferences.Default.RecentSingers.Count > 16)
                Preferences.Default.RecentSingers.RemoveRange(16, Preferences.Default.RecentSingers.Count - 16);
            changed = true;
        }
        string? phonemizerName = viewModel.Track.Phonemizer.GetType().FullName;
        if ((singerChanged || phonemizerChanged) && !string.IsNullOrEmpty(viewModel.Track.Singer?.Id) && !string.IsNullOrEmpty(phonemizerName) &&
            Preferences.Default.SingerPhonemizers.GetValueOrDefault(viewModel.Track.Singer.Id) != phonemizerName)
        {
            Preferences.Default.SingerPhonemizers[viewModel.Track.Singer.Id] = phonemizerName;
            changed = true;
        }
        if (phonemizerChanged && !string.IsNullOrEmpty(phonemizerName) &&
            (Preferences.Default.RecentPhonemizers.FirstOrDefault() != phonemizerName || Preferences.Default.RecentPhonemizers.Count > 10))
        {
            Preferences.Default.RecentPhonemizers.Remove(phonemizerName);
            Preferences.Default.RecentPhonemizers.Insert(0, phonemizerName);
            if (Preferences.Default.RecentPhonemizers.Count > 10)
                Preferences.Default.RecentPhonemizers.RemoveRange(10, Preferences.Default.RecentPhonemizers.Count - 10);
            changed = true;
        }
        if (changed) Preferences.Save();
    }
}
