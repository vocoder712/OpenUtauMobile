using OpenUtau.Core;
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
    public class DrawablePianoKeys
    {
        public SKCanvas Canvas { get; set; } = null!;
        public float HeightPerPianoKey { get; set; } // 不用× Density // 一般是180
        private Transformer Transformer { get; } = null!;
        private float Width => Canvas.DeviceClipBounds.Size.Width;
        private EditViewModel ViewModel { get; set; } = null!;

        public DrawablePianoKeys(SKCanvas canvas, EditViewModel viewModel)
        {
            Canvas = canvas;
            ViewModel = viewModel;
            HeightPerPianoKey = (float)(ViewModel.HeightPerPianoKey * ViewModel.Density);
            //Transformer = ViewModel.
        }

        public void Draw()
        {
            float viewTop = -Canvas.TotalMatrix.TransY / Canvas.TotalMatrix.ScaleY;
            float viewBottom = viewTop + Canvas.DeviceClipBounds.Size.Height / Canvas.TotalMatrix.ScaleY;
            int topKeyNum = Math.Max(0, (int)Math.Floor(viewTop / HeightPerPianoKey));
            int bottomKeyNum = Math.Min(ViewConstants.TotalPianoKeys, (int)Math.Ceiling(viewBottom / HeightPerPianoKey));
            float y = topKeyNum * HeightPerPianoKey;
            SKPaint paint = new SKPaint
            {
                Style = SKPaintStyle.Fill
            };
            for (int i = topKeyNum; i < bottomKeyNum; i++)
            {
                paint.Color = ViewConstants.PianoKeys[i].IsBlackKey ? ThemeColorsManager.Current.BlackPianoKey : ThemeColorsManager.Current.WhitePianoKey;
                Canvas.DrawRect(0, y, Width, HeightPerPianoKey, paint);
                y += HeightPerPianoKey;
            }
            // 绘制键名文本
            // 保存当前的变换矩阵
            SKMatrix originalMatrix = Canvas.TotalMatrix;
            // 恢复到默认矩阵，使文字不受缩放影响
            Canvas.ResetMatrix();
            y = (float)(topKeyNum + 0.5f) * HeightPerPianoKey * originalMatrix.ScaleY + originalMatrix.TransY;
            HeightPerPianoKey = HeightPerPianoKey * originalMatrix.ScaleY;
            PianoKey? drawingKey;
            SKPaint textPaint = new();
            using SKFont font = new()
            {
                Size = (float)(HeightPerPianoKey * 0.5),
                Typeface = ObjectProvider.NotoSansCJKscRegularTypeface
            };
            for (int i = topKeyNum; i < bottomKeyNum; i++)
            {
                drawingKey = ViewConstants.PianoKeys[i];
                int numberedNotationIndex = drawingKey.NoteNum - 60 - DocManager.Inst.Project.key;
                textPaint.Color = drawingKey.IsBlackKey ? ThemeColorsManager.Current.BlackPianoKeyText : ThemeColorsManager.Current.WhitePianoKeyText;
                Canvas.DrawText(drawingKey.NoteName, 5, y, font, textPaint);
                if (numberedNotationIndex >= 0 && numberedNotationIndex <= 11)
                {
                    Canvas.DrawText(MusicMath.NumberedNotations[numberedNotationIndex], 30, y, font, textPaint);
                }
                y += HeightPerPianoKey;
            }
            // 恢复变换矩阵
            Canvas.SetMatrix(originalMatrix);
        }

    }
}
