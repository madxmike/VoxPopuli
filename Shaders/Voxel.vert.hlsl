StructuredBuffer<float4> colorTable : register(t0, space0);

cbuffer ViewProjUB : register(b0, space1)
{
    float4x4 viewProj;
};

struct VSInput
{
    float3 position : TEXCOORD0;
    uint   typeId   : TEXCOORD1;
};

struct VSOutput
{
    float4 position : SV_Position;
    float4 color    : COLOR0;
    float  ao       : TEXCOORD0;
};

VSOutput main(VSInput input)
{
    VSOutput output;
    output.position = mul(viewProj, float4(input.position, 1.0));
    output.color    = colorTable[input.typeId & 0xFF];
    uint ao_raw     = (input.typeId >> 30) & 0x3;
    output.ao       = ao_raw / 3.0;
    return output;
}
