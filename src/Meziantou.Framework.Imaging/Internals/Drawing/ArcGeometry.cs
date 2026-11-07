using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The geometry of elliptical arcs: cutting an arc in pieces of at most a quarter turn, and the shapes made of them.</summary>
/// <remarks>
/// <para>
/// Angles are in half turns (1 is 180 degrees) from the first half-diameter of the ellipse toward the second one: with
/// the Y axis downward, a positive sweep turns clockwise on the image. Their sines and cosines come from
/// <see cref="DeterministicAngle"/>, and everything else from additions, multiplications, divisions and square roots: an
/// arc is the same on every platform.
/// </para>
/// <para>
/// Only the ends of the pieces are computed from angles. The points inside a piece come from the bisection of its two
/// unit vectors when it is flattened, as for the round joins of a stroke.
/// </para>
/// </remarks>
internal static class ArcGeometry
{
    /// <summary>
    /// Sends the arc of the ellipse <c>center + cos(t) u + sin(t) v</c> from the angle <paramref name="start"/>, over
    /// <paramref name="sweep"/> (at most a full turn either way), as pieces of at most a quarter turn. The arc starts at
    /// the current point of the receiver and ends exactly at the given end point.
    /// </summary>
    public static void Append<T>(ref T receiver, double centerX, double centerY, double ux, double uy, double vx, double vy, double start, double sweep, double endX, double endY)
        where T : struct, IPathReceiver
    {
        var count = (int)Math.Ceiling(Math.Abs(sweep) * 2);
        count = count < 1 ? 1 : count > 4 ? 4 : count;
        DeterministicAngle.SinCos(start, out var ay, out var ax);
        for (var i = 1; i <= count; i++)
        {
            DeterministicAngle.SinCos(start + (sweep * i / count), out var by, out var bx);
            var isLast = i == count;
            var x = isLast ? endX : centerX + ((ux * bx) + (vx * by));
            var y = isLast ? endY : centerY + ((uy * bx) + (vy * by));
            receiver.Arc(new ArcPiece(centerX, centerY, ux, uy, vx, vy, ax, ay, bx, by, x, y, isContinuation: i > 1));
            ax = bx;
            ay = by;
        }
    }

    /// <summary>
    /// Sends an arc of an ellipse whose axes are horizontal and vertical, given by its center and its angles in degrees.
    /// </summary>
    /// <param name="receiver">The receiver.</param>
    /// <param name="centerX">The horizontal coordinate of the center.</param>
    /// <param name="centerY">The vertical coordinate of the center.</param>
    /// <param name="radiusX">The horizontal radius.</param>
    /// <param name="radiusY">The vertical radius.</param>
    /// <param name="startAngle">The angle of the first point, in degrees from the positive x axis, clockwise on the image.</param>
    /// <param name="sweepAngle">The angle of the arc, in degrees; beyond a full turn either way, it is a full turn.</param>
    /// <param name="lineToStart"><see langword="true"/> to join the current point to the start of the arc by a straight segment; <see langword="false"/> to start a contour there.</param>
    public static void AppendCenterArc<T>(ref T receiver, double centerX, double centerY, double radiusX, double radiusY, double startAngle, double sweepAngle, bool lineToStart)
        where T : struct, IPathReceiver
    {
        var start = startAngle / 180;
        var sweep = sweepAngle / 180;
        sweep = sweep > 2 ? 2 : sweep < -2 ? -2 : sweep;
        DeterministicAngle.SinCos(start, out var sin, out var cos);
        var startX = centerX + (radiusX * cos);
        var startY = centerY + (radiusY * sin);
        if (lineToStart)
        {
            receiver.LineTo(startX, startY);
        }
        else
        {
            receiver.MoveTo(startX, startY);
        }

        if (sweep == 0)
            return;

        // A full turn ends where it starts, exactly: a contour that is closed after it gets no segment in between
        var endX = startX;
        var endY = startY;
        if (Math.Abs(sweep) < 2)
        {
            DeterministicAngle.SinCos(start + sweep, out sin, out cos);
            endX = centerX + (radiusX * cos);
            endY = centerY + (radiusY * sin);
        }

        Append(ref receiver, centerX, centerY, radiusX, 0, 0, radiusY, start, sweep, endX, endY);
    }

