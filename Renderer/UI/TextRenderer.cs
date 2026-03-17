namespace VoxPopuli.Renderer.UI;

using System;
using System.Numerics;
using System.Runtime.InteropServices;
using SDL;

/// <summary>Owns the SDL_ttf lifecycle, text GPU pipeline, font, and UITextData buffer.</summary>
internal sealed unsafe class TextRenderer : IDisposable
{
    internal const int MaxTextItems = 256;

    private readonly UITextData[] _textData;
    private int _textCount;

    private TTF_TextEngine* _textEngine;
    private TTF_Font* _font;
    private SDL_GPUGraphicsPipeline* _textPipeline;
    private SDL_GPUSampler* _textSampler;
    private SDL_GPUDevice* _device;

    // Per-frame pre-built draw data: one entry per text item, one entry per atlas sequence.
    // Built during PrepareFrame (outside render pass), consumed during Draw (inside render pass).
    private readonly FrameTextItem[] _frameItems;
    private int _frameItemCount;

    private readonly struct FrameTextItem
    {
        public readonly SDL_GPUBuffer* XyBuffer;
        public readonly SDL_GPUBuffer* UvBuffer;
        public readonly SDL_GPUBuffer* IndexBuffer;
        public readonly SDL_GPUTexture* AtlasTexture;
        public readonly uint IndexCount;
        public readonly Color4 Color;

        public FrameTextItem(SDL_GPUBuffer* xy, SDL_GPUBuffer* uv, SDL_GPUBuffer* idx, SDL_GPUTexture* atlas, uint indexCount, Color4 color)
        {
            XyBuffer = xy;
            UvBuffer = uv;
            IndexBuffer = idx;
            AtlasTexture = atlas;
            IndexCount = indexCount;
            Color = color;
        }
    }

    internal TextRenderer(SdlGpuDevice gpu, string fontPath, float fontSize)
    {
        _textData = new UITextData[MaxTextItems];
        _frameItems = new FrameTextItem[MaxTextItems * 4]; // up to 4 atlas sequences per text item
        _textCount = 0;
        _frameItemCount = 0;

        if (!SDL3_ttf.TTF_Init())
        {
            throw new UIRenderingException("Failed to initialize SDL_ttf");
        }

        _font = SDL3_ttf.TTF_OpenFont(fontPath, (int)fontSize);
        if (_font == null)
        {
            SDL3_ttf.TTF_Quit();
            throw new UIRenderingException($"Failed to open font: {fontPath}");
        }

        _device = gpu.Device;
        _textEngine = SDL3_ttf.TTF_CreateGPUTextEngine(gpu.Device);
        if (_textEngine == null)
        {
            SDL3_ttf.TTF_CloseFont(_font);
            SDL3_ttf.TTF_Quit();
            throw new UIRenderingException("Failed to create GPU text engine");
        }

        _textPipeline = CreateTextPipeline(gpu);
        if (_textPipeline == null)
        {
            SDL3_ttf.TTF_DestroyGPUTextEngine(_textEngine);
            SDL3_ttf.TTF_CloseFont(_font);
            SDL3_ttf.TTF_Quit();
            throw new UIRenderingException("Failed to create text pipeline");
        }

        _textSampler = CreateTextSampler(gpu);
        if (_textSampler == null)
        {
            SDL3.SDL_ReleaseGPUGraphicsPipeline(gpu.Device, _textPipeline);
            SDL3_ttf.TTF_DestroyGPUTextEngine(_textEngine);
            SDL3_ttf.TTF_CloseFont(_font);
            SDL3_ttf.TTF_Quit();
            throw new UIRenderingException("Failed to create text sampler");
        }
    }

    /// <summary>Returns a span into the internal buffer for UIDrawContext to write into.</summary>
    internal Span<UITextData> GetTextBuffer() => _textData.AsSpan();

    /// <summary>Reference to the text count — UIDrawContext increments this directly.</summary>
    internal ref int TextCount => ref _textCount;

    /// <summary>Resets text count and releases any GPU buffers from the previous frame.</summary>
    internal void Reset()
    {
        ReleaseFrameItems();
        _textCount = 0;
        _frameItemCount = 0;
    }

