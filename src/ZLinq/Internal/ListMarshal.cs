using System.Buffers;
using System.Collections;
using System.Diagnostics.CodeAnalysis;

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

#if !NET8_0_OR_GREATER

    // netstandard cannot copy the elements of List<T> into Span<T> in bulk,
    // so ToArray/ToList/CopyTo detect List<T> and array sources and use the bulk copy of the public API instead
    // (List<T>.ToArray, List<T>(IEnumerable<T>), List<T>.AddRange).
    // TEnumerator is a value type, so the JIT evaluates the typeof comparison as a constant and removes the unused branch.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetListSource<TEnumerator, T>(in TEnumerator enumerator, [NotNullWhen(true)] out List<T>? list)
        where TEnumerator : struct, IValueEnumerator<T>
    {
        if (typeof(TEnumerator) == typeof(FromList<T>))
        {
            list = Unsafe.As<TEnumerator, FromList<T>>(ref Unsafe.AsRef(in enumerator)).GetSource();
            return true;
        }

        list = null;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetArraySource<TEnumerator, T>(in TEnumerator enumerator, [NotNullWhen(true)] out T[]? array)
        where TEnumerator : struct, IValueEnumerator<T>
    {
        if (typeof(TEnumerator) == typeof(FromArray<T>))
        {
            array = Unsafe.As<TEnumerator, FromArray<T>>(ref Unsafe.AsRef(in enumerator)).GetSource();
            return true;
        }

        array = null;
        return false;
    }

#endif
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
    FillCollection<T>? buffer;

    public ListFiller(List<T> list, int count)
    {
        this.list = list;
        buffer = FillCollection<T>.Rent(count);
    }

    public readonly Span<T> Span
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => buffer!.Span;
    }

    public readonly void Commit()
    {
        list.AddRange(buffer!);
    }

    public void Dispose()
    {
        buffer?.Return();
        buffer = null;
    }

#endif
}

#if !NET8_0_OR_GREATER

// Read-only ICollection<T> over a temporary buffer, filled through Span and then passed to List<T>.AddRange.
// AddRange copies an ICollection<T> through CopyTo in the known implementations, which is a single Array.Copy here.
// Even if an implementation enumerates it instead, the result is still correct; only the performance differs.
internal sealed class FillCollection<T> : ICollection<T>
{
    // Buffers up to this length are kept by the cached instance, so that small fills need no ArrayPool round trip.
    // Larger buffers are rented from ArrayPool.
    const int MaxRetainedLength = 256;

    [ThreadStatic]
    static FillCollection<T>? cache;

    T[] retained = [];
    T[] array = [];
    int count;

    public static FillCollection<T> Rent(int count)
    {
        // Take the cached instance out while in use, so that a nested fill (e.g. ToList in a selector) never shares it.
        var collection = cache ?? new FillCollection<T>();
        cache = null;

        if (count <= MaxRetainedLength)
        {
            if (collection.retained.Length < count)
            {
                var length = Math.Max(16, collection.retained.Length * 2);
                while (length < count)
                {
                    length *= 2;
                }
                collection.retained = new T[length];
            }
            collection.array = collection.retained;
        }
        else
        {
            collection.array = ArrayPool<T>.Shared.Rent(count);
        }

        collection.count = count;
        return collection;
    }

    public Span<T> Span
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => array.AsSpan(0, count);
    }

    public void Return()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Array.Clear(array, 0, count);
        }

        if (!ReferenceEquals(array, retained))
        {
            ArrayPool<T>.Shared.Return(array);
        }

        array = [];
        count = 0;
        cache = this;
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
