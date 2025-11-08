using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System.Diagnostics;

namespace OpenUtauMobile.Views.Utils
{
    public class Transformer : ReactiveObject
    {
        // 当前平移和缩放状态
        [Reactive] public float PanX { get; private set; } = 0f; // 一般是负数
        [Reactive] public float PanY { get; private set; } = 0f; // 一般是负数
        [Reactive] public float ZoomX { get; private set; } = 1f;
        [Reactive] public float ZoomY { get; private set; } = 1f;

        // 变幻访问器
        public void SetPanX(float value)
        {
            PanX = InvalidatePanX(value);
        }
        public void SetPanY(float value)
        {
            PanY = InvalidatePanY(value);
        }
        public void SetZoomX(float value)
        {
            ZoomX = InvalidateZoomX(value);
        }
        public void SetZoomY(float value)
        {
            ZoomY = InvalidateZoomY(value);
        }

        // 缩放限制
        public float MinZoomX { get; set; } = 1f; // 最小横向缩放
        public float MinZoomY { get; set; } = 1f; // 最小纵向缩放
        public float MaxZoomX { get; set; } = 20f; // 最大横向缩放
        public float MaxZoomY { get; set; } = 1f; // 最大纵向缩放

        // 平移限制
        public float MinPanX { get; set; } = -500f; // 最小横向平移
        public float MinPanY { get; set; } = -100f; // 最小纵向平移
        public float MaxPanX { get; set; } = 0f; // 最大横向平移
        public float MaxPanY { get; set; } = 0f; // 最大纵向平移

        public Transformer(float panX = 0f, float panY = 0f, float zoomX = 1f, float zoomY = 1f)
        {
            // 初始化
            PanX = panX;
            PanY = panY;
            ZoomX = zoomX;
            ZoomY = zoomY;
        }


        // 平移操作状态
        private SKPoint _panInitial;
        private SKPoint _panStartActual;
        private bool _isPanning = false; // 是否正在平移

        // 缩放操作状态
        private SKPoint _zoomInitialCenterLogical; // 逻辑
        private float _initialDistanceX; // 实际
        private float _initialDistanceY; // 实际
        private float _initialZoomX;
        private float _initialZoomY;

        /// <summary>
        /// 获取当前变换矩阵
        /// </summary>
        public SKMatrix GetTransformMatrix()
        {
            return SKMatrix.CreateScale(ZoomX, ZoomY) // 先缩放
                .PostConcat(SKMatrix.CreateTranslation(PanX, PanY)); // 后平移
        }
        #region 坐标转换工具
        /// <summary>
        /// 实际坐标转逻辑坐标
        /// </summary>
        public SKPoint ActualToLogical(SKPoint actual)
        {
            return new SKPoint(
                (actual.X - PanX) / ZoomX,
                (actual.Y - PanY) / ZoomY
            );
        }
        /// <summary>
        /// 实际X坐标转逻辑X坐标
        /// </summary>
        /// <param name="actualX"></param>
        /// <returns></returns>
        public float ActualToLogicalX(float actualX)
        {
            return (actualX - PanX) / ZoomX;
        }
        /// <summary>
        /// 实际Y坐标转逻辑Y坐标
        /// </summary>
        /// <param name="actualY"></param>
        /// <returns></returns>
        public float ActualToLogicalY(float actualY)
        {
            return (actualY - PanY) / ZoomY;
        }

        /// <summary>
        /// 逻辑坐标转实际坐标
        /// </summary>
        public SKPoint LogicalToActual(SKPoint logical)
        {
            return new SKPoint(
                logical.X * ZoomX + PanX,
                logical.Y * ZoomY + PanY
            );
        }
        /// <summary>
        /// 逻辑X坐标转实际X坐标
        /// </summary>
        /// <param name="logicalX"></param>
        /// <returns></returns>
        public float LogicalToActualX(float logicalX)
        {
            return logicalX * ZoomX + PanX;
        }
        /// <summary>
        /// 实际Y坐标转逻辑Y坐标
        /// </summary>
        /// <param name="logicalY"></param>
        /// <returns></returns>
        public float LogicalToActualY(float logicalY)
        {
            return logicalY * ZoomY + PanY;
        }
        #endregion
        #region 缩放限制
        /// <summary>
        /// 设置缩放限制
        /// </summary>
        /// <param name="minX"></param>
        /// <param name="maxX"></param>
        /// <param name="minY"></param>
        /// <param name="maxY"></param>
        /// <returns>是否设置成功</returns>
        public bool SetPanLimit(float minX, float maxX, float minY, float maxY)
        {
            if (minX > maxX || minY > maxY)
            {
                return false; // 无效的限制
            }
            MinPanX = minX;
            MaxPanX = maxX;
            MinPanY = minY;
            MaxPanY = maxY;
            //Debug.WriteLine($"成功设置缩放限制 minPanY {MinPanY} maxPanY {MaxPanY}");
            // 立即应用限制
            PanX = InvalidatePanX(PanX);
            PanY = InvalidatePanY(PanY);
            return true;
        }

