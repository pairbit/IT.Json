﻿using IT.Buffers;
using IT.Json.Converters;
using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IT.Json.Tests;

public class CollectionsTest
{
    public class RentedEntity : IDisposable
    {
        //[JsonConverter(typeof(RentedArraySegmentByteJsonConverter))]
        public ArraySegment<byte> Bytes { get; set; }

        //[RentedCollectionJsonConverterFactory(40)]
        public ReadOnlySequence<int> Ints { get; set; }

        public void Dispose()
        {
            //Debug.Assert(ArrayPoolShared.TryReturnAndClear(Bytes));
            Bytes = default;

            //Debug.Assert(ArrayPoolShared.TryReturnAndClear(Ints));
            Ints = default;
        }
    }

    [Test]
    public void Test()
    {
        var count = 40;
        var bytes = ArrayPool<byte>.Shared.Rent(count);
        Random.Shared.NextBytes(bytes.AsSpan(0, count));

        var ints = ArrayPool<int>.Shared.Rent(count);
        for (int i = 0; i < count; i++)
        {
            ints[i] = Random.Shared.Next(1000, short.MaxValue);
        }

        var rentedEntity = new RentedEntity()
        {
            Bytes = new ArraySegment<byte>(bytes, 0, count),
            Ints = new ReadOnlySequence<int>(ints, 0, count)
        };

        var jso = new JsonSerializerOptions();
        jso.Converters.Add(new CollectionJsonConverterFactory());

        var bin = JsonSerializer.SerializeToUtf8Bytes(rentedEntity, jso);
        var str = System.Text.Encoding.UTF8.GetString(bin);

        using var rentedEntity2 = Json.Deserialize<RentedEntity>(bin, jso)!;

        Assert.That(SequenceEqual(rentedEntity2.Bytes, bytes.AsSpan(0, count)), Is.True);
        Assert.That(SequenceEqual(rentedEntity2.Ints, ints.AsSpan(0, count)), Is.True);
    }

    private static bool SequenceEqual<T>(ArraySegment<T> first, ReadOnlySpan<T> second)
    {
        return first.AsSpan().SequenceEqual(second);
    }

    private static bool SequenceEqual<T>(ReadOnlyMemory<T> first, ReadOnlySpan<T> second)
    {
        return first.Span.SequenceEqual(second);
    }

    private static bool SequenceEqual<T>(Memory<T> first, ReadOnlySpan<T> second)
        => SequenceEqual((ReadOnlyMemory<T>)first, second);

    public static bool SequenceEqual<T>(ReadOnlySequence<T> first, ReadOnlySpan<T> other, IEqualityComparer<T>? comparer = null)
    {
        if (first.IsSingleSegment) return first.FirstSpan.SequenceEqual(other, comparer);

        if (first.Length == other.Length)
        {
            var position = first.Start;
            while (first.TryGet(ref position, out var memory))
            {
                var span = memory.Span;

                if (!span.SequenceEqual(other[..span.Length], comparer)) return false;

                other = other[..span.Length];
            }
        }

        return true;
    }
}