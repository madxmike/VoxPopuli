cbuffer FrameUBO : register(b0, space1)
{
    float4x4 ViewProj;
};

struct ChunkOffset
{
    float3 Origin;
    float _pad;
};

StructuredBuffer<ChunkOffset> ChunkOffsets : register(t0, space2);

struct Output
{
    float4 Color : TEXCOORD0;
    float4 Position : SV_Position;
};

Output main(uint4 xyzf : TEXCOORD0, uint color : TEXCOORD1, uint instanceId : SV_InstanceID)
{
    Output output;

    float3 localPos = float3((float)xyzf.x, (float)xyzf.y, (float)xyzf.z);
    float3 worldPos = localPos + ChunkOffsets[instanceId].Origin;

    float r = (float)((color >> 24) & 0xFF) / 255.0;
    float g = (float)((color >> 16) & 0xFF) / 255.0;
    float b = (float)((color >> 8)  & 0xFF) / 255.0;

    output.Position = mul(ViewProj, float4(worldPos, 1.0));
    output.Color = float4(r, g, b, 1.0);
    return output;
}
