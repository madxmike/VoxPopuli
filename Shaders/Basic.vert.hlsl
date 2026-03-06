struct FragInput {
    float4 position : SV_Position;
    float3 color    : COLOR;
};

float4 Main(FragInput input) : SV_Target {
    return float4(input.color, 1.0);
}
