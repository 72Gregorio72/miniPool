using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Look of the picture: MSAA 4x on the URP asset, and a dark outline on everything opaque the main camera draws
/// (full-screen pass on depth + normals, shader <c>Resources/Outline.shader</c>). F8 turns the outline on and off (saved in PlayerPrefs).
/// Starts by itself; nothing to add to the renderer asset.
/// </summary>
public static class Outline
{
    const string Pref = "outline";

    /// <summary>True while the outline is drawn.</summary>
    public static bool On { get; private set; }

    static Pass pass;

    [RuntimeInitializeOnLoadMethod]
    static void Init()
    {
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp && urp.msaaSampleCount < 4) urp.msaaSampleCount = 4;
        On = PlayerPrefs.GetInt(Pref, 1) == 1;
        pass = new Pass { mat = new Material(Resources.Load<Shader>("Outline")) };
        RenderPipelineManager.beginCameraRendering -= Begin;
        RenderPipelineManager.beginCameraRendering += Begin;
    }

    // shortcut: only Camera.main (the previews and editor icons render without outline), extend when the outline is kept
    static void Begin(ScriptableRenderContext _, Camera cam)
    {
        if (cam != Camera.main) return;
        var keys = Keyboard.current; // once per frame: the main camera renders once
        if (keys != null && keys.f8Key.wasPressedThisFrame) PlayerPrefs.SetInt(Pref, (On = !On) ? 1 : 0);
        var data = cam.GetUniversalAdditionalCameraData();
        // the outline comes from the depth texture, which has no MSAA: SMAA smooths its steps (the default volume profile is neutral)
        data.renderPostProcessing = On;
        data.antialiasing = On ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
        if (!On) return;
        pass.mat.SetFloat("_Width", Mathf.Max(1, Mathf.Round(cam.pixelHeight / 720f)));
        data.scriptableRenderer.EnqueuePass(pass);
    }

    class Pass : ScriptableRenderPass
    {
        public Material mat;

        class Data { public Material mat; }

        public Pass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents; // under the UI canvas and the transparent effects
            requiresIntermediateTexture = true;
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            var res = frame.Get<UniversalResourceData>();
            using var builder = graph.AddRasterRenderPass<Data>("Outline", out var data);
            data.mat = mat;
            builder.UseTexture(res.cameraDepthTexture);
            builder.UseTexture(res.cameraNormalsTexture);
            builder.SetRenderAttachment(res.activeColorTexture, 0);
            builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) => ctx.cmd.DrawProcedural(Matrix4x4.identity, d.mat, 0, MeshTopology.Triangles, 3));
        }
    }
}
