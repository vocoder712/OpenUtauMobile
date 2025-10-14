using DynamicData.Binding;
using Melanchall.DryWetMidi.Interaction;
using Microsoft.Maui.Graphics;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Utils;
using OpenUtauMobile.ViewModels;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Views.DrawableObjects
{
    class DrawableTickBackground
    {
        public SKCanvas Canvas { get; set; } = null!;
        //public int SnapDiv { get; set; } // 网格吸附密度 1/x
        public ObservableCollectionExtended<int>? SnapTicks { get; set; } = [];
        public EditViewModel ViewModel { get; set; } = null!;
        public DrawableTickBackground(SKCanvas canvas, EditViewModel viewModel, int snapDiv = 4)
        {
            Canvas = canvas;
            ViewModel = viewModel;
            //SnapDiv = snapDiv;
        }
        public void Draw()
        {
            UProject project = OpenUtau.Core.DocManager.Inst.Project;

            int canvasWidth = Canvas.DeviceClipBounds.Size.Width;
            int canvasHeight = Canvas.DeviceClipBounds.Size.Height;

            double minLineTick = ViewConstants.MinTicklineWidth;
            double leftTick = (-Canvas.TotalMatrix.TransX) / Canvas.TotalMatrix.ScaleX;
            double rightTick = leftTick + canvasWidth / Canvas.TotalMatrix.ScaleX;
            float bottom = (-Canvas.TotalMatrix.TransY) * Canvas.TotalMatrix.ScaleY + canvasHeight;

            project.timeAxis.TickPosToBarBeat((int)leftTick, out int bar, out int beat, out int remainingTicks);
            
            if (bar > 0)
            {
                bar--;
            }
            
            int barTick = project.timeAxis.BarBeatToTickPos(bar, 0);
            #region 画笔
            // 小节线画笔
            SKPaint barPaint = new()
            {
                Color = ThemeColorsManager.Current.TimeLine,
                StrokeWidth = 2f // 设置小节线粗细
            };
            // 小节文本画笔
            SKPaint barTextPaint = new()
            {
                Color = ThemeColorsManager.Current.BarNumber
            };
            SKFontManager fontManager = SKFontManager.Default;
            SKTypeface typeface = SKTypeface.FromFamilyName("OpenSansRegular");
            SKFont barNumberFont = new(typeface, size:30f);
            // 节拍线画笔
            SKPaint linePaint = new()
            {
                Color = ThemeColorsManager.Current.TimeLine.WithAlpha(128),
                StrokeWidth = 2f // 设置节拍线粗细
            };
            // 曲速与节拍标记线画笔
            SKPaint signaturePaint = new()
            {
                StrokeWidth = 5f,
                Color = SKColors.Red // 曲速标记为红色
            };
            SKPaint tempoTextPaint = new()
            {
                Color = ThemeColorsManager.Current.TempoSignatureText
            };
            SKFont tempoFont = new(typeface, size:20f);
            SKPaint timeSigTextPaint = new()
            {
                Color = ThemeColorsManager.Current.TimeSignatureText
            };
            SKFont timeSigFont = new(typeface, size:20f);
            SKPaint timeLineBackgroudPaint = new()
            {
                Color = ThemeColorsManager.Current.TimeLineBackground.WithAlpha(50),
                Style = SKPaintStyle.Fill
            };
            #endregion
            // 保存当前的变换矩阵
            SKMatrix originalMatrix = Canvas.TotalMatrix;
            // 恢复到默认矩阵，使文字不受缩放影响
            Canvas.ResetMatrix();

            float h = ViewConstants.TimeLineHeight * (float)ViewModel.Density;
            // 绘制时间轴背景色
            Canvas.DrawRect(0, 0, canvasWidth, h, timeLineBackgroudPaint);

            // 避免绘制过于密集的线条
            int snapUnit = project.resolution * 4 / ViewModel.TrackSnapDiv;
            while (snapUnit * originalMatrix.ScaleX < ViewConstants.MinTicklineWidth)
            {
                snapUnit *= 2;
            }

            while (barTick <= rightTick)
            {
                SnapTicks?.Add(barTick);
                
                // 小节线和数字
                float x = (float)Math.Round((double)barTick) + 0.5f;
                float y = -0.5f;

                Canvas.DrawText((bar + 1).ToString(), x * originalMatrix.ScaleX + originalMatrix.TransX + 20, 30, barNumberFont, barTextPaint);
                Canvas.DrawLine(new SKPoint(x * originalMatrix.ScaleX + originalMatrix.TransX, y), new SKPoint(x * originalMatrix.ScaleX + originalMatrix.TransX, bottom + 0.5f), barPaint);

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
                        SnapTicks?.Add(tick);
                        project.timeAxis.TickPosToBarBeat(tick, out int snapBar, out int snapBeat, out int snapRemainingTicks);
                        x = (float)(tick + 0.5);
                        y = ViewConstants.TimeLineHeight * (float)ViewModel.Density + (-originalMatrix.TransY) * originalMatrix.ScaleY + originalMatrix.TransY;
                        Canvas.DrawLine(new SKPoint(x * originalMatrix.ScaleX + originalMatrix.TransX, y), new SKPoint(x * originalMatrix.ScaleX + originalMatrix.TransX, bottom + 0.5f), linePaint);
                    }
                }
                barTick = nextBarTick;
                bar++;
            }
            SnapTicks?.Add(barTick);

            float sigX;
            // 绘制曲速标记
            foreach (var tempo in project.tempos)
            {
                sigX = (float)Math.Round((double)tempo.position) * originalMatrix.ScaleX + originalMatrix.TransX;
                Canvas.DrawLine(new SKPoint(sigX, 0), new SKPoint(sigX, h), signaturePaint);
                Canvas.DrawText(tempo.bpm.ToString("#0.00"), 
                    sigX + 20, 
                    h / 2, 
                    tempoFont, 
                    tempoTextPaint);
                //var textLayout = TextLayoutCache.Get(tempo.bpm.ToString("#0.00"), ThemeManager.BarNumberBrush, 10);
                //using (var state = context.PushTransform(Matrix.CreateTranslation(x + 3, 0)))
                //{
                //    textLayout.Draw(context, new Point());
                //}
            }

            int timeSigTick;
            // 绘制拍号标记
            foreach (var timeSig in project.timeSignatures)
            {
                timeSigTick = project.timeAxis.BarBeatToTickPos(timeSig.barPosition, 0);
                sigX = (float)Math.Round(timeSigTick * originalMatrix.ScaleX + originalMatrix.TransX);
                Canvas.DrawLine(new SKPoint(sigX, 0), new SKPoint(sigX, h), signaturePaint);
                Canvas.DrawText($"{timeSig.beatPerBar}/{timeSig.beatUnit}",
                    sigX + 20,
                    h,
                    timeSigFont,
                    timeSigTextPaint);
            }

            // 恢复原始矩阵
            Canvas.SetMatrix(originalMatrix);

        }
    }
}
