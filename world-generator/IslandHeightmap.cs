using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ritgard.WorldGenerator;

[JsonConverter(typeof(IslandHeightmapConverter))]
public readonly record struct IslandHeightmap(
    int SizeX,
    int SizeY,
    int StepCount,
    int PositionX,
    int PositionY,
    int Scale,
    byte[] RawData
)
{
    public static readonly IslandHeightmap Invalid = CreateEmpty(0, 0, 0, 0, 0, 0);

    public static IslandHeightmap CreateEmpty(
        int sizeX,
        int sizeY,
        int stepCount,
        int positionX,
        int positionY,
        int scale
    )
    {
        return new IslandHeightmap(
            sizeX,
            sizeY,
            stepCount,
            positionX,
            positionY,
            scale,
            new byte[sizeX * sizeY * stepCount]
        );
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetIndex(int x, int y, int step)
    {
        EnsureBounds(x, y, step);
        return x + y * SizeX + step * (SizeX * SizeY);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetHeight(int x, int y, int step)
    {
        return ToIntHeight(RawData[GetIndex(x, y, step)]);
    }

    public Span<byte> GetRawStepSpan(int step)
    {
        return RawData.AsSpan(SizeX * SizeY * step, SizeX * SizeY);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ToByteHeight(int height)
    {
        // NB: height 0 and 1 have special meanings; 0 is invisible deep sea; 1 is shallow sea
        var intHeight = height < 0 ? 0
            : height == 0 ? 1
            : (height + 2) / Scale;
        return (byte)Math.Clamp(intHeight, 0, 255);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ToByteHeight(float height)
    {
        var intHeight = height < 0f ? 0
            : MathF.Abs(height) < float.Epsilon ? 1
            : ((int)MathF.Round(height) + 2) / Scale;
        return (byte)Math.Clamp(intHeight, 0, 255);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ToIntHeight(byte value)
    {
        return value switch
        {
            0 => -10,
            1 => -1,
            _ => value * Scale - 2
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureBounds(int x, int y, int step)
    {
        if (x < 0 || x > SizeX)
        {
            throw new ArgumentException($"X coordinate '{x}' is invalid.");
        }

        if (y < 0 || y > SizeY)
        {
            throw new ArgumentException($"Y coordinate '{y}' is invalid.");
        }

        if (step < 0 || step > StepCount)
        {
            throw new ArgumentException($"Step '{step}' is invalid.");
        }
    }
}

public class IslandHeightmapConverter : JsonConverter<IslandHeightmap>
{
    public override IslandHeightmap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var bytes = reader.GetBytesFromBase64();
        using var memoryStream = new MemoryStream(bytes);
        using var brotliStream = new BrotliStream(memoryStream, CompressionMode.Decompress);
        brotliStream.Seek(0, SeekOrigin.Begin);
        using var binaryReader = new BinaryReader(brotliStream);
        var width = binaryReader.ReadInt32();
        var height = binaryReader.ReadInt32();
        var stepCount = binaryReader.ReadInt32();
        var posX = binaryReader.ReadInt32();
        var posY = binaryReader.ReadInt32();
        var scale = binaryReader.ReadInt32();
        var rawData = binaryReader.ReadBytes(width * height * stepCount);
        return new IslandHeightmap(width, height, stepCount, posX, posY, scale, rawData);
    }

    public override void Write(Utf8JsonWriter writer, IslandHeightmap value, JsonSerializerOptions options)
    {
        using var memoryStream = new MemoryStream();
        using var brotliStream = new BrotliStream(memoryStream, CompressionMode.Compress);
        using var binaryWriter = new BinaryWriter(brotliStream);
        binaryWriter.Write(value.SizeX);
        binaryWriter.Write(value.SizeY);
        binaryWriter.Write(value.StepCount);
        binaryWriter.Write(value.PositionX);
        binaryWriter.Write(value.PositionY);
        binaryWriter.Write(value.Scale);
        binaryWriter.Write(value.RawData);
        binaryWriter.Flush();
        var span = memoryStream.GetBuffer().AsSpan(0, (int)memoryStream.Length);
        writer.WriteBase64StringValue(span);
    }
}
