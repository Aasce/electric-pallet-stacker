using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace PalletStackerRendering
{
    public sealed class SilhouetteOutlineFeature : ScriptableRendererFeature
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Only renderers on these layers contribute to the silhouette.")]
            public LayerMask layerMask = 1 << 9;

            [ColorUsage(true, true)]
            public Color outlineColor = new Color(0f, 3f, 8f, 1f);

            [Min(0.5f)]
            [Tooltip("Outline thickness in screen pixels. This value has no upper limit.")]
            public float thickness = 4f;

            public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingTransparents;
        }

        [Header("Shader References")]
        [Tooltip("Explicit mask shader reference. Keep this assigned so player builds retain the shader.")]
        [SerializeField] private Shader maskShader;
        [Tooltip("Explicit composite shader reference. Keep this assigned so player builds retain the shader.")]
        [SerializeField] private Shader compositeShader;
        [SerializeField] private Settings settings = new Settings();

        private Material maskMaterial;
        private Material compositeMaterial;
        private SilhouetteOutlinePass outlinePass;

        public override void Create()
        {
            CoreUtils.Destroy(maskMaterial);
            CoreUtils.Destroy(compositeMaterial);

            if (maskShader == null || compositeShader == null)
            {
                outlinePass = null;
                return;
            }

            maskMaterial = CoreUtils.CreateEngineMaterial(maskShader);
            compositeMaterial = CoreUtils.CreateEngineMaterial(compositeShader);
            outlinePass = new SilhouetteOutlinePass(maskMaterial, compositeMaterial, settings)
            {
                renderPassEvent = settings.injectionPoint
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (outlinePass == null || maskMaterial == null || compositeMaterial == null)
                return;

            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return;

            outlinePass.Setup(settings);
            renderer.EnqueuePass(outlinePass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(maskMaterial);
            CoreUtils.Destroy(compositeMaterial);
        }

        private sealed class SilhouetteOutlinePass : ScriptableRenderPass
        {
            private const string MaskPassName = "Pallet Silhouette Mask";
            private const string CompositePassName = "Pallet Silhouette Composite";
            private static readonly int MaskTextureId = Shader.PropertyToID("_SilhouetteMaskTexture");
            private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
            private static readonly int OutlineThicknessId = Shader.PropertyToID("_OutlineThickness");

            private readonly Material maskMaterial;
            private readonly Material compositeMaterial;
            private readonly List<ShaderTagId> shaderTagIds = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("LightweightForward")
            };

            private Settings settings;

            private sealed class MaskPassData
            {
                public RendererListHandle rendererList;
            }

            private sealed class CompositePassData
            {
                public TextureHandle source;
                public TextureHandle mask;
                public Material material;
                public Color color;
                public float thickness;
            }

            public SilhouetteOutlinePass(Material maskMaterial, Material compositeMaterial, Settings settings)
            {
                this.maskMaterial = maskMaterial;
                this.compositeMaterial = compositeMaterial;
                this.settings = settings;
                requiresIntermediateTexture = true;
            }

            public void Setup(Settings currentSettings)
            {
                settings = currentSettings;
                renderPassEvent = settings.injectionPoint;
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer)
                    return;

                TextureHandle source = resourceData.activeColorTexture;
                TextureHandle depth = resourceData.activeDepthTexture;
                if (!source.IsValid() || !depth.IsValid())
                    return;

                TextureDesc maskDescriptor = renderGraph.GetTextureDesc(source);
                maskDescriptor.name = "_PalletSilhouetteMask";
                maskDescriptor.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                maskDescriptor.clearBuffer = true;
                maskDescriptor.clearColor = Color.black;
                maskDescriptor.filterMode = FilterMode.Bilinear;
                maskDescriptor.wrapMode = TextureWrapMode.Clamp;
                TextureHandle mask = renderGraph.CreateTexture(maskDescriptor);

                RecordMaskPass(renderGraph, frameData, mask, depth);

                TextureDesc destinationDescriptor = renderGraph.GetTextureDesc(source);
                destinationDescriptor.name = "CameraColor-PalletSilhouette";
                destinationDescriptor.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(destinationDescriptor);

                RecordCompositePass(renderGraph, source, mask, destination);
                resourceData.cameraColor = destination;
            }

            private void RecordMaskPass(
                RenderGraph renderGraph,
                ContextContainer frameData,
                TextureHandle mask,
                TextureHandle depth)
            {
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.all, settings.layerMask);
                DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                    shaderTagIds,
                    renderingData,
                    cameraData,
                    lightData,
                    cameraData.defaultOpaqueSortFlags);
                drawingSettings.overrideMaterial = maskMaterial;
                drawingSettings.overrideMaterialPassIndex = 0;

                RendererListParams rendererListParams = new RendererListParams(
                    renderingData.cullResults,
                    drawingSettings,
                    filteringSettings);
                RendererListHandle rendererList = renderGraph.CreateRendererList(rendererListParams);

                using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<MaskPassData>(
                    MaskPassName,
                    out MaskPassData passData);
                passData.rendererList = rendererList;

                builder.UseRendererList(rendererList);
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(depth, AccessFlags.Read);
                builder.SetRenderFunc(static (MaskPassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawRendererList(data.rendererList);
                });
            }

            private void RecordCompositePass(
                RenderGraph renderGraph,
                TextureHandle source,
                TextureHandle mask,
                TextureHandle destination)
            {
                using IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass<CompositePassData>(
                    CompositePassName,
                    out CompositePassData passData);

                passData.source = source;
                passData.mask = mask;
                passData.material = compositeMaterial;
                passData.color = settings.outlineColor;
                passData.thickness = Mathf.Max(0.5f, settings.thickness);

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(mask, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext context) =>
                {
                    data.material.SetTexture(MaskTextureId, data.mask);
                    data.material.SetColor(OutlineColorId, data.color);
                    data.material.SetFloat(OutlineThicknessId, data.thickness);
                    Blitter.BlitTexture(
                        context.cmd,
                        data.source,
                        new Vector4(1f, 1f, 0f, 0f),
                        data.material,
                        0);
                });
            }
        }
    }
}
