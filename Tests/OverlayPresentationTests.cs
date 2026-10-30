using System.Reflection;
using System.Runtime.InteropServices;
using BetterScreenShot;
using Forms = System.Windows.Forms;

internal static class OverlayPresentationTests
{
    public static void Run(Action<string, Action> check)
    {
        foreach (var monitorMode in new[] { false, true })
        {
            check($"{(monitorMode ? "Monitor" : "Area")} overlays paint transparently on first show and reuse", () =>
            {
                // Recreate the cache so each mode exercises its own first display.
                var overlayType = typeof(MonitorSelectionOverlayForm);
                overlayType.GetMethod("DisposeCachedOverlays", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
                MonitorSelectionOverlayForm.WarmUpOverlays();
                var overlays = (List<MonitorSelectionOverlayForm>)overlayType
                    .GetField("cachedOverlays", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
                var painted = new HashSet<Forms.Form>();
                var errors = new List<string>();
                var shows = 0;
                foreach (var overlay in overlays)
                {
                    overlay.VisibleChanged += (_, _) =>
                    {
                        if (overlay.Visible)
                        {
                            shows++;
                            painted.Remove(overlay);
                            if (ReadAlpha(overlay) != 0)
                                errors.Add("An overlay was revealed before its surface was painted.");
                        }
                    };
                    overlay.Paint += (_, _) =>
                    {
                        if (painted.Add(overlay) && ReadAlpha(overlay) != 0)
                            errors.Add("An overlay's first paint was visible.");
                    };
                }

                for (var attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        var selection = monitorMode
                            ? MonitorSelectionOverlayForm.SelectMonitorAsync()
                            : MonitorSelectionOverlayForm.SelectAreaAsync();
                        Require(!selection.IsCompleted, "Selection ended before input.");
                        Require(painted.Count == overlays.Count, "Not every monitor was painted before selection started.");
                        foreach (var overlay in overlays)
                        {
                            Require(ReadAlpha(overlay) == 115, "Incorrect final dimming level.");
                            Require(overlay.BackColor == Color.Black && overlay.AllowTransparency && overlay.Opacity < 1,
                                "Managed transparency must match the native overlay.");
                        }

                        if (monitorMode)
                        {
                            var inputs = Forms.Application.OpenForms.Cast<Forms.Form>()
                                .Where(form => form.GetType().Name == "MonitorSelectionInputForm").ToArray();
                            Require(inputs.Length == overlays.Count, "Missing monitor input windows.");
                            foreach (var input in inputs)
                                Require(ReadAlpha(input) == 1 && input.AllowTransparency && input.Opacity < 1,
                                    "Monitor input windows must remain nearly transparent.");
                        }

                        Require(errors.Count == 0, string.Join(" ", errors));
                        MonitorSelectionOverlayForm.CancelActiveSelection();
                        Require(selection.IsCompletedSuccessfully && selection.Result is null, "Cancellation failed.");
                        Require(overlays.All(overlay => !overlay.Visible), "An overlay remained visible after cancellation.");
                    }
                    finally
                    {
                        MonitorSelectionOverlayForm.CancelActiveSelection();
                    }
                }

                Require(shows == overlays.Count * 2, "Both attempts must show every monitor.");
            });
        }
    }

    private static byte ReadAlpha(Forms.Form form)
    {
        Require(GetLayeredWindowAttributes(form.Handle, out _, out var alpha, out var flags) && (flags & 2) != 0,
            "The window has no native alpha transparency.");
        return alpha;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint colorKey, out byte alpha, out uint flags);
}
