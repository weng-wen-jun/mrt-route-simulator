using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace MrtRouteSimulator.App;

/// <summary>
/// Optional App-boundary input observations used by native acceptance. These timestamps describe
/// the earliest WPF receipt/preview boundary and routed-handler/application-update surrogates; they
/// are deliberately not OS injection or compositor-present timestamps.
/// </summary>
public partial class MainWindow
{
    private const int NativeAcceptanceWmMove = 0x0003;
    private const int NativeAcceptanceWmSize = 0x0005;
    private const int NativeAcceptanceWmMouseWheel = 0x020A;
    private const int NativeAcceptanceWmEnterSizeMove = 0x0231;
    private const int NativeAcceptanceWmExitSizeMove = 0x0232;

    private HwndSource? _nativeAcceptanceHwndSource;
    private HwndSourceHook? _nativeAcceptanceHwndHook;
    private MouseButtonEventHandler? _nativeAcceptancePreviewMouseDownHandler;
    private MouseButtonEventHandler? _nativeAcceptanceMouseUpHandler;
    private MouseWheelEventHandler? _nativeAcceptancePreviewMouseWheelHandler;
    private RoutedEventHandler? _nativeAcceptanceClickHandler;
    private SelectionChangedEventHandler? _nativeAcceptanceSelectionChangedHandler;
    private readonly Dictionary<long, NativeAcceptanceInputActionState> _nativeAcceptanceInputActions = [];
    private long _nativeAcceptanceNextInputActionId;
    private long _nativeAcceptanceLastInputActionId;
    private long _nativeAcceptanceWindowMoveActionId;

    /// <summary>Read-only state for tests and acceptance scripts; it never starts playback.</summary>
    public NativeAcceptanceInputDiagnosticsSnapshot NativeAcceptanceReadInputDiagnostics()
    {
        var session = _nativeAcceptanceSession;
        return new NativeAcceptanceInputDiagnosticsSnapshot(
            session is not null,
            _nativeAcceptanceHwndSource is not null,
            _nativeAcceptanceInputActions.Values.Count(action =>
                action.HandlerEndStopwatchTimestamp is null && !action.RoutedBoundarySeen),
            session?.InputActionCount ?? 0,
            session?.StallCount ?? 0,
            _nativeAcceptanceInputActions.Values.Count(action => action.HandlerEndStopwatchTimestamp is not null),
            _nativeAcceptanceInputActions.Values.Count(action =>
                action.ApplicationVisualUpdateStopwatchTimestamp is not null
                || action.VisualUpdateSurrogateStopwatchTimestamp is not null));
    }

    /// <summary>Returns the current recent token so an owner handler can bracket its real work.</summary>
    public long NativeAcceptanceGetRecentInputActionToken(string actionKind)
    {
        if (_nativeAcceptanceSession is null || string.IsNullOrWhiteSpace(actionKind))
        {
            return 0;
        }

        return FindRecentInputAction(actionKind, Stopwatch.GetTimestamp())?.ActionId ?? 0;
    }

    /// <summary>
    /// Creates a diagnostics-only synthetic action for an offscreen test. It does not invoke a
    /// button, tab, worker, world, or any other control and is rejected while measurement is off.
    /// </summary>
    public long NativeAcceptanceTriggerInputActionForTest(string actionKind)
    {
        var token = NativeAcceptanceBeginInputAction(actionKind, "diagnostics-test-only");
        if (token == 0)
        {
            return 0;
        }

        NativeAcceptanceInputHandlerStarted(token);
        NativeAcceptanceInputHandlerEnded(token, "diagnostics-test-only");
        NativeAcceptanceApplicationVisualUpdate(token, "diagnostics-test-only; no compositor claim");
        return token;
    }

    /// <summary>
    /// Starts an App-boundary action span. A recent routed preview action of the same kind is
    /// reused so explicit owner hooks can bracket the actual async handler without duplicating it.
    /// </summary>
    public long NativeAcceptanceBeginInputAction(string actionKind, string? detail = null)
        => NativeAcceptanceBeginInputActionCore(actionKind, detail, hasPreviewBoundary: false);

