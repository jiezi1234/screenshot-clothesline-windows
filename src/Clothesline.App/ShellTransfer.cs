using System;
using System.Diagnostics;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Clothesline.Core;

namespace Clothesline.App;

internal static class ShellTransfer
{
    internal static DataObject CopyData(string path)
        => BuildData(path, MediaFiles.IsVideo(path) ? null : ImageFiles.Read(path));
    internal static async Task<DataObject> CopyDataAsync(string path)
    {
        var image = MediaFiles.IsVideo(path) ? null : await ImageDecoder.Read(path);
        var png = image is null ? null : await Task.Run(() => ImageFiles.Png(image));
        return BuildData(path, image, png);
    }
    private static DataObject BuildData(string path, BitmapSource? image, byte[]? png = null)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(T("原文件已移走，可在历史中查看失效记录。"), path);
        var data = new DataObject();
        if (!MediaFiles.IsVideo(path))
        {
            data.SetImage(image!); data.SetData("PNG", new MemoryStream(png ?? ImageFiles.Png(image!)));
        }
        data.SetFileDropList(new StringCollection { path });
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));
        data.SetData("ScreenshotClothesline.OwnCopy", "1", false); return data;
    }
    private static readonly Guid DataObjectId = new("0000010e-0000-0000-C000-000000000046");
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint context, out nint pidl, uint mask, out uint attributes);
    [DllImport("shell32.dll")] private static extern nint ILFindLastID(nint pidl);
    [DllImport("shell32.dll")] private static extern int SHCreateDataObject(nint parent, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] nint[] children, nint inner, in Guid iid, [MarshalAs(UnmanagedType.Interface)] out System.Runtime.InteropServices.ComTypes.IDataObject data);
    [DllImport("shell32.dll")] private static extern int SHDoDragDrop(nint hwnd, [MarshalAs(UnmanagedType.Interface)] System.Runtime.InteropServices.ComTypes.IDataObject data, nint source, uint allowed, out uint effect);
    [DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    internal delegate int DragOperation(nint owner, System.Runtime.InteropServices.ComTypes.IDataObject data, nint source, uint allowed, out uint effect);
    public static string Drag(Window owner, DemoItem item) => Drag(new WindowInteropHelper(owner).Handle, item, SHDoDragDrop);
    internal static string Drag(nint owner, DemoItem item, DragOperation perform)
    {
        if (!File.Exists(item.Path)) return T("原文件已失效，无法拖出。");
        var ole = OleInitialize(0);
        if (ole < 0) Marshal.ThrowExceptionForHR(ole);
        nint parent = 0, child = 0;
        System.Runtime.InteropServices.ComTypes.IDataObject? data = null;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(Path.GetDirectoryName(item.Path)!, 0, out parent, 0, out _));
            Marshal.ThrowExceptionForHR(SHParseDisplayName(item.Path, 0, out child, 0, out _));
            Marshal.ThrowExceptionForHR(SHCreateDataObject(parent, 1, new[] { ILFindLastID(child) }, 0, DataObjectId, out data));
            // Offer only COPY before the target handles the file, including same-volume Explorer drops.
            // Allowing MOVE would let the target remove the source before this method returns.
            var result = perform(owner, data, 0, TransferPolicy.AllowedEffects, out var effect);
            if (result < 0) Marshal.ThrowExceptionForHR(result);
            var decision = TransferPolicy.Decide(result == 0x40100, File.Exists(item.Path), effect);
            return decision switch
            {
                TransferDisposition.RemoveMissing => T("原文件已失效，请在历史中查看记录。"),
                TransferDisposition.VerifyMove => T("目标返回了不支持的移动结果；原文件和卡片已保留。"),
                _ => result != 0x40100 || (effect & 1) == 0 ? T("拖拽未完成，原文件和卡片已保留。") : T("已交付副本，卡片保留。")
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
    public static string Recycle(string path)
    {
        SafeRecycle.File(path);
        return File.Exists(path) ? T("文件仍在原位置。") : T("示例文件已移入回收站。");
    }
    public static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    public static void Reveal(string path)
    {
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        start.ArgumentList.Add("/select," + Path.GetFullPath(path)); Process.Start(start);
    }
}
