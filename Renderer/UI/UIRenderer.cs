namespace VoxPopuli.Renderer.UI;

using System;
using System.Numerics;
using SDL;

/// <summary>GPU resource owner for UI rendering. Manages pipeline, vertex buffer, and transfer buffer.</summary>
/// <remarks>Does not implement ISubRenderer — driven directly by SdlRenderer.</remarks>
internal sealed unsafe class UIRenderer : IDisposable
{
    private const int MaxQuads = 4096;
    // UIQuadVertex is 24 bytes: Vector2 (8) + Color4 (16)
    private static readonly uint VertexBufferSize = (uint)(MaxQuads * 6 * sizeof(UIQuadVertex));

    private readonly UIQuadVertex[] _vertices;
    private int _vertexCount;
    private uint _width;
    private uint _height;

    private SDL_GPUBuffer* _vertexBuffer;
    private SDL_GPUTransferBuffer* _transferBuffer;
    private SDL_GPUGraphicsPipeline* _pipeline;
    private readonly SdlGpuDevice _gpu;
    private readonly TextRenderer _textRenderer;

    /// <summary>Creates a UI renderer with the given GPU device.</summary>
    /// <param name="gpu">The GPU device to use for rendering.</param>
    /// <param name="fontPath">Path to the font file for text rendering.</param>
    /// <param name="fontSize">Font size in points.</param>
    internal UIRenderer(SdlGpuDevice gpu, string fontPath, float fontSize)
    {
        _gpu = gpu;
        _vertices = new UIQuadVertex[MaxQuads * 6];
        _vertexCount = 0;
        _width = 0;
        _height = 0;

        _textRenderer = new TextRenderer(gpu, fontPath, fontSize);

        _pipeline = CreatePipeline(gpu);
        if (_pipeline == null)
        {
            throw new UIRenderingException("Failed to create graphics pipeline");
        }

        _vertexBuffer = CreateVertexBuffer(gpu);
        if (_vertexBuffer == null)
        {
            throw new UIRenderingException("Failed to create vertex buffer");
        }

        _transferBuffer = CreateTransferBuffer(gpu);
        if (_transferBuffer == null)
        {
            throw new UIRenderingException("Failed to create transfer buffer");
        }
    }

    /// <summary>Gets a new UIDrawContext for this frame, backed by the owned vertex buffer.</summary>
    /// <returns>A new UIDrawContext that writes into this renderer's vertex buffer.</returns>
    internal UIDrawContext GetUIDrawContext()
    {
        _vertexCount = 0;
        _textRenderer.Reset();
        return new UIDrawContext(_vertices.AsSpan(), _width, _height, ref _vertexCount, _textRenderer.GetTextBuffer(), ref _textRenderer.TextCount);
    }

    /// <summary>Prepares the frame: resets vertex count, uploads vertex data via copy pass.</summary>
    /// <param name="cmd">The command buffer to use for the copy pass.</param>
    /// <param name="frame">The frame data containing width/height and render pass info.</param>
    internal unsafe void PrepareFrame(SDL_GPUCommandBuffer* cmd, in RenderFrame frame)
    {
        _width = frame.Width;
        _height = frame.Height;

        _textRenderer.PrepareFrame(cmd);

        if (_vertexCount == 0)
        {
            return;
        }

        var transferSize = (uint)(_vertexCount * sizeof(UIQuadVertex));
        var transferBufInfo = new SDL_GPUTransferBufferCreateInfo
        {
            usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
            size = transferSize
        };
        var transferBuf = SDL3.SDL_CreateGPUTransferBuffer(_gpu.Device, &transferBufInfo);
        if (transferBuf == null)
        {
            throw new UIRenderingException("Failed to create temporary transfer buffer");
        }

        var mapped = (UIQuadVertex*)SDL3.SDL_MapGPUTransferBuffer(_gpu.Device, transferBuf, false);
        if (mapped == null)
        {
            SDL3.SDL_ReleaseGPUTransferBuffer(_gpu.Device, transferBuf);
            throw new UIRenderingException("Failed to map transfer buffer");
        }

        fixed (UIQuadVertex* src = _vertices)
        {
            Buffer.MemoryCopy(src, mapped, transferSize, transferSize);
        }

        SDL3.SDL_UnmapGPUTransferBuffer(_gpu.Device, transferBuf);

        var copyPass = SDL3.SDL_BeginGPUCopyPass(cmd);
        var srcLoc = new SDL_GPUTransferBufferLocation { transfer_buffer = transferBuf, offset = 0 };
        var dstRegion = new SDL_GPUBufferRegion { buffer = _vertexBuffer, offset = 0, size = transferSize };
        SDL3.SDL_UploadToGPUBuffer(copyPass, &srcLoc, &dstRegion, false);
        SDL3.SDL_EndGPUCopyPass(copyPass);

        SDL3.SDL_ReleaseGPUTransferBuffer(_gpu.Device, transferBuf);
    }

