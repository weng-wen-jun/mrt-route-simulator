using System.Reflection;
using System.Windows.Threading;
using MrtRouteSimulator.App;

internal static class WpfTestWait
{
    public static void Wait(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(10)
            };
            timer.Tick += (_, _) =>
            {
                if (!task.IsCompleted)
                {
                    return;
                }

                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        task.GetAwaiter().GetResult();
    }

    public static void WaitForPlannedTimeline(MainWindow window)
    {
        if (Field(window, "_plannedTimelineTask") is Task task)
        {
            Wait(task);
        }

        Invoke(window, "ApplyCompletedPlannedTimeline");
    }

    public static Task InvokeOnUiAsync(MainWindow window, string method, params object[] args)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            return Invoke(window, method, args) as Task
                ?? throw new InvalidOperationException($"{method} did not return a Task.");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    public static void Advance(MainWindow window, double simulationTimeSeconds)
    {
        var worker = (SimulationPlaybackWorker)(Field(window, "_playbackWorker")
            ?? throw new InvalidOperationException("未建立播放工作者。"));
        Wait(worker.AdvanceToSimulationTimeAsync(simulationTimeSeconds));
        Invoke(window, "UpdateV2PlaybackView");
    }

    public static PlaybackFrame LatestFrame(MainWindow window) =>
        (PlaybackFrame)(Field(window, "_latestPlaybackFrame")
            ?? throw new InvalidOperationException("播放工作者尚未發出 frame。"));

    public static void Close(MainWindow window)
    {
        if (Invoke(window, "StopCurrentPlaybackResourcesAsync") is Task stopTask)
        {
            Wait(stopTask);
        }

        typeof(MainWindow).GetField("_closeAfterPlaybackShutdown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(window, true);
        window.Close();
    }

    public static object? Invoke(MainWindow window, string method, params object[] args)
    {
        var target = typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"找不到方法 {method}。");
        var parameters = target.GetParameters();
        if (args.Length > parameters.Length)
        {
            throw new TargetParameterCountException($"方法 {method} 收到過多參數。");
        }

        var invocationArgs = new object?[parameters.Length];
        Array.Copy(args, invocationArgs, Math.Min(args.Length, invocationArgs.Length));
        for (var index = args.Length; index < parameters.Length; index++)
        {
            if (!parameters[index].IsOptional)
            {
                throw new TargetParameterCountException($"方法 {method} 缺少必要參數 {parameters[index].Name}。");
            }

            invocationArgs[index] = parameters[index].DefaultValue;
        }

        return target.Invoke(window, invocationArgs);
    }

    public static object? Field(MainWindow window, string name) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
}