    /// <summary>Sends the closed contour of a rectangle whose corners are quarters of an ellipse.</summary>
    /// <remarks>
    /// The radii are limited to half the sides. The contour starts at the left end of the straight part of the top
    /// side and turns clockwise on the image; a side whose corners take all of it has no straight part.
    /// </remarks>
    public static void AppendRoundedRectangle<T>(ref T receiver, RectangleD rectangle, double radiusX, double radiusY)
        where T : struct, IPathReceiver
    {
        var left = rectangle.Left;
        var top = rectangle.Top;
        var right = rectangle.Right;
        var bottom = rectangle.Bottom;
        if (!(radiusX > 0 && radiusY > 0 && rectangle.Width > 0 && rectangle.Height > 0))
        {
            receiver.MoveTo(left, top);
            receiver.LineTo(right, top);
            receiver.LineTo(right, bottom);
            receiver.LineTo(left, bottom);
            receiver.Close();
            return;
        }

        // The ends of the straight parts of the sides. Each corner takes its radii from these coordinates, so that it
        // starts and ends exactly on them.
        var x0 = left + radiusX;
        var x1 = right - radiusX;
        var hasHorizontal = x1 > x0;
        if (!hasHorizontal)
        {
            x0 = x1 = left + (rectangle.Width / 2);
        }

        var y0 = top + radiusY;
        var y1 = bottom - radiusY;
        var hasVertical = y1 > y0;
        if (!hasVertical)
        {
            y0 = y1 = top + (rectangle.Height / 2);
        }

        receiver.MoveTo(x0, top);
        if (hasHorizontal)
        {
            receiver.LineTo(x1, top);
        }

        Append(ref receiver, x1, y0, right - x1, 0, 0, y0 - top, 1.5, 0.5, right, y0);
        if (hasVertical)
        {
            receiver.LineTo(right, y1);
        }

        Append(ref receiver, x1, y1, right - x1, 0, 0, bottom - y1, 0, 0.5, x1, bottom);
        if (hasHorizontal)
        {
            receiver.LineTo(x0, bottom);
        }

        Append(ref receiver, x0, y1, x0 - left, 0, 0, bottom - y1, 0.5, 0.5, left, y1);
        if (hasVertical)
        {
            receiver.LineTo(left, y0);
        }

        Append(ref receiver, x0, y0, x0 - left, 0, 0, y0 - top, 1, 0.5, x0, top);
        receiver.Close();
    }

    /// <summary>Sends the closed contour of a rectangle whose corners are quarters of circles, each with its own radius.</summary>
    /// <remarks>
    /// When the radii of the two corners of a side add up to more than that side, every radius is multiplied by the
    /// largest factor that makes them all fit, as CSS does for the radii of a border. The contour starts at the left
    /// end of the straight part of the top side and turns clockwise on the image; a corner without radius is a corner
    /// of the rectangle.
    /// </remarks>
    public static void AppendRoundedRectangle<T>(ref T receiver, RectangleD rectangle, in CornerRadii radii)
        where T : struct, IPathReceiver
    {
        var left = rectangle.Left;
        var top = rectangle.Top;
        var right = rectangle.Right;
        var bottom = rectangle.Bottom;
        var width = rectangle.Width;
        var height = rectangle.Height;
        if (radii.IsEmpty || !(width > 0 && height > 0))
        {
            receiver.MoveTo(left, top);
            receiver.LineTo(right, top);
            receiver.LineTo(right, bottom);
            receiver.LineTo(left, bottom);
            receiver.Close();
            return;
        }

        var factor = 1.0;
        Limit(ref factor, width, radii.TopLeft + radii.TopRight);
        Limit(ref factor, height, radii.TopRight + radii.BottomRight);
        Limit(ref factor, width, radii.BottomRight + radii.BottomLeft);
        Limit(ref factor, height, radii.BottomLeft + radii.TopLeft);
        var topLeft = radii.TopLeft * factor;
        var topRight = radii.TopRight * factor;
        var bottomRight = radii.BottomRight * factor;
        var bottomLeft = radii.BottomLeft * factor;

        // The ends of the straight part of each side, in the order of the contour. Each corner takes its radii from
        // these coordinates, so that it starts and ends exactly on them; two corners that take a whole side meet at
        // the end of the first one.
        var topStart = Math.Min(left + topLeft, right);
        var topEnd = Math.Max(right - topRight, topStart);
        var rightStart = Math.Min(top + topRight, bottom);
        var rightEnd = Math.Max(bottom - bottomRight, rightStart);
        var bottomStart = Math.Max(right - bottomRight, left);
        var bottomEnd = Math.Min(left + bottomLeft, bottomStart);
        var leftStart = Math.Max(bottom - bottomLeft, top);
        var leftEnd = Math.Min(top + topLeft, leftStart);

        receiver.MoveTo(topStart, top);
        if (topEnd > topStart)
        {
            receiver.LineTo(topEnd, top);
        }

        Corner(ref receiver, topEnd, rightStart, right - topEnd, rightStart - top, 1.5, right, rightStart);
        if (rightEnd > rightStart)
        {
            receiver.LineTo(right, rightEnd);
        }

        Corner(ref receiver, bottomStart, rightEnd, right - bottomStart, bottom - rightEnd, 0, bottomStart, bottom);
        if (bottomStart > bottomEnd)
        {
            receiver.LineTo(bottomEnd, bottom);
        }

        Corner(ref receiver, bottomEnd, leftStart, bottomEnd - left, bottom - leftStart, 0.5, left, leftStart);
        if (leftStart > leftEnd)
        {
            receiver.LineTo(left, leftEnd);
        }

        Corner(ref receiver, topStart, leftEnd, topStart - left, leftEnd - top, 1, topStart, top);
        receiver.Close();

        static void Limit(ref double factor, double side, double sum)
        {
            if (sum > side)
            {
                factor = Math.Min(factor, side / sum);
            }
        }

        static void Corner(ref T receiver, double centerX, double centerY, double radiusX, double radiusY, double start, double endX, double endY)
        {
            if (radiusX > 0 && radiusY > 0)
            {
                Append(ref receiver, centerX, centerY, radiusX, 0, 0, radiusY, start, 0.5, endX, endY);
            }
            else if (radiusX > 0 || radiusY > 0)
            {
                // A corner that one of its sides flattened: the contour still reaches its end
                receiver.LineTo(endX, endY);
            }
        }
    }

