using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Generals
{
    /// <summary>
    /// Экранный контур для URP: объекты из SelectionOutline рисуются в маску (силуэт и его видимая часть),
    /// затем вокруг силуэта на экран кладётся линия постоянной толщины в пикселях.
    /// Где объект закрыт, линия тусклее, а закрытая часть слегка заливается цветом контура.
    /// Добавляется в ассеты рендерера автоматически (Editor/OutlineFeatureInstaller).
    /// </summary>
    public class OutlineFeature : ScriptableRendererFeature
    {
        OutlinePass pass;
        Material material;

        public override void Create()
        {
            var shader = Shader.Find("Hidden/NoobGenerals/Outline");
            if (shader == null)
                return;
            material = CoreUtils.CreateEngineMaterial(shader);
            pass = new OutlinePass(material) { renderPassEvent = RenderPassEvent.AfterRenderingTransparents };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass == null || renderingData.cameraData.cameraType != CameraType.Game || !SelectionOutline.HasTargets)
                return;
            renderer.EnqueuePass(pass);
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (pass == null || renderingData.cameraData.cameraType != CameraType.Game || !SelectionOutline.HasTargets)
                return;
            pass.SetTargets(renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);
        }

        protected override void Dispose(bool disposing)
        {
            pass?.Dispose();
            CoreUtils.Destroy(material);
        }

        class OutlinePass : ScriptableRenderPass
        {
            const int SilhouettePass = 0;
            const int VisiblePass = 1;
            const int CompositePass = 2;

            static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
            static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");
            static readonly int HiddenLineId = Shader.PropertyToID("_HiddenLineAlpha");
            static readonly int HiddenFillId = Shader.PropertyToID("_HiddenFillAlpha");

            readonly Material material;
            readonly ProfilingSampler sampler = new("Selection Outline");
            RTHandle mask;
            RTHandle cameraColor;
            RTHandle cameraDepth;

            public OutlinePass(Material material)
            {
                this.material = material;
            }

            public void SetTargets(RTHandle color, RTHandle depth)
            {
                cameraColor = color;
                cameraDepth = depth;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                // Маска того же размера и с тем же MSAA, что и камера: так её можно рисовать
                // с буфером глубины камеры, а дробная маска на краях после резолва сглаживает линию
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.graphicsFormat = GraphicsFormat.R8G8_UNorm;
                RenderingUtils.ReAllocateIfNeeded(ref mask, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_SelectionOutlineMask");

                ConfigureTarget(mask, cameraDepth);
                ConfigureClear(ClearFlag.Color, Color.clear);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, sampler))
                {
                    foreach (var r in SelectionOutline.Renderers)
                    {
                        if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                            continue;
                        cmd.DrawRenderer(r, material, 0, SilhouettePass);
                        cmd.DrawRenderer(r, material, 0, VisiblePass);
                    }

                    float height = renderingData.cameraData.cameraTargetDescriptor.height;
                    material.SetColor(ColorId, SelectionOutline.Color);
                    material.SetFloat(WidthId, Mathf.Max(2f, SelectionOutline.WidthAt1080 * height / 1080f));
                    material.SetFloat(HiddenLineId, SelectionOutline.HiddenLineAlpha);
                    material.SetFloat(HiddenFillId, SelectionOutline.HiddenFillAlpha);

                    CoreUtils.SetRenderTarget(cmd, cameraColor);
                    Blitter.BlitTexture(cmd, mask, new Vector4(1f, 1f, 0f, 0f), material, CompositePass);
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                mask?.Release();
                mask = null;
            }
        }
    }
}
