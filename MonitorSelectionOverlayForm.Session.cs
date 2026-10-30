using Forms = System.Windows.Forms;

namespace BetterScreenShot
{
    internal sealed partial class MonitorSelectionOverlayForm
    {
        public static Task<Rectangle?> SelectAreaAsync() => SelectAsync(SelectionMode.Area);

        public static Task<Rectangle?> SelectMonitorAsync() => SelectAsync(SelectionMode.Monitor);

        public static void CancelActiveSelection() => activeSession?.Dispose();

        private static Task<Rectangle?> SelectAsync(SelectionMode mode)
        {
            if (activeSession is not null)
            {
                throw new InvalidOperationException("A capture selection is already active.");
            }

            var overlays = GetOrCreateOverlays();
            var inputWindows = new List<MonitorSelectionInputForm>();
            MonitorSelectionHooks? hooks = null;
            var session = new CaptureSelectionSession(() =>
            {
                var errors = new List<Exception>();
                void Release(Action release)
                {
                    try
                    {
                        release();
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }

                foreach (var inputWindow in inputWindows)
                {
                    Release(inputWindow.Dispose);
                }

                Release(() => hooks?.Dispose());
                foreach (var overlay in overlays)
                {
                    Release(overlay.EndSelectionSession);
                }

                activeSession = null;
                if (errors.Count > 0)
                {
                    throw new AggregateException("Could not close all capture overlays.", errors);
                }
            });

            activeSession = session;
            try
            {
                void CompleteMonitor(MonitorSelectionOverlayForm? overlay) => session.Complete(overlay?.captureBounds);
                hooks = new MonitorSelectionHooks(CompleteMonitor);
                foreach (var overlay in overlays)
                {
                    if (session.Task.IsCompleted)
                    {
                        break;
                    }

                    overlay.BeginSelectionSession(mode, session.Complete);
                    overlay.Show();
                    overlay.BringInputToFront();
                }

                if (mode == SelectionMode.Monitor && !session.Task.IsCompleted)
                {
                    foreach (var overlay in overlays)
                    {
                        if (session.Task.IsCompleted)
                        {
                            break;
                        }

                        var inputWindow = new MonitorSelectionInputForm(overlay, overlays, CompleteMonitor);
                        inputWindows.Add(inputWindow);
                        inputWindow.Show();
                    }

                    if (!session.Task.IsCompleted)
                    {
                        RefreshMonitorHoverState(overlays, Forms.Control.MousePosition);
                        ApplyMonitorSelectionCursor();
                    }
                }
            }
            catch (Exception ex)
            {
                session.Fail(ex);
            }

            return session.Task;
        }
    }
}
