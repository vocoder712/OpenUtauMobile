using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Utils;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Views.Utils;
using SkiaSharp;
using System;
namespace OpenUtauMobile.Views.DrawableObjects
{
    public class DrawableNotes
    {
        public SKCanvas Canvas { get; set; } = null!;
        public UVoicePart Part { get; set; } = null!;
        public float HeightPerPianoKey { get; set; }
        public SKColor NotesColor { get; set; }
        /// <summary>
        /// 实际坐标而非逻辑坐标
        /// </summary>
        private static float Spacing => 15f;
        /// <summary>
        /// 实际坐标而非逻辑坐标
        /// </summary>
        private const int DefaultTouchTargetSize = 16;
        public float HalfHandleSize => (float)(DefaultTouchTargetSize * ViewModel.Density);
        private float HandleSize => HalfHandleSize * 2;
        /// <summary>
        /// 当前分片的起始位置tick
        /// </summary>
        public float PositionX { get; set; }
        public EditViewModel ViewModel { get; set; } = null!;
        // 计算可视区域的左右边界（逻辑坐标）
        private int LeftTick { get; set; }
        private int RightTick { get; set; }

        public DrawableNotes(SKCanvas canvas, UVoicePart part, EditViewModel viewModel, SKColor notesColor)
        {
            Part = part;
            Canvas = canvas;
            HeightPerPianoKey = (float)viewModel.HeightPerPianoKey * (float)viewModel.Density;
            //Transformer = transformer;
            PositionX = part.position;
            ViewModel = viewModel;
            NotesColor = notesColor;
            // 计算可视区域的左右边界（逻辑坐标）
            LeftTick = (int)(-ViewModel.PianoRollTransformer.PanX / ViewModel.PianoRollTransformer.ZoomX);
            RightTick = (int)(Canvas.DeviceClipBounds.Width / ViewModel.PianoRollTransformer.ZoomX + LeftTick);

        }
        public void Draw()
        {
            DrawRectangle();
            DrawLyrics();
        }

        public void DrawRectangle()
        {
            SKPaint paint = new()
            {
                Color = NotesColor,
            };
            SKPaint selectedNotePaint = new()
            {
                Color = ThemeColorsManager.Current.SelectedNoteBorder,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 5
            };
            foreach (UNote note in Part.notes)
            {
                // 计算音符的绝对位置
                int noteStart = (int)(PositionX + note.position);
                int noteEnd = noteStart + note.duration;

                // 跳过左侧不可见的音符
                if (noteEnd < LeftTick)
                {
                    continue;
                }

                // 跳过右侧不可见的音符 notes按position排序
                if (noteStart > RightTick)
                {
                    break;
                }

                // 如果是错误音符，颜色增加透明度
                if (note.Error)
                {
                    paint.Color = paint.Color.WithAlpha(100);
                }
                // 绘制边框
                if (ViewModel.SelectedNotes.Contains(note))
                {
                    Canvas.DrawRect((PositionX + note.position) * ViewModel.PianoRollTransformer.ZoomX + ViewModel.PianoRollTransformer.PanX, (ViewConstants.TotalPianoKeys - note.tone - 1) * HeightPerPianoKey * ViewModel.PianoRollTransformer.ZoomY + ViewModel.PianoRollTransformer.PanY, note.duration * ViewModel.PianoRollTransformer.ZoomX, HeightPerPianoKey * ViewModel.PianoRollTransformer.ZoomY, selectedNotePaint);
                }
                // 绘制实心矩形
                Canvas.DrawRect((PositionX + note.position) * ViewModel.PianoRollTransformer.ZoomX + ViewModel.PianoRollTransformer.PanX, (ViewConstants.TotalPianoKeys - note.tone - 1) * HeightPerPianoKey * ViewModel.PianoRollTransformer.ZoomY + ViewModel.PianoRollTransformer.PanY, note.duration * ViewModel.PianoRollTransformer.ZoomX, HeightPerPianoKey * ViewModel.PianoRollTransformer.ZoomY, paint);
            }
            // 绘制可拖拽手柄
            if (ViewModel.SelectedNotes.Count > 0 && ViewModel.CurrentNoteEditMode == EditViewModel.NoteEditMode.EditNote)
            {
                SKPaint handlePaint = new()
                {
                    Color = SKColors.Yellow,
                    Style = SKPaintStyle.Fill
                };
                SKPaint trianglePaint = new()
                {
                    Color = SKColors.White,
                    Style = SKPaintStyle.Fill
                };
                foreach (UNote note in ViewModel.SelectedNotes)
                {
                    float left = (PositionX + note.position + note.duration) * ViewModel.PianoRollTransformer.ZoomX + ViewModel.PianoRollTransformer.PanX + Spacing;
                    float right = left + HandleSize;
                    float top = (ViewConstants.TotalPianoKeys - note.tone - 0.5f) * HeightPerPianoKey * ViewModel.PianoRollTransformer.ZoomY - HalfHandleSize + ViewModel.PianoRollTransformer.PanY;
                    float centerY = top + HalfHandleSize;
                    float centerX = left + HalfHandleSize;
                    float bottom = top + HandleSize;
                    // 右侧手柄
                    Canvas.DrawRoundRect(left,
                        top,
                        HandleSize,
                        HandleSize,
                        4,
                        4,
                        handlePaint);
                    // 里面画两个小三角形，表示可拖拽
                    SKPath trianglePath = new();
                    trianglePath.MoveTo(left + 4, centerY);
                    trianglePath.LineTo(centerX - 2, bottom - 6);
                    trianglePath.LineTo(centerX - 2, top + 6);
                    trianglePath.Close();
                    Canvas.DrawPath(trianglePath, trianglePaint);
                    trianglePath.Reset();
                    trianglePath.MoveTo(right - 4, centerY);
                    trianglePath.LineTo(centerX + 2, bottom - 6);
                    trianglePath.LineTo(centerX + 2, top + 6);
                    trianglePath.Close();
                    Canvas.DrawPath(trianglePath, trianglePaint);
                }
            }
        }

