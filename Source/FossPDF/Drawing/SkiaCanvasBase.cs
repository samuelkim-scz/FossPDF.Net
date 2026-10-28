using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FossPDF.Infrastructure;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace FossPDF.Drawing
{
    internal abstract class SkiaCanvasBase : ICanvas, IRenderingCanvas, IDisposable
    {
        private sealed class ZIndexLayer : IDisposable
        {
            public SKPictureRecorder Recorder { get; } = new SKPictureRecorder();
            public SKCanvas Canvas { get; }

            public ZIndexLayer(SKRect bounds)
            {
                Canvas = Recorder.BeginRecording(bounds);
            }

            public void Dispose()
            {
                Canvas.Dispose();
                Recorder.Dispose();
            }
        }

        private bool ZIndexLayersEnabled { get; set; }
        private int CurrentZIndex { get; set; }
        private SKRect PageBounds { get; set; }
        private Dictionary<int, ZIndexLayer> ZIndexLayers { get; } = new Dictionary<int, ZIndexLayer>();

        internal SKCanvas? Canvas { get; set; }

        protected SKCanvas ActiveCanvas
        {
            get
            {
                if (ZIndexLayersEnabled && ZIndexLayers.TryGetValue(CurrentZIndex, out var layer))
                    return layer.Canvas;

                return Canvas ?? throw new InvalidOperationException("No page is being rendered.");
            }
        }

        /// <summary>
        /// Captures each z-index level in its own picture so the page can compose them in depth order.
        /// </summary>
        internal void EnableZIndexLayers()
        {
            ZIndexLayersEnabled = true;
        }

        protected void BeginZIndexLayers(Size pageSize)
        {
            DisposeZIndexLayers();

            PageBounds = new SKRect(0, 0, pageSize.Width, pageSize.Height);
            CurrentZIndex = 0;

            if (ZIndexLayersEnabled)
                GetZIndexLayer(0);
        }

        protected void EndZIndexLayers()
        {
            if (!ZIndexLayersEnabled)
                return;

            var layers = ZIndexLayers
                .OrderBy(x => x.Key)
                .Select(x => x.Value)
                .ToList();

            ZIndexLayers.Clear();

            try
            {
                foreach (var layer in layers)
                {
                    using var picture = layer.Recorder.EndRecording();
                    Canvas?.DrawPicture(picture);
                }
            }
            finally
            {
                foreach (var layer in layers)
                    layer.Dispose();

                CurrentZIndex = 0;
            }
        }

        protected void DisposeZIndexLayers()
        {
            foreach (var layer in ZIndexLayers.Values)
                layer.Dispose();

            ZIndexLayers.Clear();
            CurrentZIndex = 0;
        }

        private SKCanvas GetZIndexLayer(int index)
        {
            if (ZIndexLayers.TryGetValue(index, out var existing))
                return existing.Canvas;

            var layer = new ZIndexLayer(PageBounds);
            ZIndexLayers.Add(index, layer);
            return layer.Canvas;
        }

        public int GetZIndex()
        {
            return CurrentZIndex;
        }

        public void SetZIndex(int index)
        {
            if (!ZIndexLayersEnabled)
            {
                CurrentZIndex = index;
                return;
            }

            var matrix = ActiveCanvas.TotalMatrix;

            CurrentZIndex = index;
            GetZIndexLayer(index).SetMatrix(matrix);
        }

        public Matrix4x4 Transform
        {
            get
            {
                var matrix = ActiveCanvas.TotalMatrix;

                return new Matrix4x4(
                    matrix.ScaleX, matrix.SkewY, 0, 0,
                    matrix.SkewX, matrix.ScaleY, 0, 0,
                    0, 0, 1, 0,
                    matrix.TransX, matrix.TransY, 0, 1);
            }
        }

        public abstract void Dispose();

        public abstract void BeginDocument();
        public abstract void EndDocument();

        public abstract void BeginPage(Size size);
        public abstract void EndPage();

        public void Translate(Position vector)
        {
            ActiveCanvas.Translate(vector.X, vector.Y);
        }

        public void DrawRectangle(Position vector, Size size, string color)
        {
            if (size.Width < Size.Epsilon || size.Height < Size.Epsilon)
                return;

            var paint = color.ColorToPaint();
            ActiveCanvas.DrawRect(vector.X, vector.Y, size.Width, size.Height, paint);
        }

        public void DrawLine(Position start, Position end, LineStyle style)
        {
            if (style.Width < Size.Epsilon)
                return;

            var length = GetLineLength(start, end);

            if (length < Size.Epsilon)
                return;

            using var shader = CreateLineShader(style, start, end);
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = SKColor.Parse(style.Color ?? Helpers.Colors.Black),
                Shader = shader
            };

            using var path = new SKPath();

            if (style.DashPattern == null || style.DashPattern.Length < 2)
            {
                AddLineQuad(path, start, end, style.Width, 0, length);
            }
            else
            {
                // Dashes are drawn as filled quads instead of an SKPathEffect: Skia's SVG backend
                // silently drops path effects, so this keeps dashes vector in every backend and
                // makes the dash geometry exactly the requested lengths in points.
                AddDashedLineQuads(path, start, end, style, length);
            }

            ActiveCanvas.DrawPath(path, paint);
        }

        private static float GetLineLength(Position start, Position end)
        {
            var deltaX = end.X - start.X;
            var deltaY = end.Y - start.Y;

            return (float)Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        }

        private static void AddLineQuad(SKPath path, Position start, Position end, float width, float from, float to)
        {
            var deltaX = end.X - start.X;
            var deltaY = end.Y - start.Y;
            var length = GetLineLength(start, end);

            if (length < Size.Epsilon || to - from <= 0)
                return;

            var directionX = deltaX / length;
            var directionY = deltaY / length;
            var halfWidth = width / 2;
            var normalX = -directionY * halfWidth;
            var normalY = directionX * halfWidth;

            var startX = start.X + directionX * from;
            var startY = start.Y + directionY * from;
            var endX = start.X + directionX * to;
            var endY = start.Y + directionY * to;

            path.MoveTo(startX + normalX, startY + normalY);
            path.LineTo(endX + normalX, endY + normalY);
            path.LineTo(endX - normalX, endY - normalY);
            path.LineTo(startX - normalX, startY - normalY);
            path.Close();
        }

        private static void AddDashedLineQuads(SKPath path, Position start, Position end, LineStyle style, float length)
        {
            var pattern = style.DashPattern!;
            var total = 0f;

            foreach (var value in pattern)
                total += value;

            if (total < Size.Epsilon)
                return;

            var offset = style.DashPhase % total;

            if (offset < 0)
                offset += total;

            var index = 0;

            while (offset >= pattern[index])
            {
                offset -= pattern[index];
                index = (index + 1) % pattern.Length;
            }

            var remaining = pattern[index] - offset;
            var isDash = index % 2 == 0;
            var position = 0f;

            while (position < length)
            {
                var segment = Math.Min(remaining, length - position);

                if (isDash && segment > 0)
                    AddLineQuad(path, start, end, style.Width, position, position + segment);

                position += segment;
                remaining -= segment;

                // Zero-length dashes/gaps advance immediately; at least one pattern value is
                // positive (enforced by the fluent API), so this always terminates.
                if (remaining <= Size.Epsilon)
                {
                    index = (index + 1) % pattern.Length;
                    remaining = pattern[index];
                    isDash = !isDash;
                }
            }
        }

        private static SKShader? CreateLineShader(LineStyle style, Position start, Position end)
        {
            if (style.GradientColors == null || style.GradientColors.Length < 2)
                return null;

            return SKShader.CreateLinearGradient(
                new SKPoint(start.X, start.Y),
                new SKPoint(end.X, end.Y),
                style.GradientColors.Select(SKColor.Parse).ToArray(),
                null,
                SKShaderTileMode.Clamp);
        }

        public virtual void DrawShadow(Position vector, Size size, BorderRadius cornerRadius, BoxShadowStyle shadow)
        {
            if (!TryCreateShadowShape(vector, size, cornerRadius, shadow, out var rect, out var radii))
                return;

            using var roundRect = CreateRoundRect(rect, radii);
            using var maskFilter = shadow.Blur > 0
                ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, shadow.Blur / 2)
                : null;
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Color = SKColor.Parse(shadow.Color),
                MaskFilter = maskFilter
            };

            ActiveCanvas.DrawRoundRect(roundRect, paint);
        }

        protected static bool TryCreateShadowShape(
            Position vector,
            Size size,
            BorderRadius cornerRadius,
            BoxShadowStyle shadow,
            out SKRect rect,
            out SKPoint[] radii)
        {
            rect = default;
            radii = Array.Empty<SKPoint>();

            if (size.Width < Size.Epsilon || size.Height < Size.Epsilon)
                return false;

            var spread = shadow.Spread;

            rect = new SKRect(
                vector.X - spread + shadow.OffsetX,
                vector.Y - spread + shadow.OffsetY,
                vector.X + size.Width + spread + shadow.OffsetX,
                vector.Y + size.Height + spread + shadow.OffsetY);

            if (rect.Width < Size.Epsilon || rect.Height < Size.Epsilon)
                return false;

            radii = ToRadii(
                Math.Max(0, cornerRadius.TopLeft + spread),
                Math.Max(0, cornerRadius.TopRight + spread),
                Math.Max(0, cornerRadius.BottomRight + spread),
                Math.Max(0, cornerRadius.BottomLeft + spread));

            return true;
        }

        public void DrawRectangle(Position vector, Size size, RectangleStyle style)
        {
            if (size.Width < Size.Epsilon || size.Height < Size.Epsilon)
                return;

            if (style.HasFill)
                DrawDecoratedFill(vector, size, style);

            if (style.HasBorder)
                DrawDecoratedBorder(vector, size, style);
        }

        private void DrawDecoratedFill(Position vector, Size size, RectangleStyle style)
        {
            if (style.FillGradient == null)
            {
                var paint = style.FillColor!.ColorToPaint();

                if (style.CornerRadius.IsZero)
                {
                    ActiveCanvas.DrawRect(vector.X, vector.Y, size.Width, size.Height, paint);
                }
                else
                {
                    using var roundRect = CreateRoundRect(vector, size, style.CornerRadius);
                    ActiveCanvas.DrawRoundRect(roundRect, paint);
                }

                return;
            }

            using var gradientPaint = CreateGradientPaint(style.FillGradient, vector, size);
            using var gradientRoundRect = CreateRoundRect(vector, size, style.CornerRadius);
            ActiveCanvas.DrawRoundRect(gradientRoundRect, gradientPaint);
        }

        private void DrawDecoratedBorder(Position vector, Size size, RectangleStyle style)
        {
            var expansion = GetBorderExpansion(style);

            var outerRect = new SKRect(
                vector.X - expansion.Left,
                vector.Y - expansion.Top,
                vector.X + size.Width + expansion.Right,
                vector.Y + size.Height + expansion.Bottom);

            var innerRect = new SKRect(
                outerRect.Left + style.Left,
                outerRect.Top + style.Top,
                outerRect.Right - style.Right,
                outerRect.Bottom - style.Bottom);

            var outerRadii = GetOuterRadii(style, expansion);
            var innerRadii = GetInnerRadii(style, expansion);

            using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
            using var outerRoundRect = CreateRoundRect(outerRect, outerRadii);
            path.AddRoundRect(outerRoundRect, SKPathDirection.Clockwise);

            var hasInnerRectangle = innerRect.Width > Size.Epsilon && innerRect.Height > Size.Epsilon;

            if (hasInnerRectangle)
            {
                using var innerRoundRect = CreateRoundRect(innerRect, innerRadii);
                path.AddRoundRect(innerRoundRect, SKPathDirection.CounterClockwise);
            }

            if (style.BorderGradient != null)
            {
                using var gradientPaint = CreateGradientPaint(style.BorderGradient, vector, size);
                ActiveCanvas.DrawPath(path, gradientPaint);
            }
            else
            {
                ActiveCanvas.DrawPath(path, (style.BorderColor ?? Helpers.Colors.Black).ColorToPaint());
            }
        }

        public void DrawText(SKTextBlob skTextBlob, Position position, TextStyle style, DocumentSpecificFontManager fontManager)
        {
            ActiveCanvas.DrawText(skTextBlob, position.X, position.Y, fontManager.ToPaint(style));
        }

        public void DrawImage(SKImage image, Position vector, Size size)
        {
            ActiveCanvas.DrawImage(image, new SKRect(vector.X, vector.Y, size.Width, size.Height));
        }

        public void SaveState()
        {
            ActiveCanvas.Save();
        }

        public void RestoreState()
        {
            ActiveCanvas.Restore();
        }

        public void ClipRectangle(Position vector, Size size, BorderRadius cornerRadius)
        {
            if (size.Width < Size.Epsilon || size.Height < Size.Epsilon)
                return;

            if (cornerRadius.IsZero)
            {
                ActiveCanvas.ClipRect(new SKRect(vector.X, vector.Y, vector.X + size.Width, vector.Y + size.Height), SKClipOperation.Intersect, true);
            }
            else
            {
                using var roundRect = CreateRoundRect(vector, size, cornerRadius);
                ActiveCanvas.ClipRoundRect(roundRect, SKClipOperation.Intersect, true);
            }
        }

        public void DrawHyperlink(string url, Size size)
        {
            ActiveCanvas.DrawUrlAnnotation(new SKRect(0, 0, size.Width, size.Height), url).Dispose();
        }

        public void DrawSectionLink(string sectionName, Size size)
        {
            ActiveCanvas.DrawLinkDestinationAnnotation(new SKRect(0, 0, size.Width, size.Height), sectionName).Dispose();
        }

        public void DrawSection(string sectionName)
        {
            ActiveCanvas.DrawNamedDestinationAnnotation(new SKPoint(0, 0), sectionName).Dispose();
        }

        public void Rotate(float angle)
        {
            ActiveCanvas.RotateDegrees(angle);
        }

        public void Scale(float scaleX, float scaleY)
        {
            ActiveCanvas.Scale(scaleX, scaleY);
        }

        private readonly struct BorderExpansion
        {
            public BorderExpansion(float left, float top, float right, float bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }

            public float Left { get; }
            public float Top { get; }
            public float Right { get; }
            public float Bottom { get; }
        }

        private static BorderExpansion GetBorderExpansion(RectangleStyle style)
        {
            return new BorderExpansion(
                GetExpansion(style.Left, style.BorderAlignment),
                GetExpansion(style.Top, style.BorderAlignment),
                GetExpansion(style.Right, style.BorderAlignment),
                GetExpansion(style.Bottom, style.BorderAlignment));
        }

        private static float GetExpansion(float width, BorderAlignment alignment)
        {
            return alignment switch
            {
                BorderAlignment.Inside => 0,
                BorderAlignment.Center => width / 2,
                BorderAlignment.Outside => width,
                _ => 0
            };
        }

        private static SKPoint[] GetOuterRadii(RectangleStyle style, BorderExpansion expansion)
        {
            var radius = style.CornerRadius;

            var topLeft = radius.TopLeft + Math.Max(expansion.Top, expansion.Left);
            var topRight = radius.TopRight + Math.Max(expansion.Top, expansion.Right);
            var bottomRight = radius.BottomRight + Math.Max(expansion.Bottom, expansion.Right);
            var bottomLeft = radius.BottomLeft + Math.Max(expansion.Bottom, expansion.Left);

            return ToRadii(topLeft, topRight, bottomRight, bottomLeft);
        }

        private static SKPoint[] GetInnerRadii(RectangleStyle style, BorderExpansion expansion)
        {
            var radius = style.CornerRadius;

            var topLeft = Math.Max(0, radius.TopLeft + Math.Max(expansion.Top, expansion.Left) - Math.Max(style.Top, style.Left));
            var topRight = Math.Max(0, radius.TopRight + Math.Max(expansion.Top, expansion.Right) - Math.Max(style.Top, style.Right));
            var bottomRight = Math.Max(0, radius.BottomRight + Math.Max(expansion.Bottom, expansion.Right) - Math.Max(style.Bottom, style.Right));
            var bottomLeft = Math.Max(0, radius.BottomLeft + Math.Max(expansion.Bottom, expansion.Left) - Math.Max(style.Bottom, style.Left));

            return ToRadii(topLeft, topRight, bottomRight, bottomLeft);
        }

        protected static SKPoint[] ToRadii(float topLeft, float topRight, float bottomRight, float bottomLeft)
        {
            return new[]
            {
                new SKPoint(topLeft, topLeft),
                new SKPoint(topRight, topRight),
                new SKPoint(bottomRight, bottomRight),
                new SKPoint(bottomLeft, bottomLeft)
            };
        }

        private static SKRoundRect CreateRoundRect(Position vector, Size size, BorderRadius radius)
        {
            var rect = new SKRect(vector.X, vector.Y, vector.X + size.Width, vector.Y + size.Height);
            return CreateRoundRect(rect, ToRadii(radius.TopLeft, radius.TopRight, radius.BottomRight, radius.BottomLeft));
        }

        protected static SKRoundRect CreateRoundRect(SKRect rect, SKPoint[] radii)
        {
            var roundRect = new SKRoundRect();
            roundRect.SetRectRadii(rect, radii);
            return roundRect;
        }

        private static SKPaint CreateGradientPaint(LinearGradient gradient, Position vector, Size size)
        {
            var (start, end) = GetGradientPoints(gradient.Direction, vector, size);
            var shader = SKShader.CreateLinearGradient(
                start,
                end,
                new[] { SKColor.Parse(gradient.FromColor), SKColor.Parse(gradient.ToColor) },
                null,
                SKShaderTileMode.Clamp);

            return new SKPaint
            {
                IsAntialias = true,
                Shader = shader
            };
        }

        private static (SKPoint Start, SKPoint End) GetGradientPoints(GradientDirection direction, Position vector, Size size)
        {
            var left = vector.X;
            var top = vector.Y;
            var right = vector.X + size.Width;
            var bottom = vector.Y + size.Height;

            return direction switch
            {
                GradientDirection.Horizontal => (new SKPoint(left, top), new SKPoint(right, top)),
                GradientDirection.Vertical => (new SKPoint(left, top), new SKPoint(left, bottom)),
                GradientDirection.DiagonalTopLeftToBottomRight => (new SKPoint(left, top), new SKPoint(right, bottom)),
                GradientDirection.DiagonalTopRightToBottomLeft => (new SKPoint(right, top), new SKPoint(left, bottom)),
                _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
            };
        }
    }
}