    /// <summary>Draws all UI geometry for this frame.</summary>
    /// <param name="frame">The frame data containing the render pass.</param>
    internal void Draw(in RenderFrame frame)
    {
        var pass = frame.RenderPass;
        var cmd = frame.CommandBuffer;

        if (_vertexCount > 0)
        {
            var screenSize = new Vector2(_width, _height);
            SDL3.SDL_PushGPUVertexUniformData(cmd, 0, (IntPtr)(&screenSize), (uint)sizeof(Vector2));

            SDL3.SDL_BindGPUGraphicsPipeline(pass, _pipeline);

            var vertexBinding = new SDL_GPUBufferBinding { buffer = _vertexBuffer, offset = 0 };
            SDL3.SDL_BindGPUVertexBuffers(pass, 0, &vertexBinding, 1);

            SDL3.SDL_DrawGPUPrimitives(pass, (uint)_vertexCount, 1, 0, 0);
        }

        _textRenderer.Draw(pass, cmd, _width, _height);
    }

    /// <summary>Disposes all GPU resources.</summary>
    public void Dispose()
    {
        if (_pipeline != null)
        {
            SDL3.SDL_ReleaseGPUGraphicsPipeline(_gpu.Device, _pipeline);
            _pipeline = null;
        }

        if (_vertexBuffer != null)
        {
            SDL3.SDL_ReleaseGPUBuffer(_gpu.Device, _vertexBuffer);
            _vertexBuffer = null;
        }

        if (_transferBuffer != null)
        {
            SDL3.SDL_ReleaseGPUTransferBuffer(_gpu.Device, _transferBuffer);
            _transferBuffer = null;
        }

        _textRenderer.Dispose();
    }

    private static SDL_GPUGraphicsPipeline* CreatePipeline(SdlGpuDevice gpu)
    {
        var vert = gpu.LoadShaderInternal("UI.vert", numUniformBuffers: 1);
        if (vert == null)
        {
            throw new UIRenderingException("Failed to load UI vertex shader");
        }

        var frag = gpu.LoadShaderInternal("UI.frag", numUniformBuffers: 0);
        if (frag == null)
        {
            SDL3.SDL_ReleaseGPUShader(gpu.Device, vert);
            throw new UIRenderingException("Failed to load UI fragment shader");
        }

        var bufDesc = new SDL_GPUVertexBufferDescription
        {
            slot = 0,
            pitch = (uint)sizeof(UIQuadVertex),
            input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX,
            instance_step_rate = 0
        };

        var attrs = stackalloc SDL_GPUVertexAttribute[2];
        attrs[0] = new SDL_GPUVertexAttribute
        {
            location = 0,
            buffer_slot = 0,
            format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2,
            offset = 0
        };
        attrs[1] = new SDL_GPUVertexAttribute
        {
            location = 1,
            buffer_slot = 0,
            format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4,
            offset = 8
        };

        var swapchainFormat = SDL3.SDL_GetGPUSwapchainTextureFormat(gpu.Device, gpu.Window);
        var colorTargetDescs = stackalloc SDL_GPUColorTargetDescription[1];
        colorTargetDescs[0] = new SDL_GPUColorTargetDescription
        {
            format = swapchainFormat,
            blend_state = new SDL_GPUColorTargetBlendState
            {
                enable_blend = true,
                src_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA,
                dst_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
                color_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                src_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                dst_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
                alpha_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
            }
        };

        var pipelineInfo = new SDL_GPUGraphicsPipelineCreateInfo
        {
            vertex_shader = vert,
            fragment_shader = frag,
            vertex_input_state = new SDL_GPUVertexInputState
            {
                vertex_buffer_descriptions = &bufDesc,
                num_vertex_buffers = 1,
                vertex_attributes = attrs,
                num_vertex_attributes = 2
            },
            primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
            rasterizer_state = new SDL_GPURasterizerState
            {
                cull_mode = SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE,
                front_face = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_COUNTER_CLOCKWISE,
                fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL
            },
            depth_stencil_state = new SDL_GPUDepthStencilState
            {
                enable_depth_test = false,
                enable_depth_write = false,
                compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_ALWAYS
            },
            target_info = new SDL_GPUGraphicsPipelineTargetInfo
            {
                color_target_descriptions = colorTargetDescs,
                num_color_targets = 1,
                depth_stencil_format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT,
                has_depth_stencil_target = false
            }
        };

        var pipeline = SDL3.SDL_CreateGPUGraphicsPipeline(gpu.Device, &pipelineInfo);

        SDL3.SDL_ReleaseGPUShader(gpu.Device, vert);
        SDL3.SDL_ReleaseGPUShader(gpu.Device, frag);

        return pipeline;
    }

    private static SDL_GPUBuffer* CreateVertexBuffer(SdlGpuDevice gpu)
    {
        var bufInfo = new SDL_GPUBufferCreateInfo
        {
            usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX,
            size = VertexBufferSize
        };
        return SDL3.SDL_CreateGPUBuffer(gpu.Device, &bufInfo);
    }

    private static SDL_GPUTransferBuffer* CreateTransferBuffer(SdlGpuDevice gpu)
    {
        var bufInfo = new SDL_GPUTransferBufferCreateInfo
        {
            usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
            size = VertexBufferSize
        };
        return SDL3.SDL_CreateGPUTransferBuffer(gpu.Device, &bufInfo);
    }
}
