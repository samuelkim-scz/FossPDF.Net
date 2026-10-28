using System;

namespace FossPDF.Infrastructure
{
    /// <summary>
    /// Corner radii of a rounded rectangle. Values are expressed in points and must be finite and
    /// non-negative. The four values are given clockwise starting at the top-left corner.
    /// </summary>
    public readonly struct BorderRadius : IEquatable<BorderRadius>
    {
        public BorderRadius(float topLeft, float topRight, float bottomRight, float bottomLeft)
        {
            Validate(topLeft, nameof(topLeft));
            Validate(topRight, nameof(topRight));
            Validate(bottomRight, nameof(bottomRight));
            Validate(bottomLeft, nameof(bottomLeft));

            TopLeft = topLeft;
            TopRight = topRight;
            BottomRight = bottomRight;
            BottomLeft = bottomLeft;
        }

        public float TopLeft { get; }
        public float TopRight { get; }
        public float BottomRight { get; }
        public float BottomLeft { get; }

        public static BorderRadius Zero => default;

        public static BorderRadius Uniform(float value)
        {
            return new BorderRadius(value, value, value, value);
        }

        public bool IsZero =>
            TopLeft <= 0 && TopRight <= 0 && BottomRight <= 0 && BottomLeft <= 0;

        public bool Equals(BorderRadius other)
        {
            return TopLeft.Equals(other.TopLeft) &&
                   TopRight.Equals(other.TopRight) &&
                   BottomRight.Equals(other.BottomRight) &&
                   BottomLeft.Equals(other.BottomLeft);
        }

        public override bool Equals(object? obj)
        {
            return obj is BorderRadius other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = TopLeft.GetHashCode();
                hashCode = (hashCode * 397) ^ TopRight.GetHashCode();
                hashCode = (hashCode * 397) ^ BottomRight.GetHashCode();
                hashCode = (hashCode * 397) ^ BottomLeft.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(BorderRadius left, BorderRadius right) => left.Equals(right);

        public static bool operator !=(BorderRadius left, BorderRadius right) => !left.Equals(right);

        public override string ToString()
        {
            return $"TopLeft: {TopLeft:N2}, TopRight: {TopRight:N2}, BottomRight: {BottomRight:N2}, BottomLeft: {BottomLeft:N2}";
        }

        private static void Validate(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameterName, value, "Corner radius must be a finite number.");

            if (value < 0)
                throw new ArgumentOutOfRangeException(parameterName, value, "Corner radius cannot be negative.");
        }
    }
}
