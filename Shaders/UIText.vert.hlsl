cbuffer UIRendererUB : register(b0, space1)
{
    float2 ScreenSize;
};

struct VSInput
{
    float2 Position : TEXCOORD0;
    float2 TexCoord : TEXCOORD1;
};

struct VSOutput
{
    float4 Position : SV_Position;
    float2 TexCoord : COLOR0;
};

VSOutput main(VSInput input)
{
    VSOutput output;
    float2 ndc;
    ndc.x = (input.Position.x / ScreenSize.x) * 2.0 - 1.0;
    ndc.y = 1.0 - (-input.Position.y / ScreenSize.y) * 2.0;
    output.Position = float4(ndc, 0.0, 1.0);
    output.TexCoord = input.TexCoord;
    return output;
}