        public bool SetZoomLimit(float minX, float maxX, float minY, float maxY)
        {
            if (minX > maxX || minY > maxY)
            {
                return false; // 无效的限制
            }
            MinZoomX = minX;
            MaxZoomX = maxX;
            MinZoomY = minY;
            MaxZoomY = maxY;
            // 立即应用限制
            ZoomX = InvalidateZoomX(ZoomX);
            ZoomY = InvalidateZoomY(ZoomY);
            return true;
        }
        #endregion

        #region 平移操作
        public void StartPan(SKPoint actualStart)
        {
            _panInitial = new SKPoint(PanX, PanY);
            _panStartActual = actualStart;
            _isPanning = true;
        }

        public void UpdatePan(SKPoint currentActual)
        {
            if (!_isPanning)
            {
                return; // 如果没有开始平移，则不处理
            }
            var delta = currentActual - _panStartActual;
            PanX = Math.Clamp(_panInitial.X + delta.X, MinPanX, MaxPanX);
            PanY = Math.Clamp(_panInitial.Y + delta.Y, MinPanY, MaxPanY);
        }

        public void EndPan() 
        { 
            _isPanning = false; // 结束平移操作
        }
        #endregion

        #region 缩放操作
        /// <summary>
        /// 初始化一次缩放操作
        /// </summary>
        /// <param name="actualPoint1">画布坐标系</param>
        /// <param name="actualPoint2">画布坐标系</param>
        public void StartZoom(SKPoint actualPoint1, SKPoint actualPoint2)
        {
            // 保存初始状态
            _zoomInitialCenterLogical = MidPoint(ActualToLogical(actualPoint1), ActualToLogical(actualPoint2)); // 逻辑
            _initialDistanceX = Math.Abs(actualPoint1.X - actualPoint2.X);
            _initialDistanceY = Math.Abs(actualPoint1.Y - actualPoint2.Y);
            _initialZoomX = ZoomX;
            _initialZoomY = ZoomY;
        }

        /// <summary>
        /// 实时更新缩放
        /// </summary>
        /// <param name="currentPoint1"></param>
        /// <param name="currentPoint2"></param>
        public void UpdateZoom(SKPoint currentPoint1, SKPoint currentPoint2)
        {
            if (_initialDistanceX == 0 || _initialDistanceY == 0)
            {
                return; // 避免除以零
            }
            // 计算当前各轴距离
            float currentDistanceX = Math.Abs(currentPoint1.X - currentPoint2.X); // 实际
            float currentDistanceY = Math.Abs(currentPoint1.Y - currentPoint2.Y); // 实际
            // 计算理论缩放比例，相对于起始状态
            float scaleX = currentDistanceX / _initialDistanceX;
            float scaleY = currentDistanceY / _initialDistanceY;
            // 计算当前缩放中心
            SKPoint currentCenter = MidPoint(currentPoint1, currentPoint2); // 实际
            // 计算理论平移
            float panX = currentCenter.X - _zoomInitialCenterLogical.X * scaleX * _initialZoomX;
            float panY = currentCenter.Y - _zoomInitialCenterLogical.Y * scaleY * _initialZoomY;
            // 应用受限的缩放和平移
            ZoomX = InvalidateZoomX(_initialZoomX * scaleX);
            ZoomY = InvalidateZoomY(_initialZoomY * scaleY);
            PanX = InvalidatePanX(panX);
            PanY = InvalidatePanY(panY);
            Debug.WriteLine($"ZoomX: {ZoomX}, ZoomY: {ZoomY}"); // 调试输出
        }
        #endregion

