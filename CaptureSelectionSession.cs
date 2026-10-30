using System.Drawing;
using System.Threading.Tasks;

namespace BetterScreenShot
{
    // Every exit path releases the overlays before resuming the capture caller.
    internal sealed class CaptureSelectionSession : IDisposable
    {
        private readonly TaskCompletionSource<Rectangle?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Action cleanup;
        private bool completed;

        public CaptureSelectionSession(Action cleanup)
        {
            this.cleanup = cleanup;
        }

        public Task<Rectangle?> Task => completion.Task;

        public void Complete(Rectangle? bounds) => Finish(bounds, null);

        public void Fail(Exception error) => Finish(null, error);

        public void Dispose() => Complete(null);

        private void Finish(Rectangle? bounds, Exception? error)
        {
            if (completed)
            {
                return;
            }

            completed = true;
            try
            {
                cleanup();
            }
            catch (Exception cleanupError)
            {
                error = error is null ? cleanupError : new AggregateException(error, cleanupError);
            }

            if (error is null)
            {
                completion.SetResult(bounds);
            }
            else
            {
                completion.SetException(error);
            }
        }
    }
}
