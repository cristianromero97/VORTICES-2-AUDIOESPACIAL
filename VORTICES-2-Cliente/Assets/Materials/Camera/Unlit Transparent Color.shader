Shader "Unlit/Unlit Transparent Color" {
Properties {
    _Color ("Main Color", Color) = (1,1,1,1)
}

SubShader {
    Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}
    LOD 100

    ZTest Always
    ZWrite Off
    Blend SrcAlpha OneMinusSrcAlpha
    Cull Off

    Pass {
        CGPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        // Soporte para Single Pass Instanced (OpenXR / MockHMD en desktop)
        #pragma multi_compile_instancing
        #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

        #include "UnityCG.cginc"

        struct appdata {
            float4 vertex : POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f {
            float4 pos : SV_POSITION;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        fixed4 _Color;

        v2f vert (appdata v) {
            v2f o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.pos = UnityObjectToClipPos(v.vertex);
            return o;
        }

        fixed4 frag (v2f i) : SV_Target {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            return _Color;
        }
        ENDCG
    }
}
}