    /// <summary>
    /// Gets the radii of the ellipse of two conjugate half-diameters and the angle of its first radius, in degrees from
    /// the x axis: what describes the ellipse of an arc of SVG.
    /// </summary>
    /// <remarks>
    /// The radii are the singular values of the matrix of the two vectors. Half-diameters that are horizontal and
    /// vertical give their lengths and no rotation, exactly; a circle has no rotation.
    /// </remarks>
    public static void GetAxes(double ux, double uy, double vx, double vy, out double radiusX, out double radiusY, out double rotation)
    {
        rotation = 0;
        if (uy == 0 && vx == 0)
        {
            radiusX = Math.Abs(ux);
            radiusY = Math.Abs(vy);
            return;
        }

        if (ux == 0 && vy == 0)
        {
            radiusX = Math.Abs(vx);
            radiusY = Math.Abs(uy);
            return;
        }

        // The squares of the radii are the eigenvalues of the symmetric matrix [[a, b], [b, d]]
        var a = (ux * ux) + (vx * vx);
        var b = (ux * uy) + (vx * vy);
        var d = (uy * uy) + (vy * vy);
        var half = (a + d) / 2;
        var difference = (a - d) / 2;
        var root = Math.Sqrt((difference * difference) + (b * b));
        var small = half - root;
        radiusX = Math.Sqrt(half + root);
        radiusY = small > 0 ? Math.Sqrt(small) : 0;
        if (root > 0)
        {
            // The first radius is along the eigenvector of the largest eigenvalue, at half the angle of (difference, b)
            rotation = DeterministicAngle.Atan2(b, difference) * 90;
        }
    }

    /// <summary>
    /// Finds the ellipse of an arc given by its two ends, as the arcs of SVG are: the radii and the rotation of the
    /// ellipse, whether the arc is the larger of the two the ellipse has between the points, and the way it turns.
    /// </summary>
    /// <remarks>
    /// Radii too small for the ellipse to reach both points are enlarged, in proportion, until it does. The ellipse is
    /// returned as its center and two conjugate half-diameters, with the angle of the start and the sweep in half turns.
    /// </remarks>
    /// <returns><see langword="false"/> when there is no arc: both points are the same, or a radius is zero (the arc is then a straight segment).</returns>
    public static bool TryGetCenterForm(double startX, double startY, double endX, double endY, double radiusX, double radiusY, double rotation, bool largeArc, bool clockwise, out double centerX, out double centerY, out double ux, out double uy, out double vx, out double vy, out double start, out double sweep)
    {
        centerX = centerY = ux = uy = vx = vy = start = sweep = 0;
        if ((startX == endX && startY == endY) || !(radiusX > 0 && radiusY > 0))
            return false;

        DeterministicAngle.SinCos(rotation / 180, out var sin, out var cos);

        // Half the vector from the end to the start, in the axes of the ellipse
        var halfX = (startX - endX) / 2;
        var halfY = (startY - endY) / 2;
        var x = (cos * halfX) + (sin * halfY);
        var y = (cos * halfY) - (sin * halfX);

        // The radii that are too small are enlarged until the ellipse reaches both points
        var reach = ((x / radiusX) * (x / radiusX)) + ((y / radiusY) * (y / radiusY));
        if (reach > 1)
        {
            var scale = Math.Sqrt(reach);
            radiusX *= scale;
            radiusY *= scale;
        }

        // The center, in the axes of the ellipse, relative to the middle of the two points
        var a = radiusX * y;
        var b = radiusY * x;
        var divisor = (a * a) + (b * b);
        var numerator = ((radiusX * radiusX) * (radiusY * radiusY)) - divisor;
        var factor = numerator > 0 && divisor > 0 ? Math.Sqrt(numerator / divisor) : 0;
        if (largeArc == clockwise)
        {
            factor = -factor;
        }

        var cx = factor * (a / radiusY);
        var cy = -factor * (b / radiusX);
        centerX = ((cos * cx) - (sin * cy)) + ((startX + endX) / 2);
        centerY = ((sin * cx) + (cos * cy)) + ((startY + endY) / 2);
        ux = radiusX * cos;
        uy = radiusX * sin;
        vx = -radiusY * sin;
        vy = radiusY * cos;

        // The angles of both points on the ellipse
        start = DeterministicAngle.Atan2((y - cy) / radiusY, (x - cx) / radiusX);
        var end = DeterministicAngle.Atan2((-y - cy) / radiusY, (-x - cx) / radiusX);
        sweep = end - start;
        if (clockwise && sweep < 0)
        {
            sweep += 2;
        }
        else if (!clockwise && sweep > 0)
        {
            sweep -= 2;
        }

        return sweep != 0;
    }
}
