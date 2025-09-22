using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Godot;

namespace Ritgard;

public sealed partial class StructureBuffer : IDictionary<Vector3I, uint>
{
    public uint this[Vector3I key]
    {
        get => ((IDictionary<Vector3I, uint>)data)[key];
        set => ((IDictionary<Vector3I, uint>)data)[key] = value;
    }

    public ICollection<Vector3I> Keys => ((IDictionary<Vector3I, uint>)data).Keys;

    public ICollection<uint> Values => ((IDictionary<Vector3I, uint>)data).Values;

    public int Count => ((ICollection<KeyValuePair<Vector3I, uint>>)data).Count;

    public bool IsReadOnly => ((ICollection<KeyValuePair<Vector3I, uint>>)data).IsReadOnly;

    public void Add(Vector3I key, uint value)
    {
        ((IDictionary<Vector3I, uint>)data).Add(key, value);
    }

    public void Add(KeyValuePair<Vector3I, uint> item)
    {
        ((ICollection<KeyValuePair<Vector3I, uint>>)data).Add(item);
    }

    public void Clear()
    {
        ((ICollection<KeyValuePair<Vector3I, uint>>)data).Clear();
    }

    public bool Contains(KeyValuePair<Vector3I, uint> item)
    {
        return ((ICollection<KeyValuePair<Vector3I, uint>>)data).Contains(item);
    }

    public bool ContainsKey(Vector3I key)
    {
        return ((IDictionary<Vector3I, uint>)data).ContainsKey(key);
    }

    public void CopyTo(KeyValuePair<Vector3I, uint>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<Vector3I, uint>>)data).CopyTo(array, arrayIndex);
    }

    public IEnumerator<KeyValuePair<Vector3I, uint>> GetEnumerator()
    {
        return ((IEnumerable<KeyValuePair<Vector3I, uint>>)data).GetEnumerator();
    }

    public bool Remove(Vector3I key)
    {
        return ((IDictionary<Vector3I, uint>)data).Remove(key);
    }

    public bool Remove(KeyValuePair<Vector3I, uint> item)
    {
        return ((ICollection<KeyValuePair<Vector3I, uint>>)data).Remove(item);
    }

    public bool TryGetValue(Vector3I key, [MaybeNullWhen(false)] out uint value)
    {
        return ((IDictionary<Vector3I, uint>)data).TryGetValue(key, out value);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)data).GetEnumerator();
    }
}
