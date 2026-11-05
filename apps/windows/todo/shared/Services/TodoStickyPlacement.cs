using Fowan.Todo.Shared.Models;

namespace Fowan.Todo.Shared.Services;

internal static class TodoStickyPlacement
{
    private const double FloatingEdgeOverhangRatio = 1d / 5d;
    private const double WindowsMinimizedCoordinate = -32000;
    private const double WindowsMinimizedCoordinateTolerance = 0.5;

    internal static bool IsUsableStoredOrigin(
        double? left,
        double? top,
        double horizontalDpiScale = 1,
        double verticalDpiScale = 1) =>
        left is double storedLeft &&
        top is double storedTop &&
        double.IsFinite(storedLeft) &&
        double.IsFinite(storedTop) &&
        !IsWindowsMinimizedCoordinate(storedLeft, horizontalDpiScale) &&
        !IsWindowsMinimizedCoordinate(storedTop, verticalDpiScale);

    internal static string? FindDockEdgeByWindowBounds(
        double workLeft,
        double workRight,
        double windowLeft,
        double windowRight)
    {
        var allowedOverhang = Math.Max(0, windowRight - windowLeft) * FloatingEdgeOverhangRatio;
        var hitsLeft = windowLeft < workLeft;
        var hitsRight = windowRight > workRight + allowedOverhang;
        if (!hitsLeft && !hitsRight)
        {
            return null;
        }

        return hitsLeft && hitsRight
            ? NearestEdge(workLeft, workRight, (windowLeft + windowRight) / 2)
            : hitsLeft
                ? TodoStickyFloatingEdges.Left
                : TodoStickyFloatingEdges.Right;
    }

    internal static string NearestEdge(double workLeft, double workRight, double anchorX)
    {
        return Math.Abs(anchorX - workLeft) <= Math.Abs(workRight - anchorX)
            ? TodoStickyFloatingEdges.Left
            : TodoStickyFloatingEdges.Right;
    }

    internal static double AlignCenters(double sourceTop, double sourceHeight, double targetHeight)
    {
        return sourceTop + sourceHeight / 2 - targetHeight / 2;
    }

    internal static (double Top, double Height) BodyGeometryFromExpanded(
        double expandedTop,
        double expandedHeight,
        double menuHeight) =>
        (expandedTop + menuHeight, expandedHeight - menuHeight);

    internal static (double Top, double Height) ExpandedGeometryFromBody(
        double bodyTop,
        double bodyHeight,
        double menuHeight) =>
        (bodyTop - menuHeight, bodyHeight + menuHeight);

    private static bool IsWindowsMinimizedCoordinate(double coordinate, double dpiScale) =>
        double.IsFinite(dpiScale) &&
        dpiScale > 0 &&
        Math.Abs(coordinate * dpiScale - WindowsMinimizedCoordinate) < WindowsMinimizedCoordinateTolerance;
}
