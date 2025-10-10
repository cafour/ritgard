using System;
using System.Threading.Tasks;
using Godot;

namespace Ritgard;

public static class GaussianBlur
{
    public static void Blur(byte[,] data, float[] kernel, float[,] temp = null)
    {
        var height = data.GetLength(0);
        var width = data.GetLength(1);

        if (kernel.Length == 0)
        {
            throw new ArgumentException("Cannot blur with an empty kernel.");
        }

        var radius = (kernel.Length - 1) / 2;
        temp ??= new float[height, width];

        // horizontal pass
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                float weightSum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int px = x + k;
                    if (px >= 0 && px < width)
                    {
                        float w = kernel[k + radius];
                        sum += data[y, px] * w;
                        weightSum += w;
                    }
                }

                temp[y, x] = sum / weightSum;
            }
        });

        // vertical pass
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                float weightSum = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int py = y + k;
                    if (py >= 0 && py < height)
                    {
                        float w = kernel[k + radius];
                        sum += temp[py, x] * w;
                        weightSum += w;
                    }
                }

                data[y, x] = (byte)Mathf.Clamp(Mathf.RoundToInt(sum / weightSum), 0, 255);
            }
        });
    }

    public static float[] CreateKernel(float sigma, int radius = -1)
    {
        if (radius == -1)
        {
            radius = Mathf.CeilToInt(3 * sigma);
        }

        var kernel = new float[radius * 2 + 1];
        var s2 = 2 * sigma * sigma;
        var sum = 0f;

        for (int i = -radius; i <= radius; i++)
        {
            var v = Mathf.Exp(-(i * i) / s2);
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
