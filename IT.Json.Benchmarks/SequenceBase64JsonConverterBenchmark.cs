﻿using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using IT.Buffers;
using IT.Buffers.Pool;
using IT.Json.Converters;
using System.Buffers;
using System.Text.Json;

namespace IT.Json.Benchmarks;

[MemoryDiagnoser]
[MinColumn, MaxColumn]
[Orderer(SummaryOrderPolicy.FastestToSlowest, MethodOrderPolicy.Declared)]
public class SequenceBase64JsonConverterBenchmark
{
    private static byte[] _data = null!;
    private static ReadOnlySequence<byte> _dataBase64;
    private static JsonSerializerOptions _jso = null!;
    private static ReadOnlySequenceBuilder<byte> _sequenceBuilder = null!;

    [Params(100)]
    public int Length { get; set; } = 100;

    [Params(1, 10)]
    public int Segments { get; set; } = 10;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _jso = new JsonSerializerOptions();
        _jso.Converters.Add(new MemoryByteJsonConverter());
        _jso.Converters.Add(new ReadOnlyMemoryByteJsonConverter());
        _jso.Converters.Add(new ArrayByteJsonConverter());
        _jso.Converters.Add(new MemoryOwnerByteJsonConverter());

        _data = new byte[Length];
        Random.Shared.NextBytes(_data);

        var dataBase64 = JsonSerializer.SerializeToUtf8Bytes(_data);

        _sequenceBuilder = ReadOnlySequenceBuilderPool<byte>.Rent(Segments);
        _dataBase64 = _sequenceBuilder.Add(dataBase64, Segments).Build();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        ReadOnlySequenceBuilderPool<byte>.Return(_sequenceBuilder);
    }

    [Benchmark]
    public Memory<byte> Deserialize_Memory_Default() => Deserialize<Memory<byte>>(_dataBase64);

    [Benchmark]
    public Memory<byte> Deserialize_Memory_IT() => Deserialize<Memory<byte>>(_dataBase64, _jso);

    [Benchmark]
    public byte[] Deserialize_Array_Default() => Deserialize<byte[]>(_dataBase64)!;

    [Benchmark]
    public byte[] Deserialize_Array_IT() => Deserialize<byte[]>(_dataBase64, _jso)!;

    [Benchmark]
    public void Deserialize_Owner_IT()
    {
        using var owner = Deserialize<IMemoryOwner<byte>>(_dataBase64, _jso)!;
        if (!owner.Memory.Span.SequenceEqual(_data)) throw new InvalidOperationException(nameof(Deserialize_Owner_IT));
    }

    public void Test()
    {
        GlobalSetup();

        if (!Deserialize_Memory_Default().Span.SequenceEqual(_data)) throw new InvalidOperationException(nameof(Deserialize_Memory_Default));
        if (!Deserialize_Memory_IT().Span.SequenceEqual(_data)) throw new InvalidOperationException(nameof(Deserialize_Memory_IT));

        if (!Deserialize_Array_Default().SequenceEqual(_data)) throw new InvalidOperationException(nameof(Deserialize_Array_Default));
        if (!Deserialize_Array_IT().SequenceEqual(_data)) throw new InvalidOperationException(nameof(Deserialize_Array_IT));

        Deserialize_Owner_IT();
    }

    private static TValue? Deserialize<TValue>(in ReadOnlySequence<byte> utf8Json, JsonSerializerOptions? options = null)
    {
        var utf8Reader = new Utf8JsonReader(utf8Json);
        return JsonSerializer.Deserialize<TValue>(ref utf8Reader, options);
    }
}