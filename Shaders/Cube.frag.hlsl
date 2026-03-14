struct Input
{
    float4 Color : TEXCOORD0;
    float4 Position : SV_Position;
};

float4 main(Input input) : SV_Target0
{
    return input.Color;
}
