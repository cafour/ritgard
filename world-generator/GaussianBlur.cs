using System;

namespace Ritgard.WorldGenerator;

public static class GaussianBlur
{
    public static void Blur(IslandHeightmap heightmap, int step, float[] kernel, float[,] temp)
    {
        if (kernel.Length == 0)
        {
            throw new ArgumentException("Cannot blur with an empty kernel.");
        }

        var radius = (kernel.Length - 1) / 2;

        var slice = heightmap.GetRawStepSpan(step);
        // horizontal pass
        for (int y = 0; y < heightmap.SizeY; y++)
        {
            for (int x = 0; x < heightmap.SizeX; x++)
            {
                float sum = 0;
                float weightSum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int px = x + k;
                    if (px >= 0 && px < heightmap.SizeX)
                    {
                        float w = kernel[k + radius];
                        sum += heightmap.ToIntHeight(slice[px + y * heightmap.SizeX]) * w;
                        weightSum += w;
                    }
                }

                temp[y, x] = sum / weightSum;
            }
        }

        // vertical pass
        for (int y = 0; y < heightmap.SizeY; ++y)
        {
            for (int x = 0; x < heightmap.SizeX; x++)
            {
                float sum = 0;
                float weightSum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int py = y + k;
                    if (py >= 0 && py < heightmap.SizeY)
                    {
                        float w = kernel[k + radius];
                        sum += temp[py, x] * w;
                        weightSum += w;
                    }
                }

                slice[x + y * heightmap.SizeX] = heightmap.ToByteHeight((int)MathF.Round(sum / weightSum));
            }
        }
    }

    public static float[] CreateKernel(float sigma, int radius = -1)
    {
        if (radius == -1)
        {
            radius = (int)MathF.Ceiling(3 * sigma);
        }

        var kernel = new float[radius * 2 + 1];
        var s2 = 2 * sigma * sigma;
        var sum = 0f;

        for (int i = -radius; i <= radius; i++)
        {
            var v = MathF.Exp(-(i * i) / s2);
            kernel[i + radius] = v;
            sum += v;
        }

        for (int i = 0; i < kernel.Length; i++)
        {
            kernel[i] /= sum;
        }

        return kernel;
    }
}
