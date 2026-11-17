Shader "Hidden/CosmeticExportFixture"
{
    Properties
    {
        _BaseMap_Atlas ("Atlas", 2DArray) = "" {}
        _USE_TEX_ARRAY_ATLAS ("Use Atlas", Float) = 1
        _BaseMap_AtlasSlice ("Slice", Float) = 0
        _BaseMap ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 position : POSITION) : SV_POSITION { return UnityObjectToClipPos(position); }
            float4 frag() : SV_Target { return float4(1,1,1,1); }
            ENDHLSL
        }
    }
}