        public void DrawLyrics()
        {
            // 设置歌词文本画笔
            using SKPaint lyricPaint = new()
            {
                Color = ThemeColorsManager.Current.LyricsText,
                IsAntialias = true
            };
            SKTypeface typeface = OpenUtauMobile.Utils.FontManager.OpenSans;
            SKFont lyricsFont = new(typeface, 15 * (float)ViewModel.Density);
            // 绘制歌词文本
            foreach (UNote note in Part.notes)
            {
                // 计算音符的绝对位置
                int noteStart = (int)(PositionX + note.position);
                int noteEnd = noteStart + note.duration;

                // 跳过左侧不可见的音符
                if (noteEnd < LeftTick)
                {
                    continue;
                }

                // 跳过右侧不可见的音符 notes按position排序
                if (noteStart > RightTick)
                {
                    break;
                }
                // 计算文本位置
                float x = noteStart * ViewModel.PianoRollTransformer.ZoomX + ViewModel.PianoRollTransformer.PanX;
                float y = (ViewConstants.TotalPianoKeys - note.tone - 1.5f) * HeightPerPianoKey * ViewModel.PianoRollTransformer.ZoomY + ViewModel.PianoRollTransformer.PanY;
                // 绘制歌词
                if (!string.IsNullOrEmpty(note.lyric))
                {
                    Canvas.DrawText(note.lyric, x, y, lyricsFont, lyricPaint);
                }
            }
        }

        public UNote? IsPointInNote(SKPoint point)
        {
            float left;
            float right;
            float top;
            float bottom;
            foreach (UNote note in Part.notes)
            {
                left = PositionX + note.position;
                right = left + note.duration;
                top = (ViewConstants.TotalPianoKeys - note.tone - 1) * HeightPerPianoKey;
                bottom = top + HeightPerPianoKey;
                if (point.X >= left && point.X <= right && point.Y >= top && point.Y <= bottom)
                {
                    return note;
                }
            }
            return null;
        }

        /// <summary>
        /// 判断点是否在可拖拽手柄上
        /// </summary>
        /// <param name="point">逻辑坐标</param>
        /// <returns></returns>
        public UNote? IsPointInHandle(SKPoint point)
        {
            if (ViewModel.SelectedNotes.Count == 0 || ViewModel.CurrentNoteEditMode != EditViewModel.NoteEditMode.EditNote)
            {
                return null;
            }
            foreach (UNote note in ViewModel.SelectedNotes)
            {
                float left = PositionX + note.position + note.duration + Spacing / ViewModel.PianoRollTransformer.ZoomX;
                float right = left + HandleSize / ViewModel.PianoRollTransformer.ZoomX;
                float top = (ViewConstants.TotalPianoKeys - note.tone - 0.5f) * HeightPerPianoKey - HalfHandleSize * ViewModel.PianoRollTransformer.ZoomY;
                float bottom = top + HandleSize * ViewModel.PianoRollTransformer.ZoomY;
                //Debug.WriteLine($"Handle: ({left}, {right}), Point: ({point.X})");
                if (point.X >= left && point.X <= right && point.Y >= top && point.Y <= bottom)
                {
                    return note;
                }
            }
            return null;
        }
    }
}
