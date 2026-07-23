using Fowan.Report.Shared.Application.Ports;
using System.Runtime.InteropServices;
using System.Text;

namespace Fowan.Report.Windows.Platform.Windows;

/// <summary>
/// Owner-bound common dialogs for the unpackaged WinUI process. This uses a pointer-only
/// OPENFILENAME declaration so the runtime never needs to marshal managed string fields.
/// </summary>
internal sealed class WindowsReportFileDialogService(nint ownerWindow) : IReportFileDialogService
{
    private const int FileBufferLength = 32_768;
    private const uint CdErrStructSize = 0x0001;
    private const uint OfnOverwritePrompt = 0x0000_0002;
    private const uint OfnPathMustExist = 0x0000_0800;
    private const uint OfnFileMustExist = 0x0000_1000;
    private const uint OfnNoChangeDirectory = 0x0000_0008;
    private const uint OfnExplorer = 0x0008_0000;

    internal static int NativeStructureSize => Marshal.SizeOf<OpenFileNameNative>();

    public Task<string?> PickOpenAsync(
        ReportFileOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOwnerWindow();
        return Task.FromResult(ShowOpenDialog(BuildFilter(request.Extensions)));
    }

    public Task<string?> PickSaveAsync(
        ReportFileSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOwnerWindow();
        var extension = NormalizeExtension(request.Extension);
        return Task.FromResult(ShowSaveDialog(
            BuildFilter([extension]),
            Path.GetFileName(request.SuggestedFileName),
            extension[1..]));
    }

    internal static string BuildFilter(IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        var normalized = extensions.Select(NormalizeExtension).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalized.Length == 0)
        {
            throw new ReportTemplateValidationException("未提供可选择的文件类型。");
        }

        var entries = new List<string>(normalized.Length * 2);
        if (normalized.Length > 1)
        {
            var allPatterns = string.Join(';', normalized.Select(extension => $"*{extension}"));
            entries.Add($"所有支持的模板 ({allPatterns})");
            entries.Add(allPatterns);
        }
        foreach (var extension in normalized)
        {
            var pattern = $"*{extension}";
            entries.Add(extension switch
            {
                ".docx" => $"Word 文档 ({pattern})",
                ".xlsx" => $"Excel 工作簿 ({pattern})",
                _ => $"文件 ({pattern})"
            });
            entries.Add(pattern);
        }
        return string.Join('\0', entries) + "\0\0";
    }

    /// <summary>Non-interactive ABI probe: an invalid size must be rejected by comdlg32 before it can show UI.</summary>
    internal static uint ProbeNativeSignature()
    {
        var dialog = new OpenFileNameNative { lStructSize = 0 };
        _ = GetOpenFileName(ref dialog);
        return CommDlgExtendedError();
    }

    private string? ShowOpenDialog(string filter)
    {
        using var buffers = new NativeDialogBuffers(filter, string.Empty, null);
        var dialog = CreateDialog(buffers, OfnExplorer | OfnPathMustExist | OfnFileMustExist | OfnNoChangeDirectory);
        return GetOpenFileName(ref dialog) ? buffers.ReadSelectedPath() : HandleCancelOrFailure();
    }

    private string? ShowSaveDialog(string filter, string suggestedFileName, string defaultExtension)
    {
        using var buffers = new NativeDialogBuffers(filter, suggestedFileName, defaultExtension);
        var dialog = CreateDialog(buffers, OfnExplorer | OfnPathMustExist | OfnOverwritePrompt | OfnNoChangeDirectory);
        return GetSaveFileName(ref dialog) ? buffers.ReadSelectedPath() : HandleCancelOrFailure();
    }

    private OpenFileNameNative CreateDialog(NativeDialogBuffers buffers, uint flags) => new()
    {
        lStructSize = NativeStructureSize,
        hwndOwner = ownerWindow,
        lpstrFilter = buffers.Filter,
        lpstrFile = buffers.File,
        nMaxFile = FileBufferLength,
        lpstrDefExt = buffers.DefaultExtension,
        Flags = flags
    };

    private void EnsureOwnerWindow()
    {
        if (ownerWindow == nint.Zero)
        {
            throw new ReportTemplateValidationException("汇报窗口尚未准备完成，无法打开文件选择器。请稍候重试。");
        }
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new ReportTemplateValidationException("文件类型无效，无法打开文件选择器。");
        }
        var normalized = extension.StartsWith('.') ? extension : $".{extension}";
        if (normalized.IndexOfAny(['\\', '/', '*', '?', '\0']) >= 0)
        {
            throw new ReportTemplateValidationException("文件类型无效，无法打开文件选择器。");
        }
        return normalized;
    }

    private static string? HandleCancelOrFailure()
    {
        var error = CommDlgExtendedError();
        if (error == 0) return null;
        throw new ReportTemplateValidationException("无法启动 Windows 文件选择器，请关闭后重试。");
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileNameNative dialog);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileName(ref OpenFileNameNative dialog);

    [DllImport("comdlg32.dll")]
    private static extern uint CommDlgExtendedError();

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenFileNameNative
    {
        public int lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public nint lpstrFilter;
        public nint lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public nint lpstrFile;
        public int nMaxFile;
        public nint lpstrFileTitle;
        public int nMaxFileTitle;
        public nint lpstrInitialDir;
        public nint lpstrTitle;
        public uint Flags;
        public ushort nFileOffset;
        public ushort nFileExtension;
        public nint lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public nint lpTemplateName;
        public nint pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    private sealed class NativeDialogBuffers : IDisposable
    {
        public nint Filter { get; }
        public nint File { get; }
        public nint DefaultExtension { get; }

        public NativeDialogBuffers(string filter, string suggestedFileName, string? defaultExtension)
        {
            if (suggestedFileName.Length >= FileBufferLength)
            {
                throw new ReportTemplateValidationException("建议的文件名过长，无法打开文件选择器。");
            }
            Filter = Marshal.StringToCoTaskMemUni(filter);
            File = Marshal.AllocCoTaskMem(FileBufferLength * sizeof(char));
            DefaultExtension = string.IsNullOrEmpty(defaultExtension) ? nint.Zero : Marshal.StringToCoTaskMemUni(defaultExtension);
            var bytes = Encoding.Unicode.GetBytes(suggestedFileName + '\0');
            Marshal.Copy(bytes, 0, File, bytes.Length);
        }

        public string? ReadSelectedPath()
        {
            var value = Marshal.PtrToStringUni(File, FileBufferLength) ?? string.Empty;
            var terminator = value.IndexOf('\0');
            if (terminator >= 0) value = value[..terminator];
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        public void Dispose()
        {
            if (Filter != nint.Zero) Marshal.FreeCoTaskMem(Filter);
            if (File != nint.Zero) Marshal.FreeCoTaskMem(File);
            if (DefaultExtension != nint.Zero) Marshal.FreeCoTaskMem(DefaultExtension);
        }
    }
}