    /// <summary>
    /// Builds GPU buffers for all queued text items. Must be called outside the render pass (during PrepareFrame).
    /// </summary>
    internal void PrepareFrame(SDL_GPUCommandBuffer* cmd)
    {
        if (_textCount == 0)
        {
            return;
        }

        for (int i = 0; i < _textCount; i++)
        {
            var item = _textData[i];

            var textPtr = SDL3_ttf.TTF_CreateText(_textEngine, _font, item.Text, 0);
            if (textPtr == null)
            {
                continue;
            }

            var color = item.Color;
            SDL3_ttf.TTF_SetTextColor(textPtr, (byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255), (byte)(color.A * 255));
            SDL3_ttf.TTF_SetTextPosition(textPtr, (int)item.Position.X, (int)item.Position.Y);

            var seq = SDL3_ttf.TTF_GetGPUTextDrawData(textPtr);
            int seqCount = 0;
            while (seq != null && _frameItemCount < _frameItems.Length)
            {
                seqCount++;
                var xySize = (uint)(seq->num_vertices * sizeof(SDL_FPoint));
                var uvSize = (uint)(seq->num_vertices * sizeof(SDL_FPoint));
                var indexSize = (uint)(seq->num_indices * sizeof(int));

                var xyBuf = UploadVertexBuffer(cmd, (void*)seq->xy, xySize);
                var uvBuf = UploadVertexBuffer(cmd, (void*)seq->uv, uvSize);
                var idxBuf = UploadIndexBuffer(cmd, (void*)seq->indices, indexSize);

                if (xyBuf != null && uvBuf != null && idxBuf != null)
                {
                    _frameItems[_frameItemCount++] = new FrameTextItem(xyBuf, uvBuf, idxBuf, seq->atlas_texture, (uint)seq->num_indices, color);
                }
                else
                {
                    if (xyBuf != null) SDL3.SDL_ReleaseGPUBuffer(_device, xyBuf);
                    if (uvBuf != null) SDL3.SDL_ReleaseGPUBuffer(_device, uvBuf);
                    if (idxBuf != null) SDL3.SDL_ReleaseGPUBuffer(_device, idxBuf);
                }

                seq = seq->next;
            }

            SDL3_ttf.TTF_DestroyText(textPtr);
        }
    }

    /// <summary>Issues GPU draw calls for all pre-built text items. Must be called inside the render pass.</summary>
    internal void Draw(SDL_GPURenderPass* pass, SDL_GPUCommandBuffer* cmd, uint width, uint height)
    {
        if (_frameItemCount == 0)
        {
            return;
        }

        SDL3.SDL_BindGPUGraphicsPipeline(pass, _textPipeline);

        var screenSize = new Vector2(width, height);
        SDL3.SDL_PushGPUVertexUniformData(cmd, 0, (IntPtr)(&screenSize), (uint)sizeof(Vector2));

        for (int i = 0; i < _frameItemCount; i++)
        {
            var item = _frameItems[i];

            var vertexBindings = stackalloc SDL_GPUBufferBinding[2]
            {
                new SDL_GPUBufferBinding { buffer = item.XyBuffer, offset = 0 },
                new SDL_GPUBufferBinding { buffer = item.UvBuffer, offset = 0 }
            };
            SDL3.SDL_BindGPUVertexBuffers(pass, 0, vertexBindings, 2);

            var indexBinding = new SDL_GPUBufferBinding { buffer = item.IndexBuffer, offset = 0 };
            SDL3.SDL_BindGPUIndexBuffer(pass, &indexBinding, SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);

            var textureSamplerBinding = new SDL_GPUTextureSamplerBinding
            {
                texture = item.AtlasTexture,
                sampler = _textSampler
            };
            SDL3.SDL_BindGPUFragmentSamplers(pass, 0, &textureSamplerBinding, 1);

            var color = item.Color;
            SDL3.SDL_PushGPUFragmentUniformData(cmd, 0, (IntPtr)(&color), (uint)sizeof(Color4));

            SDL3.SDL_DrawGPUIndexedPrimitives(pass, item.IndexCount, 1, 0, 0, 0);
        }
    }

    public void Dispose()
    {
        ReleaseFrameItems();

        if (_textSampler != null)
        {
            SDL3.SDL_ReleaseGPUSampler(_device, _textSampler);
            _textSampler = null;
        }

        if (_textPipeline != null)
        {
            SDL3.SDL_ReleaseGPUGraphicsPipeline(_device, _textPipeline);
            _textPipeline = null;
        }

        if (_textEngine != null)
        {
            SDL3_ttf.TTF_DestroyGPUTextEngine(_textEngine);
            _textEngine = null;
        }

        if (_font != null)
        {
            SDL3_ttf.TTF_CloseFont(_font);
            _font = null;
        }

        SDL3_ttf.TTF_Quit();
    }

    private void ReleaseFrameItems()
    {
        for (int i = 0; i < _frameItemCount; i++)
        {
            var item = _frameItems[i];
            SDL3.SDL_ReleaseGPUBuffer(_device, item.XyBuffer);
            SDL3.SDL_ReleaseGPUBuffer(_device, item.UvBuffer);
            SDL3.SDL_ReleaseGPUBuffer(_device, item.IndexBuffer);
        }
    }

