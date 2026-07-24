using Fowan.Todo.Shared.Models;

namespace Fowan.Todo.Shared.Services;

internal static class TodoStickyPlacement
{
    internal static string? FindDockEdgeByCenter(
        double workLeft,
        double workRight,
        double windowCenter,
        double threshold)
    {
        var hitsLeft = windowCenter <= workLeft + threshold;
        var hitsRight = windowCenter >= workRight - threshold;
        if (!hitsLeft && !hitsRight)
        {
            return null;
        }

        return hitsLeft && hitsRight
            ? NearestEdge(workLeft, workRight, windowCenter)
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
}
