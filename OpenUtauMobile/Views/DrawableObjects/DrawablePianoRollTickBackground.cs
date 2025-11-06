using DynamicData.Binding;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Utils;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Views.Utils;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Views.DrawableObjects
{
    public class DrawablePianoRollTickBackground
    {
        public SKCanvas Canvas { get; set; } = null!;
        //public double HeightPerPianoKey { get; set; }
        //public Transformer Transformer { get; set; } = null!;
        //public int SnapDiv { get; set; } = 4; // 网格吸附密度 1/x
        public EditViewModel ViewModel { get; set; } = null!;
        //public ObservableCollectionExtended<int>? SnapTicks { get; set; } = [];

        public DrawablePianoRollTickBackground(SKCanvas canvas, EditViewModel viewModel)
        {
            Canvas = canvas;
            //HeightPerPianoKey = heightPerPianoKey;
            //Transformer = transformer;
            //SnapDiv = snapDiv;
            ViewModel = viewModel;
        }

        public void Draw()
        {
            var project = OpenUtau.Core.DocManager.Inst.Project;
            int snapUnit = project.resolution * 4 / ViewModel.PianoRollSnapDiv;
            while (snapUnit < ViewConstants.MinTicklineWidth)
            {
                snapUnit *= 2; // 避免绘制过于密集的线条
            }
            int canvasWidth = Canvas.DeviceClipBounds.Size.Width;
            int canvasHeight = Canvas.DeviceClipBounds.Size.Height;

            double minLineTick = ViewConstants.MinTicklineWidth;
            double leftTick = (-Canvas.TotalMatrix.TransX) / Canvas.TotalMatrix.ScaleX;
            double rightTick = leftTick + canvasWidth / Canvas.TotalMatrix.ScaleX;
            float bottom = (-Canvas.TotalMatrix.TransY) * Canvas.TotalMatrix.ScaleY + canvasHeight;

            project.timeAxis.TickPosToBarBeat((int)leftTick, out int bar, out int _, out int _);
            // 保证bar不会小于0
            bar = Math.Max(0, bar);

            if (bar > 0)
            {
                bar--;
            }

            int barTick = project.timeAxis.BarBeatToTickPos(bar, 0);
            #region 画笔
            // 小节文本画笔
            SKPaint barTextPaint = new()
            {
                Color = ThemeColorsManager.Current.BarNumber
            };
            SKTypeface typeface = OpenUtauMobile.Utils.FontManager.OpenSans;
            SKFont font = new(typeface, size: 30f);

            #endregion

            // 保存当前的变换矩阵
            SKMatrix originalMatrix = Canvas.TotalMatrix;
            // 恢复到默认矩阵
            Canvas.ResetMatrix();
            while (barTick <= rightTick)
            {
                //SnapTicks?.Add(barTick);

                // 小节线和数字
                float x = (float)Math.Round((double)barTick) + 0.5f;
                float y = 20 * (float)ViewModel.Density;

                Canvas.DrawText((bar + 1).ToString(), (x + 10) * originalMatrix.ScaleX + originalMatrix.TransX, 30, font, barTextPaint);
                Canvas.DrawLine(new SKPoint(x * originalMatrix.ScaleX + originalMatrix.TransX, y), new SKPoint(x * originalMatrix.ScaleX + originalMatrix.TransX, bottom + 0.5f), ThemeColorsManager.Current.PianoRollBarlinePaint);
                Canvas.DrawLine(new SKPoint(x * originalMatrix.ScaleX + originalMatrix.TransX, 0), new SKPoint(x * originalMatrix.ScaleX + originalMatrix.TransX, y), ThemeColorsManager.Current.PianoRollBarlineHeadPaint);

                // 小节之间的线
                UTimeSignature timeSig = project.timeAxis.TimeSignatureAtBar(bar);
                int nextBarTick = project.timeAxis.BarBeatToTickPos(bar + 1, 0);

                int ticksPerBeat = project.resolution * 4 * timeSig.beatPerBar / timeSig.beatUnit;
                int ticksPerLine = snapUnit;
                if (ticksPerBeat < snapUnit)
                {
                    ticksPerLine = ticksPerBeat;
                }
                else if (ticksPerBeat % snapUnit != 0)
                {
                    if (ticksPerBeat > minLineTick)
                    {
                        ticksPerLine = ticksPerBeat;
                    }
                    else
                    {
                        ticksPerLine = nextBarTick - barTick;
                    }
                }
                if (nextBarTick > leftTick)
                {
                    for (int tick = barTick + ticksPerLine; tick < nextBarTick; tick += ticksPerLine)
                    {
                        //SnapTicks?.Add(tick);
                        project.timeAxis.TickPosToBarBeat(tick, out int snapBar, out int snapBeat, out int snapRemainingTicks);
                        x = (float)(tick + 0.5);
                        y = 20 * (float)ViewModel.Density;
                        if (snapRemainingTicks == 0)
                        {
                            // 节拍线
                            Canvas.DrawLine(new SKPoint(x * originalMatrix.ScaleX + originalMatrix.TransX, y), new SKPoint(x * originalMatrix.ScaleX + originalMatrix.TransX, bottom + 0.5f), ThemeColorsManager.Current.PianoRollBeatlinePaint);
                            Canvas.DrawLine(new SKPoint(x * originalMatrix.ScaleX + originalMatrix.TransX, 0), new SKPoint(x * originalMatrix.ScaleX + originalMatrix.TransX, y), ThemeColorsManager.Current.PianoRollBeatlineHeadPaint);
                        }
                        else
                        {
                            // 非节拍线
                            Canvas.DrawLine(new SKPoint(x * originalMatrix.ScaleX + originalMatrix.TransX, y), new SKPoint(x * originalMatrix.ScaleX + originalMatrix.TransX, bottom + 0.5f), ThemeColorsManager.Current.PianoRollBeatlinePaint);
                        }
                    }
                }
                barTick = nextBarTick;
                bar++;
            }
            //SnapTicks?.Add(barTick);

            SKPaint shadowPaint = new()
            {
                Color = ThemeColorsManager.Current.PianoRollShadow,
                Style = SKPaintStyle.Fill
            };

            // 绘制全部阴影（如果没有编辑的分片）
            if (ViewModel.EditingPart == null)
            {
                Canvas.DrawRect(0, 0, canvasWidth, bottom + 0.5f, shadowPaint);
            }
            else
            {
                // 绘制左侧阴影
                if (ViewModel.EditingPart.position > rightTick)
                {
                    // 整个编辑分片在右侧
                    Canvas.DrawRect(0, 0, canvasWidth, bottom + 0.5f, shadowPaint);
                }
                else if (ViewModel.EditingPart.End <= leftTick)
                {
                    // 整个编辑分片在左侧
                    Canvas.DrawRect(0, 0, canvasWidth, bottom + 0.5f, shadowPaint);
                }
                else
                {
                    if (ViewModel.EditingPart.position > leftTick)
                    {
                        // 露出左侧部分
                        float partStartX = (float)ViewModel.EditingPart.position * (float)originalMatrix.ScaleX + originalMatrix.TransX;
                        Canvas.DrawRect(0, 0, partStartX, bottom + 0.5f, shadowPaint);
                    }
                    if (ViewModel.EditingPart.End < rightTick)
                    {
                        // 露出右侧部分
                        float partEndX = (float)ViewModel.EditingPart.End * (float)originalMatrix.ScaleX + originalMatrix.TransX;
                        Canvas.DrawRect(partEndX, 0, canvasWidth, bottom + 0.5f, shadowPaint);
                    }
                }
            }

            // 绘制回放指针线
            float posX = (float)DeviceDisplay.Current.MainDisplayInfo.Density * ViewConstants.PianoRollPlaybackLinePos;
            Canvas.DrawLine(new SKPoint(posX, 0), new SKPoint(posX, bottom + 0.5f), ThemeColorsManager.Current.PianoRollPlaybackPosLinePaint);

            // 恢复原始矩阵
            Canvas.SetMatrix(originalMatrix);
        }
    }
}
