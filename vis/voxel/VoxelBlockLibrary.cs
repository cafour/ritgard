using System;
using System.Collections.Immutable;
using System.Linq;
using Godot;

namespace Ritgard.Voxel;

[GlobalClass]
public partial class VoxelBlockLibrary : Resource
{
    public string NoneName { get; set; } = "air";

    [Export]
    public Godot.Collections.Array<VoxelBlockType> Types { get; set; } = [];

    public ImmutableDictionary<string, byte> NameValueMap { get; private set; }
        = ImmutableDictionary<string, byte>.Empty;

    public ImmutableHashSet<Material> Materials { get; private set; } = [];

    public void Bake()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, byte>();
        builder.Add(NoneName, 0);
        if (Types.Count > 255)
        {
            throw new ArgumentOutOfRangeException(
                $"Only a maximum of 255 block types are supported. Your library has {Types.Count}."
            );
        }

        foreach (var (type, i) in Types.Select((k, i) => (k, i)))
        {
            if (string.IsNullOrWhiteSpace(type.Name))
            {
                throw new InvalidOperationException("No block type may have a null, empty, or whitespace-only name.");
            }

            builder.Add(type.Name, (byte)(i + 1));
        }

        NameValueMap = builder.ToImmutable();
        Materials = Types.Select(t => t.Material).OfType<Material>().ToImmutableHashSet();
    }

    public byte GetBlockTypeValue(string name)
    {
        if (NameValueMap.IsEmpty)
        {
            throw new InvalidOperationException("The library is empty. Did you bake it?");
        }

        if (NameValueMap.TryGetValue(name, out var value))
        {
            return value;
        }

        return 0;
    }

    public VoxelBlockType? GetBlockType(byte value)
    {
        if (value == VoxelBuffer.NoneValue)
        {
            return null;
        }

        return Types[value - 1];
    }

    public Color GetColor(byte value)
    {
        if (value == 0)
        {
            return Color.Color8(0x00, 0x00, 0x00, 0x00);
        }

        if (value > Types.Count - 2)
        {
            throw new ArgumentOutOfRangeException(
                $"Cannot get color of block type {value} because there are only {Types.Count} block types."
            );
        }

        return Types[value - 1].Color;
    }
}
