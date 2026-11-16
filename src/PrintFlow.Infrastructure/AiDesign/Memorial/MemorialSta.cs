using System.Collections.Concurrent;

namespace PrintFlow.Infrastructure.AiDesign.Memorial;

/// <summary>One apartment for WIC/WPF composition, encoding and display decoding.</summary>
internal static class MemorialSta
{
    private static readonly Lazy<BlockingCollection<Action>> Work = new(Start);

    public static Task<T> RunAsync<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Work.Value.Add(() =>
        {
            try { completion.SetResult(work()); }
            catch (Exception ex) { completion.SetException(ex); }
        });
        return completion.Task;
    }

    private static BlockingCollection<Action> Start()
    {
        var queue = new BlockingCollection<Action>();
        var thread = new Thread(() =>
        {
            foreach (var job in queue.GetConsumingEnumerable()) job();
        }) { IsBackground = true, Name = "Memorial image composition" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return queue;
    }
}
