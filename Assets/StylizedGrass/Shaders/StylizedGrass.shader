Shader "Custom/Stylized Grass Transparent"
{
    Properties 
    {
        [MainTexture] _MainTexture ("Texture", 2D) = "white" {}
        [MainColor] _MainColor ("Color", Color) = (1, 1, 1, 1)
        _GroundColor ("Ground Color", Color) = (1, 1, 1, 1)
        _GroundBlendHeight ("Ground Blend Height", Float) = .25
        _AlphaClipThreshold ("Alpha Clipping Threshold", Float) = .5
        [Header(Lighting)][Space] _RampTexture ("Ramp Texture", 2D) = "white" {}
        _ShadowColor ("Shadow Color", Color) = (0.25, 0.25, 0.25, 1)
    }
    SubShader
    {
        Tags { "IgnoreProjector" = "True" "Queue" = "Transparent" }

        HLSLINCLUDE
            // The Core.hlsl file contains definitions of frequently used HLSL
            // macros and functions, and also contains #include references to other
            // HLSL files (for example, Common.hlsl, SpaceTransforms.hlsl, etc.).
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "NoiseSimplex.cginc"
            #include "GrassHelpers.hlsl"

            // URP Compatability
            CBUFFER_START(UnityPerMaterial)
            sampler2D _MainTexture;
            sampler2D _RampTexture;
            float4 _MainColor;
            float4 _GroundColor;
            float _GroundBlendHeight;
            float _AlphaClipThreshold;
            float4 _ShadowColor;
            CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Unlit Transparent"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            // Shadow keywords
            // Cascaded shadows for main light
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            // Shadows for additional lights
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            // make fog work
            #pragma multi_compile_fog

            #pragma shader_feature_local RANDOM_HEIGHT_ON
            #pragma shader_feature_local RANDOM_ROTATION_ON
            #pragma shader_feature_local BILLBOARD_ON

            struct Varyings
            {
                float4 color : COLOR0;
                float4 pos : SV_POSITION;
                half3 normal : NORMAL;
                float4 posWS : POSITIONT0;
                float3 posOS : POSITIONT1;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
    
                // Fetch vertex information
                o.posOS = _Positions[_Triangles[vertexID + _StartIndex] + _BaseVertexIndex];
                o.normal = _Normals[_Triangles[vertexID + _StartIndex] + _BaseVertexIndex];
                o.uv = _UVs[_Triangles[vertexID + _StartIndex] + _BaseVertexIndex];

                float3 fwd = float3(0, 0, 1);

#if BILLBOARD_ON
                fwd = CalculateBillboardForward(instanceID);
#endif
    
                ScaleInstance(o.posOS);        
    
                RandomizeInstance(o.posOS, fwd, instanceID);

                float3 interactorFwd;
                float blend;
                if(CalculateInteractorForward(instanceID, o.posOS, interactorFwd, blend))
                {
                    fwd = lerp(fwd, interactorFwd, blend);
                }

                LookAtTransformation(o.posOS, o.normal, fwd);
    
                o.posOS = CalculateWind(o.posOS, instanceID);
    
                // Convert object position to world position
                o.posWS = mul(_ObjectToWorld, float4(o.posOS + _InstancePoints[instanceID], 1));
    
                o.pos = mul(UNITY_MATRIX_VP, o.posWS);

                return o;
            }

            float4 frag(Varyings i) : SV_Target
            {
                // Texture and color
                float4 color = tex2D(_MainTexture, i.uv) * _MainColor;
    
                // Mix ground color
                color.rgb = lerp(_GroundColor.rgb, color.rgb, min(i.posOS.y / _GroundBlendHeight, 1));

                color.rgb *= CalculateSimpleMainLight(_ShadowColor, i.posWS) + CalculateAdditionalLight(i.posWS, _ShadowColor, _RampTexture);          
    
                // Fog
                #if (defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2))
                    float nearToFarZ = max(i.pos.w - _ProjectionParams.y, 0);
                    // ComputeFogFactorZ0ToFar and MixFog functions can be found in ShaderVariablesFunctions.hlsl of URP shader library
                    half fogFactor = ComputeFogFactorZ0ToFar(nearToFarZ);
                    color.rgb = MixFog(color.rgb, fogFactor);
                #endif

                clip(color.a - _AlphaClipThreshold);

                return color;
            }
            ENDHLSL
        }
    }
}