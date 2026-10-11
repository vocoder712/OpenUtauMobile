using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia;
using OpenUtau.Core;
using Serilog;

namespace OpenUtauMobile.DesktopUI.Services
{
    public sealed class DesktopLayoutState
    {
        public double Width { get; set; } = 1440;
        public double Height { get; set; } = 900;
        public double ArrangementRatio { get; set; } = .28;
        public double InspectorWidth { get; set; } = 320;
        public bool ArrangementVisible { get; set; } = true;
        public bool PianoRollVisible { get; set; } = true;
        public double ParameterHeight { get; set; } = 128;
        public bool ParametersVisible { get; set; } = true;
        public bool InspectorVisible { get; set; } = true;
        public Dictionary<string, bool> InspectorSections { get; set; } = [];
        public Dictionary<string, DesktopStoredWindowGeometry> UtilityWindows { get; set; } = [];
        public DesktopStoredWindowGeometry? MixerWindow { get; set; }
        public double MixerFxPaneWidth { get; set; } = 288;
        public DesktopStoredWindowGeometry? MainWindowGeometry { get; set; }
        public WindowState WindowState { get; set; }
    }

    public sealed class DesktopStoredWindowGeometry
    {
        public int X { get; set; }
        public int Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    public sealed class DesktopLayoutStore
    {
        private string FilePath => Path.Combine(PathManager.Inst.DataPath, "desktop-layout.json");
        public DesktopLayoutState State { get; private set; } = new();
        public event Action? LayoutReset;

        public DesktopLayoutStore()
        {
            try
            {
                if (File.Exists(FilePath)) State = JsonSerializer.Deserialize<DesktopLayoutState>(File.ReadAllText(FilePath)) ?? new();
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Warning(e, "读取桌面布局失败");
            }
            State.Width = Normalize(State.Width, 1440, 800, 3840);
            State.InspectorSections ??= [];
            State.UtilityWindows ??= [];
            foreach (DesktopStoredWindowGeometry geometry in State.UtilityWindows.Values)
            {
                geometry.Width = Normalize(geometry.Width, 960, 320, 3840);
                geometry.Height = Normalize(geometry.Height, 720, 240, 2160);
            }
            if (State.MixerWindow != null)
            {
                State.MixerWindow.Width = Normalize(State.MixerWindow.Width, 900, 320, 3840);
                State.MixerWindow.Height = Normalize(State.MixerWindow.Height, 560, 240, 2160);
            }
            if (State.MainWindowGeometry != null)
            {
                State.MainWindowGeometry.Width = Normalize(State.MainWindowGeometry.Width, State.Width, 320, 3840);
                State.MainWindowGeometry.Height = Normalize(State.MainWindowGeometry.Height, State.Height, 240, 2160);
            }
            State.Height = Normalize(State.Height, 900, 500, 2160);
            State.ArrangementRatio = Normalize(State.ArrangementRatio, .28, 0, 1);
            State.ParameterHeight = Normalize(State.ParameterHeight, 128, 32, 2000);
            State.InspectorWidth = Normalize(State.InspectorWidth, 320, 0, 2400);
            State.MixerFxPaneWidth = Normalize(State.MixerFxPaneWidth, 288, 220, 1200);
            if (State.WindowState != WindowState.Maximized) State.WindowState = WindowState.Normal;
        }
        private static double Normalize(double value, double fallback, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        public void Reset() { State = new(); Save(); LayoutReset?.Invoke(); }
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                string temporary = FilePath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(State));
                File.Move(temporary, FilePath, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warning(e, "保存桌面布局失败");
            }
        }
    }
}
