using OpenUtauMobile.Views.Utils;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.Views.DrawableObjects
{
    public class DrawableTrackPlayPosLine
    {
        public SKCanvas Canvas { get; set; } = null!;
        public int PlayPosTick { get; set; }
        public double TotalHeight { get; set; } = 0d;
        public double ResolutionX { get; set; } = 480d;
        public DrawableTrackPlayPosLine(SKCanvas canvas, int playPosTick, double totalHeight, double resolutionX = 480)
        {
            Canvas = canvas;
            PlayPosTick = playPosTick;
            TotalHeight = totalHeight;
            ResolutionX = resolutionX;
        }
        public void Draw()
        {
            // 保存当前的变换矩阵
            SKMatrix originalMatrix = Canvas.TotalMatrix;
            // 恢复到默认矩阵，使文字不受缩放影响
            Canvas.ResetMatrix();
            // 计算位置
            float x = (float)(PlayPosTick * originalMatrix.ScaleX + originalMatrix.TransX);
            float y = 0f;
            // 创建画笔
            using (SKPaint paint = new SKPaint())
            {
                paint.StrokeWidth = 3f; // 设置线条宽度
                paint.Color = SKColor.Parse("#B3F353"); // 设置线条颜色为绿色
                // 绘制线条
                Canvas.DrawLine(x, y, x, (float)TotalHeight, paint);
            }
            // 恢复原始矩阵
            Canvas.SetMatrix(originalMatrix);
        }
    }
}
