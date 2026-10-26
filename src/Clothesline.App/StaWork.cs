using System;
using System.Threading;
using System.Threading.Tasks;

namespace Clothesline.App;

internal static class StaWork
{
    internal static Task<T> Run<T>(Func<T> action)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { result.SetResult(action()); } catch (Exception ex) { result.SetException(ex); } }) { IsBackground = true, Name = "Clothesline image rendering" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return result.Task;
    }
}
