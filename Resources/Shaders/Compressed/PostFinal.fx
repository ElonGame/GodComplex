#include"Inc/Global.fx"
Texture2D _TexNoise:register(t0);cbuffer cbTextureLOD:register( b0){float _LOD;};struct VS_IN{float4 Position:SV_POSITION;};VS_IN VS(VS_IN V){return V;}float4 PS(VS_IN V):SV_TARGET0{float2 P=2.*V.Position.xy*INV_SCREEN_SIZE;return Tex2DLOD(_TexNoise,LinearWrap,P,_LOD);}