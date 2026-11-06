using System.Diagnostics;
using System.Reflection;
using System.Numerics;
using System.Text.Json;
using Kh1Import;

internal static class PlaybackRegression
{
    internal static void AuditDirectory(string directory, string reportPath)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { AuditOnUiThread(directory, reportPath); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw new Exception("Playback audit failed.", failure);
    }

    private static void AuditOnUiThread(string directory, string reportPath)
    {
        var results = new List<object>();
        int clips = 0, animatedClips = 0, movingClips = 0, ticks = 0, failures = 0;
        using var form = new NativeViewerForm();
        var viewport = Get<NativeGlViewport>(form, "_viewport");
        var timer = Get<System.Windows.Forms.Timer>(form, "_msetPlaybackTimer");
        try
        {
            foreach (string archive in Directory.EnumerateFiles(directory, "*.ard"))
            {
                foreach (var model in ArdViewer.Read(archive))
                {
                    viewport.SetModel(model);
                    float[] vertices = model.Meshes.SelectMany(mesh => mesh.Positions).ToArray();
                    Set(viewport, "_basePositions", vertices);
                    Set(viewport, "_positions", vertices.ToArray());
                    Set(viewport, "_jointIds", model.Meshes.SelectMany(mesh => mesh.JointIds).ToArray());
                    Set(form, "_selected", model);
                    for (int clip = 0; clip < model.MsetSections.Count; clip++)
                    {
                        var section = model.MsetSections[clip];
                        clips++;
                        if (!section.HasAnimation || section.FrameEnd <= 0 || section.FramesPerSecond <= 0 ||
                            section.JointLocalMatrices.Count != model.Joints.Count) continue;
                        animatedClips++;
                        bool vertexMovement = false, boneMovement = false, curveMovement = false;
                        float maxVertexDelta = 0;
                        int clipTicks = 0;
                        string? error = null;
                        try
                        {
                            Set(form, "_activeMsetSection", clip);
                            Call(form, "ToggleMsetPlayback");
                            if (!timer.Enabled) throw new Exception("Play did not start.");
                            if (Get<int>(form, "_activeMsetSection") != clip)
                                throw new Exception("Play selected a different clip.");
                            float[] initial = Get<float[]>(viewport, "_positions").ToArray();
                            var initialBones = Get<Matrix4x4[]>(viewport, "_jointWorldMatrices").ToArray();
                            var curves = section.Curves.Concat(section.HelperCurves).ToArray();
                            var frames = new SortedSet<float>();
                            for (int i = 1; i < 16; i++) frames.Add(section.FrameEnd * i / 16f);
                            foreach (var curve in curves)
                                for (int i = 0; i < curve.Keys.Count; i++)
                                {
                                    frames.Add(curve.Keys[i].Time / 256f);
                                    if (i > 0) frames.Add((curve.Keys[i - 1].Time + curve.Keys[i].Time) / 512f);
                                }
                            foreach (float frame in frames.Where(f => f > 0 && f < section.FrameEnd))
                            {
                                Set(form, "_msetPlaybackStart", Stopwatch.GetTimestamp() -
                                    (long)(frame / section.FramesPerSecond * Stopwatch.Frequency));
                                Call(form, "AdvanceMsetPose");
                                if (!timer.Enabled) throw new Exception("Tick stopped playback.");
                                float actualFrame = Get<float>(form, "_activeMsetFrame");
                                if (actualFrame <= 0) throw new Exception("Frame did not advance.");
                                float[] positions = Get<float[]>(viewport, "_positions");
                                for (int i = 0; i < positions.Length; i++)
                                {
                                    if (!float.IsFinite(positions[i])) throw new Exception("Non-finite vertex.");
                                    maxVertexDelta = Math.Max(maxVertexDelta, Math.Abs(positions[i] - initial[i]));
                                }
                                var bones = Get<Matrix4x4[]>(viewport, "_jointWorldMatrices");
                                for (int i = 0; i < bones.Length; i++)
                                {
                                    if (!float.IsFinite(bones[i].GetDeterminant())) throw new Exception("Non-finite bone.");
                                    boneMovement |= MatrixDelta(initialBones[i], bones[i]) > 0.00001f;
                                }
                                curveMovement |= curves.Any(curve => Math.Abs(curve.Sample(actualFrame) - curve.Sample(0)) > 0.00001f);
                                ticks++;
                                clipTicks++;
                            }
                            vertexMovement = maxVertexDelta > 0.00001f;
                            if (vertexMovement) movingClips++;
                            Call(form, "ToggleMsetPlayback");
                            if (timer.Enabled) throw new Exception("Pause did not stop.");
                        }
                        catch (Exception ex)
                        {
                            error = ex.GetBaseException().Message;
                            failures++;
                            if (timer.Enabled) Call(form, "StopMsetPlayback");
                        }
                        results.Add(new { Archive = Path.GetFileName(archive), Model = model.Name, Clip = clip,
                            section.FrameEnd, JointCurves = section.Curves.Count, HelperCurves = section.HelperCurves.Count,
                            CurveMovement = curveMovement, BoneMovement = boneMovement, VertexMovement = vertexMovement,
                            MaxVertexDelta = maxVertexDelta, Ticks = clipTicks, Error = error });
                    }
                }
                Console.WriteLine($"Audited {Path.GetFileName(archive)}: {animatedClips} playable, {movingClips} moving, {ticks} ticks, {failures} failures.");
            }
        }
        finally { timer.Dispose(); }
        File.WriteAllText(reportPath, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"TOTAL: {clips} clips, {animatedClips} playable, {movingClips} moving, {ticks} ticks, {failures} failures.");
        if (failures != 0) throw new Exception($"{failures} playback failures; see {reportPath}");
    }

    private static float MatrixDelta(Matrix4x4 a, Matrix4x4 b)
    {
        Matrix4x4 d = a - b;
        return new[] { d.M11,d.M12,d.M13,d.M14,d.M21,d.M22,d.M23,d.M24,
            d.M31,d.M32,d.M33,d.M34,d.M41,d.M42,d.M43,d.M44 }.Max(Math.Abs);
    }

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
