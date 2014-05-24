Shader "Hidden/CandelaSSRRv1_POM" {
	Properties {
		_MainTex ("Base (RGB)", 2D) = "white" {}
	}

//================================================================================================================================================
//USING UNITY DEPTH TEXTURE - BEGIN OF PASS 2
//================================================================================================================================================
	
SubShader {
	ZTest Always Cull Off ZWrite Off Fog { Mode Off }
	Pass {

CGPROGRAM
#pragma target 3.0
#pragma vertex VS
#pragma fragment PS
#pragma fragmentoption ARB_precision_hint_fastest
//#extension GL_ARB_shader_texture_lod : enable
#pragma glsl
#include "UnityCG.cginc"

uniform float		_SSRRcomposeMode;
uniform sampler2D	_CameraDepthTexture;
uniform sampler2D	_CameraNormalsTexture;
uniform float4x4	_ViewMatrix;
uniform float4x4	_ProjectionInv;
uniform float4x4	_ProjMatrix;
uniform float		_bias;
uniform float		_stepGlobalScale;
uniform float		_maxStep;
uniform float		_maxFineStep;
uniform float		_maxDepthCull;
uniform float		_fadePower;
uniform sampler2D	_MainTex;
//uniform float4		_ZBufferParams;
//uniform float4		_ScreenParams;

#if TARGET_GLSL
#define	_tex2Dlod( s, uv ) tex2D( s, uv.xy )	// SIMPLE AS THAT???? ��
#else
#define	_tex2Dlod( s, uv ) tex2Dlod( s, uv )
#endif

struct PS_IN {
	float4 pos : POSITION;
	float2 uv : TEXCOORD0;
};

PS_IN VS( appdata_img v )
{
	PS_IN o;
	o.pos = mul (UNITY_MATRIX_MVP, v.vertex);
	o.uv = MultiplyUV( UNITY_MATRIX_TEXTURE0, v.texcoord );

	return o;
}

float4	PS( PS_IN _In ) : COLOR
{
	float4 Result = 0.0;
	float4 SourceColor = _tex2Dlod(_MainTex, float4( _In.uv, 0, 0.0 ) );

	if ( SourceColor.w == 0.0 ) {
		Result = float4(0.0, 0.0, 0.0, 0.0);
		return Result;
	}

//return float4( 1, 1, 0, 1 );

//*
	float	Zproj = _tex2Dlod(_CameraDepthTexture, float4(_In.uv, 0, 0.0)).x;
	float	Z = 1.0 / (((_ZBufferParams.x * Zproj) + _ZBufferParams.y));
//return 80 * Z;

	if ( Z > _maxDepthCull )
		return 0.0;

	int tmpvar_23 = int(_maxStep);

	// Compute position in projected space
	float4	projPosition = float4( (_In.uv * 2.0) - 1.0, Zproj, 1.0 );

	// Transform back into camera space
	float4	csPosition = mul( _ProjectionInv, projPosition );
			csPosition = csPosition / csPosition.w;

	// Compute view vector
	float3	csView = normalize( csPosition.xyz );

//return float4( csPosition.xy, -csPosition.z, csPosition.w );

	float4 wsNormal;
	wsNormal.w = 0.0;
	wsNormal.xyz = (_tex2Dlod( _CameraNormalsTexture, float4(_In.uv, 0, 0.0) ).xyz * 2.0) - 1.0;

	float3 csNormal;
	csNormal = normalize( mul(_ViewMatrix, wsNormal).xyz );

//return float4( csNormal, 0 );

//	float3	csReflectedView = normalize( csView - (2.0 * (dot( csNormal, csView) * csNormal)) );
	float3	csReflectedView = csView - (2.0 * (dot( csNormal, csView) * csNormal));	// No use to normalize this!
//return float4( csReflectedView, 1 );

	float4	projOffsetPosition = mul( _ProjMatrix, float4( csPosition.xyz + csReflectedView, 1.0 ) );	// Position offset by reflected view
			projOffsetPosition /= projOffsetPosition.w;
	float3	projRay = normalize( projOffsetPosition - projPosition.xyz );	// Some kind of target vector in projective space (the ray?)
//return float4( projRay, 1 );


// ScreenWidth = _ScreenParams.xy = (687, 325)
//return 0.5 * _ScreenParams.x / 687.0;
//return 0.5 * _ScreenParams.y / 325.0;

	float3	lkjwejhsdkl_1 = float3( 0.5 * projRay.xy, projRay.z );
	float	tmpvar_32 = length( lkjwejhsdkl_1.xy );
	float3	globalRay = lkjwejhsdkl_1 * ((2.0 * _stepGlobalScale / _ScreenParams.x) / tmpvar_32);

	float	tmpvar_31 = 2.0 / _ScreenParams.x;

	int		MaxStepsCount = int(_maxStep);
	float	reflectionDistance = 0.0;

	bool	globalHitValid = false;

	float3	projCurrentPos = float3( _In.uv, Zproj ) + globalRay;
	float4	projHitPosition;
	for ( int StepIndex=0; StepIndex < MaxStepsCount; StepIndex++ )
	{
		float	ScreenZ = 1.0 / (_ZBufferParams.x * _tex2Dlod( _CameraDepthTexture, float4( projCurrentPos.xy, 0, 0.0 ) ).x + _ZBufferParams.y);
		float	CurrentZ = 1.0 / (_ZBufferParams.x * projCurrentPos.z + _ZBufferParams.y);
		if ( ScreenZ < CurrentZ - 1e-06 )
		{	// Got a hit
			projHitPosition = float4( projCurrentPos, 1.0 );	// W set to 1
			globalHitValid = true;
			break;
		}

		projCurrentPos += globalRay;
		reflectionDistance += 1.0;
	}

	if ( !globalHitValid )
		projHitPosition = float4( projCurrentPos, 0.0 );	// W set to 0 if no hit

	float4	opahwcte_2 = projHitPosition;
	float	tmpvar_37 = abs( projHitPosition.x - 0.5 );

	float4 acccols_8 = float4( 0.0, 0.0, 0.0, 0.0 );
	if ( _SSRRcomposeMode > 0.0 )
	{
		float4 tmpvar_38;
		tmpvar_38.w = 0.0;
		tmpvar_38.xyz = SourceColor.xyz;
		acccols_8 = tmpvar_38;
	}

	if ( tmpvar_37 > 0.5 )
		return acccols_8;

	if ( abs( projHitPosition.y - 0.5 ) > 0.5 )
		return acccols_8;

	if ( 1.0 / (_ZBufferParams.x * projHitPosition.z + _ZBufferParams.y) > _maxDepthCull )
		return float4( 0.0, 0.0, 0.0, 0.0 );

	if ( projHitPosition.z < 0.1 )
		return float4( 0.0, 0.0, 0.0, 0.0 );

	if ( projHitPosition.w == 1.0 )
	{	// Fine step tracing
		float4	greyfsd_41;
		float4	alsdmes_45;
		float3	tmpvar_49 = projHitPosition.xyz - globalRay;
		float3	tmpvar_50 = lkjwejhsdkl_1 * (tmpvar_31 / tmpvar_32);
		float3	refDir_44_47 = tmpvar_50;
		int		maxfeis_46 = int(_maxFineStep);
		bool	fjekfesa_44 = false;
		float3	poffses_42 = tmpvar_49;
		float3	oifejef_48 = tmpvar_49 + tmpvar_50;
		int		i_49_43 = 0;
		int		j_40 = 0;
		for (int j_40 = 0; j_40 < 20; )
		{
			if ( i_49_43 >= maxfeis_46 )
				break;

			float	tmpvar_51 = 1.0 / (_ZBufferParams.x * _tex2Dlod( _CameraDepthTexture, float4(oifejef_48.xy, 0, 0.0) ).x + _ZBufferParams.y);
			float	tmpvar_52 = 1.0 / (_ZBufferParams.x * oifejef_48.z + _ZBufferParams.y);
			if ( tmpvar_51 < tmpvar_52 )
			{
				if ( tmpvar_52 - tmpvar_51 < _bias )
				{
					greyfsd_41.w = 1.0;
					greyfsd_41.xyz = oifejef_48;
					alsdmes_45 = greyfsd_41;
					fjekfesa_44 = bool(1);
					break;
				};
				float3 tmpvar_53;
				tmpvar_53 = (refDir_44_47 * 0.5);
				refDir_44_47 = tmpvar_53;
				oifejef_48 = (poffses_42 + tmpvar_53);
			}
			else
			{
				poffses_42 = oifejef_48;
				oifejef_48 = (oifejef_48 + refDir_44_47);
			}

			i_49_43 = (i_49_43 + 1);
			j_40 = (j_40 + 1);
		}

		if ( fjekfesa_44 == bool(0) )
		{
			float4 tmpvar_55_54;
			tmpvar_55_54.w = 0.0;
			tmpvar_55_54.xyz = oifejef_48;
			alsdmes_45 = tmpvar_55_54;
			fjekfesa_44 = bool(1);
		}

		opahwcte_2 = alsdmes_45;
	}

	if ( opahwcte_2.w < 0.01 )
	{
		Result = acccols_8;
	}
	else
	{
		float4 tmpvar_57_55;
		tmpvar_57_55.xyz = _tex2Dlod(_MainTex, float4(opahwcte_2.xy, 0, 0.0)).xyz;
		tmpvar_57_55.w = (((opahwcte_2.w * (1.0 - (Z / _maxDepthCull))) * (1.0 - pow ( reflectionDistance / float(tmpvar_23), _fadePower))) * pow (clamp (((dot (normalize(csReflectedView), normalize(csPosition).xyz) + 1.0) + (_fadePower * 0.1)), 0.0, 1.0), _fadePower));
		Result = tmpvar_57_55;
	}
//*/

	return Result;
}
ENDCG

	}	//	Pass
}	//SubShader

Fallback off
}	// Shader