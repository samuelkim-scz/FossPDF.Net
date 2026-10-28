using System;
using FossPDF.Elements;
using FossPDF.Infrastructure;

namespace FossPDF.Fluent
{
    /// <summary>
    /// Rounded corners, border alignment, gradients and rounded clipping for containers. These
    /// methods style the same decoration as <c>Background</c> and <c>Border</c>, so chaining them in
    /// any order produces one consistent rectangle.
    /// </summary>
    public static class ContainerStyleExtensions
    {
        private static RectangleDecoration GetDecoration(IContainer element)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));

            return element as RectangleDecoration ?? element.Element(new Background());
        }

        /// <summary>
        /// Rounds all four corners by the same radius. The radius only affects the background, the
        /// border and content clipped with <c>Clip</c>; it does not change layout bounds.
        /// </summary>
        public static IContainer CornerRadius(this IContainer element, float value, Unit unit = Unit.Point)
        {
            return element.CornerRadius(BorderRadius.Uniform(value.ToPoints(unit)));
        }

        /// <summary>
        /// Rounds each corner independently, given clockwise starting at the top-left corner.
        /// </summary>
        public static IContainer CornerRadius(this IContainer element, float topLeft, float topRight, float bottomRight, float bottomLeft, Unit unit = Unit.Point)
        {
            return element.CornerRadius(new BorderRadius(
                topLeft.ToPoints(unit),
                topRight.ToPoints(unit),
                bottomRight.ToPoints(unit),
                bottomLeft.ToPoints(unit)));
        }

        /// <summary>Rounds the corners using radii expressed in points.</summary>
        public static IContainer CornerRadius(this IContainer element, BorderRadius radius)
        {
            var decoration = GetDecoration(element);
            decoration.CornerRadius = radius;
            return decoration;
        }

        public static IContainer CornerRadiusTopLeft(this IContainer element, float value, Unit unit = Unit.Point)
        {
            return element.CornerRadius(x => x.TopLeft = value.ToPoints(unit));
        }

        public static IContainer CornerRadiusTopRight(this IContainer element, float value, Unit unit = Unit.Point)
        {
            return element.CornerRadius(x => x.TopRight = value.ToPoints(unit));
        }

        public static IContainer CornerRadiusBottomRight(this IContainer element, float value, Unit unit = Unit.Point)
        {
            return element.CornerRadius(x => x.BottomRight = value.ToPoints(unit));
        }

        public static IContainer CornerRadiusBottomLeft(this IContainer element, float value, Unit unit = Unit.Point)
        {
            return element.CornerRadius(x => x.BottomLeft = value.ToPoints(unit));
        }

        private static IContainer CornerRadius(this IContainer element, Action<BorderRadiusEditor> handler)
        {
            var decoration = GetDecoration(element);
            var editor = new BorderRadiusEditor(decoration.CornerRadius);
            handler(editor);
            decoration.CornerRadius = editor.ToBorderRadius();
            return decoration;
        }

        /// <summary>
        /// Places the border inside, centered on, or outside the layout bounds. The default is
        /// <c>BorderAlignment.Center</c>, which matches the historical border behavior.
        /// </summary>
        public static IContainer BorderAlignment(this IContainer element, Infrastructure.BorderAlignment alignment)
        {
            if (alignment < Infrastructure.BorderAlignment.Inside || alignment > Infrastructure.BorderAlignment.Outside)
                throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "Unknown border alignment.");

            var decoration = GetDecoration(element);
            decoration.BorderAlignment = alignment;
            return decoration;
        }

        /// <summary>Fills the background with a two-stop linear gradient.</summary>
        public static IContainer BackgroundGradient(this IContainer element, LinearGradient gradient)
        {
            if (gradient == null)
                throw new ArgumentNullException(nameof(gradient));

            var decoration = GetDecoration(element);
            decoration.FillGradient = gradient;
            return decoration;
        }

        /// <summary>Fills the background with a two-stop linear gradient between the given colors.</summary>
        public static IContainer BackgroundGradient(this IContainer element, string fromColor, string toColor, GradientDirection direction = GradientDirection.Horizontal)
        {
            return element.BackgroundGradient(new LinearGradient(fromColor, toColor, direction));
        }

        /// <summary>Paints the border with a two-stop linear gradient.</summary>
        public static IContainer BorderGradient(this IContainer element, LinearGradient gradient)
        {
            if (gradient == null)
                throw new ArgumentNullException(nameof(gradient));

            var decoration = GetDecoration(element);
            decoration.BorderGradient = gradient;
            return decoration;
        }

        /// <summary>Paints the border with a two-stop linear gradient between the given colors.</summary>
        public static IContainer BorderGradient(this IContainer element, string fromColor, string toColor, GradientDirection direction = GradientDirection.Horizontal)
        {
            return element.BorderGradient(new LinearGradient(fromColor, toColor, direction));
        }

        /// <summary>
        /// Paints a box shadow behind the container, following its corner radii. The shadow is
        /// drawn before the background, border and content, so it never covers them. A zero blur
        /// keeps the shadow sharp; a positive blur diffuses its edge.
        /// </summary>
        public static IContainer Shadow(this IContainer element, BoxShadowStyle shadow)
        {
            if (shadow == null)
                throw new ArgumentNullException(nameof(shadow));

            shadow.Validate();

            var decoration = GetDecoration(element);
            decoration.Shadow = shadow;
            return decoration;
        }

        /// <summary>
        /// Clips the content to the container bounds, rounded by the configured corner radii.
        /// Backgrounds and borders are not clipped. Combine with <c>CornerRadius</c> to clip content
        /// to rounded bounds.
        /// </summary>
        public static IContainer Clip(this IContainer element)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));

            var decoration = element as RectangleDecoration ?? element.Element(new Clip());
            decoration.ClipContent = true;
            return decoration;
        }

        private sealed class BorderRadiusEditor
        {
            public BorderRadiusEditor(BorderRadius radius)
            {
                TopLeft = radius.TopLeft;
                TopRight = radius.TopRight;
                BottomRight = radius.BottomRight;
                BottomLeft = radius.BottomLeft;
            }

            public float TopLeft { get; set; }
            public float TopRight { get; set; }
            public float BottomRight { get; set; }
            public float BottomLeft { get; set; }

            public BorderRadius ToBorderRadius()
            {
                return new BorderRadius(TopLeft, TopRight, BottomRight, BottomLeft);
            }
        }
    }
}