    private SDL_GPUBuffer* UploadVertexBuffer(SDL_GPUCommandBuffer* cmd, void* data, uint size)
    {
        var transferInfo = new SDL_GPUTransferBufferCreateInfo
        {
            usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
            size = size
        };
        var transfer = SDL3.SDL_CreateGPUTransferBuffer(_device, &transferInfo);
        if (transfer == null) return null;

        var mapped = SDL3.SDL_MapGPUTransferBuffer(_device, transfer, false);
        if (mapped == IntPtr.Zero) { SDL3.SDL_ReleaseGPUTransferBuffer(_device, transfer); return null; }
        Buffer.MemoryCopy(data, (void*)mapped, size, size);
        SDL3.SDL_UnmapGPUTransferBuffer(_device, transfer);

        var bufInfo = new SDL_GPUBufferCreateInfo { usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, size = size };
        var buf = SDL3.SDL_CreateGPUBuffer(_device, &bufInfo);
        if (buf == null) { SDL3.SDL_ReleaseGPUTransferBuffer(_device, transfer); return null; }

        var copyPass = SDL3.SDL_BeginGPUCopyPass(cmd);
        var src = new SDL_GPUTransferBufferLocation { transfer_buffer = transfer, offset = 0 };
        var dst = new SDL_GPUBufferRegion { buffer = buf, offset = 0, size = size };
        SDL3.SDL_UploadToGPUBuffer(copyPass, &src, &dst, false);
        SDL3.SDL_EndGPUCopyPass(copyPass);
        SDL3.SDL_ReleaseGPUTransferBuffer(_device, transfer);

        return buf;
    }

    private SDL_GPUBuffer* UploadIndexBuffer(SDL_GPUCommandBuffer* cmd, void* data, uint size)
    {
        var transferInfo = new SDL_GPUTransferBufferCreateInfo
        {
            usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
            size = size
        };
        var transfer = SDL3.SDL_CreateGPUTransferBuffer(_device, &transferInfo);
        if (transfer == null) return null;

        var mapped = SDL3.SDL_MapGPUTransferBuffer(_device, transfer, false);
        if (mapped == IntPtr.Zero) { SDL3.SDL_ReleaseGPUTransferBuffer(_device, transfer); return null; }
        Buffer.MemoryCopy(data, (void*)mapped, size, size);
        SDL3.SDL_UnmapGPUTransferBuffer(_device, transfer);

        var bufInfo = new SDL_GPUBufferCreateInfo { usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX, size = size };
        var buf = SDL3.SDL_CreateGPUBuffer(_device, &bufInfo);
        if (buf == null) { SDL3.SDL_ReleaseGPUTransferBuffer(_device, transfer); return null; }

        var copyPass = SDL3.SDL_BeginGPUCopyPass(cmd);
        var src = new SDL_GPUTransferBufferLocation { transfer_buffer = transfer, offset = 0 };
        var dst = new SDL_GPUBufferRegion { buffer = buf, offset = 0, size = size };
        SDL3.SDL_UploadToGPUBuffer(copyPass, &src, &dst, false);
        SDL3.SDL_EndGPUCopyPass(copyPass);
        SDL3.SDL_ReleaseGPUTransferBuffer(_device, transfer);

        return buf;
    }

    private static SDL_GPUGraphicsPipeline* CreateTextPipeline(SdlGpuDevice gpu)
    {
        var vert = gpu.LoadShaderInternal("UIText.vert", numUniformBuffers: 1);
        if (vert == null) return null;

        var frag = gpu.LoadShaderInternal("UIText.frag", numUniformBuffers: 1, numSamplers: 1);
        if (frag == null) { SDL3.SDL_ReleaseGPUShader(gpu.Device, vert); return null; }

        var bufDescs = stackalloc SDL_GPUVertexBufferDescription[2];
        bufDescs[0] = new SDL_GPUVertexBufferDescription { slot = 0, pitch = (uint)sizeof(SDL_FPoint), input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX };
        bufDescs[1] = new SDL_GPUVertexBufferDescription { slot = 1, pitch = (uint)sizeof(SDL_FPoint), input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX };

        var attrs = stackalloc SDL_GPUVertexAttribute[2];
        attrs[0] = new SDL_GPUVertexAttribute { location = 0, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = 0 };
        attrs[1] = new SDL_GPUVertexAttribute { location = 1, buffer_slot = 1, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, offset = 0 };

        var swapchainFormat = SDL3.SDL_GetGPUSwapchainTextureFormat(gpu.Device, gpu.Window);
        var colorTarget = stackalloc SDL_GPUColorTargetDescription[1];
        colorTarget[0] = new SDL_GPUColorTargetDescription
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
                vertex_buffer_descriptions = bufDescs,
                num_vertex_buffers = 2,
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
                color_target_descriptions = colorTarget,
                num_color_targets = 1,
                has_depth_stencil_target = false
            }
        };

        var pipeline = SDL3.SDL_CreateGPUGraphicsPipeline(gpu.Device, &pipelineInfo);
        SDL3.SDL_ReleaseGPUShader(gpu.Device, vert);
        SDL3.SDL_ReleaseGPUShader(gpu.Device, frag);
        return pipeline;
    }

    private static SDL_GPUSampler* CreateTextSampler(SdlGpuDevice gpu)
    {
        var info = new SDL_GPUSamplerCreateInfo
        {
            mag_filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
            min_filter = SDL_GPUFilter.SDL_GPU_FILTER_LINEAR,
            mipmap_mode = SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_NEAREST,
            address_mode_u = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
            address_mode_v = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
            address_mode_w = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
        };
        return SDL3.SDL_CreateGPUSampler(gpu.Device, &info);
    }
}
