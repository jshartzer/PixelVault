// Prismatic Century foil. Shader model 2 also works in WPF's software renderer.
sampler2D Artwork : register(s0);
float2 Light : register(c0);
float Sweep : register(c1);
float ColorRetention : register(c2);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 source = tex2D(Artwork, uv);
    float3 art = source.rgb / max(source.a, 0.001);
    float gray = saturate((dot(art, float3(.299, .587, .114)) - .5) * 1.06 + .5);
    float angle = dot(uv, float2(.82, .57));
    float offset = dot(Light, float2(.68, .42));
    float phase = angle * .78 - offset * .85;
    float3 spectrum = saturate(abs(frac(phase + float3(0, .3333, .6667)) * 6 - 3) - 1);
    // Preserve the ink silhouette while tinting the pale paper with a pastel spectrum.
    float3 color = lerp(gray.xxx, art, ColorRetention) * (.68 + spectrum * .32);
    float band = saturate(1 - abs(angle - offset) * 5.2);
    float spot = saturate(1 - dot(uv - Light, uv - Light) * 3);
    float sweep = saturate(1 - abs(angle - Sweep) * 8);
    float grain = frac(dot(uv, float2(311, 197)));
    float reflection = band * band * .2 + spot * spot * .08 + sweep * sweep * .47;
    color += (1 - color) * (reflection + grain * .018);
    return float4(saturate(color) * source.a, source.a);
}
