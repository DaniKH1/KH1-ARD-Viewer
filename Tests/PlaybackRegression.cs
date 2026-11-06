using System.Diagnostics;
using System.Reflection;
using Kh1Import;

internal static class PlaybackRegression
{
    internal static void Check(ArdViewerModel model, int clip)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { CheckOnUiThread(model, clip); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("Native playback regression failed.", failure);
    }

    private static void CheckOnUiThread(ArdViewerModel model, int clip)
    {
        // Exercise the production Play and Tick handlers without showing a form
        // or creating an OpenGL context. The elapsed playback time is injected.
        using var form = new NativeViewerForm();
        var viewport = Get<NativeGlViewport>(form, "_viewport");
        var timer = Get<System.Windows.Forms.Timer>(form, "_msetPlaybackTimer");
        try
        {
            viewport.SetModel(model);
            Set(viewport, "_basePositions", model.Meshes.SelectMany(mesh => mesh.Positions).ToArray());
            Set(viewport, "_positions", model.Meshes.SelectMany(mesh => mesh.Positions).ToArray());
            Set(viewport, "_jointIds", model.Meshes.SelectMany(mesh => mesh.JointIds).ToArray());
            Set(form, "_selected", model);
            Set(form, "_activeMsetSection", clip);
            Call(form, "ToggleMsetPlayback");
            if (!timer.Enabled) throw new Exception("Play did not start the playback timer.");
            float[] initial = Get<float[]>(viewport, "_positions").ToArray();
            Set(form, "_msetPlaybackStart", Stopwatch.GetTimestamp() - Stopwatch.Frequency / 2);
            Call(form, "AdvanceMsetPose");
            if (!timer.Enabled) throw new Exception("The first timer tick stopped a valid helper-only clip.");
            if (Get<float>(form, "_activeMsetFrame") <= 0)
                throw new Exception("The timer did not advance the animation frame.");
            float[] animated = Get<float[]>(viewport, "_positions");
            if (!initial.Zip(animated).Any(pair => Math.Abs(pair.First - pair.Second) > 0.00001f))
                throw new Exception("The timer advanced without moving any rendered vertices.");
            Call(form, "ToggleMsetPlayback");
            if (timer.Enabled) throw new Exception("Pause did not stop the playback timer.");
        }
        finally { timer.Dispose(); }
    }

    private static FieldInfo Field(object target, string name) => target.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static T Get<T>(object target, string name) => (T)Field(target, name).GetValue(target)!;
    private static void Set(object target, string name, object value) => Field(target, name).SetValue(target, value);
    private static void Call(object target, string name) => target.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null);
}
