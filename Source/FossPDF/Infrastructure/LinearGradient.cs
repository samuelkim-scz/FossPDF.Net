using System;
using FossPDF.Helpers;

namespace FossPDF.Infrastructure
{
    /// <summary>
    /// A two-stop linear gradient used for backgrounds and borders. Stops are interpolated in the
    /// backend's sRGB space. Both color channels and alpha are interpolated without premultiplication,
    /// so a fully transparent black stop fades through grey; use a transparent tint of the target
    /// color (for example <c>#00ff0000</c> to <c>#ffff0000</c>) for a pure opacity fade.
    /// </summary>
    public sealed class LinearGradient
    {
        public LinearGradient(string fromColor, string toColor, GradientDirection direction = GradientDirection.Horizontal)
        {
            if (direction < GradientDirection.Horizontal || direction > GradientDirection.DiagonalTopRightToBottomLeft)
                throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown gradient direction.");

            ColorValidator.Validate(fromColor);
            ColorValidator.Validate(toColor);

            FromColor = fromColor;
            ToColor = toColor;
            Direction = direction;
        }

        /// <summary>Color at the start edge of the gradient.</summary>
        public string FromColor { get; }

        /// <summary>Color at the end edge of the gradient.</summary>
        public string ToColor { get; }

        public GradientDirection Direction { get; }

        public static LinearGradient Horizontal(string fromColor, string toColor) =>
            new LinearGradient(fromColor, toColor, GradientDirection.Horizontal);

        public static LinearGradient Vertical(string fromColor, string toColor) =>
            new LinearGradient(fromColor, toColor, GradientDirection.Vertical);

        public static LinearGradient Diagonal(string fromColor, string toColor) =>
            new LinearGradient(fromColor, toColor, GradientDirection.DiagonalTopLeftToBottomRight);

        public override string ToString() => $"Linear gradient {Direction}: {FromColor} -> {ToColor}";
    }
}
