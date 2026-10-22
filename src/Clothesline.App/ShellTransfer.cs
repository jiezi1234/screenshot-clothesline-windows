using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using System.Windows.Interop;
using Clothesline.Core;

namespace Clothesline.App;

internal static class ShellTransfer
{
    private static readonly Guid DataObjectId = new("0000010e-0000-0000-C000-000000000046");
    private static readonly Guid RecycleBinId = new("645FF040-5081-101B-9F08-00AA002F954E");
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint context, out nint pidl, uint mask, out uint attributes);
    [DllImport("shell32.dll")] private static extern nint ILFindLastID(nint pidl);
    [DllImport("shell32.dll")] private static extern int SHCreateDataObject(nint parent, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] nint[] children, nint inner, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out System.Runtime.InteropServices.ComTypes.IDataObject data);
    [DllImport("shell32.dll")] private static extern int SHDoDragDrop(nint hwnd, [MarshalAs(UnmanagedType.Interface)] System.Runtime.InteropServices.ComTypes.IDataObject data, nint source, uint allowed, out uint effect);
    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string name);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint memory);

    public static string Drag(Window owner, DemoItem item)
    {
        if (!File.Exists(item.Path)) return "文件已移走；请恢复示例。";
        var ole = OleInitialize(0);
        if (ole < 0) Marshal.ThrowExceptionForHR(ole);
        nint parent = 0, child = 0;
        System.Runtime.InteropServices.ComTypes.IDataObject? data = null;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(Path.GetDirectoryName(item.Path)!, 0, out parent, 0, out _));
            Marshal.ThrowExceptionForHR(SHParseDisplayName(item.Path, 0, out child, 0, out _));
            Marshal.ThrowExceptionForHR(SHCreateDataObject(parent, 1, new[] { ILFindLastID(child) }, 0, DataObjectId, out data));
            // Shell data object supplies real file identity and supports optimized Explorer moves.
            var result = SHDoDragDrop(new WindowInteropHelper(owner).Handle, data, 0, 3, out var effect);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
            var target = ReadBytes(data, "TargetCLSID", 16);
            var inRecycleBin = target is not null && new Guid(target) == RecycleBinId;
            var decision = TransferPolicy.Decide(result == 0x40100, File.Exists(item.Path), inRecycleBin, effect);
            return decision switch
            {
                TransferDisposition.RecycleRequested => Recycle(item.Path),
                TransferDisposition.RemoveMissing => "文件已移走，卡片已取下。",
                TransferDisposition.VerifyMove => "目标报告移动；源文件仍在，已保留。",
                _ => result == 0x40101 || effect == 0 ? "拖拽未完成，原文件保留。" : "副本已交给目标，原文件保留。"
            };
        }
        finally
        {
            if (data is not null && Marshal.IsComObject(data)) Marshal.ReleaseComObject(data);
            if (child != 0) Marshal.FreeCoTaskMem(child);
            if (parent != 0) Marshal.FreeCoTaskMem(parent);
            OleUninitialize();
        }
    }
    private static byte[]? ReadBytes(System.Runtime.InteropServices.ComTypes.IDataObject data, string name, int size)
    {
        var format = new FORMATETC { cfFormat = unchecked((short)RegisterClipboardFormatW(name)), dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
        if (data.QueryGetData(ref format) != 0) return null;
        data.GetData(ref format, out var medium);
        try
        {
            if (medium.tymed != TYMED.TYMED_HGLOBAL || GlobalSize(medium.unionmember) < (nuint)size) return null;
            var ptr = GlobalLock(medium.unionmember);
            if (ptr == 0) return null;
            try { var bytes = new byte[size]; Marshal.Copy(ptr, bytes, 0, size); return bytes; }
            finally { GlobalUnlock(medium.unionmember); }
        }
        finally { ReleaseStgMedium(ref medium); }
    }
    public static string Recycle(string path)
    {
        // The platform owns its confirmation/error UI. Never fall back to File.Delete.
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
            Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
            Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
        return File.Exists(path) ? "文件仍在原位置。" : "示例文件已移入回收站。";
    }
    public static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}
