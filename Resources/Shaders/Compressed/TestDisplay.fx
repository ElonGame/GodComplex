#include"Inc/Global.fx"
Texture2D _TexNoise:register(t10);cbuffer cbObject:register( b10){float4x4 _Local2World;};struct VS_IN{float3 Position:POSITION;float3 Normal:NORMAL;float3 Tangent:TANGENT;float2 UV:TEXCOORD0;};struct PS_IN{float4 __Position:SV_POSITION;float3 Normal:NORMAL;float2 UV:TEXCOORD0;};PS_IN VS(VS_IN P){float4 V=mul(float4(P.Position,1.),_Local2World);PS_IN f;f.__Position=mul(V,_World2Proj);f.Normal=P.Normal;f.UV=P.UV;return f;}float4 PS(PS_IN P):SV_TARGET0{return float4(P.Normal,1.);}