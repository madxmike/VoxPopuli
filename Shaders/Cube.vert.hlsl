cbuffer UBO : register(b0, space1)
{
    float4x4 ModelViewProj;
};

struct Input
{
    float3 Position : TEXCOORD0;
    float3 Color : TEXCOORD1;
};

struct Output
{
    float4 Color : TEXCOORD0;
    float4 Position : SV_Position;
};

Output main(Input input)
{
    Output output;
    output.Position = mul(ModelViewProj, float4(input.Position, 1.0));
    output.Color = float4(input.Color, 1.0);
    return output;
}
