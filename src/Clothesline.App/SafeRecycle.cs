using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Clothesline.App;

internal static class SafeRecycle
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, nint context, in Guid iid, out IShellItem item);
    public static bool File(string path)
    {
        if (!System.IO.File.Exists(path)) return true;
        var type = Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"), true)!;
        var operation = (IFileOperation)Activator.CreateInstance(type)!;
        IShellItem? item = null;
        try
        {
            // Recycle only; a progress sink additionally rejects any permanent deletion fallback.
            operation.SetOperationFlags(0x00080000 | 0x00100000 | 0x00000400 | 0x00000004 | 0x00000010);
            var iid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
            SHCreateItemFromParsingName(Path.GetFullPath(path), 0, iid, out item);
            var sink = new RecycleSink(); operation.DeleteItem(item, sink); operation.PerformOperations();
            operation.GetAnyOperationsAborted(out var aborted);
            if (aborted || sink.Failed || System.IO.File.Exists(path)) throw new IOException(T("未能移入回收站，文件已保留。"));
            return true;
        }
        finally { if (item is not null) Marshal.ReleaseComObject(item); Marshal.ReleaseComObject(operation); }
    }
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(nint sink, out uint cookie); void Unadvise(uint cookie);
        void SetOperationFlags(uint flags); void SetProgressMessage(nint message); void SetProgressDialog(nint dialog);
        void SetProperties(nint properties); void SetOwnerWindow(uint owner);
        void ApplyPropertiesToItem(nint item); void ApplyPropertiesToItems(nint items);
        void RenameItem(nint item, nint name, nint sink); void RenameItems(nint items, nint name);
        void MoveItem(nint item, nint folder, nint name, nint sink); void MoveItems(nint items, nint folder);
        void CopyItem(nint item, nint folder, nint name, nint sink); void CopyItems(nint items, nint folder);
        void DeleteItem(IShellItem item, IProgressSink sink);
        void DeleteItems(nint items); void NewItem(nint folder, uint attributes, nint name, nint template, nint sink);
        void PerformOperations(); void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(nint context, in Guid handler, in Guid iid, out nint result);
        void GetParent(out IShellItem parent); void GetDisplayName(uint kind, out nint name);
        void GetAttributes(uint mask, out uint attributes); void Compare(IShellItem other, uint hint, out int order);
    }
    [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IProgressSink
    {
        [PreserveSig] int StartOperations(); [PreserveSig] int FinishOperations(int result);
        [PreserveSig] int PreRenameItem(uint flags, nint item, nint name); [PreserveSig] int PostRenameItem(uint flags, nint item, nint name, int result, nint created);
        [PreserveSig] int PreMoveItem(uint flags, nint item, nint folder, nint name); [PreserveSig] int PostMoveItem(uint flags, nint item, nint folder, nint name, int result, nint created);
        [PreserveSig] int PreCopyItem(uint flags, nint item, nint folder, nint name); [PreserveSig] int PostCopyItem(uint flags, nint item, nint folder, nint name, int result, nint created);
        [PreserveSig] int PreDeleteItem(uint flags, nint item); [PreserveSig] int PostDeleteItem(uint flags, nint item, int result, nint created);
        [PreserveSig] int PreNewItem(uint flags, nint folder, nint name); [PreserveSig] int PostNewItem(uint flags, nint folder, nint name, nint template, uint attributes, int result, nint created);
        [PreserveSig] int UpdateProgress(uint total, uint soFar); [PreserveSig] int ResetTimer(); [PreserveSig] int PauseTimer(); [PreserveSig] int ResumeTimer();
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class RecycleSink : IProgressSink
    {
        public bool Failed { get; private set; }
        public int PreDeleteItem(uint flags, nint item) { if ((flags & 0x80) != 0) return 0; Failed = true; return unchecked((int)0x80004004); }
        public int PostDeleteItem(uint flags, nint item, int result, nint created) { Failed |= result < 0; return 0; }
        public int StartOperations() => 0; public int FinishOperations(int result) { Failed |= result < 0; return 0; }
        public int PreRenameItem(uint flags, nint item, nint name) => 0; public int PostRenameItem(uint flags, nint item, nint name, int result, nint created) => 0;
        public int PreMoveItem(uint flags, nint item, nint folder, nint name) => 0; public int PostMoveItem(uint flags, nint item, nint folder, nint name, int result, nint created) => 0;
        public int PreCopyItem(uint flags, nint item, nint folder, nint name) => 0; public int PostCopyItem(uint flags, nint item, nint folder, nint name, int result, nint created) => 0;
        public int PreNewItem(uint flags, nint folder, nint name) => 0; public int PostNewItem(uint flags, nint folder, nint name, nint template, uint attributes, int result, nint created) => 0;
        public int UpdateProgress(uint total, uint soFar) => 0; public int ResetTimer() => 0; public int PauseTimer() => 0; public int ResumeTimer() => 0;
    }
}
