// WPF ps_2_0: artwork-aware foil, supported by both hardware and software rendering.
// Grain sampler carries honeycomb emboss + micro stipple (see TradingCardFoilEffect.CreateGrain).
sampler2D Artwork : register(s0);
sampler2D Grain : register(s1);
float2 Light : register(c0);
float2 Size : register(c1);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 source = tex2D(Artwork, uv);
    float3 art = source.rgb / max(source.a, 0.001);
    float grain = tex2D(Grain, frac(uv * Size / 128.0)).r;
    float hexRidge = saturate((grain - 0.58) * 5.5);
    float hexFace = 1.0 - hexRidge;
    float substrate = hexFace * (0.95 + (grain - 0.5) * 0.12) + hexRidge * 1.05;
    float angle = dot(uv, float2(0.82, 0.57));
    float offset = dot(Light, float2(0.68, 0.42));
    float distance = abs(angle - offset);
    float broad = saturate(1 - distance * 2.1);
    float reflection = saturate(1 - distance * 5.2);
    reflection *= reflection;
    reflection *= reflection;
    float phase = angle * 1.65 - offset * 1.3 + grain * 0.08;
    float3 spectrum = saturate(abs(frac(phase + float3(0, 0.3333, 0.6667)) * 6 - 3) - 1);
    float3 metal = art * (0.83 + broad * 0.17);
    metal += (spectrum - 0.5) * art * (1 - art) * broad * (0.88 + hexRidge * 0.12);
    float3 reflected = (0.45 + spectrum * 0.55) * broad * broad * 0.19;
    // The moving reflection already follows Light. Reuse its broad falloff to stay
    // within ps_2_0's 64 arithmetic slots, including the honeycomb and sparkle terms.
    reflected += reflection * (0.35 + grain * 0.12) * (0.35 + broad * 0.65);
    reflected += saturate((grain - 0.79) * 5) * broad * broad * 0.12;
    reflected += hexRidge * broad * 0.06;
    float3 color = (metal + (1 - metal) * reflected) * substrate;
    return float4(saturate(color) * source.a, source.a);
}
