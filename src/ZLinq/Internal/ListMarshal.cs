using System.Buffers;
using System.Collections;

namespace ZLinq.Internal;

// Accesses the elements of List<T> for the operators specialized for List<T>.
// .NET 8 or later uses System.Runtime.InteropServices.CollectionsMarshal.
// netstandard has no public API to access the backing array of List<T>, so it goes through the public List<T> API only.
// Do not rely on the private field layout of List<T> here; it differs between runtimes (e.g. Unity's Mono/IL2CPP).
internal static class ListMarshal
{
#if NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlySpan<T> GetElements<T>(List<T> list) => CollectionsMarshal.AsSpan(list);
#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ListElements<T> GetElements<T>(List<T> list) => new(list);
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetSpan<T>(List<T> list, out ReadOnlySpan<T> span)
    {
#if NET8_0_OR_GREATER
        span = CollectionsMarshal.AsSpan(list);
        return true;
#else
        span = default;
        return false;
#endif
    }

    public static bool TryCopyTo<T>(List<T> list, Span<T> destination, Index offset)
    {
#if NET8_0_OR_GREATER
        if (EnumeratorHelper.TryGetSlice<T>(CollectionsMarshal.AsSpan(list), offset, destination.Length, out var slice))
        {
            slice.CopyTo(destination);
            return true;
        }
        return false;
#else
        if (EnumeratorHelper.TryGetSliceRange(list.Count, offset, destination.Length, out var start, out var count))
        {
            for (var i = 0; i < count; i++)
            {
                destination[i] = list[start + i];
            }
            return true;
        }
        return false;
#endif
    }
}

#if !NET8_0_OR_GREATER

// Exposes the same members as ReadOnlySpan<T> that the List<T> specialized operators use (Length and indexer),
// so the operators can be written once for both ReadOnlySpan<T> (.NET 8 or later) and this type (netstandard).
internal readonly struct ListElements<T>(List<T> list)
{
    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => list.Count;
    }

    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => list[index];
    }
}

#endif

// Fills an empty List<T> with exactly `count` elements through Span<T>.
// Usage: create, write all elements to Span, call Commit, then Dispose (use `using`).
// If Commit is not called (e.g. an exception is thrown while filling), the list is left empty on netstandard.
internal ref struct ListFiller<T>
{
#if NET8_0_OR_GREATER

    readonly Span<T> span;

    public ListFiller(List<T> list, int count)
    {
        CollectionsMarshal.SetCount(list, count);
        span = CollectionsMarshal.AsSpan(list);
    }

    public readonly Span<T> Span
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => span;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Commit()
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
    }

#else

    readonly List<T> list;
    readonly int count;
    T[] buffer;

    public ListFiller(List<T> list, int count)
    {
        this.list = list;
        this.count = count;
        buffer = count == 0 ? [] : ArrayPool<T>.Shared.Rent(count);
    }

    public readonly Span<T> Span
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => buffer.AsSpan(0, count);
    }

    public readonly void Commit()
    {
        ArrayPrefixCollection<T>.AddRange(list, buffer, count);
    }

    public void Dispose()
    {
        if (buffer.Length != 0)
        {
            ArrayPool<T>.Shared.Return(buffer, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
        }
        buffer = [];
    }

#endif
}

#if !NET8_0_OR_GREATER

// Read-only ICollection<T> over the first `count` elements of an array, passed to List<T>.AddRange.
// AddRange copies an ICollection<T> through CopyTo in the known implementations, which is a single Array.Copy here.
// Even if an implementation enumerates it instead, the result is still correct; only the performance differs.
internal sealed class ArrayPrefixCollection<T> : ICollection<T>
{
    [ThreadStatic]
    static ArrayPrefixCollection<T>? cache;

    T[] array = [];
    int count;

    public static void AddRange(List<T> list, T[] array, int count)
    {
        // Take the cached instance out while in use, so that a nested call never shares it.
        var collection = cache ?? new ArrayPrefixCollection<T>();
        cache = null;

        collection.array = array;
        collection.count = count;
        try
        {
            list.AddRange(collection);
        }
        finally
        {
            collection.array = [];
            collection.count = 0;
            cache = collection;
        }
    }

    public int Count => count;

    public bool IsReadOnly => true;

    public void CopyTo(T[] destination, int arrayIndex)
    {
        Array.Copy(array, 0, destination, arrayIndex, count);
    }

    public bool Contains(T item)
    {
        return Array.IndexOf(array, item, 0, count) >= 0;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < count; i++)
        {
            yield return array[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Add(T item) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public bool Remove(T item) => throw new NotSupportedException();
}

#endif
