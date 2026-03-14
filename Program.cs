namespace VoxPopuli;

using System;
using System.Runtime.InteropServices;
using System.Text;
using SDL;

public static class RenderConstants
{
    public const string ShaderPath = "Shaders/";
}

internal static class Program
{
    private static readonly float[] CubeVertexData =
    [
        /* Front face. */
        -0.5f,  0.5f, -0.5f, 1.0f, 0.0f, 0.0f,
         0.5f, -0.5f, -0.5f, 0.0f, 0.0f, 1.0f,
        -0.5f, -0.5f, -0.5f, 0.0f, 1.0f, 0.0f,
        -0.5f,  0.5f, -0.5f, 1.0f, 0.0f, 0.0f,
         0.5f,  0.5f, -0.5f, 1.0f, 1.0f, 0.0f,
         0.5f, -0.5f, -0.5f, 0.0f, 0.0f, 1.0f,
        /* Left face */
        -0.5f,  0.5f,  0.5f, 1.0f, 1.0f, 1.0f,
        -0.5f, -0.5f, -0.5f, 0.0f, 1.0f, 0.0f,
        -0.5f, -0.5f,  0.5f, 0.0f, 1.0f, 1.0f,
        -0.5f,  0.5f,  0.5f, 1.0f, 1.0f, 1.0f,
        -0.5f,  0.5f, -0.5f, 1.0f, 0.0f, 0.0f,
        -0.5f, -0.5f, -0.5f, 0.0f, 1.0f, 0.0f,
        /* Top face */
        -0.5f,  0.5f,  0.5f, 1.0f, 1.0f, 1.0f,
         0.5f,  0.5f, -0.5f, 1.0f, 1.0f, 0.0f,
        -0.5f,  0.5f, -0.5f, 1.0f, 0.0f, 0.0f,
        -0.5f,  0.5f,  0.5f, 1.0f, 1.0f, 1.0f,
         0.5f,  0.5f,  0.5f, 0.0f, 0.0f, 0.0f,
         0.5f,  0.5f, -0.5f, 1.0f, 1.0f, 0.0f,
        /* Right face */
         0.5f,  0.5f, -0.5f, 1.0f, 1.0f, 0.0f,
         0.5f, -0.5f,  0.5f, 1.0f, 0.0f, 1.0f,
         0.5f, -0.5f, -0.5f, 0.0f, 0.0f, 1.0f,
         0.5f,  0.5f, -0.5f, 1.0f, 1.0f, 0.0f,
         0.5f,  0.5f,  0.5f, 0.0f, 0.0f, 0.0f,
         0.5f, -0.5f,  0.5f, 1.0f, 0.0f, 1.0f,
        /* Back face */
         0.5f,  0.5f,  0.5f, 0.0f, 0.0f, 0.0f,
        -0.5f, -0.5f,  0.5f, 0.0f, 1.0f, 1.0f,
         0.5f, -0.5f,  0.5f, 1.0f, 0.0f, 1.0f,
         0.5f,  0.5f,  0.5f, 0.0f, 0.0f, 0.0f,
        -0.5f,  0.5f,  0.5f, 1.0f, 1.0f, 1.0f,
        -0.5f, -0.5f,  0.5f, 0.0f, 1.0f, 1.0f,
        /* Bottom face */
        -0.5f, -0.5f, -0.5f, 0.0f, 1.0f, 0.0f,
         0.5f, -0.5f,  0.5f, 1.0f, 0.0f, 1.0f,
        -0.5f, -0.5f,  0.5f, 0.0f, 1.0f, 1.0f,
        -0.5f, -0.5f, -0.5f, 0.0f, 1.0f, 0.0f,
         0.5f, -0.5f, -0.5f, 0.0f, 0.0f, 1.0f,
         0.5f, -0.5f,  0.5f, 1.0f, 0.0f, 1.0f,
    ];
    [STAThread]
    private static void Main()
    {
        SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO);

