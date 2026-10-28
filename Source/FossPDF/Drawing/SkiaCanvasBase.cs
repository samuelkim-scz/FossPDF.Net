using System;
using FossPDF.Infrastructure;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace FossPDF.Drawing
{
    internal abstract class SkiaCanvasBase : ICanvas, IRenderingCanvas, IDisposable
    {
        internal SKCanvas? Canvas { get; set; }
        protected SKCanvas ActiveCanvas => Canvas ?? throw new InvalidOperationException("No page is being rendered.");

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

        private static SKPoint[] ToRadii(float topLeft, float topRight, float bottomRight, float bottomLeft)
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

        private static SKRoundRect CreateRoundRect(SKRect rect, SKPoint[] radii)
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
