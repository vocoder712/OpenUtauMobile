using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

public class ExpressionsPopupViewModel : PopupViewModelBase
{
    private readonly UProject project;
    private readonly UTrack? track;
    private readonly ObservableCollection<ExpressionBuilder> projectExpressions;
    private readonly ObservableCollection<ExpressionBuilder> trackExpressions;
    private bool isTrackOverride;
    private ExpressionBuilder? expression;

    [Reactive] public string Error { get; set; } = string.Empty;
    public ObservableCollection<ExpressionBuilder> Expressions => IsTrackOverride ? trackExpressions : projectExpressions;
    public ObservableCollection<ExpressionBuilder> SelectedExpressions { get; } = new();
    public ObservableCollection<ExpressionBuilder> AddOptions { get; } = new();
    public bool IsSwitchVisible => track != null;
    public bool IsSelected => Expression != null;
    public string Title => L.S("Expressions.Title");
    public string CustomDefaultLabel => L.S(IsTrackOverride ? "Expressions.TrackDefault" : "Expressions.ProjectDefault");

    public bool IsTrackOverride
    {
        get => isTrackOverride;
        set
        {
            if (value == isTrackOverride || value && track == null) return;
            this.RaiseAndSetIfChanged(ref isTrackOverride, value);
            SelectedExpressions.Clear();
            this.RaisePropertyChanged(nameof(Expressions));
            this.RaisePropertyChanged(nameof(CustomDefaultLabel));
            Expression = Expressions.FirstOrDefault();
            Error = string.Empty;
        }
    }

    public ExpressionBuilder? Expression
    {
        get => expression;
        set
        {
            this.RaiseAndSetIfChanged(ref expression, value);
            this.RaisePropertyChanged(nameof(IsSelected));
        }
    }

    public ReactiveCommand<Unit, Unit> ProjectCommand { get; }
    public ReactiveCommand<Unit, Unit> TrackCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }
    public ReactiveCommand<Unit, Unit> SuggestionsCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<ExpressionBuilder, Unit> AddOverrideCommand { get; }

    public ExpressionsPopupViewModel(UProject project, UTrack? track)
    {
        this.project = project;
        this.track = track;
        projectExpressions = new(project.expressions.Values.Select(descriptor => new ExpressionBuilder(descriptor)));
        trackExpressions = new(track?.TrackExpressions.Select(descriptor => new ExpressionBuilder(descriptor, true))
            ?? Enumerable.Empty<ExpressionBuilder>());
        isTrackOverride = track != null;
        Expression = Expressions.FirstOrDefault();
        ProjectCommand = ReactiveCommand.Create(() => { IsTrackOverride = false; });
        TrackCommand = ReactiveCommand.Create(() => { IsTrackOverride = true; });
        RemoveCommand = ReactiveCommand.Create(Remove);
        SuggestionsCommand = ReactiveCommand.Create(GetSuggestions);
        ApplyCommand = ReactiveCommand.Create(Apply);
        CancelCommand = ReactiveCommand.Create(() => RaiseClose(null));
        AddOverrideCommand = ReactiveCommand.Create<ExpressionBuilder>(AddOverride);
    }

    public void Add()
    {
        if (!IsTrackOverride)
        {
            ExpressionBuilder added = new();
            projectExpressions.Add(added);
            Expression = added;
            return;
        }
        AddOptions.Clear();
        foreach (ExpressionBuilder candidate in projectExpressions.Where(candidate =>
            !trackExpressions.Any(existing => existing.Abbr == candidate.Abbr)))
        {
            AddOptions.Add(candidate);
        }
        AddOptions.Add(new ExpressionBuilder());
    }

    private void AddOverride(ExpressionBuilder source)
    {
        try
        {
            ExpressionBuilder added = new(source.Build(), true);
            trackExpressions.Add(added);
            Expression = added;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    private void Remove()
    {
        foreach (ExpressionBuilder selected in SelectedExpressions.ToArray())
        {
            if (selected.IsRemovable) Expressions.Remove(selected);
        }
        if (Expression == null || !Expressions.Contains(Expression)) Expression = Expressions.FirstOrDefault();
    }

    private void GetSuggestions()
    {
        if (IsTrackOverride) return;
        try
        {
            foreach (UTrack source in project.tracks)
            {
                UExpressionDescriptor[]? suggestions = source.RendererSettings.Renderer
                    ?.GetSuggestedExpressions(source.Singer, source.RendererSettings);
                if (suggestions == null) continue;
                foreach (UExpressionDescriptor suggestion in suggestions)
                {
                    if (!projectExpressions.Any(existing => existing.Abbr == suggestion.abbr))
                        projectExpressions.Add(new ExpressionBuilder(suggestion));
                }
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }

    private bool ValidateScope(ObservableCollection<ExpressionBuilder> builders, bool trackScope)
    {
        foreach (ExpressionBuilder builder in builders)
        {
            string? error = builder.Validate();
            if (error == null) continue;
            IsTrackOverride = trackScope;
            Expression = builder;
            Error = $"{error} ({builder.Name})";
            return false;
        }
        string[] duplicates = builders.GroupBy(builder => builder.Abbr).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToArray();
        if (duplicates.Length > 0)
        {
            IsTrackOverride = trackScope;
            Error = $"{L.S("Expressions.Error.DuplicateAbbr")}: {string.Join(", ", duplicates)}";
            return false;
        }
        duplicates = builders.Where(builder => !string.IsNullOrEmpty(builder.Flag)).GroupBy(builder => builder.Flag)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicates.Length > 0)
        {
            IsTrackOverride = trackScope;
            Error = $"{L.S("Expressions.Error.DuplicateFlag")}: {string.Join(", ", duplicates)}";
            return false;
        }
        return true;
    }

    private void Apply()
    {
        Error = string.Empty;
        if (!ValidateScope(projectExpressions, false) || track != null && !ValidateScope(trackExpressions, true)) return;
        try
        {
            UExpressionDescriptor[] projectDescriptors = projectExpressions.Select(builder => builder.Build()).ToArray();
            UExpressionDescriptor[] trackDescriptors = trackExpressions.Select(builder => builder.Build()).ToArray();
            // 构建和检查先完成，避免格式化后的重复缩写使命令在替换定义途中失败。
            _ = projectDescriptors.ToDictionary(descriptor => descriptor.abbr);
            ConfigureExpressionsCommand command = track == null
                ? new ConfigureExpressionsCommand(project, projectDescriptors)
                : new ConfigureExpressionsCommand(project, projectDescriptors, track, trackDescriptors);
            DocManager.Inst.StartUndoGroup(track == null ? "command.project.exp" : "command.track.exp");
            try
            {
                DocManager.Inst.ExecuteCmd(command);
            }
            finally
            {
                DocManager.Inst.EndUndoGroup();
            }
            RaiseClose(true);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
}
