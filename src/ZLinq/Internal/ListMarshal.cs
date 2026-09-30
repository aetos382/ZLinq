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

#if !NET8_0_OR_GREATER
    // Up to this count, reading through the List<T> indexer is cheaper than renting a buffer for ListChunks<T>.
    // Measured on Unity 2022.3 and 6000.6 IL2CPP, where the break-even point is between 32 and 64 elements.
    internal const int ChunkedReadThreshold = 48;
#endif

    // Whether the operators should read the list with ListChunks<T> rather than GetElements.
    // Always true on .NET 8 or later, so the JIT removes the other branch.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool UseChunks<T>(List<T> list)
    {
#if NET8_0_OR_GREATER
        return true;
#else
        return list.Count > ChunkedReadThreshold;
#endif
    }

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
            // First/Last/ElementAt copy a single element, which must not rent a buffer.
            if (count <= ChunkedReadThreshold)
            {
                for (var i = 0; i < count; i++)
                {
                    destination[i] = list[start + i];
                }
                return true;
            }

            var written = 0;
            foreach (var chunk in new ListChunks<T>(list, start, count))
            {
                chunk.CopyTo(destination.Slice(written));
                written += chunk.Length;
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

// Reads the elements of List<T> as a sequence of ReadOnlySpan<T> chunks.
// Usage: `foreach (var chunk in new ListChunks<T>(list)) { ... }`
// On netstandard, check ListMarshal.UseChunks first; small lists are cheaper to read through the indexer.
// Use it only with foreach, which disposes it; a `using` local is read-only and would not advance.
// .NET 8 or later returns the whole backing array as a single chunk.
// netstandard copies the elements into a pooled buffer with List<T>.CopyTo chunk by chunk,
// which avoids calling the List<T> indexer for each element.
// Like a span over the backing array, the number of elements read is fixed when the reader is created.
internal ref struct ListChunks<T>
{
#if NET8_0_OR_GREATER

    readonly ReadOnlySpan<T> span;
    bool consumed;

    public ListChunks(List<T> list)
    {
        span = CollectionsMarshal.AsSpan(list);
    }

    public ListChunks(List<T> list, int start, int count)
    {
        span = CollectionsMarshal.AsSpan(list).Slice(start, count);
    }

    public readonly ReadOnlySpan<T> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => span;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (consumed)
        {
            return false;
        }

        consumed = true;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Dispose()
    {
    }

#else

    const int ChunkSize = 512;

    readonly List<T> list;
    readonly int end;
    int index;
    T[]? buffer;
    int length;
    int usedLength;

    public ListChunks(List<T> list)
        : this(list, 0, list.Count)
    {
    }

    public ListChunks(List<T> list, int start, int count)
    {
        this.list = list;
        index = start;
        end = start + count;
    }

    public readonly ReadOnlySpan<T> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new(buffer, 0, length);
    }

    public bool MoveNext()
    {
        // The list may shrink while it is being read (e.g. by a predicate); never read beyond its current count.
        var remaining = Math.Min(end, list.Count) - index;
        if (remaining <= 0)
        {
            return false;
        }

        buffer ??= ArrayPool<T>.Shared.Rent(Math.Min(remaining, ChunkSize));

        length = Math.Min(remaining, buffer.Length);
        list.CopyTo(index, buffer, 0, length);
        index += length;
        usedLength = Math.Max(usedLength, length);
        return true;
    }

    public void Dispose()
    {
        if (buffer != null)
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                Array.Clear(buffer, 0, usedLength);
            }

            ArrayPool<T>.Shared.Return(buffer);
            buffer = null;
        }
    }

#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly ListChunks<T> GetEnumerator() => this;
}

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