        unsafe
        {
            var window = SDL3.SDL_CreateWindow("VoxPopuli", 800, 600, SDL_WindowFlags.SDL_WINDOW_RESIZABLE);
            if (window == null)
            {
                Console.WriteLine($"could not create SDL window: {SDL3.SDL_GetError()}");
                return;
            }

            var device = SDL3.SDL_CreateGPUDevice(SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL, false, (byte*)null);
            if (window == null)
            {
                Console.WriteLine($"could not create SDL gpu device: {SDL3.SDL_GetError()}");
                return;
            }

            if (!SDL3.SDL_ClaimWindowForGPUDevice(device, window))
            {
                Console.WriteLine($"could not claim SDL window for gpu device: {SDL3.SDL_GetError()}");
                return;
            }

            var basicGraphicsPipeline = CreateBasicGraphicsPipeline(device, window);
            if (basicGraphicsPipeline == null)
            {
                Console.WriteLine($"could not create basic graphics pipeline: {SDL3.SDL_GetError()}");
                return;
            }

            var vertexBuffer = CreateVertexBuffer(device);
            if (vertexBuffer == null)
            {
                Console.WriteLine($"could not create vertex buffer: {SDL3.SDL_GetError()}");
                return;
            }

            int pw = 0, ph = 0;
            SDL3.SDL_GetWindowSizeInPixels(window, &pw, &ph);
            uint depthTextureWidth = (uint)pw;
            uint depthTextureHeight = (uint)ph;

            var cubePipeline = CreateCubePipeline(device, window);
            if (cubePipeline == null)
            {
                Console.WriteLine($"could not create cube pipeline: {SDL3.SDL_GetError()}");
                return;
            }

            var depthTexture = CreateDepthTexture(device, depthTextureWidth, depthTextureHeight);
            if (depthTexture == null)
            {
                Console.WriteLine($"could not create depth texture: {SDL3.SDL_GetError()}");
                return;
            }

            var run = true;
            while (run)
            {
                SDL_Event @event;
                while (SDL3.SDL_PollEvent(&@event))
                {
                    if (@event.type == (uint)SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED)
                    {
                        run = false;
                    }
                }
                DrawFrame(device, window, cubePipeline, vertexBuffer, &depthTexture, ref depthTextureWidth, ref depthTextureHeight);
            }

            SDL3.SDL_ReleaseGPUGraphicsPipeline(device, cubePipeline);
            SDL3.SDL_ReleaseGPUTexture(device, depthTexture);
            SDL3.SDL_ReleaseGPUBuffer(device, vertexBuffer);
        }


    }

    private static unsafe SDL_GPUBuffer* CreateVertexBuffer(SDL_GPUDevice* device)
    {
        const uint size = 36 * 6 * sizeof(float);

        var bufferCreateInfo = new SDL_GPUBufferCreateInfo
        {
            usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX,
            size = size
        };
        var gpuBuffer = SDL3.SDL_CreateGPUBuffer(device, &bufferCreateInfo);
        if (gpuBuffer == null)
        {
            Console.WriteLine($"could not create vertex buffer: {SDL3.SDL_GetError()}");
            return null;
        }

        var transferCreateInfo = new SDL_GPUTransferBufferCreateInfo
        {
            usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
            size = size
        };
        var transferBuffer = SDL3.SDL_CreateGPUTransferBuffer(device, &transferCreateInfo);
        if (transferBuffer == null)
        {
            Console.WriteLine($"could not create transfer buffer: {SDL3.SDL_GetError()}");
            return null;
        }

        var map = SDL3.SDL_MapGPUTransferBuffer(device, transferBuffer, false);
        Marshal.Copy(CubeVertexData, 0, (nint)map, CubeVertexData.Length);
        SDL3.SDL_UnmapGPUTransferBuffer(device, transferBuffer);

        var cmd = SDL3.SDL_AcquireGPUCommandBuffer(device);
        var copyPass = SDL3.SDL_BeginGPUCopyPass(cmd);
        var src = new SDL_GPUTransferBufferLocation { transfer_buffer = transferBuffer, offset = 0 };
        var dst = new SDL_GPUBufferRegion { buffer = gpuBuffer, offset = 0, size = size };
        SDL3.SDL_UploadToGPUBuffer(copyPass, &src, &dst, false);
        SDL3.SDL_EndGPUCopyPass(copyPass);
        SDL3.SDL_SubmitGPUCommandBuffer(cmd);

        SDL3.SDL_ReleaseGPUTransferBuffer(device, transferBuffer);

        return gpuBuffer;
    }

    private static float[] BuildMVP(uint w, uint h)
    {
        float f = 1.0f / MathF.Tan(MathF.PI / 8f);
        float aspect = (float)w / h;
        float near = 0.01f, far = 100f;
        float[] proj = new float[16];
        proj[0]  = f / aspect;
        proj[5]  = f;
        proj[10] = (near + far) / (near - far);
        proj[11] = -1f;
        proj[14] = (2f * near * far) / (near - far);

        float[] view = new float[16];
        view[0] = view[5] = view[10] = view[15] = 1f;
        view[14] = -2.5f;

        float[] mvp = new float[16];
        for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
                for (int k = 0; k < 4; k++)
                    mvp[j*4+i] += proj[k*4+i] * view[j*4+k];
        return mvp;
    }

    private static unsafe void DrawFrame(
        SDL_GPUDevice* device,
        SDL_Window* window,
        SDL_GPUGraphicsPipeline* pipeline,
        SDL_GPUBuffer* vertexBuffer,
        SDL_GPUTexture** depthTexture,
        ref uint depthW,
        ref uint depthH)
    {
        var cmd = SDL3.SDL_AcquireGPUCommandBuffer(device);
        SDL_GPUTexture* swapchainTex; uint sw, sh;
        SDL3.SDL_WaitAndAcquireGPUSwapchainTexture(cmd, window, &swapchainTex, &sw, &sh);
        if (swapchainTex == null) { SDL3.SDL_CancelGPUCommandBuffer(cmd); return; }

        if (sw != depthW || sh != depthH)
        {
            SDL3.SDL_ReleaseGPUTexture(device, *depthTexture);
            *depthTexture = CreateDepthTexture(device, sw, sh);
            depthW = sw; depthH = sh;
        }

        var mvp = BuildMVP(sw, sh);
        fixed (float* mvpPtr = mvp) { SDL3.SDL_PushGPUVertexUniformData(cmd, 0, (nint)mvpPtr, 64); }

        var colorTarget = new SDL_GPUColorTargetInfo {
            texture = swapchainTex,
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
            clear_color = new SDL_FColor { r = 0f, g = 0f, b = 0f, a = 1f }
        };
        var depthTarget = new SDL_GPUDepthStencilTargetInfo {
            texture = *depthTexture,
            clear_depth = 1.0f,
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
            stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            cycle = true
        };

        var pass = SDL3.SDL_BeginGPURenderPass(cmd, &colorTarget, 1, &depthTarget);
        SDL3.SDL_BindGPUGraphicsPipeline(pass, pipeline);
        var vbBinding = new SDL_GPUBufferBinding { buffer = vertexBuffer, offset = 0 };
        SDL3.SDL_BindGPUVertexBuffers(pass, 0, &vbBinding, 1);
        SDL3.SDL_DrawGPUPrimitives(pass, 36, 1, 0, 0);
        SDL3.SDL_EndGPURenderPass(pass);
        SDL3.SDL_SubmitGPUCommandBuffer(cmd);
    }

    private static unsafe SDL_GPUGraphicsPipeline* CreateBasicGraphicsPipeline(SDL_GPUDevice* device, SDL_Window* window)
    {
        SDL_GPUShader* vertexShader = LoadAndCompileShader(device, "Basic.vert");
        SDL_GPUShader* fragmentShader = LoadAndCompileShader(device, "Basic.frag");

        var colorTargetDescriptions = new SDL_GPUColorTargetDescription
        {
            format = SDL3.SDL_GetGPUSwapchainTextureFormat(device, window),
            blend_state = new SDL_GPUColorTargetBlendState
            {
                enable_blend = true,
                color_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                alpha_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                src_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA,
                dst_color_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
                src_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA,
                dst_alpha_blendfactor = SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA,
            }
        };

        var targetInfo = new SDL_GPUGraphicsPipelineTargetInfo
        {
            num_color_targets = 1,
            color_target_descriptions = &colorTargetDescriptions
        };

        var vertexBufferDescriptions = new SDL_GPUVertexBufferDescription[]
        {
            new() {
                slot = 0,
                pitch = sizeof(float) * 5,
                input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX,
                instance_step_rate = 0
            }
        };

        var vertexAttributes = new SDL_GPUVertexAttribute[]
        {
            new() {
                location = 0,
                buffer_slot = 0,
                offset = 0,
                format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2
            },
            new() {
                location = 1,
                buffer_slot = 0,
                offset = sizeof(float) * 2,
                format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3
            }
        };

        SDL_GPUGraphicsPipeline* graphicsPipeline;
        fixed (SDL_GPUVertexBufferDescription* vertexBufferDescriptionsPtr = vertexBufferDescriptions)
        fixed (SDL_GPUVertexAttribute* vertexAttributesPtr = vertexAttributes)
        {
            var createInfo = new SDL_GPUGraphicsPipelineCreateInfo
            {
                vertex_shader = vertexShader,
                fragment_shader = fragmentShader,

                vertex_input_state = new SDL_GPUVertexInputState
                {
                    num_vertex_attributes = (uint)vertexAttributes.Length,
                    vertex_attributes = vertexAttributesPtr,

                    num_vertex_buffers = (uint)vertexBufferDescriptions.Length,
                    vertex_buffer_descriptions = vertexBufferDescriptionsPtr,

                },
                primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST
            };
            graphicsPipeline = SDL3.SDL_CreateGPUGraphicsPipeline(device, &createInfo);
        }

        SDL3.SDL_ReleaseGPUShader(device, vertexShader);
        SDL3.SDL_ReleaseGPUShader(device, fragmentShader);

        return graphicsPipeline;
    }

    private static unsafe SDL_GPUTexture* CreateDepthTexture(SDL_GPUDevice* device, uint w, uint h)
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
        var texture = SDL3.SDL_CreateGPUTexture(device, &createInfo);
        if (texture == null)
            Console.WriteLine($"could not create depth texture: {SDL3.SDL_GetError()}");
        return texture;
    }

    private static unsafe SDL_GPUGraphicsPipeline* CreateCubePipeline(SDL_GPUDevice* device, SDL_Window* window)
    {
        var vertexShader = LoadAndCompileShader(device, "Cube.vert", numUniformBuffers: 1);
        var fragmentShader = LoadAndCompileShader(device, "Cube.frag");

        var colorTargetDesc = new SDL_GPUColorTargetDescription
        {
            format = SDL3.SDL_GetGPUSwapchainTextureFormat(device, window)
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
                vertex_shader = vertexShader,
                fragment_shader = fragmentShader,
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
            pipeline = SDL3.SDL_CreateGPUGraphicsPipeline(device, &createInfo);
        }

        SDL3.SDL_ReleaseGPUShader(device, vertexShader);
        SDL3.SDL_ReleaseGPUShader(device, fragmentShader);

        if (pipeline == null)
            Console.WriteLine($"could not create cube pipeline: {SDL3.SDL_GetError()}");
        return pipeline;
    }

    private static unsafe SDL_GPUShader* LoadAndCompileShader(SDL_GPUDevice* device, string name, uint numUniformBuffers = 0)
    {
        SDL_GPUShaderStage shaderStage = name switch
        {
            _ when name.Contains(".vert") => SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX,
            _ when name.Contains(".frag") => SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT,
            _ => throw new ArgumentException($"Unknown shader stage for file: {name}", nameof(name))
        };

        var (shaderExtension, shaderFormat) = true switch
        {
            _ when OperatingSystem.IsWindows() => ("dxil", SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL),
            _ when OperatingSystem.IsMacOS() => ("msl", SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL),
            _ when OperatingSystem.IsLinux() => ("spv", SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV),
            _ => throw new PlatformNotSupportedException()
        };

        string filePath = $"Shaders/compiled/{shaderExtension}/{name}.hlsl.{shaderExtension}";

        var source = File.ReadAllBytes(filePath);
        var entryPoint = shaderFormat switch
        {
            SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL => "main0",
            _ => "main"
        };

        SDL_GPUShader* shader;
        fixed (byte* sourceBytes = source)
        {
            fixed (byte* entryPointBytes = Encoding.UTF8.GetBytes(entryPoint))
            {
                var createShaderInfo = new SDL_GPUShaderCreateInfo
                {
                    code_size = (nuint)source.Length,
                    code = sourceBytes,
                    entrypoint = entryPointBytes,
                    format = shaderFormat,
                    stage = shaderStage,
                    num_uniform_buffers = numUniformBuffers
                };

                shader = SDL3.SDL_CreateGPUShader(device, &createShaderInfo);
                if (shader == null)
                {
                    Console.WriteLine($"could not compile shader {name}: {SDL3.SDL_GetError()}");
                    return null; // TODO (Michael): Throw an exception here instead
                }
            }

        }

        return shader;
    }
}
