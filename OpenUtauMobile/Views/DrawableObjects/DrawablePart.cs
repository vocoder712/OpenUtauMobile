using Microsoft.Maui.ApplicationModel;
using NAudio.CoreAudioApi;
using NWaves.Signals;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Utils;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Views.Utils;
using Serilog;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Color = Microsoft.Maui.Graphics.Color;

namespace OpenUtauMobile.Views.DrawableObjects
{
    /// <summary>
    /// 可绘制分片类型
    /// </summary>
    public class DrawablePart
    {
        public SKCanvas Canvas { get; set; } = null!;
        public EditViewModel ViewModel { get; set; } = null!;
        public float HeightPerTrack => (float)ViewModel.HeightPerTrack * (float)ViewModel.Density;
        public Transformer CurrentTransformer {  get; set; } = null!;
        public double ResolutionX { get; set; } = 480d;
        public bool IsSelected { get; set; } = false; // 是否被选中
        public bool IsResizable { get; set; } = true; // 是否可调整长度
        private float RightHandleX { get; set; } // 逻辑坐标
        private float RightHandleY { get; set; } // 逻辑坐标
        private float R { get; set; } // 手柄半径，逻辑坐标
        /// <summary>
        /// 关联UPart对象
        /// </summary>
        public UPart Part { get; set; } = null!;

        /// <summary>
        /// 构造可绘制分片
        /// </summary>
        /// <param name="canvas">要绘制的目标画布</param>
        /// <param name="part">关联的UPart对象</param>
        /// <param name="resolutionX">UProject类中定义了resolution = 480，这是常见的MIDI文件刻度分辨率</param>
        public DrawablePart(SKCanvas canvas, UPart part,EditViewModel viewModel, Transformer transformer, double resolutionX = 480, bool isSelected = false, bool isResizable = false)
        {
            Canvas = canvas;
            Part = part;
            ResolutionX = resolutionX;
            IsSelected = isSelected;
            IsResizable = isResizable;
            ViewModel = viewModel;
            CurrentTransformer = transformer;
            // 计算右侧手柄位置
            if (isResizable)
            {
                RightHandleX = (float)(Part.position + Part.Duration);
                RightHandleY = (float)(Part.trackNo + 0.5f) * HeightPerTrack;
                R = 10f; // 手柄半径
            }
        }

        /// <summary>
        /// 判断一个点是否在分片内
        /// </summary>
        /// <param name="point">逻辑坐标</param>
        /// <returns></returns>
        public bool IsPointInside(SKPoint point)
        {
            double left = Part.position;
            double right = Part.position + Part.Duration;
            double top = Part.trackNo * HeightPerTrack;
            double bottom = top + HeightPerTrack;
            return point.X >= left && point.X <= right && point.Y >= top && point.Y <= bottom;
        }

        public bool IsPointInHandle(SKPoint point)
        {
            // 将手柄中心点转换为实际坐标
            float handleActualX = RightHandleX * CurrentTransformer.ZoomX + CurrentTransformer.PanX;
            float handleActualY = RightHandleY * CurrentTransformer.ZoomY + CurrentTransformer.PanY;
            
            // 将检测点从逻辑坐标转换为实际坐标
            float pointActualX = point.X * CurrentTransformer.ZoomX + CurrentTransformer.PanX;
            float pointActualY = point.Y * CurrentTransformer.ZoomY + CurrentTransformer.PanY;
            
            // 在实际坐标系中计算距离
            float distance = (float)Math.Sqrt(
                Math.Pow(pointActualX - handleActualX, 2) + 
                Math.Pow(pointActualY - handleActualY, 2)
            );
            
            // 与手柄实际半径比较（乘以设备密度因子）
            float actualRadius = R * (float)DeviceDisplay.Current.MainDisplayInfo.Density;
            
            return distance <= actualRadius;
        }

