float3 RGB2YCbCr(float3 LumaRBG, float3 ChromaRGB)
{
	float Y = LumaRBG.x * 0.29900f + LumaRBG.y * 0.58700f + LumaRBG.z * 0.11400f;
	float Cb = ChromaRGB.x * -0.16874f + ChromaRGB.y * -0.33126f + ChromaRGB.z * 0.50000f + .5f;
	float Cr = ChromaRGB.x * 0.50000f + ChromaRGB.y * -0.41869f + ChromaRGB.z * -0.08131f + .5f;

	return float3(Y, Cb, Cr);
}

float3 YCbCr2RGB(float3 YCbCr)
{
	float R = YCbCr.x + (YCbCr.z - .5f) * 1.40200f;
	float G = YCbCr.x + (YCbCr.y - .5f) * -0.34414f + (YCbCr.z - .5f) * -0.71414f;
	float B = YCbCr.x + (YCbCr.y - .5f) * 1.77200f;

	return float3(R, G, B);
}

//will probably run in to issues if texture isn't a perfect square. Might change that
void ChromaSubsampling_float(float4 ScreenPos, float2 ScreenDimensions, uint SubsampleRatio, UnityTexture2D SourceTexture, UnitySamplerState SS, out float4 Out)
{
	uint2 pixelPos = int2(round(ScreenPos.x * ScreenDimensions.x), round(ScreenPos.y * ScreenDimensions.y));
	float2 subsamplePos = float2(pixelPos.x - (pixelPos.x % SubsampleRatio), pixelPos.y - (pixelPos.y % SubsampleRatio));
	subsamplePos = float2(subsamplePos.x / ScreenDimensions.x, subsamplePos.y / ScreenDimensions.y);

	float3 first = RGB2YCbCr(
		SAMPLE_TEXTURE2D(SourceTexture, SS, ScreenPos).xyz,
		// currently just takes bottom right color of block.
		// could instead take center pixel value or average value of block
		SAMPLE_TEXTURE2D(SourceTexture, SS, float4(subsamplePos.x, subsamplePos.y, ScreenPos.zw)).xyz
	);
	float3 last = YCbCr2RGB(first);
	//Out = float4(first.xyz, 1);
	Out = float4(last.xyz, 1);
}

