Shader "Hidden/Custom RP/Camera Renderer"
{
	SubShader
	{
		Cull Off
		ZTest Always
		ZWrite Off
		
		HLSLINCLUDE
		#include "../ShaderLibrary/Common.hlsl"
		#include "CameraRendererPasses.hlsl"
		ENDHLSL
		
		Pass
		{
			Name "Copy"
			
			Blend [_CameraSrcBlend] [_CameraDstBlend]

			HLSLPROGRAM
				#pragma target 3.5
				#pragma vertex DefaultPassVertex
				#pragma fragment CopyPassFragment
			ENDHLSL
		}

		Pass
		{
			Name "Copy Depth"

			ColorMask 0
			ZWrite On
			
			HLSLPROGRAM
				#pragma target 3.5
				#pragma vertex DefaultPassVertex
				#pragma fragment CopyDepthPassFragment
			ENDHLSL
		}

		Pass
		{
			Name "Bloom Prefilter"

			HLSLPROGRAM
				#pragma target 3.5
				#pragma vertex DefaultPassVertex
				#pragma fragment BloomPrefilterPassFragment
			ENDHLSL
		}

		Pass
		{
			Name "Bloom Downsample"

			HLSLPROGRAM
				#pragma target 3.5
				#pragma vertex DefaultPassVertex
				#pragma fragment BloomDownsampleFragment
			ENDHLSL
		}

		Pass
		{
			Name "Bloom Horizontal 5"

			HLSLPROGRAM
				#pragma target 3.5
				#pragma vertex DefaultPassVertex
				#pragma fragment BloomBlurHorizontal5Fragment
			ENDHLSL
		}

		Pass
		{
			Name "Bloom Vertical 5"

			HLSLPROGRAM
				#pragma target 3.5
				#pragma vertex DefaultPassVertex
				#pragma fragment BloomBlurVertical5Fragment
			ENDHLSL
		}

		Pass
		{
			Name "Bloom Horizontal 9"

			HLSLPROGRAM
				#pragma target 3.5
				#pragma vertex DefaultPassVertex
				#pragma fragment BloomBlurHorizontal9Fragment
			ENDHLSL
		}

		Pass
		{
			Name "Bloom Vertical 9"

			HLSLPROGRAM
				#pragma target 3.5
				#pragma vertex DefaultPassVertex
				#pragma fragment BloomBlurVertical9Fragment
			ENDHLSL
		}
		
		Pass
		{
		    Name "Final Bloom And Tone Mapping"

		    Blend [_CameraSrcBlend] [_CameraDstBlend]

		    HLSLPROGRAM
		        #pragma target 3.5
		        #pragma vertex DefaultPassVertex
		        #pragma fragment FinalPostFXFragment
		    ENDHLSL
		}

		Pass
		{
		    Name "Final Bloom"

		    Blend [_CameraSrcBlend] [_CameraDstBlend]

		    HLSLPROGRAM
		        #pragma target 3.5
		        #pragma vertex DefaultPassVertex
		        #pragma fragment FinalPostFXWithoutToneMappingFragment
		    ENDHLSL
		}

		Pass
		{
		    Name "Final Tone Mapping"

		    Blend [_CameraSrcBlend] [_CameraDstBlend]

		    HLSLPROGRAM
		        #pragma target 3.5
		        #pragma vertex DefaultPassVertex
		        #pragma fragment FinalToneMappingWithoutBloomFragment
		    ENDHLSL
		}
	}
}