        /// <summary>
        /// 绘制逻辑封装
        /// </summary>
        public void Draw()
        {
            DrawRectangle();
            if (IsResizable)
            {
                DrawHandle();
            }
            DrawTitle();
            if (Part is UVoicePart voicePart && voicePart.notes.Count > 0)
            {
                DrawNotes(voicePart);
            }
            else if (Part is UWavePart wavePart)
            {
                // 增强波形状态显示逻辑
                if (wavePart.Peaks == null)
                {
                    DrawWaveLoadingInfo("等待波形处理...");
                    return;
                }

                switch (wavePart.Peaks.Status)
                {
                    case TaskStatus.Canceled:
                        DrawWaveLoadingInfo("波形处理被取消");
                        break;
                    case TaskStatus.Faulted:
                        DrawWaveLoadingInfo($"波形处理错误: {wavePart.Peaks.Exception?.Message ?? "未知错误"}");
                        break;
                    case TaskStatus.Running:
                        DrawWaveLoadingInfo("波形文件渲染中...");
                        break;
                    case TaskStatus.WaitingForActivation:
                        break;
                    case TaskStatus.WaitingToRun:
                        DrawWaveLoadingInfo("波形文件渲染中...");
                        break;
                    case TaskStatus.Created:
                        DrawWaveLoadingInfo("波形处理未开始");
                        break;
                    case TaskStatus.RanToCompletion when wavePart.Peaks.Result == null:
                        DrawWaveLoadingInfo("波形数据为空");
                        break;
                    case TaskStatus.RanToCompletion:
                        try 
                        { 
                            DrawWaveform(wavePart);
                        }
                        catch (Exception ex)
                        {
                            DrawWaveLoadingInfo($"波形处理错误: {ex.Message}");
                        }
                        break;
                    default:
                        DrawWaveLoadingInfo($"未知状态: {wavePart.Peaks.Status}");
                        break;
                }
            }
        }

        private void DrawWaveLoadingInfo(string info)
        {
            // 保存当前的变换矩阵
            SKMatrix originalMatrix = Canvas.TotalMatrix;
            // 恢复到默认矩阵，使文字不受缩放影响
            Canvas.ResetMatrix();

            // 计算位置
            float x = (float)(Part.position * originalMatrix.ScaleX + originalMatrix.TransX + 5);
            float y = (float)(Part.trackNo * HeightPerTrack * originalMatrix.ScaleY + originalMatrix.TransY + 60);
            // 创建画笔
            using (SKPaint paint = new())
            {
                paint.Color = SKColors.Black;
                // 使用合适的字体
                float fontSize = 30f;
                SKFontManager fontManager = SKFontManager.Default;
                SKTypeface typeface = fontManager.MatchCharacter('中');
                SKFont font = new(typeface, fontSize);
                // 绘制信息文本
                Canvas.DrawText(info, x, y, SKTextAlign.Left, font, paint);
            }

            // 恢复原始变换矩阵
            Canvas.SetMatrix(originalMatrix);
        }

        /// <summary>
        /// 画出轮廓矩形
        /// </summary>
        public void DrawRectangle()
        {
            // 保存当前的变换矩阵
            SKMatrix originalMatrix = Canvas.TotalMatrix;
            // 恢复到默认矩阵，使矩形大小不受缩放影响
            Canvas.ResetMatrix();
            // 计算位置
            float x = (Part.position + 1) * (float)originalMatrix.ScaleX + (float)originalMatrix.TransX;
            float y = (float)(Part.trackNo * HeightPerTrack + 1) * (float)originalMatrix.ScaleY + (float)originalMatrix.TransY;
            float width = (Part.Duration - 2) * (float)originalMatrix.ScaleX;
            float height = (float)(HeightPerTrack - 2) * (float)originalMatrix.ScaleY;
            string color = DocManager.Inst.Project.tracks[Part.trackNo].TrackColor;
            SKColor sKColor = ViewConstants.TrackSkiaColors[color].WithAlpha(150);
            // 创建画笔
            using SKPaint paint = new();
            paint.Color = sKColor;
            paint.Style = SKPaintStyle.Fill;
            // 绘制矩形
            Canvas.DrawRect(x, y, width, height, paint);
            // 如果被选中，绘制边框
            if (IsSelected)
            {
                paint.Color = ThemeColorsManager.Current.SelectedPartBorder;
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 3;
                Canvas.DrawRect(x, y, width, height, paint);
            }
            // 恢复原始变换矩阵
            Canvas.SetMatrix(originalMatrix);
        }