        #region X缩放操作
        /// <summary>
        /// 初始化一次缩放操作
        /// </summary>
        /// <param name="actualPoint1">画布坐标系</param>
        /// <param name="actualPoint2">画布坐标系</param>
        public void StartXZoom(SKPoint actualPoint1, SKPoint actualPoint2)
        {

            // 保存初始状态
            _zoomInitialCenterLogical = MidPoint(ActualToLogical(actualPoint1), ActualToLogical(actualPoint2)); // 逻辑
            _initialDistanceX = Math.Abs(actualPoint1.X - actualPoint2.X);
            _initialZoomX = ZoomX;
        }

        /// <summary>
        /// 实时更新缩放
        /// </summary>
        /// <param name="currentPoint1"></param>
        /// <param name="currentPoint2"></param>
        public void UpdateXZoom(SKPoint currentPoint1, SKPoint currentPoint2)
        {
            if (_initialDistanceX == 0)
            {
                return; // 避免除以零
            }
            // 计算当前各轴距离
            float currentDistanceX = Math.Abs(currentPoint1.X - currentPoint2.X); // 实际
            // 计算理论缩放比例，相对于起始状态
            float scaleX = currentDistanceX / _initialDistanceX;
            // 计算当前缩放中心
            SKPoint currentCenter = MidPoint(currentPoint1, currentPoint2); // 实际
            // 计算理论平移
            float panX = currentCenter.X - _zoomInitialCenterLogical.X * scaleX * _initialZoomX;
            // 应用受限的缩放和平移
            ZoomX = InvalidateZoomX(_initialZoomX * scaleX);
            if (ZoomX != _initialZoomX * scaleX)
            {
                // 如果缩放被限制，重新计算panX
                panX = currentCenter.X - _zoomInitialCenterLogical.X * ZoomX;
            }
            PanX = InvalidatePanX(panX);
            //Debug.WriteLine($"仅X缩放: {ZoomX}, ZoomY: {ZoomY}"); // 调试输出
        }
        #endregion

        #region Y缩放操作
        /// <summary>
        /// 初始化一次缩放操作
        /// </summary>
        /// <param name="actualPoint1">画布坐标系</param>
        /// <param name="actualPoint2">画布坐标系</param>
        public void StartYZoom(SKPoint actualPoint1, SKPoint actualPoint2)
        {
            // 保存初始状态
            _zoomInitialCenterLogical = MidPoint(ActualToLogical(actualPoint1), ActualToLogical(actualPoint2)); // 逻辑
            _initialDistanceY = Math.Abs(actualPoint1.Y - actualPoint2.Y);
            _initialZoomY = ZoomY;
        }

        /// <summary>
        /// 实时更新缩放
        /// </summary>
        /// <param name="currentPoint1"></param>
        /// <param name="currentPoint2"></param>
        public void UpdateYZoom(SKPoint currentPoint1, SKPoint currentPoint2)
        {
            if (_initialDistanceX == 0 || _initialDistanceY == 0)
            {
                return; // 避免除以零
            }
            // 计算当前各轴距离
            float currentDistanceY = Math.Abs(currentPoint1.Y - currentPoint2.Y); // 实际
            // 计算理论缩放比例，相对于起始状态
            float scaleY = currentDistanceY / _initialDistanceY;
            // 计算当前缩放中心
            SKPoint currentCenter = MidPoint(currentPoint1, currentPoint2); // 实际
            // 计算理论平移
            float panY = currentCenter.Y - _zoomInitialCenterLogical.Y * scaleY * _initialZoomY;
            // 应用受限的缩放和平移
            ZoomY = InvalidateZoomY(_initialZoomY * scaleY);
            PanY = InvalidatePanY(panY);
            Debug.WriteLine($"仅Y缩放: {ZoomX}, ZoomY: {ZoomY}"); // 调试输出
        }
        #endregion

        /// <summary>
        /// 计算两点之间的中点
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        private static SKPoint MidPoint(SKPoint a, SKPoint b) =>
            new SKPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        #region 限制检查
        private float InvalidatePanX(float value)
        {
            return Math.Clamp(value, MinPanX, MaxPanX);
        }
        private float InvalidatePanY(float value)
        {
            Debug.WriteLine($"当前PanY限制：minPanY {MinPanY} maxPanY {MaxPanY}");
            return Math.Clamp(value, MinPanY, MaxPanY);
        }
        private float InvalidateZoomX(float value)
        {
            return Math.Clamp(value, MinZoomX, MaxZoomX);
        }
        private float InvalidateZoomY(float value)
        {
            return Math.Clamp(value, MinZoomY, MaxZoomY);
        }
        #endregion
    }
}
