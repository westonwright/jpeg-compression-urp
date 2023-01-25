float GetG(float CurDev, float X, float Y)
{
	return (1 / (6.28318530f * pow(CurDev, 2))) * pow(2.71828182f, -((pow(X, 2) + pow(Y, 2)) / (2 * pow(CurDev, 2))));
}

void Gaussian_float(float4 ScreenPos, float ScreenWidth, float ScreenHeight, int SampleSize, float StDev, float TexScale, UnityTexture2D BaseTexture, UnitySamplerState SS, out float4 Out)
//void Gaussian_float(Texture2D BaseTexture, SamplerState SS, out float4 Out)
{
	float total = 0;
	//StDev = pow(StDev, 2);
	//conv is the convolution
	float4 conv = float4(0, 0, 0, 0);
	for (int x = 0; x < SampleSize; x++)
	{
		float xPos = ((float)x / ScreenWidth) / TexScale;
		for (int y = 0; y < SampleSize; y++)
		{
			float yPos = ((float)y / ScreenHeight) / TexScale;
			//float G = GetG(StDev, x / TexScale, y / TexScale);
			float G = GetG(StDev, x, y);
			conv += SAMPLE_TEXTURE2D(BaseTexture, SS, float4(ScreenPos.x + xPos, ScreenPos.y + yPos, ScreenPos.zw)) * G;
			total += G;
			if (x > 0)
			{
				//G = GetG(StDev, x / TexScale, y / TexScale);
				G = GetG(StDev, x, y);
				conv += SAMPLE_TEXTURE2D(BaseTexture, SS, float4(ScreenPos.x - xPos, ScreenPos.y + yPos, ScreenPos.zw)) * G;
				total += G;
			}
			if (y > 0)
			{
				//G = GetG(StDev, x / TexScale, y / TexScale);
				G = GetG(StDev, x, y);
				conv += SAMPLE_TEXTURE2D(BaseTexture, SS, float4(ScreenPos.x + xPos, ScreenPos.y - yPos, ScreenPos.zw)) * G;
				total += G;
			}
			if (x > 0 && y > 0)
			{
				//G = GetG(StDev, x / TexScale, y / TexScale);
				G = GetG(StDev, x, y);
				conv += SAMPLE_TEXTURE2D(BaseTexture, SS, float4(ScreenPos.x - xPos, ScreenPos.y - yPos, ScreenPos.zw)) * G;
				total += G;
			}
		}
	}
	conv *= 1 / total;
	Out = conv;
}