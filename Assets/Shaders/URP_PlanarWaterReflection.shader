Shader "EndlessOcean/URP/MirrorSurfaceSRP_RayPlane"
{
    Properties
    {
        // Base / color
        [MainColor]_Tint("Tint (Colour & Alpha Blend)", Color) = (1,1,1,0.9)
        _BaseMap("Base (optional)", 2D) = "white" {}
        _BaseMap_ST("Tiling/Offset", Vector) = (1,1,0,0)

        // Script-provided (do not edit)
        [NoScaleOffset]_MirrorTex("(script)", 2D) = "black" {}
        _PlanePosWS("(script)", Vector) = (0,0,0,0)
        _PlaneNormalWS("(script)", Vector) = (0,1,0,0)

        // Detail maps
        [NoScaleOffset]_NormalMap("Normal (RG or Unity Normal)", 2D) = "bump" {}
        _NormalScale("Normal Scale", Range(0,4)) = 0.0
        _UseRGPacked("Use RG-packed normal (if not imported as Normal)", Float) = 0

        [NoScaleOffset]_HeightMap("Height (gray)", 2D) = "black" {}
        _HeightScale("Height Scale", Range(0,0.1)) = 0.0

        [NoScaleOffset]_OcclusionMap("Ambient Occlusion", 2D) = "white" {}
        _OcclusionStrength("AO Strength", Range(0,1)) = 1.0

        // PBR controls (URP/Lit-like)
        _Metallic   ("Metallic",   Range(0,1)) = 0.0
        _Smoothness ("Smoothness", Range(0,1)) = 0.8

        // Reflection controls
        _Reflectiveness("Reflectiveness", Range(0,1)) = 1.0
        _FresnelPower("Fresnel Power", Range(0.1,10)) = 5.0
        _DistortionStrength("Reflection Distortion (by normal)", Range(0,1)) = 0.15

        // Ambient diffuse (fix grey blotches) — 0 = off, 1 = full SH
        _AmbientDiffuse("Ambient Diffuse (SH)", Range(0,1)) = 0

        // How to blend planar reflection with PBR lit result:
        // 0 = LERP (original), 1 = SCREEN (preserve glints), 2 = ADD (strongest)
        _ReflectionBlend("Reflection Blend (0=LERP,1=SCREEN,2=ADD)", Float) = 1

        // Match URP/Lit toggles
        [ToggleOff] _SPECULARHIGHLIGHTS_OFF("Specular Highlights", Float) = 0
        [ToggleOff] _ENVIRONMENTREFLECTIONS_OFF("Environment Reflections", Float) = 0
    }

    SubShader
    {
        Tags{ "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        ZWrite Off
        Cull Back
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Forward"
            Tags{ "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            // URP lighting variants like Lit
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // Textures
            TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);
            TEXTURE2D(_MirrorTex);    SAMPLER(sampler_MirrorTex);
            TEXTURE2D(_NormalMap);    SAMPLER(sampler_NormalMap);
            TEXTURE2D(_HeightMap);    SAMPLER(sampler_HeightMap);
            TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);

            // Material params
            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float4 _BaseMap_ST;
                float4 _PlanePosWS;
                float4 _PlaneNormalWS;
                float  _NormalScale;
                float  _UseRGPacked;
                float  _HeightScale;
                float  _OcclusionStrength;
                float  _Metallic;
                float  _Smoothness;
                float  _Reflectiveness;
                float  _FresnelPower;
                float  _DistortionStrength;
                float  _AmbientDiffuse;
                float  _ReflectionBlend;
            CBUFFER_END

            // From script (MPB)
            float4x4 _MirrorVP;

            // --- NEW: Player radius mask (from MPB) ---
            float4 _PlayerPosWS;   // xyz = position
            float  _Radius;        // <=0 disables mask
            float  _RadiusFeather; // soft edge

            struct A {
                float4 posOS : POSITION;
                float3 nrmOS : NORMAL;
                float4 tanOS : TANGENT;
                float2 uv    : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct V {
                float4 posCS : SV_POSITION;
                float3 posWS : TEXCOORD0;
                float3 nWS   : TEXCOORD1;
                float3 tWS   : TEXCOORD2;
                float3 bWS   : TEXCOORD3;
                float2 uv    : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Unpackers
            float3 UnpackNormalRG(float4 c)
            {
                float2 xy = c.rg * 2.0 - 1.0;
                float z = sqrt(saturate(1.0 - dot(xy, xy)));
                return float3(xy, z);
            }
            float3 UnpackUnityNormal(float4 c) { return UnpackNormal(c); }

            // Cheap parallax (base only; reflection must stay planar-stable)
            float2 ParallaxOffset(float2 uv, float3 Vws, float height)
            {
                float v = saturate(1.0 - abs(dot(normalize(Vws), float3(0,1,0))));
                return uv + (uv - 0.5) * height * v;
            }

            V vert (A IN)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs p = GetVertexPositionInputs(IN.posOS.xyz);
                VertexNormalInputs   n = GetVertexNormalInputs(IN.nrmOS, IN.tanOS);

                o.posCS = p.positionCS;
                o.posWS = p.positionWS;
                o.nWS   = n.normalWS;
                o.tWS   = n.tangentWS;
                o.bWS   = n.bitangentWS;
                o.uv    = TRANSFORM_TEX(IN.uv, _BaseMap);
                return o;
            }

            float3x3 CotangentFrame(float3 nWS, float3 posWS, float2 uv)
            {
                float3 dp1 = ddx(posWS);
                float3 dp2 = ddy(posWS);
                float2 duv1 = ddx(uv);
                float2 duv2 = ddy(uv);

                float3 t = normalize(duv2.y * dp1 - duv1.y * dp2);
                float3 b = normalize(-duv2.x * dp1 + duv1.x * dp2);
                float3 n = normalize(nWS);
                t = normalize(t - n * dot(n, t));
                b = cross(n, t);
                return float3x3(t,b,n);
            }

            float3 BlendScreen(float3 a, float3 b) { return 1.0 - (1.0 - a) * (1.0 - b); }

            float4 frag (V i) : SV_Target
            {
                // ===== Normal from map → world =====
                float3 geomN = normalize(i.nWS);
                float4 nTex = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv);
                float3 nTS = (_UseRGPacked > 0.5) ? UnpackNormalRG(nTex) : UnpackUnityNormal(nTex);
                nTS.xy *= _NormalScale;
                nTS = normalize(nTS);

                // Prefer mesh tangents; if missing, derive at pixel
                float hasT = any(abs(i.tWS) + abs(i.bWS)) ? 1.0 : 0.0;
                float3x3 TBN_mesh = float3x3(normalize(i.tWS), normalize(i.bWS), geomN);
                float3x3 TBN_der  = CotangentFrame(geomN, i.posWS, i.uv);
                float3x3 TBN      = (hasT > 0.5) ? TBN_mesh : TBN_der;

                float3 N = normalize(mul(TBN, nTS));
                float3 Vws = normalize(_WorldSpaceCameraPos - i.posWS);

                // ===== InputData for URP PBR =====
                InputData inputData;
                inputData.positionWS               = i.posWS;
                inputData.normalWS                 = N;
                inputData.viewDirectionWS          = Vws;
                inputData.shadowCoord              = TransformWorldToShadowCoord(i.posWS);
                inputData.fogCoord                 = 0;
                // scale/disable ambient SH
                inputData.bakedGI                  = SampleSH(N) * _AmbientDiffuse;
                inputData.vertexLighting           = 0;
                inputData.normalizedScreenSpaceUV  = GetNormalizedScreenSpaceUV(i.posCS);
                inputData.shadowMask               = 1;
                inputData.tangentToWorld           = TBN;

                // ===== Base + AO + parallax =====
                float2 uvBase = i.uv;
                if (_HeightScale > 0.0001)
                {
                    float h = SAMPLE_TEXTURE2D(_HeightMap, sampler_HeightMap, i.uv).r * 2.0 - 1.0;
                    uvBase = ParallaxOffset(uvBase, Vws, h * _HeightScale);
                }

                float3 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvBase).rgb;
                float3 albedo  = baseTex * _Tint.rgb;

                float aoTex = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, i.uv).r;
                float ao     = lerp(1.0, aoTex, saturate(_OcclusionStrength));

                SurfaceData surf;
                surf.albedo      = albedo;
                surf.metallic    = saturate(_Metallic);
                surf.specular    = 0;                 // metallic workflow
                surf.smoothness  = saturate(_Smoothness);
                surf.occlusion   = ao;
                surf.emission    = 0;
                surf.alpha       = saturate(_Tint.a);
                surf.clearCoatMask      = 0;
                surf.clearCoatSmoothness= 0;
                surf.normalTS           = float3(0,0,1); // normal already in inputData.normalWS

                half4 pbr = UniversalFragmentPBR(inputData, surf);
                float3 litColor = pbr.rgb;

                // ===== Planar reflection sample =====
                float3 P0 = i.posWS;
                float3 Pp = _PlanePosWS.xyz;
                float3 Np = normalize(_PlaneNormalWS.xyz);

                float3 Rws = reflect(-Vws, N);
                float denom = dot(Rws, Np);
                float3 hit = P0;
                if (abs(denom) > 1e-4)
                {
                    float t = dot(Pp - P0, Np) / denom;
                    if (t > 0.0) hit = P0 + Rws * t;
                }

                float4 clip = mul(_MirrorVP, float4(hit, 1));
                float2 uvR = clip.xy / max(clip.w, 1e-5);
                uvR = uvR * 0.5 + 0.5;
                uvR += N.xz * _DistortionStrength;

                float3 planar = SAMPLE_TEXTURE2D(_MirrorTex, sampler_MirrorTex, uvR).rgb;

                // --- NEW: Player-centric radius mask on the PLANE ---
                // Project fragment position and player-to-fragment delta onto plane (remove normal component).
                float3 d = i.posWS - _PlayerPosWS.xyz;
                float distOnPlane = length(d - Np * dot(d, Np)); // world distance along plane
                float mask = 1.0;
                // If _Radius <= 0, keep mask = 1 (disabled). Else smooth falloff from radius to radius+feather.
                if (_Radius > 0.0)
                {
                    float r0 = _Radius;
                    float r1 = max(r0, r0 + _RadiusFeather);
                    // inside r0 → 1, beyond r1 → 0
                    mask = saturate(1.0 - smoothstep(r0, r1, distOnPlane));
                }

                // Fresnel & blend
                float F = pow(1.0 - saturate(dot(N, Vws)), _FresnelPower);
                float reflectMix = saturate(_Reflectiveness * F) * mask;

                float3 finalRGB;
                if (_ReflectionBlend < 0.5) // 0 = LERP
                {
                    finalRGB = lerp(litColor, planar, reflectMix);
                }
                else if (_ReflectionBlend < 1.5) // 1 = SCREEN
                {
                    float3 reflScaled = planar * reflectMix;
                    finalRGB = BlendScreen(litColor, reflScaled);
                }
                else // 2 = ADD
                {
                    finalRGB = litColor + planar * reflectMix;
                }

                return float4(finalRGB, surf.alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