    private long NativeAcceptanceBeginInputActionCore(
        string actionKind,
        string? detail,
        bool hasPreviewBoundary)
    {
        var session = _nativeAcceptanceSession;
        if (session is null || string.IsNullOrWhiteSpace(actionKind))
        {
            return 0;
        }

        var now = Stopwatch.GetTimestamp();
        // A physical routed preview is a new receipt even when the same gesture repeats quickly.
        // Only explicit owner boundaries may reuse a recent preview token.
        var existing = hasPreviewBoundary ? null : FindRecentInputAction(actionKind, now);
        if (existing is not null)
        {
            existing.Detail ??= detail;
            if (hasPreviewBoundary)
            {
                existing.PreviewStopwatchTimestamp ??= now;
            }

            return existing.ActionId;
        }

        var action = new NativeAcceptanceInputActionState(
            ++_nativeAcceptanceNextInputActionId,
            actionKind,
            detail,
            now,
            hasPreviewBoundary);
        _nativeAcceptanceInputActions[action.ActionId] = action;
        _nativeAcceptanceLastInputActionId = action.ActionId;
        session.InputActionCount++;
        NativeAcceptanceTrimInputActions();
        NativeAcceptanceWrite(session, new
        {
            type = "inputAction",
            phase = "inputReceived",
            sessionId = session.SessionId,
            actionId = action.ActionId,
            action = action.Kind,
            detail = action.Detail,
            utc = DateTimeOffset.UtcNow,
            inputReceivedStopwatchTimestamp = action.InputReceivedStopwatchTimestamp,
            previewStopwatchTimestamp = action.PreviewStopwatchTimestamp,
            inputBoundary = "earliest WPF routed preview/receipt; not OS injection",
            osInjectionMeasured = false,
            activeTab = GetActiveTabMetadata(),
            simulationTimeSeconds = _latestPlaybackFrame?.SimulationTimeSeconds
        });
        return action.ActionId;
    }

    /// <summary>Explicit handler-start boundary for owner async handlers.</summary>
    public void NativeAcceptanceInputHandlerStarted(long token)
    {
        if (!TryGetInputAction(token, out var action) || action.HandlerStartStopwatchTimestamp is not null)
        {
            return;
        }

        action.HandlerStartStopwatchTimestamp = Stopwatch.GetTimestamp();
        NativeAcceptanceWriteInputActionBoundary(action, "handlerStart", action.HandlerStartStopwatchTimestamp.Value, null);
    }

    /// <summary>
    /// Explicit handler-end boundary. For async commands the owner should call this after the
    /// normal command acknowledgement, not after a separate automation path.
    /// </summary>
    public void NativeAcceptanceInputHandlerEnded(long token, string? detail = null)
    {
        if (!TryGetInputAction(token, out var action) || action.HandlerEndStopwatchTimestamp is not null)
        {
            return;
        }

        action.HandlerEndStopwatchTimestamp = Stopwatch.GetTimestamp();
        if (!string.IsNullOrWhiteSpace(detail))
        {
            action.Detail ??= detail;
        }

        NativeAcceptanceWriteInputActionBoundary(action, "handlerEnd", action.HandlerEndStopwatchTimestamp.Value, detail);
    }

    /// <summary>
    /// Records the first App visual-update callback known to the owner. This is an application
    /// update boundary, not a claim that the compositor has presented pixels.
    /// </summary>
    public void NativeAcceptanceApplicationVisualUpdate(long token, string? detail = null)
    {
        if (!TryGetInputAction(token, out var action) || action.ApplicationVisualUpdateStopwatchTimestamp is not null)
        {
            return;
        }

        action.ApplicationVisualUpdateStopwatchTimestamp = Stopwatch.GetTimestamp();
        NativeAcceptanceWriteInputActionBoundary(
            action,
            "applicationVisualUpdate",
            action.ApplicationVisualUpdateStopwatchTimestamp.Value,
            detail ?? "Dispatcher/application visual update; not compositor present");
    }

