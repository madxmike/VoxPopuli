float4 main(float4 inColor : COLOR0, float ao : TEXCOORD0) : SV_Target0
{
    // Remap AO: raise the floor so fully-occluded corners aren't black,
    // and apply a power curve for a softer, more natural falloff.
    static const float AO_MIN   = 0.7;
    static const float AO_RANGE = 0.3;
    static const float AO_GAMMA = 0.8;
    float aoSmooth = smoothstep(0.0, 1.0, ao);
    float aoFactor = AO_MIN + AO_RANGE * pow(aoSmooth, AO_GAMMA);
    return float4(inColor.rgb * aoFactor, inColor.a);
}
