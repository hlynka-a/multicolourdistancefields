Shader "Custom/DistanceFieldShaderInteractive"
{
    Properties
    {
        _Color ("RelSDF Color", Color) = (1,1,1,1)                              // this is the single solid color the RelSDF renders (RelSDF represents the shape mask)
        _ColorTolerance ("Color Tolerance", Range(0,1)) = 0.5                   // a stored parameter for generation of the SDF - how similar colors can be to be considered equal (lower means colors are typically considered distinct). Recommended value: 0.1 - 0.2.
        _ModeR("RelSDF channel", Float) = 1.0                                   // indicates what channel (R,G,B,A) stores the RelSDF. 0 = none, 1 = R, 2 = G, 3 = B, 4 = A
        _MainTex ("Main Tex", 2D) = "white" {}                                  // original texture (not a df) - as a fallback or backup, not necessary                   
        _BackgroundTex("Background Tex", 2D) = "white" {}                       // original texture with outline removed (primarily for debugging), not necessary
        // Note that we separate every channel into its own data for debugging here 
        // (this also ensures each channel is stored accurately, the A alpha channel is usually handled differently in engine...), 
        // but this can be re-written to store them as a combined RGBA asset, and to separate them by .r, .g, .b, .a during a draw call.
        // (originally, the code was written with them combined)
        _SC_DFTex("Single-Chan. DF for Outline", 2D) = "white" {}               // RelSDF (1 channel, use for outline of object)
        _SC_DFTexLUT("Single-Chan. RegDF LUT", 2D) = "white" {}                 // RegSDF - LUT (LUT can also be stored by other means, e.g. a list)
        _SC_DFTexBlendR("Single-Chan. RegDF R", 2D) = "white"{}                 // RegSDF - Region R
        _SC_DFTexBlendG("Single-Chan. RegDF G", 2D) = "white"{}                 // RegSDF - Region G
        _SC_DFTexBlendB("Single-Chan. RegDF B", 2D) = "white"{}                 // RegSDF - Region B
        _SC_DFTexBlendA("Single-Chan. RegDF A", 2D) = "white"{}                 // RegSDF - Region A
        _SC_DFTexBlendMapR("Single-Chan. RegDF Map to LUT R", 2D) = "white"{}   // RegSDF - mapping from Region R to LUT
        _SC_DFTexBlendMapG("Single-Chan. RegDF Map to LUT G", 2D) = "white"{}   // RegSDF - mapping from Region G to LUT
        _SC_DFTexBlendMapB("Single-Chan. RegDF Map to LUT B", 2D) = "white"{}   // RegSDF - mapping from Region B to LUT
        _SC_DFTexBlendMapA("Single-Chan. RegDF Map to LUT A", 2D) = "white"{}   // RegSDF - mapping from Region A to LUT
        _DistanceCutoff("RelSDF Distance cutoff", Range(0,1)) = 0.5             // RelSDF outline cutoff (use 0.5 for accurate rendering, < 0.5 for thicker lines, > 0.5 for thinner lines)
        _BlendCutoff("Blend cutoff", Range(0,1)) = 0.0                          // RegSDF region cutoff (use 0.5 for accurate rendering, > 0.5 for thinner regions)
        _BlendGradCutoff("Blend Gradient cutoff", Range(0,1)) = 0.0             // RegSDF experimental parameter to blend 2 colours like a "gradient"
        [MaterialToggle]_RenderOutline("Render Outline", Float) = 1.0           // turn RelSDF on or off
        [MaterialToggle]_RenderBackground("Render Background", Float) = 1.0     // turn RegSDF on or off
        _FilterMode("Filter Mode", Range(0,1)) = 1.0                            // 0 = point filter, 1 = bilinear filter (setting to 1 is recommended)
        _RenderColorMode("Render Color Mode", Range(0, 1)) = 0.0                // 1 = RelSDF and RegSDF, else it renders the original image normally 
        _ColorSpaceMode("Color Space Mode", Range(0, 4)) = 0.0                  // to convert color-space of stored color data. 0 = Linear (default from C#), 1 = to RGB, 2 = to HSV.
        _AntiAliasing("Anti Aliasing", Range(0, 1)) = 0.0                       // to turn on shader-based anti-aliasing for RelSDF only
        // below: leftover material parameters
        _Cutoff("Alpha cutoff", Range(0,1)) = 0.5                               
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }
    SubShader
    {
        // Tags { "Queue"="AlphaTest" "IgnoreProjector"="True" "RenderType"="TransparentCutoff" }
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        LOD 200

        // 2 lines below required for alpha between 0 and 1 (fade transparent)
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        // background color
        Pass {
            CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #pragma target 2.0
                #pragma multi_compile_fog

                #include "UnityCG.cginc"

                struct appdata_t {
                    float4 vertex : POSITION;
                    float2 texcoord : TEXCOORD0;
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                };

                struct v2f {
                    float4 vertex : SV_POSITION;
                    float2 texcoord : TEXCOORD0;
                    UNITY_FOG_COORDS(1)
                    UNITY_VERTEX_OUTPUT_STEREO
                };

                float4 _Color;
                int _Mode;
                sampler2D _MainTex;
                float4 _MainTex_ST;
                fixed _Cutoff;
                fixed _DistanceCutoff;
                sampler2D _BackgroundTex;
                float _RenderBackground;
                float _FilterMode;

                sampler2D _SC_DFTex;
                uniform fixed4 _SC_DFTex_ST;            //info about tiling?
                uniform fixed4 _SC_DFTex_TexelSize;     //info about resolution
                sampler2D _SC_DFTexLUT;
                sampler2D _SC_DFTexBlendR;
                sampler2D _SC_DFTexBlendG;
                sampler2D _SC_DFTexBlendB;
                sampler2D _SC_DFTexBlendA;
                sampler2D _SC_DFTexBlendMapR;
                sampler2D _SC_DFTexBlendMapG;
                sampler2D _SC_DFTexBlendMapB;
                sampler2D _SC_DFTexBlendMapA;

                float _RenderColorMode;
                float _ColorSpaceMode;  // 0 = Linear (default from C#), 1 = RGB, 2 = HSV. See https://docs.unity3d.com/Packages/com.unity.shadergraph@6.9/manual/Colorspace-Conversion-Node.html
                fixed _BlendCutoff;
                fixed _BlendGradCutoff;

                float4 MyBilinear(sampler2D _BackgroundTex, v2f i) {
                    fixed4 colNew;
                    // sample 4 points: each point is effectively 0.5 pixels away from centroid, but weight is based on distance of center of those 4 pixels to current point
                    fixed4 col0 = 0;
                    fixed4 col1 = 0;
                    fixed4 col2 = 0;
                    fixed4 col3 = 0;

                    float disX0 = i.texcoord.x - (floor((i.texcoord.x + (1.0 / _SC_DFTex_TexelSize.z) * 0.5) * _SC_DFTex_TexelSize.z) / (_SC_DFTex_TexelSize.z));
                    float disY0 = i.texcoord.y - (floor((i.texcoord.y + (1.0 / _SC_DFTex_TexelSize.w) * 0.5) * _SC_DFTex_TexelSize.w) / (_SC_DFTex_TexelSize.w));
                    float dis0 = sqrt(disX0 * disX0 + disY0 * disY0);
                    float disX1 = i.texcoord.x - (floor((i.texcoord.x - (1.0 / _SC_DFTex_TexelSize.z) * 0.5) * _SC_DFTex_TexelSize.z) / (_SC_DFTex_TexelSize.z));
                    float disY1 = i.texcoord.y - (floor((i.texcoord.y + (1.0 / _SC_DFTex_TexelSize.w) * 0.5) * _SC_DFTex_TexelSize.w) / (_SC_DFTex_TexelSize.w));
                    float dis1 = sqrt(disX1 * disX1 + disY1 * disY1);
                    float disX2 = i.texcoord.x - (floor((i.texcoord.x + (1.0 / _SC_DFTex_TexelSize.z) * 0.5) * _SC_DFTex_TexelSize.z) / (_SC_DFTex_TexelSize.z));
                    float disY2 = i.texcoord.y - (floor((i.texcoord.y - (1.0 / _SC_DFTex_TexelSize.w) * 0.5) * _SC_DFTex_TexelSize.w) / (_SC_DFTex_TexelSize.w));
                    float dis2 = sqrt(disX2 * disX2 + disY2 * disY2);
                    float disX3 = i.texcoord.x - (floor((i.texcoord.x - (1.0 / _SC_DFTex_TexelSize.z) * 0.5) * _SC_DFTex_TexelSize.z) / (_SC_DFTex_TexelSize.z));
                    float disY3 = i.texcoord.y - (floor((i.texcoord.y - (1.0 / _SC_DFTex_TexelSize.w) * 0.5) * _SC_DFTex_TexelSize.w) / (_SC_DFTex_TexelSize.w));
                    float dis3 = sqrt(disX3 * disX3 + disY3 * disY3);
                    // if distance is far, it should have less weight
                    float maxDis = sqrt(((1.0 / _SC_DFTex_TexelSize.z)) * ((1.0 / _SC_DFTex_TexelSize.z)) * 2);
                    dis0 = maxDis - dis0;
                    dis1 = maxDis - dis1;
                    dis2 = maxDis - dis2;
                    dis3 = maxDis - dis3;
                    float totalDis = dis0 + dis1 + dis2 + dis3;
                    float weight0 = dis0 / totalDis;
                    float weight1 = dis1 / totalDis;
                    float weight2 = dis2 / totalDis;
                    float weight3 = dis3 / totalDis;
                    colNew = col0 * weight0 + col1 * weight1 + col2 * weight2 + col3 * weight3;
                    return colNew;
                }

                float4 SampleBilinear(sampler2D tex, float2 uv, float4 texelSize)
                {
                    // source: https://discussions.unity.com/t/how-to-make-data-shader-support-bilinear-trilinear/598639/8
                   
                    // scale & offset uvs to integer values at texel centers
                    float2 uv_texels = uv * texelSize.zw + 0.5;

                    // get uvs for the center of the 4 surrounding texels by flooring
                    float4 uv_min_max = float4((floor(uv_texels) - 0.5) * texelSize.xy, (floor(uv_texels) + 0.5) * texelSize.xy);

                    // blend factor
                    float2 uv_frac = frac(uv_texels);

                    // sample all 4 texels
                    float4 texelA = tex2Dlod(tex, float4(uv_min_max.xy, 0, 0));
                    float4 texelB = tex2Dlod(tex, float4(uv_min_max.xw, 0, 0));
                    float4 texelC = tex2Dlod(tex, float4(uv_min_max.zy, 0, 0));
                    float4 texelD = tex2Dlod(tex, float4(uv_min_max.zw, 0, 0));

                    // bilinear interpolation
                    return lerp(lerp(texelA, texelB, uv_frac.y), lerp(texelC, texelD, uv_frac.y), uv_frac.x);
                }

                float4 SampleBilinear2(sampler2D tex, float2 uv, float4 texelSize) {
                    // scale & offset uvs to integer values at texel centers

                    // scale & offset uvs to integer values at texel centers
                    float2 uv_texels = uv * texelSize.zw + 0.5;

                    // get uvs for the center of the 4 surrounding texels by flooring
                    float4 uv_min_max = float4((floor(uv_texels) - 0.5) * texelSize.xy, (floor(uv_texels) + 0.5) * texelSize.xy);

                    // blend factor
                    float2 uv_frac = frac(uv_texels);

                    // sample all 4 texels
                    float4 texelA = tex2Dlod(tex, float4(uv_min_max.xy, 0, 0));
                    float4 texelB = tex2Dlod(tex, float4(uv_min_max.xw, 0, 0));
                    float4 texelC = tex2Dlod(tex, float4(uv_min_max.zy, 0, 0));
                    float4 texelD = tex2Dlod(tex, float4(uv_min_max.zw, 0, 0));

                    float4 result = lerp(lerp(texelA, texelB, uv_frac.y), lerp(texelC, texelD, uv_frac.y), uv_frac.x);
                    float4 returnValue;

                    float diffA = texelA - result;
                    float diffB = texelB - result;
                    float diffC = texelC - result;
                    float diffD = texelD - result;
                    if (diffA < diffB && diffA < diffC && diffA < diffD) {
                        returnValue = texelA;
                    }
                    else if (diffB < diffA && diffB < diffC && diffB < diffD) {
                        returnValue = texelB;
                    }
                    else if (diffC < diffA && diffC < diffB && diffC < diffD) {
                        returnValue = texelC;
                    }
                    else if (diffD < diffA && diffD < diffB && diffD < diffC) {
                        returnValue = texelD;
                    }
                    else {
                        returnValue = result;
                    }

                    // bilinear interpolation
                    return result;
                }

                void Unity_ColorspaceConversion_Linear_RGB_float(float4 In, out float4 Out)
                {
                    float3 In3 = float3(In.x, In.y, In.z);
                    float3 sRGBLo = In3 * 12.92;
                    float3 sRGBHi = (pow(max(abs(In3), 1.192092896e-07), float3(1.0 / 2.4, 1.0 / 2.4, 1.0 / 2.4)) * 1.055) - 0.055;
                    float3 Out3 = float3(In3 <= 0.0031308) ? sRGBLo : sRGBHi;
                    Out = float4(Out3.x, Out3.y, Out3.z, In.w);
                }

                void Unity_ColorspaceConversion_Linear_HSV_float(float4 In, out float4 Out)
                {
                    float3 In3 = float3(In.x, In.y, In.z);
                    float3 sRGBLo = In3 * 12.92;
                    float3 sRGBHi = (pow(max(abs(In3), 1.192092896e-07), float3(1.0 / 2.4, 1.0 / 2.4, 1.0 / 2.4)) * 1.055) - 0.055;
                    float3 Linear = float3(In3 <= 0.0031308) ? sRGBLo : sRGBHi;
                    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                    float4 P = lerp(float4(Linear.bg, K.wz), float4(Linear.gb, K.xy), step(Linear.b, Linear.g));
                    float4 Q = lerp(float4(P.xyw, Linear.r), float4(Linear.r, P.yzx), step(P.x, Linear.r));
                    float D = Q.x - min(Q.w, Q.y);
                    float  E = 1e-10;
                    float3 Out3 = float3(abs(Q.z + (Q.w - Q.y) / (6.0 * D + E)), D / (Q.x + E), Q.x);
                    Out = float4(Out3.x, Out3.y, Out3.z, In.w);
                }

                void Unity_ColorspaceConversion_RGB_Linear_float(float4 In, out float4 Out)
                {
                    // correct one to use. Weird, documentation says this is RGB -> Linear, I think it's really Linear -> RGB
                    float3 In3 = float3(In.x, In.y, In.z);
                    float3 linearRGBLo = In3 / 12.92;
                    float3 linearRGBHi = pow(max(abs((In3 + 0.055) / 1.055), 1.192092896e-07), float3(2.4, 2.4, 2.4));
                    float3 Out3 = float3(In3 <= 0.04045) ? linearRGBLo : linearRGBHi;
                    Out = float4(Out3.x, Out3.y, Out3.z, In.w);
                }

                void Unity_ColorspaceConversion_HSV_Linear_float(float4 In, out float4 Out)
                {
                    float3 In3 = float3(In.x, In.y, In.z);
                    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                    float3 P = abs(frac(In3.xxx + K.xyz) * 6.0 - K.www);
                    float3 RGB = In3.z * lerp(K.xxx, saturate(P - K.xxx), In3.y);
                    float3 linearRGBLo = RGB / 12.92;
                    float3 linearRGBHi = pow(max(abs((RGB + 0.055) / 1.055), 1.192092896e-07), float3(2.4, 2.4, 2.4));
                    float3 Out3 = float3(RGB <= 0.04045) ? linearRGBLo : linearRGBHi;
                    Out = float4(Out3.x, Out3.y, Out3.z, In.w);
                }

                fixed4 CalcColorMode(fixed4 col) {

                    if (_ColorSpaceMode == 0.0) {

                    }
                    else if (_ColorSpaceMode == 1.0) {
                        Unity_ColorspaceConversion_Linear_RGB_float(col, col);
                    }
                    else if (_ColorSpaceMode == 2.0) {
                        Unity_ColorspaceConversion_Linear_HSV_float(col, col);
                    }
                    else if (_ColorSpaceMode == 3.0) {
                        Unity_ColorspaceConversion_RGB_Linear_float(col, col);
                    }
                    else if (_ColorSpaceMode == 4.0) {
                        Unity_ColorspaceConversion_HSV_Linear_float(col, col);
                    }
                    return col;
                }

                v2f vert(appdata_t v)
                {
                    v2f o;
                    UNITY_SETUP_INSTANCE_ID(v);
                    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                    o.vertex = UnityObjectToClipPos(v.vertex);
                    o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                    UNITY_TRANSFER_FOG(o,o.vertex);
                    return o;
                }

                fixed4 frag(v2f i) : SV_Target
                {
                    fixed4 col = tex2D(_BackgroundTex, i.texcoord);

                    if (_RenderColorMode == 0.0) {
                        if (_FilterMode == 0) { 
                            // Point Filter Mode, assuming Unity filter mode for texture is also = Point
                        }
                        else if (_FilterMode == 1) {
                            // Bilinear Filter Mode
                            //col = MyBilinear(_BackgroundTex, i);
                            col = SampleBilinear(_BackgroundTex, i.texcoord, _SC_DFTex_TexelSize);
                        }
                        else if (_FilterMode == 2) {
                            // ...
                            col = SampleBilinear2(_BackgroundTex, i.texcoord, _SC_DFTex_TexelSize);
                        }
                        if (_RenderBackground == 0.0)
                        {
                            clip(-1);
                        }
                        else if (_RenderBackground == 1.0) {
                            clip(col.a - _Cutoff);
                        }
                    }
                    else if (_RenderColorMode == 1.0) {
                        if (_RenderBackground == 0.0)
                        {
                            clip(-1);
                        }
                        else if (_RenderBackground == 1.0) {

                            fixed colR = 0;
                            fixed colG = 0;
                            fixed colB = 0;
                            fixed colA = 0;

                            if (_FilterMode == 0) {
                                // Point Filter Mode, assuming Unity filter mode for texture is also = Point
                                colR = tex2D(_SC_DFTexBlendR, i.texcoord).r;
                                colG = tex2D(_SC_DFTexBlendG, i.texcoord).r;
                                colB = tex2D(_SC_DFTexBlendB, i.texcoord).r;
                                colA = tex2D(_SC_DFTexBlendA, i.texcoord).r;
                            }
                            else if (_FilterMode == 1) {
                                // Bilinear Filter Mode
                                colR = SampleBilinear(_SC_DFTexBlendR, i.texcoord, _SC_DFTex_TexelSize).r;
                                colG = SampleBilinear(_SC_DFTexBlendG, i.texcoord, _SC_DFTex_TexelSize).r;
                                colB = SampleBilinear(_SC_DFTexBlendB, i.texcoord, _SC_DFTex_TexelSize).r;
                                colA = SampleBilinear(_SC_DFTexBlendA, i.texcoord, _SC_DFTex_TexelSize).r;
                            }
                            //else if (_FilterMode == 2) {
                                // ...
                            //    col = SampleBilinear2(_DFTexBlend, i.texcoord, _SC_DFTex_TexelSize);
                            //}
                            float4 dfc = 0;   //tex2D(_DFTexBlend, i.texcoord); //col;    // can be set to col because "col" is overwritten in above FilterMode checks to come from DFTexBlend.
                            float4 dfm = 0;
                            float4 dfm2 = 0;
                            float dfr = 0;
                            float dfg = 0;
                            float dfb = 0;
                            float dfa = 0;  
                            float dfmr = 0;
                            float dfmg = 0;
                            float dfmb = 0;
                            float dfma = 0;
                            float dfc2 = 0;

                            dfmr = tex2D(_SC_DFTexBlendMapR, i.texcoord).r;
                            dfmg = tex2D(_SC_DFTexBlendMapG, i.texcoord).r;
                            dfmb = tex2D(_SC_DFTexBlendMapB, i.texcoord).r;
                            dfma = tex2D(_SC_DFTexBlendMapA, i.texcoord).r;
 
                            dfr = colR;
                            dfg = colG;
                            dfb = colB;
                            dfa = colA;

                            float dfcutoff = 0;
                            float dfcutoff2 = 0;
                            float dfcutoff3 = 0;
                            float luti = dfmr;
                            float luti2 = dfmr;
                            float luti3 = dfmr;
                            if (dfr >= dfg && dfr >= dfb && dfr >= dfa) {
                                luti = dfmr;
                                dfcutoff = dfr;

                                if (dfg >= dfb && dfg >= dfa) {
                                    luti2 = dfmg;
                                    dfcutoff2 = dfg;
                                    if (dfb >= dfa) {
                                        luti3 = dfmb;
                                        dfcutoff3 = dfb;
                                    }
                                    else {
                                        luti3 = dfma;
                                        dfcutoff3 = dfa;
                                    }
                                }
                                else if (dfb >= dfa){
                                    luti2 = dfmb;
                                    dfcutoff2 = dfb;
                                    if (dfg >= dfa) {
                                        luti3 = dfmg;
                                        dfcutoff3 = dfg;
                                    }
                                    else {
                                        luti3 = dfma;
                                        dfcutoff3 = dfa;
                                    }
                                }
                                else {
                                    luti2 = dfma;
                                    dfcutoff2 = dfa;
                                    if (dfg >= dfb) {
                                        luti3 = dfmg;
                                        dfcutoff3 = dfg;
                                    }
                                    else {
                                        luti3 = dfmb;
                                        dfcutoff3 = dfb;
                                    }
                                }

                            }
                            else if (dfg >= dfb && dfg >= dfa) {
                                luti = dfmg;
                                dfcutoff = dfg;

                                if (dfr >= dfb && dfr >= dfa) {
                                    luti2 = dfmr;
                                    dfcutoff2 = dfr;
                                    if (dfb >= dfa) {
                                        luti3 = dfmb;
                                        dfcutoff3 = dfb;
                                    }
                                    else {
                                        luti3 = dfma;
                                        dfcutoff3 = dfa;
                                    }
                                }
                                else if (dfb >= dfa) {
                                    luti2 = dfmb;
                                    dfcutoff2 = dfb;
                                    if (dfr >= dfa) {
                                        luti3 = dfmr;
                                        dfcutoff3 = dfr;
                                    }
                                    else {
                                        luti3 = dfma;
                                        dfcutoff3 = dfa;
                                    }
                                }
                                else {
                                    luti2 = dfma;
                                    dfcutoff2 = dfa;
                                    if (dfr >= dfb) {
                                        luti3 = dfmr;
                                        dfcutoff3 = dfr;
                                    }
                                    else {
                                        luti3 = dfmb;
                                        dfcutoff3 = dfb;
                                    }
                                }

                                
                            }
                            else if (dfb >= dfa) {
                                luti = dfmb;
                                dfcutoff = dfb;

                                if (dfr >= dfg && dfr >= dfa) {
                                    luti2 = dfmr;
                                    dfcutoff2 = dfr;
                                    if (dfg >= dfa) {
                                        luti3 = dfmg;
                                        dfcutoff3 = dfg;
                                    }
                                    else {
                                        luti3 = dfma;
                                        dfcutoff3 = dfa;
                                    }
                                }
                                else if (dfg >= dfa) {
                                    luti2 = dfmg;
                                    dfcutoff2 = dfg;
                                    if (dfr >= dfa) {
                                        luti3 = dfmr;
                                        dfcutoff3 = dfr;
                                    }
                                    else {
                                        luti3 = dfma;
                                        dfcutoff3 = dfa;
                                    }
                                }
                                else {
                                    luti2 = dfma;
                                    dfcutoff2 = dfa;
                                    if (dfr >= dfg) {
                                        luti3 = dfmr;
                                        dfcutoff3 = dfr;
                                    }
                                    else {
                                        luti3 = dfmg;
                                        dfcutoff3 = dfg;
                                    }
                                }

                            }
                            else if (dfa >= dfr && dfa >= dfg && dfa >= dfb){
                                luti = dfma;
                                dfcutoff = dfa;

                                if (dfr >= dfg && dfr >= dfb) {
                                    luti2 = dfmr;
                                    dfcutoff2 = dfr;
                                    if (dfg >= dfb) {
                                        luti3 = dfmg;
                                        dfcutoff3 = dfg;
                                    }
                                    else {
                                        luti3 = dfmb;
                                        dfcutoff3 = dfb;
                                    }
                                }
                                else if (dfg >= dfb) {
                                    luti2 = dfmg;
                                    dfcutoff2 = dfg;
                                    if (dfr >= dfb) {
                                        luti3 = dfmr;
                                        dfcutoff3 = dfr;
                                    }
                                    else {
                                        luti3 = dfmb;
                                        dfcutoff3 = dfb;
                                    }
                                }
                                else {
                                    luti2 = dfmb;
                                    dfcutoff2 = dfb;
                                    if (dfr >= dfg) {
                                        luti3 = dfmr;
                                        dfcutoff3 = dfr;
                                    }
                                    else {
                                        luti3 = dfmg;
                                        dfcutoff3 = dfg;
                                    }
                                }
                            }
                            float lutiog = luti;
                            luti = luti * 255.0f;

                            float widthT = _SC_DFTex_TexelSize.z;
                            float heightT = _SC_DFTex_TexelSize.w;
                            float lutix = luti % widthT;
                            float lutiy = floor(luti / widthT);

                            float2 lutcoordr = float2(lutix, lutiy + floor(0.0f * heightT));
                            lutcoordr = float2(lutcoordr.x / widthT, lutcoordr.y / heightT);
                            float2 lutcoordg = float2(lutix, lutiy + floor(0.25f * heightT));
                            lutcoordg = float2(lutcoordg.x / widthT, lutcoordg.y / heightT);
                            float2 lutcoordb = float2(lutix, lutiy + floor(0.5f * heightT));
                            lutcoordb = float2(lutcoordb.x / widthT, lutcoordb.y / heightT);
                            float2 lutcoorda = float2(lutix, lutiy + floor(0.75f * heightT));
                            lutcoorda = float2(lutcoorda.x / widthT, lutcoorda.y / heightT);

                            float lutr = 0;
                            float lutg = 0;
                            float lutb = 0;
                            float luta = 0;
                            lutr = tex2D(_SC_DFTexLUT, lutcoordr).r;
                            lutg = tex2D(_SC_DFTexLUT, lutcoordg).r;
                            lutb = tex2D(_SC_DFTexLUT, lutcoordb).r;
                            luta = tex2D(_SC_DFTexLUT, lutcoorda).r;

                            float4 color1 = float4(lutr, lutg, lutb, luta);
                            color1 = CalcColorMode(color1);
                            col = color1;

                            clip(dfcutoff - _BlendCutoff);  
                            if (_BlendGradCutoff <= 0.0f) {
                                clip(dfcutoff - _BlendCutoff);
                            }
                            else {
                                float4 color2 = color1;
                                float4 color3 = color1;

                                luti = luti2 * 256.0f;
                                lutix = luti % widthT;
                                lutiy = floor(luti / widthT);

                                lutcoordr = float2(lutix, lutiy + floor(0.0f * heightT));
                                lutcoordr = float2(lutcoordr.x / widthT, lutcoordr.y / heightT);
                                lutr = tex2D(_SC_DFTexLUT, lutcoordr).r;

                                lutcoordg = float2(lutix, lutiy + floor(0.25f * heightT));
                                lutcoordg = float2(lutcoordg.x / widthT, lutcoordg.y / heightT);
                                lutg = tex2D(_SC_DFTexLUT, lutcoordg).r;

                                lutcoordb = float2(lutix, lutiy + floor(0.5f * heightT));
                                lutcoordb = float2(lutcoordb.x / widthT, lutcoordb.y / heightT);
                                lutb = tex2D(_SC_DFTexLUT, lutcoordb).r;

                                lutcoorda = float2(lutix, lutiy + floor(0.75f * heightT));
                                lutcoorda = float2(lutcoorda.x / widthT, lutcoorda.y / heightT);
                                luta = tex2D(_SC_DFTexLUT, lutcoorda).r;

                                color2 = float4(lutr, lutg, lutb, luta);

                                luti = luti3 * 256.0f;
                                lutix = luti % widthT;
                                lutiy = floor(luti / widthT);

                                lutcoordr = float2(lutix, lutiy + floor(0.0f * heightT));
                                lutcoordr = float2(lutcoordr.x / widthT, lutcoordr.y / heightT);
                                lutr = tex2D(_SC_DFTexLUT, lutcoordr).r;

                                lutcoordg = float2(lutix, lutiy + floor(0.25f * heightT));
                                lutcoordg = float2(lutcoordg.x / widthT, lutcoordg.y / heightT);
                                lutg = tex2D(_SC_DFTexLUT, lutcoordg).r;

                                lutcoordb = float2(lutix, lutiy + floor(0.5f * heightT));
                                lutcoordb = float2(lutcoordb.x / widthT, lutcoordb.y / heightT);
                                lutb = tex2D(_SC_DFTexLUT, lutcoordb).r;

                                lutcoorda = float2(lutix, lutiy + floor(0.75f * heightT));
                                lutcoorda = float2(lutcoorda.x / widthT, lutcoorda.y / heightT);
                                luta = tex2D(_SC_DFTexLUT, lutcoorda).r;

                                color3 = float4(lutr, lutg, lutb, luta);

                                float weight1 = dfcutoff + 0.5 - _BlendGradCutoff;
                                if (weight1 <= 0.0) {
                                    weight1 = 0.0;
                                }
                                if (weight1 >= 1.0) {
                                    weight1 = 1.0;
                                }
                                if (dfcutoff2 <= 0.0) {
                                    dfcutoff2 = 0.0;
                                }
                                if (dfcutoff2 >= 1.0)
                                    dfcutoff2 = 1.0;
                                if (dfcutoff3 <= 0.0) {
                                    dfcutoff3 = 0.0;
                                }
                                if (dfcutoff3 >= 1.0)
                                    dfcutoff3 = 1.0; 
                                float weightRem = 1.0 - weight1;
                                float weightRemTot = dfcutoff2 + dfcutoff3;
                                float weight2 = dfcutoff2 * (weightRem / weightRemTot);
                                float weight3 = dfcutoff3 * (weightRem / weightRemTot);

                                if (weight2 <= 0.0) {
                                    weight2 = 0.0;
                                }
                                if (weight2 >= 1.0)
                                    weight2 = 1.0;
                                if (weight3 <= 0.0) {
                                    weight3 = 0.0;
                                }
                                if (weight3 >= 1.0)
                                    weight3 = 1.0;

                                weight1 = weight1 * weight1;
                                weight2 = weight2 * weight2;
                                weight3 = weight3 * weight3;
                                weightRemTot = weight1 + weight2 + weight3;
                                weight1 = weight1 / weightRemTot;
                                weight2 = weight2 / weightRemTot;
                                weight3 = weight3 / weightRemTot;
                                   
                                float4 colorTotal = float4( 
                                    (weight1 * color1.x) + (weight2 * color2.x) + (weight3 * color3.x),
                                    (weight1 * color1.y) + (weight2 * color2.y) + (weight3 * color3.y),
                                    (weight1 * color1.z) + (weight2 * color2.z) + (weight3 * color3.z),
                                    color1.w); 
                                col = colorTotal;
                                if (col.x == 0.0 && col.y == 0.0) {
                                    weight1 = 1.0;
                                    color1 = float4(1.0, 0.0, 0.0, 1.0);
                                    col = color1;
                                }
                                clip(dfcutoff - _BlendCutoff);
                            }
                        }
                    }
                    UNITY_APPLY_FOG(i.fogCoord, col);
                    return col;
                }


            ENDCG
        }

        // outline (RelSDF) (render last, to be on top of the previous pass with RegSDF)
        Pass{
            CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #pragma target 2.0
                #pragma multi_compile_fog

                #include "UnityCG.cginc"

                struct appdata_t {
                    float4 vertex : POSITION;
                    float2 texcoord : TEXCOORD0;
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                };

                struct v2f {
                    float4 vertex : SV_POSITION;
                    float2 texcoord : TEXCOORD0;
                    UNITY_FOG_COORDS(1)
                    UNITY_VERTEX_OUTPUT_STEREO
                };

                struct Input {
                    float4 color : COLOR;
                };

                float4 _Color;
                int _ModeR;

                sampler2D _MainTex;
                uniform fixed4 _MainTex_ST;
                uniform fixed4 _MainTex_TexelSize;
                fixed _Cutoff;
                //sampler2D _DistanceFieldTex;
                //uniform fixed4 _DistanceFieldTex_ST;        //info about tiling?
                //uniform fixed4 _DistanceFieldTex_TexelSize; //info about resolution... x,y = [0.0,1.0], z,w = [e.g. 0,512]
                fixed _DistanceCutoff;
                sampler2D _BackgroundTex;

                sampler2D _SC_DFTex;
                uniform fixed4 _SC_DFTex_ST;
                uniform fixed4 _SC_DFTex_TexelSize;

                float _RenderOutline;
                float _FilterMode;
                float _AntiAliasing;

                float4 Mylerp(float4 f1, float4 f2, float fi) {
                    return f1 + (f2 - f1) * fi;
                }

                float4 SampleBilinearDF(sampler2D tex, float2 uv, float4 texelSize)
                {
                    // source: https://discussions.unity.com/t/how-to-make-data-shader-support-bilinear-trilinear/598639/8
                    
                    // scale & offset uvs to integer values at texel centers
                    float2 uv_texels = uv * texelSize.zw + 0.5;

                    // get uvs for the center of the 4 surrounding texels by flooring
                    float4 uv_min_max = float4((floor(uv_texels) - 0.5) * texelSize.xy, (floor(uv_texels) + 0.5) * texelSize.xy);

                    // blend factor
                    float2 uv_frac = frac(uv_texels);

                    // sample all 4 texels
                    float4 texelA = tex2Dlod(tex, float4(uv_min_max.xy, 0, 0));
                    float4 texelB = tex2Dlod(tex, float4(uv_min_max.xw, 0, 0));
                    float4 texelC = tex2Dlod(tex, float4(uv_min_max.zy, 0, 0));
                    float4 texelD = tex2Dlod(tex, float4(uv_min_max.zw, 0, 0));

                    // bilinear interpolation
                    // ERROR: using "lerp" returns error in Web build, maybe because UnityCG wasn't included in build? So I implemented "lerp" from scratch.
                    //return lerp(lerp(texelA, texelB, uv_frac.y), lerp(texelC, texelD, uv_frac.y), uv_frac.x);
                    return Mylerp(Mylerp(texelA, texelB, uv_frac.y), Mylerp(texelC, texelD, uv_frac.y), uv_frac.x);
                }

                float AntiAliasReturn(sampler2D tex, float2 uv, float4 texelSize, float _DistanceCutoff, float dfFinalValue){
                    
                    float r =  min(_ScreenParams.x, _ScreenParams.y);
                    float u_blur = 4;
                    float width = u_blur / r;
                    float col_a = 1.0f - ((_DistanceCutoff - dfFinalValue) / width);
                    if (col_a > 1.0f){
                        col_a = 1.0f;
                    } else if (col_a < 0.0f){
                        col_a = 0.0f;
                    }

                    return col_a;
                    
                }


                v2f vert(appdata_t v)
                {
                    v2f o;
                    UNITY_SETUP_INSTANCE_ID(v);
                    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                    o.vertex = UnityObjectToClipPos(v.vertex);
                    o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                    UNITY_TRANSFER_FOG(o,o.vertex);
                    return o;
                }

                fixed4 frag(v2f i) : SV_Target
                {
                    fixed4 col = _Color;
                    
                    float4 df = 0;
                    df = tex2D(_SC_DFTex, i.texcoord);

                    if (_FilterMode == 0) {
                        // Point Filter Mode, assuming Unity filter mode for texture is also = Point
                    }
                    else if (_FilterMode == 1) {
                        // Bilinear Filter Mode
                        df = SampleBilinearDF(_SC_DFTex, i.texcoord, _SC_DFTex_TexelSize);
                    }
                 
                    float dfValue = 0;
                    float dfCount = 0;
                    if (_RenderOutline == 0.0) {
                        // don't render outline (RelSDF), only render RegSDF (previous pass)
                        dfValue = 1.0;
                        dfCount = 1.0;
                        clip(-1);
                    }
                    else if (_RenderOutline == 1.0)
                    {
                        // render outline (RelSDF), based on what channel the data is in
                        if (_ModeR == 1) {
                            dfValue = df.r;
                        }
                        if (_ModeR == 2) {
                            dfValue = df.g;
                        }
                        if (_ModeR == 3) {
                            dfValue = df.b;
                        }
                        if (_ModeR == 4) {
                            dfValue = df.a;
                        }
                        float dfFinalValue = dfValue;

                        float dfCutoff = dfFinalValue - _DistanceCutoff;        
                        if (_AntiAliasing == 0.0){
                            clip(dfCutoff);
                        } else {
                            clip(dfCutoff + 0.1);
                            col.a = AntiAliasReturn(_SC_DFTex, i.texcoord, _SC_DFTex_TexelSize, _DistanceCutoff, dfFinalValue);
                        }
                        
                    }
                    
                    UNITY_APPLY_FOG(i.fogCoord, col);
                    return col;
                }
            ENDCG
        }
    }
    //FallBack "Diffuse"
}
