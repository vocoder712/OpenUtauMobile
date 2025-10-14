using OpenUtau.Core;
using OpenUtauMobile.Utils;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Views.DrawableObjects
{
    public class DrawableTrackBackground
    {
        public SKCanvas Canvas { get; set; } = null!;
        public double HeightPerTrack { get; set; }
        public DrawableTrackBackground(SKCanvas canvas, double heightPerTrack)
        {
            Canvas = canvas;
            HeightPerTrack = heightPerTrack;
        }
        public void Draw()
        {
            // 保存当前的变换矩阵
            SKMatrix originalMatrix = Canvas.TotalMatrix;
            // 恢复到默认矩阵
            Canvas.ResetMatrix();
            for (int i = 1; i < DocManager.Inst.Project.tracks.Count + 1; i++)
            {
                float y1 = i * (float)HeightPerTrack * originalMatrix.ScaleY + originalMatrix.TransY;
                Canvas.DrawLine(0, y1, Canvas.DeviceClipBounds.Size.Width, y1, ThemeColorsManager.Current.TrackHorizontalLinePaint);
            }
            // 恢复原始矩阵
            Canvas.SetMatrix(originalMatrix);
        }
    }
}
