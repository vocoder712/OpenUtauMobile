using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Utils;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Views.Utils;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Views.DrawableObjects
{
    public class DrawableNotes
    {
        public SKCanvas Canvas { get; set; } = null!;
        public UVoicePart Part { get; set; } = null!;
        public float HeightPerPianoKey { get; set; }
        //public Transformer Transformer { get; set; }
        public SKColor NotesColor { get; set; }
        /// <summary>
        /// 实际坐标而非逻辑坐标
        /// </summary>
        private static float Spacing => 15f;
        /// <summary>
        /// 实际坐标而非逻辑坐标
        /// </summary>
        public float HalfHandleSize => (float)(8 * ViewModel.Density);
        private float HandleSize => HalfHandleSize * 2;
        /// <summary>
        /// 当前分片的起始位置tick
        /// </summary>
        public float PositionX { get; set; }
        public EditViewModel ViewModel { get; set; } = null!;
        public DrawableNotes(SKCanvas canvas, UVoicePart part, EditViewModel viewModel, SKColor notesColor)
        {
            Part = part;
            Canvas = canvas;
            HeightPerPianoKey = (float)viewModel.HeightPerPianoKey * (float)viewModel.Density;
            //Transformer = transformer;
            PositionX = part.position;
            ViewModel = viewModel;
            NotesColor = notesColor;
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
                foreach (UNote note in ViewModel.SelectedNotes)
                {
                    float left = (PositionX + note.position + note.duration) * ViewModel.PianoRollTransformer.ZoomX + ViewModel.PianoRollTransformer.PanX + Spacing;
                    // 右侧手柄
                    Canvas.DrawRect(left,
                        (ViewConstants.TotalPianoKeys - note.tone - 0.5f) * HeightPerPianoKey * ViewModel.PianoRollTransformer.ZoomY - HalfHandleSize + ViewModel.PianoRollTransformer.PanY,
                        HandleSize,
                        HandleSize,
                        handlePaint);
                }
            }
        }

        public void DrawLyrics()
        {
            // 保存当前的变换矩阵
            //SKMatrix originalMatrix = Canvas.TotalMatrix;
            // 恢复到默认矩阵，使文字不受缩放影响
            //Canvas.ResetMatrix();
            // 设置歌词文本画笔
            using SKPaint lyricPaint = new()
            {
                Color = ThemeColorsManager.Current.LyricsText,
                IsAntialias = true
            };
            SKFont lyricsFont = new(SKFontManager.Default.MatchCharacter('中'), 15 * (float)ViewModel.Density);
            // 绘制歌词文本
            foreach (UNote note in Part.notes)
            {
                // 计算文本位置
                float x = (PositionX + note.position + 5) * ViewModel.PianoRollTransformer.ZoomX + ViewModel.PianoRollTransformer.PanX;
                float y = (ViewConstants.TotalPianoKeys - note.tone - 1.5f) * HeightPerPianoKey * ViewModel.PianoRollTransformer.ZoomY + ViewModel.PianoRollTransformer.PanY;
                // 绘制歌词
                if (!string.IsNullOrEmpty(note.lyric))
                {
                    Canvas.DrawText(note.lyric, x, y, lyricsFont, lyricPaint);
                }
            }
            // 恢复原始矩阵
            //Canvas.SetMatrix(originalMatrix);
        }

        public UNote? IsPointInNote(SKPoint point)
        {
            float left = 0f;
            float right = 0f;
            float top = 0f;
            float bottom = 0f;
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