    private void NativeAcceptanceInstallInputHooks()
    {
        if (_nativeAcceptanceSession is null || _nativeAcceptanceHwndSource is not null)
        {
            return;
        }

        _nativeAcceptancePreviewMouseDownHandler = NativeAcceptancePreviewMouseDown;
        _nativeAcceptanceMouseUpHandler = NativeAcceptanceMouseUp;
        _nativeAcceptancePreviewMouseWheelHandler = NativeAcceptancePreviewMouseWheel;
        _nativeAcceptanceClickHandler = NativeAcceptanceClick;
        _nativeAcceptanceSelectionChangedHandler = NativeAcceptanceSelectionChanged;

        AddHandler(UIElement.PreviewMouseDownEvent, _nativeAcceptancePreviewMouseDownHandler, true);
        AddHandler(UIElement.MouseUpEvent, _nativeAcceptanceMouseUpHandler, true);
        AddHandler(UIElement.PreviewMouseWheelEvent, _nativeAcceptancePreviewMouseWheelHandler, true);
        AddHandler(ButtonBase.ClickEvent, _nativeAcceptanceClickHandler, true);
        AddHandler(Selector.SelectionChangedEvent, _nativeAcceptanceSelectionChangedHandler, true);

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != 0 && HwndSource.FromHwnd(handle) is { } source)
        {
            _nativeAcceptanceHwndSource = source;
            var hook = new HwndSourceHook(NativeAcceptanceWindowMessageHook);
            _nativeAcceptanceHwndHook = hook;
            source.AddHook(hook);
        }
    }

    private void NativeAcceptanceUninstallInputHooks()
    {
        if (_nativeAcceptancePreviewMouseDownHandler is not null)
        {
            RemoveHandler(UIElement.PreviewMouseDownEvent, _nativeAcceptancePreviewMouseDownHandler);
        }

        if (_nativeAcceptanceMouseUpHandler is not null)
        {
            RemoveHandler(UIElement.MouseUpEvent, _nativeAcceptanceMouseUpHandler);
        }

        if (_nativeAcceptancePreviewMouseWheelHandler is not null)
        {
            RemoveHandler(UIElement.PreviewMouseWheelEvent, _nativeAcceptancePreviewMouseWheelHandler);
        }

        if (_nativeAcceptanceClickHandler is not null)
        {
            RemoveHandler(ButtonBase.ClickEvent, _nativeAcceptanceClickHandler);
        }

        if (_nativeAcceptanceSelectionChangedHandler is not null)
        {
            RemoveHandler(Selector.SelectionChangedEvent, _nativeAcceptanceSelectionChangedHandler);
        }

        if (_nativeAcceptanceHwndSource is not null && _nativeAcceptanceHwndHook is not null)
        {
            _nativeAcceptanceHwndSource.RemoveHook(_nativeAcceptanceHwndHook);
        }

        _nativeAcceptanceHwndSource = null;
        _nativeAcceptanceHwndHook = null;
        _nativeAcceptancePreviewMouseDownHandler = null;
        _nativeAcceptanceMouseUpHandler = null;
        _nativeAcceptancePreviewMouseWheelHandler = null;
        _nativeAcceptanceClickHandler = null;
        _nativeAcceptanceSelectionChangedHandler = null;
        _nativeAcceptanceInputActions.Clear();
        _nativeAcceptanceWindowMoveActionId = 0;
    }

    private void NativeAcceptancePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var (kind, detail) = NativeAcceptanceClassifyInputSource(source);
        var token = NativeAcceptanceBeginInputActionCore(kind, detail, hasPreviewBoundary: true);
        if (token == 0 || !TryGetInputAction(token, out var action))
        {
            return;
        }

        action.PreviewStopwatchTimestamp ??= Stopwatch.GetTimestamp();
        NativeAcceptanceScheduleVisualUpdate(token, "routed input update surrogate; not compositor present");
    }

    private void NativeAcceptanceMouseUp(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var (kind, detail) = NativeAcceptanceClassifyInputSource(source);
        if (kind is not ("trainMarkerClick" or "otherClick"))
        {
            return;
        }

        var token = FindRecentInputAction(kind, Stopwatch.GetTimestamp(), includeEnded: true)?.ActionId
            ?? NativeAcceptanceBeginInputAction(kind, detail);
        if (token == 0)
        {
            return;
        }

        NativeAcceptanceWriteInputActionBoundary(
            _nativeAcceptanceInputActions[token],
            "routedHandlerSettledProxy",
            Stopwatch.GetTimestamp(),
            "routed mouse-up observation; explicit owner handler boundaries remain preferred");
        NativeAcceptanceScheduleVisualUpdate(token, "routed mouse-up update surrogate; not compositor present");
    }

    private void NativeAcceptancePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var token = NativeAcceptanceBeginInputActionCore(
            "scroll",
            source?.GetType().Name,
            hasPreviewBoundary: true);
        if (token != 0)
        {
            NativeAcceptanceScheduleVisualUpdate(token, "routed scroll update surrogate; not compositor present");
        }
    }

    private void NativeAcceptanceClick(object sender, RoutedEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        var (kind, detail) = NativeAcceptanceClassifyInputSource(source);
        if (kind is "otherClick")
        {
            return;
        }

        var token = FindRecentInputAction(kind, Stopwatch.GetTimestamp(), includeEnded: true)?.ActionId
            ?? NativeAcceptanceBeginInputAction(kind, detail);
        if (token == 0)
        {
            return;
        }

        NativeAcceptanceWriteInputActionBoundary(
            _nativeAcceptanceInputActions[token],
            "routedHandlerSettledProxy",
            Stopwatch.GetTimestamp(),
            "ButtonBase.Click route observation; not a claim about the exact owner handler end");
        NativeAcceptanceScheduleVisualUpdate(token, "routed click update surrogate; not compositor present");
    }

    private void NativeAcceptanceSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selector.SelectionChanged bubbles from many controls. Only a TabControl source is a
        // tab action; do not infer a tab click from an arbitrary control inside a TabItem.
        if (e.OriginalSource is not TabControl tabControl)
        {
            return;
        }

        var tab = tabControl.SelectedItem as TabItem;
        var kind = tabControl?.Name == "SimulationViewTabControl" ? "nestedTabClick" : "tabClick";
        var detail = tab?.Header?.ToString() ?? tabControl?.Name;
        var token = FindRecentInputAction(kind, Stopwatch.GetTimestamp(), includeEnded: true)?.ActionId
            ?? NativeAcceptanceBeginInputAction(kind, detail);
        if (token == 0)
        {
            return;
        }

        NativeAcceptanceWriteInputActionBoundary(
            _nativeAcceptanceInputActions[token],
            "selectionChangedSettledProxy",
            Stopwatch.GetTimestamp(),
            "Selector.SelectionChanged observation; visual update remains a WPF surrogate");
        NativeAcceptanceScheduleVisualUpdate(token, "tab selection WPF update surrogate; not compositor present");
    }

    private nint NativeAcceptanceWindowMessageHook(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (_nativeAcceptanceSession is null)
        {
            return 0;
        }

        switch (message)
        {
            case NativeAcceptanceWmEnterSizeMove:
                _nativeAcceptanceWindowMoveActionId = NativeAcceptanceBeginInputAction(
                    "windowMoveResize",
                    "WM_ENTERSIZEMOVE; HwndSource receipt");
                if (_nativeAcceptanceWindowMoveActionId != 0)
                {
                    NativeAcceptanceWriteInputActionBoundary(
                        _nativeAcceptanceInputActions[_nativeAcceptanceWindowMoveActionId],
                        "nativeWindowMessageReceipt",
                        Stopwatch.GetTimestamp(),
                        "WM_ENTERSIZEMOVE received by WPF HwndSource; not OS injection");
                }

                break;
            case NativeAcceptanceWmMove:
            case NativeAcceptanceWmSize:
                if (_nativeAcceptanceWindowMoveActionId == 0)
                {
                    _nativeAcceptanceWindowMoveActionId = NativeAcceptanceBeginInputAction(
                        message == NativeAcceptanceWmMove ? "windowMove" : "windowResize",
                        "WM_MOVE/WM_SIZE; HwndSource receipt");
                }

                if (_nativeAcceptanceWindowMoveActionId != 0
                    && _nativeAcceptanceInputActions.TryGetValue(_nativeAcceptanceWindowMoveActionId, out var resizeAction))
                {
                    resizeAction.NativeWindowMessageCount++;
                    resizeAction.LastNativeWindowMessage = message == NativeAcceptanceWmMove ? "WM_MOVE" : "WM_SIZE";
                }

                break;
            case NativeAcceptanceWmExitSizeMove:
                if (_nativeAcceptanceWindowMoveActionId != 0
                    && _nativeAcceptanceInputActions.TryGetValue(_nativeAcceptanceWindowMoveActionId, out var moveAction))
                {
                    NativeAcceptanceWriteInputActionBoundary(
                        moveAction,
                        "nativeWindowMessageReceipt",
                        Stopwatch.GetTimestamp(),
                        $"WM_EXITSIZEMOVE received by WPF HwndSource; messages={moveAction.NativeWindowMessageCount}");
                    NativeAcceptanceInputHandlerEnded(moveAction.ActionId, "resize/move message span ended");
                    NativeAcceptanceScheduleVisualUpdate(moveAction.ActionId, "window move/resize WPF update surrogate; not compositor present");
                    _nativeAcceptanceWindowMoveActionId = 0;
                }

                break;
            case NativeAcceptanceWmMouseWheel:
                // Mouse-wheel timing is normally captured by PreviewMouseWheel. Keep the native
                // message as a boundary note only, avoiding duplicate action spans.
                break;
        }

        return 0;
    }

    private void NativeAcceptanceScheduleVisualUpdate(long token, string detail)
    {
        try
        {
            Dispatcher.BeginInvoke(
                new Action(() => NativeAcceptanceVisualUpdateSurrogate(token, detail)),
                DispatcherPriority.Loaded);
        }
        catch
        {
        }
    }

    private void NativeAcceptanceVisualUpdateSurrogate(long token, string detail)
    {
        if (!TryGetInputAction(token, out var action)
            || action.VisualUpdateSurrogateStopwatchTimestamp is not null)
        {
            return;
        }

        action.VisualUpdateSurrogateStopwatchTimestamp = Stopwatch.GetTimestamp();
        NativeAcceptanceWriteInputActionBoundary(action, "applicationVisualUpdateSurrogate", action.VisualUpdateSurrogateStopwatchTimestamp.Value, detail);
    }

    /// <summary>
    /// Captures a bounded opt-in phase duration for an owner handler. Phase values are kept in
    /// memory and emitted with the normal handler boundary, so diagnostics do not add a writer
    /// round-trip or move the input/handler measurement boundaries.
    /// </summary>
    private void NativeAcceptancePhaseStart(long token, string phase)
    {
        if (!TryGetInputAction(token, out var action) || string.IsNullOrWhiteSpace(phase))
        {
            return;
        }

        action.PhaseStartStopwatchTimestamps.TryAdd(phase, Stopwatch.GetTimestamp());
    }

    private void NativeAcceptancePhaseEnd(long token, string phase)
    {
        if (!TryGetInputAction(token, out var action)
            || string.IsNullOrWhiteSpace(phase)
            || !action.PhaseStartStopwatchTimestamps.Remove(phase, out var start))
        {
            return;
        }

        var end = Stopwatch.GetTimestamp();
        if (end >= start)
        {
            action.PhaseDurationsMilliseconds[phase] = Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
        }
    }

    private NativeAcceptancePhaseScope NativeAcceptanceMeasurePhase(long token, string phase) =>
        new(this, token, phase);

    private readonly struct NativeAcceptancePhaseScope : IDisposable
    {
        private readonly MainWindow _owner;
        private readonly long _token;
        private readonly string _phase;

        public NativeAcceptancePhaseScope(MainWindow owner, long token, string phase)
        {
            _owner = owner;
            _token = token;
            _phase = phase;
            _owner.NativeAcceptancePhaseStart(token, phase);
        }

        public void Dispose() => _owner.NativeAcceptancePhaseEnd(_token, _phase);
    }

    private NativeAcceptanceInputActionState? FindRecentInputAction(
        string kind,
        long now,
        bool includeEnded = false)
    {
        return _nativeAcceptanceInputActions.Values
            .Where(action => string.Equals(action.Kind, kind, StringComparison.Ordinal)
                && (includeEnded || action.HandlerEndStopwatchTimestamp is null)
                && !action.RoutedBoundarySeen
                && now >= action.InputReceivedStopwatchTimestamp
                && Stopwatch.GetElapsedTime(action.InputReceivedStopwatchTimestamp, now).TotalSeconds <= 1)
            .OrderByDescending(action => action.ActionId)
            .FirstOrDefault();
    }

    private bool TryGetInputAction(long token, out NativeAcceptanceInputActionState action)
    {
        if (token != 0 && _nativeAcceptanceSession is not null
            && _nativeAcceptanceInputActions.TryGetValue(token, out action!))
        {
            return true;
        }

        action = null!;
        return false;
    }

    private void NativeAcceptanceWriteInputActionBoundary(
        NativeAcceptanceInputActionState action,
        string phase,
        long timestamp,
        string? detail)
    {
        if (_nativeAcceptanceSession is not { } session)
        {
            return;
        }

        NativeAcceptanceWrite(session, new
        {
            type = "inputAction",
            phase,
            sessionId = session.SessionId,
            actionId = action.ActionId,
            action = action.Kind,
            detail = detail ?? action.Detail,
            utc = DateTimeOffset.UtcNow,
            inputReceivedStopwatchTimestamp = action.InputReceivedStopwatchTimestamp,
            previewStopwatchTimestamp = action.PreviewStopwatchTimestamp,
            handlerStartStopwatchTimestamp = action.HandlerStartStopwatchTimestamp,
            handlerEndStopwatchTimestamp = action.HandlerEndStopwatchTimestamp,
            applicationVisualUpdateStopwatchTimestamp = action.ApplicationVisualUpdateStopwatchTimestamp,
            visualUpdateSurrogateStopwatchTimestamp = action.VisualUpdateSurrogateStopwatchTimestamp,
            boundaryTimestamp = timestamp,
            inputToHandlerMilliseconds = action.HandlerStartStopwatchTimestamp is { } start
                ? Stopwatch.GetElapsedTime(action.InputReceivedStopwatchTimestamp, start).TotalMilliseconds
                : (double?)null,
            handlerDurationMilliseconds = action.HandlerStartStopwatchTimestamp is { } handlerStart
                && action.HandlerEndStopwatchTimestamp is { } handlerEnd
                ? Stopwatch.GetElapsedTime(handlerStart, handlerEnd).TotalMilliseconds
                : (double?)null,
            inputToApplicationVisualUpdateMilliseconds = action.ApplicationVisualUpdateStopwatchTimestamp is { } visual
                ? Stopwatch.GetElapsedTime(action.InputReceivedStopwatchTimestamp, visual).TotalMilliseconds
                : (double?)null,
            inputToVisualUpdateSurrogateMilliseconds = action.VisualUpdateSurrogateStopwatchTimestamp is { } surrogate
                ? Stopwatch.GetElapsedTime(action.InputReceivedStopwatchTimestamp, surrogate).TotalMilliseconds
                : (double?)null,
            phaseTimingsMilliseconds = action.PhaseDurationsMilliseconds.Count == 0
                ? null
                : action.PhaseDurationsMilliseconds,
            activeTab = GetActiveTabMetadata(),
            simulationTimeSeconds = _latestPlaybackFrame?.SimulationTimeSeconds,
            inputBoundary = "earliest WPF receipt/preview; not OS injection",
            visualBoundary = "application visual update callback; not compositor present"
        });

        if (phase.Contains("Proxy", StringComparison.OrdinalIgnoreCase)
            || phase.Contains("Settled", StringComparison.OrdinalIgnoreCase))
        {
            action.RoutedBoundarySeen = true;
        }
    }

    private void NativeAcceptanceTrimInputActions()
    {
        while (_nativeAcceptanceInputActions.Count > 128)
        {
            var oldest = _nativeAcceptanceInputActions.Keys.Min();
            _nativeAcceptanceInputActions.Remove(oldest);
        }
    }

    private string NativeAcceptanceRecentInputActionDescription()
    {
        if (_nativeAcceptanceLastInputActionId == 0
            || !_nativeAcceptanceInputActions.TryGetValue(_nativeAcceptanceLastInputActionId, out var action))
        {
            return "none";
        }

        return $"{action.Kind}#{action.ActionId}"
            + $" input={action.InputReceivedStopwatchTimestamp}"
            + $" handlerStart={action.HandlerStartStopwatchTimestamp?.ToString() ?? "null"}"
            + $" handlerEnd={action.HandlerEndStopwatchTimestamp?.ToString() ?? "null"}";
    }

    private (string Kind, string? Detail) NativeAcceptanceClassifyInputSource(DependencyObject? source)
    {
        var button = FindVisualAncestor<ButtonBase>(source) ?? (source as ButtonBase);
        if (button is not null)
        {
            var name = button.Name;
            // 標題列播放控制是圖示鈕，Content 只是字形；語意以自動化名稱為準。
            var automationName = AutomationProperties.GetName(button);
            var content = string.IsNullOrWhiteSpace(automationName)
                ? button.Content?.ToString() ?? string.Empty
                : automationName;
            if (string.Equals(name, "PlayButton", StringComparison.Ordinal)
                || content.Contains("播放", StringComparison.Ordinal))
            {
                var isResume = _nativeAcceptanceSession?.Run is { HasPause: true, Completed: false, IsPlaying: false };
                return (isResume ? "resume" : "play", content);
            }

            if (content.Contains("暫停", StringComparison.Ordinal))
            {
                return ("pause", content);
            }

            if (content.Contains("重設", StringComparison.Ordinal))
            {
                return ("reset", content);
            }

            return ("otherClick", content);
        }

        var menuItem = FindVisualAncestor<MenuItem>(source) ?? (source as MenuItem);
        if (menuItem is not null)
        {
            var tag = menuItem.Tag?.ToString();
            var content = menuItem.Header?.ToString() ?? AutomationProperties.GetName(menuItem);
            var semantic = string.IsNullOrWhiteSpace(tag) ? content : tag;
            if (semantic.Contains("播放", StringComparison.OrdinalIgnoreCase)
                || semantic.Contains("play", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "play", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "resume", StringComparison.OrdinalIgnoreCase))
            {
                var isResume = string.Equals(tag, "resume", StringComparison.OrdinalIgnoreCase)
                    || _nativeAcceptanceSession?.Run is { HasPause: true, Completed: false, IsPlaying: false };
                return (isResume ? "resume" : "play", content);
            }

            if (semantic.Contains("暫停", StringComparison.OrdinalIgnoreCase)
                || semantic.Contains("pause", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "pause", StringComparison.OrdinalIgnoreCase))
            {
                return ("pause", content);
            }

            if (semantic.Contains("重設", StringComparison.OrdinalIgnoreCase)
                || semantic.Contains("reset", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag, "reset", StringComparison.OrdinalIgnoreCase))
            {
                return ("reset", content);
            }
        }

        var border = FindVisualAncestor<Border>(source) ?? (source as Border);
        if (border?.Tag is string vehicleId && !string.IsNullOrWhiteSpace(vehicleId))
        {
            return ("trainMarkerClick", vehicleId);
        }

        var tab = FindVisualAncestor<TabItem>(source) ?? (source as TabItem);
        if (tab is not null)
        {
            var tabControl = FindVisualAncestor<TabControl>(tab);
            return (tabControl?.Name == "SimulationViewTabControl" ? "nestedTabClick" : "tabClick", tab.Header?.ToString());
        }

        return ("otherClick", source?.GetType().Name);
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual || current is Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private sealed class NativeAcceptanceInputActionState
    {
        public NativeAcceptanceInputActionState(
            long actionId,
            string kind,
            string? detail,
            long timestamp,
            bool hasPreviewBoundary)
        {
            ActionId = actionId;
            Kind = kind;
            Detail = detail;
            InputReceivedStopwatchTimestamp = timestamp;
            PreviewStopwatchTimestamp = hasPreviewBoundary ? timestamp : null;
        }

        public long ActionId { get; }
        public string Kind { get; }
        public string? Detail { get; set; }
        public long InputReceivedStopwatchTimestamp { get; }
        public long? PreviewStopwatchTimestamp { get; set; }
        public long? HandlerStartStopwatchTimestamp { get; set; }
        public long? HandlerEndStopwatchTimestamp { get; set; }
        public long? ApplicationVisualUpdateStopwatchTimestamp { get; set; }
        public long? VisualUpdateSurrogateStopwatchTimestamp { get; set; }
        public Dictionary<string, long> PhaseStartStopwatchTimestamps { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, double> PhaseDurationsMilliseconds { get; } = new(StringComparer.Ordinal);
        public bool RoutedBoundarySeen { get; set; }
        public int NativeWindowMessageCount { get; set; }
        public string? LastNativeWindowMessage { get; set; }
    }
}

/// <summary>
/// Compact, read-only input instrumentation state for offscreen tests and acceptance scripts.
/// </summary>
public readonly record struct NativeAcceptanceInputDiagnosticsSnapshot(
    bool IsActive,
    bool HwndSourceHookInstalled,
    int OpenActionCount,
    int TotalActionCount,
    int StallCount,
    int HandlerEndedActionCount,
    int ApplicationVisualUpdateCount);
