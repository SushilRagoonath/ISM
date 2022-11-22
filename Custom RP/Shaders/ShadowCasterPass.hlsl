#ifndef CUSTOM_SHADOW_CASTER_PASS_INCLUDED
#define CUSTOM_SHADOW_CASTER_PASS_INCLUDED

struct Attributes {
	float3 positionOS : POSITION;
	float2 baseUV : TEXCOORD0;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings {
	float4 positionCS : SV_POSITION;
	float2 baseUV : VAR_BASE_UV;
	UNITY_VERTEX_INPUT_INSTANCE_ID
};

bool _ShadowPancaking;

Varyings ShadowCasterPassVertex (Attributes input) {
	Varyings output;
	UNITY_SETUP_INSTANCE_ID(input);
	UNITY_TRANSFER_INSTANCE_ID(input, output);
	float3 positionWS = TransformObjectToWorld(input.positionOS);
	//positionWS = TransformWorldToHClip(positionWS);
	output.positionCS = float4(positionWS,1.0f);
	output.positionCS = TransformWorldToHClip(positionWS);
	//output.positionCS = mul(input.positionOS , UNITY_MATRIX_M * UNITY_MATRIX_V);
	//output.positionCS = TransformWorldToHClip(output.positionCS);
	if (_ShadowPancaking) {
		#if UNITY_REVERSED_Z
			output.positionCS.z = min(
				output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE
			);
		#else
			output.positionCS.z = max(
				output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE
			);
		#endif
	}
	output.baseUV = TransformBaseUV(input.baseUV);
	//float3 pos = positionWS;
	//float l = length(pos);
	//pos = normalize(pos);
	//pos.xy /= 1.0 - pos.z;
	//pos.z = l / 500.0;
	//pos.z = pos.z * 2.0 - 1.0;
	//output.positionCS = float4(pos,1.0);
	
	
	//output.positionCS.z = output.positionCS.z * 2.0 - 1.0;
	//output.positionCS.z  = (l - 0.1)/(500.0-0.1);
	
	//output.positionCS.w = .5f;
	//output.positionCS.z = output.positionCS.z * 2.0 - 1.0;
	//output.positionCS *= l;
	//output.positionCS = TransformWorldToHClip(output.positionCS);
	return output;
}
float4 test_out(Varyings input): SV_Target{
	return (255,255,255,255);
}
void ShadowCasterPassFragment (Varyings input) {
	UNITY_SETUP_INSTANCE_ID(input);
	ClipLOD(input.positionCS.xy, unity_LODFade.x);

	InputConfig config = GetInputConfig(input.baseUV);
	float4 base = GetBase(config);
	base.a = 0.0;
	//#if defined(_SHADOWS_CLIP)
	//	clip(base.a - GetCutoff(config));
	//#elif defined(_SHADOWS_DITHER)
	//	float dither = InterleavedGradientNoise(input.positionCS.xy, 0);
	//	clip(base.a - dither);
	//#endif
}
	//output.baseUV = TransformBaseUV(input.baseUV);
	//output.positionCS /= output.positionCS.w;
	//output.positionCS.z *= -1.0;
	//float l = length(output.positionCS.xyz);
	//output.positionCS /= l;
	//output.positionCS.z += 1.0;
	//output.positionCS.xy /= output.positionCS.z;
	//output.positionCS.xy = 0.5f * output.positionCS.xy + 0.5f;
	////output.positionCS.z = output.positionCS.z * 2.0 - 1.0;
	////output.positionCS.z  = (l - 0.1)/(500.0-0.1);
	
	////output.positionCS.w = .5f;
	////output.positionCS.z = output.positionCS.z * 2.0 - 1.0;
	////output.positionCS *= l;
	////output.positionCS = TransformWorldToHClip(output.positionCS);
	//return output;
#endif