        /// <summary>
        /// 绘制标题
        /// </summary>
        public void DrawTitle()
        {
            // 保存当前的变换矩阵
            SKMatrix originalMatrix = Canvas.TotalMatrix;
            // 恢复到默认矩阵，使文字不受缩放影响
            Canvas.ResetMatrix();

            // 计算位置
            float x = (float)(Part.position * originalMatrix.ScaleX + originalMatrix.TransX + 5);
            float y = (float)(Part.trackNo * HeightPerTrack * originalMatrix.ScaleY + originalMatrix.TransY + 30);
            // 创建画笔
            using (SKPaint paint = new())
            {
                paint.Color = ThemeColorsManager.Current.PartLabel;
                SKFont font = new(SKFontManager.Default.MatchCharacter('中'), 30f);
                // 绘制标题
                Canvas.DrawText(Part.DisplayName, x, y, SKTextAlign.Left, font, paint);
            }

            // 恢复原始变换矩阵
            Canvas.SetMatrix(originalMatrix);
        }

        /// <summary>
        /// 绘制音符
        /// </summary>
        public void DrawNotes(UVoicePart voicePart)
        {
            // Notes
            int maxTone = voicePart.notes.Max(note => note.tone);
            int minTone = voicePart.notes.Min(note => note.tone);
            int leftTick = (int)(-ViewModel.TrackTransformer.PanX / ViewModel.TrackTransformer.ZoomX);
            int rightTick = (int)(Canvas.DeviceClipBounds.Size.Width / ViewModel.TrackTransformer.ZoomX + leftTick);

            if (maxTone - minTone < 10) // 如果音域较窄，则扩展到10
            {
                int additional = (10 - (maxTone - minTone)) / 2;
                minTone -= additional;
                maxTone += additional;
            }
            Application app = App.Current ?? new Application();
            using SKPaint paint = new()
            {
                Color = app.Resources.TryGetValue("TrackNote", out var accentColor) && accentColor is Color color ? SKColor.Parse(color.ToHex()) : SKColors.Magenta,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = HeightPerTrack / (maxTone - minTone + 10),
            };
            foreach (var note in voicePart.notes)
            {
                if (note.End + Part.position < leftTick)
                {
                    continue;
                }
                if (note.position + Part.position > rightTick)
                {
                    break;
                }
                float y = (Part.trackNo + 1 - (float)(note.tone - (minTone - 5)) / (maxTone - minTone + 10)) * HeightPerTrack;
                SKPoint start = new(note.position + Part.position, y);
                SKPoint end = new(note.End + Part.position, y);
                Canvas.DrawLine(start, end, paint);
            }
        }

