namespace VoxPopuli.Renderer;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SDL;

internal sealed unsafe class GpuDevice : IDisposable
{
    internal SDL_GPUDevice* Device { get; private set; }
    internal SDL_Window* Window { get; private set; }
    internal SDL_GPUGraphicsPipeline* Pipeline { get; private set; }
    internal SDL_GPUTexture* DepthTexture { get; private set; }
    internal uint DepthWidth { get; private set; }
    internal uint DepthHeight { get; private set; }

    internal GpuDevice(string title, int width, int height)
    {
        SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO);

        Window = SDL3.SDL_CreateWindow(title, width, height, SDL_WindowFlags.SDL_WINDOW_RESIZABLE);
        if (Window == null)
            throw new Exception($"SDL_CreateWindow failed: {SDL3.SDL_GetError()}");

        Device = SDL3.SDL_CreateGPUDevice(SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL, false, (byte*)null);
        if (Device == null)
            throw new Exception($"SDL_CreateGPUDevice failed: {SDL3.SDL_GetError()}");

        if (!SDL3.SDL_ClaimWindowForGPUDevice(Device, Window))
            throw new Exception($"SDL_ClaimWindowForGPUDevice failed: {SDL3.SDL_GetError()}");

        Pipeline = CreateCubePipeline();
        if (Pipeline == null)
            throw new Exception($"CreateCubePipeline failed: {SDL3.SDL_GetError()}");

        int pw = 0, ph = 0;
        SDL3.SDL_GetWindowSizeInPixels(Window, &pw, &ph);
        DepthWidth = (uint)pw;
        DepthHeight = (uint)ph;
        DepthTexture = CreateDepthTexture(DepthWidth, DepthHeight);
        if (DepthTexture == null)
            throw new Exception($"CreateDepthTexture failed: {SDL3.SDL_GetError()}");
    }

    internal SDL_GPUCommandBuffer* AcquireCommandBuffer() =>
        SDL3.SDL_AcquireGPUCommandBuffer(Device);

    internal void ResizeDepthTextureIfNeeded(uint w, uint h)
    {
        if (w == DepthWidth && h == DepthHeight) return;
        SDL3.SDL_ReleaseGPUTexture(Device, DepthTexture);
        DepthTexture = CreateDepthTexture(w, h);
        DepthWidth = w;
        DepthHeight = h;
    }

    private SDL_GPUGraphicsPipeline* CreateCubePipeline()
    {
        var vert = LoadShader("Cube.vert", numUniformBuffers: 1);
        var frag = LoadShader("Cube.frag");

        var colorTargetDesc = new SDL_GPUColorTargetDescription
        {
            format = SDL3.SDL_GetGPUSwapchainTextureFormat(Device, Window)
        };

        var vertexBufferDesc = new SDL_GPUVertexBufferDescription
        {
            slot = 0,
            pitch = 24,
            input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX,
            instance_step_rate = 0
        };

        var vertexAttributes = new SDL_GPUVertexAttribute[]
        {
            new() { location = 0, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, offset = 0 },
            new() { location = 1, buffer_slot = 0, format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, offset = 12 }
        };

        SDL_GPUGraphicsPipeline* pipeline;
        fixed (SDL_GPUVertexAttribute* attribsPtr = vertexAttributes)
        {
            var createInfo = new SDL_GPUGraphicsPipelineCreateInfo
            {
                vertex_shader = vert,
                fragment_shader = frag,
                vertex_input_state = new SDL_GPUVertexInputState
                {
                    num_vertex_buffers = 1,
                    vertex_buffer_descriptions = &vertexBufferDesc,
                    num_vertex_attributes = 2,
                    vertex_attributes = attribsPtr
                },
                depth_stencil_state = new SDL_GPUDepthStencilState
                {
                    enable_depth_test = true,
                    enable_depth_write = true,
                    compare_op = SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS_OR_EQUAL
                },
                target_info = new SDL_GPUGraphicsPipelineTargetInfo
                {
                    num_color_targets = 1,
                    color_target_descriptions = &colorTargetDesc,
                    depth_stencil_format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D16_UNORM,
                    has_depth_stencil_target = true
                },
                primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST
            };
            pipeline = SDL3.SDL_CreateGPUGraphicsPipeline(Device, &createInfo);
        }

        SDL3.SDL_ReleaseGPUShader(Device, vert);
        SDL3.SDL_ReleaseGPUShader(Device, frag);
        return pipeline;
    }

    internal SDL_GPUTexture* CreateDepthTexture(uint w, uint h)
    {
        var createInfo = new SDL_GPUTextureCreateInfo
        {
            type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
            format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D16_UNORM,
            usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET,
            width = w,
            height = h,
            layer_count_or_depth = 1,
            num_levels = 1,
            sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1
        };
        return SDL3.SDL_CreateGPUTexture(Device, &createInfo);
    }

    private SDL_GPUShader* LoadShader(string name, uint numUniformBuffers = 0)
    {
        var stage = name.Contains(".vert")
            ? SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX
            : SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT;

        var (ext, format) = true switch
        {
            _ when OperatingSystem.IsWindows() => ("dxil", SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL),
            _ when OperatingSystem.IsMacOS()   => ("msl",  SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL),
            _                                  => ("spv",  SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV)
        };

        var source = File.ReadAllBytes($"Shaders/compiled/{ext}/{name}.hlsl.{ext}");
        var entryPoint = format == SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL ? "main0" : "main";

        fixed (byte* src = source)
        fixed (byte* ep = Encoding.UTF8.GetBytes(entryPoint))
        {
            var info = new SDL_GPUShaderCreateInfo
            {
                code_size = (nuint)source.Length,
                code = src,
                entrypoint = ep,
                format = format,
                stage = stage,
                num_uniform_buffers = numUniformBuffers
            };
            return SDL3.SDL_CreateGPUShader(Device, &info);
        }
    }

    public void Dispose()
    {
        SDL3.SDL_ReleaseGPUGraphicsPipeline(Device, Pipeline);
        SDL3.SDL_ReleaseGPUTexture(Device, DepthTexture);
        SDL3.SDL_DestroyGPUDevice(Device);
        SDL3.SDL_DestroyWindow(Window);
    }
}
