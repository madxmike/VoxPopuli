cbuffer UIRendererUB : register(b0, space1)
{
    float2 ScreenSize;
};

struct VSInput
{
    float2 position : TEXCOORD0;
    float4 color    : TEXCOORD1;
};

struct VSOutput
{
    float4 position : SV_Position;
    float4 color    : COLOR0;
};

VSOutput main(VSInput input)
{
    VSOutput output;
    float2 ndc;
    ndc.x = (input.position.x / ScreenSize.x) * 2 - 1;
    ndc.y = 1 - (input.position.y / ScreenSize.y) * 2;
    output.position = float4(ndc, 0.0, 1.0);
    output.color    = input.color;
    return output;
}