        /// <summary>
        /// 绘制音频波形
        /// </summary>
        /// <param name="wavePart">包含峰值数据的音频片段</param>
        private void DrawWaveform(UWavePart wavePart)
        {
            if (wavePart.Peaks == null ||
                !wavePart.Peaks.IsCompletedSuccessfully ||
                wavePart.Peaks.Result == null)
            {
                return;
            }
            // 保存当前的变换矩阵
            SKMatrix originalMatrix = Canvas.TotalMatrix;
            Canvas.ResetMatrix(); // 恢复到默认矩阵，使波形不受缩放影响
            float height = (float)ViewModel.HeightPerTrack * (float)ViewModel.Density; // 总高度
            float monoChnlAmp = height / 2; // 如果是单声道的振幅高度（画布）
            float stereoChnlAmp = height / 4; // 如果是双声道的振幅高度（画布）
            float tickOffset = (float)(-CurrentTransformer.PanX / CurrentTransformer.ZoomX); // 当前视图的Tick偏移位置
            float tickWidth = (float)CurrentTransformer.ZoomX; // 每个Tick对应的像素宽度

            TimeAxis timeAxis = DocManager.Inst.Project.timeAxis;
            DiscreteSignal[] peaks = wavePart.Peaks.Result;
            int x = 0; // 正在绘制的像素位置
            if (tickOffset <= wavePart.position)
            {
                // Part starts in or to the right of view. // 分片在视图中或右侧开始
                x = (int)(tickWidth * (wavePart.position - tickOffset));
            }
            if (x >= Canvas.DeviceClipBounds.Width)
            {
                // 如果绘制起点已经在画布右侧之外，则不绘制
                return;
            }
            int posTick = (int)(tickOffset + x / tickWidth); // 当前绘制位置对应的Tick位置
            double posMs = timeAxis.TickPosToMsPos(posTick); // 当前绘制位置对应的ms位置
            double offsetMs = timeAxis.TickPosToMsPos(wavePart.position); // 分片开始位置对应的ms位置
            int sampleIndex = (int)(wavePart.peaksSampleRate * (posMs - offsetMs) * 0.001); // 当前绘制位置对应的采样点位置，0.001是为了将毫秒转换为秒，吗？
            sampleIndex = Math.Clamp(sampleIndex, 0, peaks[0].Length);
            SKPaint paint = new()
            {
                Color = ThemeColorsManager.Current.TrackNote.WithAlpha(200),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, tickWidth),
            };
            //Debug.WriteLine($"开始绘制位置X: {x}, Tick: {posTick}, SampleIndex: {sampleIndex}");
            while (x < Canvas.DeviceClipBounds.Width)
            {
                if (posTick >= wavePart.position + wavePart.Duration)
                {
                    //Debug.WriteLine("绘制到了分片末尾，停止绘制");
                    break;
                }
                int nextPosTick = (int)(tickOffset + (x + 1) / tickWidth); // 下一个像素位置对应的Tick位置
                double nexPosMs = timeAxis.TickPosToMsPos(nextPosTick); // 下一个像素位置对应的ms位置
                int nextSampleIndex = (int)(wavePart.peaksSampleRate * (nexPosMs - offsetMs) * 0.001); // 下一个像素位置对应的采样点位置
                nextSampleIndex = Math.Clamp(nextSampleIndex, 0, peaks[0].Length);
                if (nextSampleIndex > sampleIndex)
                { // 如果成功移动到下一个采样点位置
                    for (int i = 0; i < peaks.Length; ++i)
                    { // 遍历所有声道
                        ArraySegment<float> segment = new ArraySegment<float>(peaks[i].Samples, sampleIndex, nextSampleIndex - sampleIndex); // 取出当前采样点位置到下一个采样点位置之间的采样数据
                        float min = segment.Min(); // min取值范围[-1, 1]
                        float max = segment.Max(); // max取值范围[-1, 1]
                        float ySpan = peaks.Length == 1 ? monoChnlAmp : stereoChnlAmp; // 计算当前声道的振幅高度
                        float yOffset = i == 1 ? monoChnlAmp : 0; // 如果是右声道，则需要向下偏移一个单声道的振幅高度
                        Canvas.DrawLine(x,
                            (float)(ySpan * (1 + -min) + yOffset + Part.trackNo * height) * originalMatrix.ScaleY + originalMatrix.TransY,
                            x,
                            (float)(ySpan * (1 + -max) + yOffset + Part.trackNo * height) * originalMatrix.ScaleY + originalMatrix.TransY,
                            paint);
                    }
                }
                //else
                //{
                //    //Debug.WriteLine($"跳过绘制位置X: {x}, Tick: {posTick}, SampleIndex: {sampleIndex}，因为没有新的采样点");
                //}
                x++;
                posTick = nextPosTick;
                posMs = nexPosMs;
                sampleIndex = nextSampleIndex;
            }
            //Debug.WriteLine($"最终绘制位置X: {x}, Tick: {posTick}, SampleIndex: {sampleIndex}");
            // 恢复原始变换矩阵
            Canvas.SetMatrix(originalMatrix);
        }

        /// <summary>
        /// 绘制右侧长度调整手柄
        /// </summary>
        private void DrawHandle()
        {
            // 保存当前的变换矩阵
            SKMatrix originalMatrix = Canvas.TotalMatrix;
            // 恢复到默认矩阵，使手柄大小不受缩放影响
            Canvas.ResetMatrix();
            // 创建手柄画笔
            float x = RightHandleX * originalMatrix.ScaleX + originalMatrix.TransX;
            float y = RightHandleY * originalMatrix.ScaleY + originalMatrix.TransY;
            float r = R * (float)DeviceDisplay.Current.MainDisplayInfo.Density;
            using (SKPaint paint = new SKPaint())
            {
                paint.Color = SKColors.Yellow;
                paint.Style = SKPaintStyle.Fill;
                // 绘制手柄矩形
                Canvas.DrawCircle(x, y, r, paint);
            }
            // 恢复原始变换矩阵
            Canvas.SetMatrix(originalMatrix);
        }

    }
}