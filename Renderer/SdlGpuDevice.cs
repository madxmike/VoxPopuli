namespace VoxPopuli.Renderer;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SDL;

/// <summary>Owns all SDL3 GPU resources: window, device, pipeline, and depth texture.</summary>
internal sealed unsafe class SdlGpuDevice : IDisposable
{
    /// <summary>SDL3 GPU device handle.</summary>
    internal SDL_GPUDevice* Device { get; private set; }
    /// <summary>Window handle.</summary>
    internal SDL_Window* Window { get; private set; }
    /// <summary>Depth texture handle.</summary>
    internal SDL_GPUTexture* DepthTexture { get; private set; }
    /// <summary>Current depth texture width.</summary>
    internal uint DepthWidth { get; private set; }
    /// <summary>Current depth texture height.</summary>
    internal uint DepthHeight { get; private set; }

    /// <summary>Initialises SDL, creates the window and GPU device, builds the cube pipeline, and allocates the initial depth texture.</summary>
    internal SdlGpuDevice(string title, int width, int height)
    {
        SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO);

        Window = SDL3.SDL_CreateWindow(title, width, height, SDL_WindowFlags.SDL_WINDOW_RESIZABLE);
        if (Window == null)
        {
            throw new Exception($"SDL_CreateWindow failed: {SDL3.SDL_GetError()}");
        }

        Device = SDL3.SDL_CreateGPUDevice(SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL, false, (byte*)null);
        if (Device == null)
        {
            throw new Exception($"SDL_CreateGPUDevice failed: {SDL3.SDL_GetError()}");
        }

        if (!SDL3.SDL_ClaimWindowForGPUDevice(Device, Window))
        {
            throw new Exception($"SDL_ClaimWindowForGPUDevice failed: {SDL3.SDL_GetError()}");
        }

        int pw = 0, ph = 0;
        SDL3.SDL_GetWindowSizeInPixels(Window, &pw, &ph);
        DepthWidth = (uint)pw;
        DepthHeight = (uint)ph;
        DepthTexture = CreateDepthTexture(DepthWidth, DepthHeight);
        if (DepthTexture == null)
        {
            throw new Exception($"CreateDepthTexture failed: {SDL3.SDL_GetError()}");
        }
    }

    internal SDL_GPUCommandBuffer* AcquireCommandBuffer() =>
        SDL3.SDL_AcquireGPUCommandBuffer(Device);

    /// <summary>Acquires a command buffer from the GPU device.</summary>
    internal SDL_GPUCommandBuffer* AcquireCommandBuffer() =>
        SDL3.SDL_AcquireGPUCommandBuffer(Device);

    /// <summary>Recreates the depth texture when the swapchain dimensions change.</summary>
    internal void ResizeDepthTextureIfNeeded(uint w, uint h)
    {
        if (w == DepthWidth && h == DepthHeight)
        {
            return;
        }
        SDL3.SDL_ReleaseGPUTexture(Device, DepthTexture);
        DepthTexture = CreateDepthTexture(w, h);
        DepthWidth = w;
        DepthHeight = h;
    }

    /// <summary>Creates a D16_UNORM depth texture at the given pixel dimensions.</summary>
    internal SDL_GPUTexture* CreateDepthTexture(uint w, uint h)
    {
        var createInfo = new SDL_GPUTextureCreateInfo
        {
            type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
            format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT,
            usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET,
            width = w,
            height = h,
            layer_count_or_depth = 1,
            num_levels = 1,
            sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1
        };
        return SDL3.SDL_CreateGPUTexture(Device, &createInfo);
    }

    internal SDL_GPUShader* LoadShaderInternal(string name, uint numUniformBuffers = 0, uint numReadOnlyStorageBuffers = 0)
        => LoadShader(name, numUniformBuffers, numReadOnlyStorageBuffers);

    /// <summary>Loads a pre-compiled compute shader binary and creates a compute pipeline.</summary>
    internal SDL_GPUComputePipeline* LoadComputePipeline(
        string name,
        uint numReadOnlyStorageBuffers,
        uint numReadWriteStorageBuffers,
        uint numUniformBuffers)
    {
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
            var info = new SDL_GPUComputePipelineCreateInfo
            {
                code_size = (nuint)source.Length,
                code = src,
                entrypoint = ep,
                format = format,
                num_readonly_storage_buffers  = numReadOnlyStorageBuffers,
                num_readwrite_storage_buffers = numReadWriteStorageBuffers,
                num_uniform_buffers           = numUniformBuffers
            };
            var pipeline = SDL3.SDL_CreateGPUComputePipeline(Device, &info);
            if (pipeline == null)
            {
                throw new Exception($"SDL_CreateGPUComputePipeline failed: {SDL3.SDL_GetError()}");
            }
            return pipeline;
        }
    }

    /// <summary>Loads a pre-compiled shader from the compiled shaders directory.</summary>
    private SDL_GPUShader* LoadShader(string name, uint numUniformBuffers = 0, uint numReadOnlyStorageBuffers = 0)
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
                num_uniform_buffers = numUniformBuffers,
                num_storage_buffers = numReadOnlyStorageBuffers
            };
            return SDL3.SDL_CreateGPUShader(Device, &info);
        }
    }

    /// <summary>Releases GPU resources in reverse-creation order.</summary>
    public void Dispose()
    {
        SDL3.SDL_ReleaseGPUTexture(Device, DepthTexture);
        SDL3.SDL_DestroyGPUDevice(Device);
        SDL3.SDL_DestroyWindow(Window);
    }
}
