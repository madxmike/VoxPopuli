Texture2D AtlasTexture : register(t0);
SamplerState LinearSampler : register(s0);

cbuffer UIRendererUB : register(b0, space1)
{
    float4 TextColor;
};

struct PSInput
{
    float2 TexCoord : COLOR0;
};

float4 main(PSInput input) : SV_Target0
{
    float atlasSample = AtlasTexture.Sample(LinearSampler, input.TexCoord).a;
    return float4(TextColor.rgb, TextColor.a * atlasSample);
}
