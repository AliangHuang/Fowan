using System.Text.RegularExpressions;
using Fowan.ToolCenter.Windows.Application;

namespace Fowan.ToolCenter.Windows.Platform.Windows;

internal static class ManagedToolPaths
{
    internal static string Canonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\") ||
            path.Length < 3 || path[1] != ':' || path[2] is not ('\\' or '/') ||
            path[2..].Contains(':')) throw new ToolManagementException("Center_UnsafePath");
        foreach (var segment in path[3..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)) ValidateSegment(segment);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    internal static void ValidateSegment(string segment)
    {
        if (segment is "." or ".." || segment.Length == 0 || segment.EndsWith(' ') || segment.EndsWith('.') ||
            segment.Any(character => character < 32 || "<>:\"/\\|?*".Contains(character)) ||
            Regex.IsMatch(segment, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
            throw new ToolManagementException("Center_UnsafePath");
    }

    internal static string Child(string root, string relative)
    {
        var canonicalRoot = Canonical(root);
        var child = Canonical(Path.Combine(canonicalRoot, relative));
        var prefix = Path.EndsInDirectorySeparator(canonicalRoot) ? canonicalRoot : canonicalRoot + Path.DirectorySeparatorChar;
        if (!child.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ToolManagementException("Center_UnsafePath");
        return child;
    }

    internal static void RejectLinksOnPath(string path)
    {
        for (string? current = Canonical(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ToolManagementException("Center_UnsafePath");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    internal static void RejectLinksInTree(string directory)
    {
        RejectLinksOnPath(directory);
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new ToolManagementException("Center_UnsafePath");
            if ((attributes & FileAttributes.Directory) != 0) RejectLinksInTree(path);
        }
    }

    // Callers must establish ownership before invoking this helper. Never follow a link.
    internal static void DeleteTree(string directory)
    {
        RejectLinksInTree(directory);
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            RejectLinksOnPath(path);
            if (Directory.Exists(path)) DeleteTree(path);
            else File.Delete(path);
        }
        Directory.Delete(directory);
    }
}
