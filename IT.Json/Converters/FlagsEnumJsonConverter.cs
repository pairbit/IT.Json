﻿using System;
using System.Buffers;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using XXH = System.IO.Hashing.XxHash32;

namespace IT.Json.Converters;

public class FlagsEnumJsonConverter<TEnum, TNumber> : EnumJsonConverter<TEnum>
    where TEnum : unmanaged, Enum
    where TNumber : unmanaged, IBitwiseOperators<TNumber, TNumber, TNumber>, IComparisonOperators<TNumber, TNumber, bool>
{
    private const int MaxStackallocBytes = 256;

    private readonly byte[] _sep;
    private readonly int _maxLength;
    private readonly TNumber _maxNumber;
    private readonly (TNumber, byte[])[] _numberUtf8Name;
    private readonly FrozenDictionary<int, TNumber> _xxhToNumber;

    static FlagsEnumJsonConverter()
    {
        if (typeof(TNumber) != typeof(TEnum).GetEnumUnderlyingType())
            throw new ArgumentException($"UnderlyingType enum '{typeof(TEnum).FullName}' is '{typeof(TEnum).GetEnumUnderlyingType().FullName}'", nameof(TNumber));

        if (_values.Length == 1) throw new ArgumentException($"Enum '{typeof(TEnum).FullName}' must contain more than one value", nameof(TEnum));
    }

    public FlagsEnumJsonConverter(JsonNamingPolicy? namingPolicy, int seed = 0, byte[]? sep = null)
            : base(namingPolicy, seed)
    {
        if (sep == null || sep.Length == 0) sep = ", "u8.ToArray();

        //TODO: возможно определить более эффективный размер??
        var sumNameLength = 0;
        TNumber maxNumber = default;
        var values = _values;
        var numberUtf8Name = new (TNumber, byte[])[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            var key = values[i];
            TNumber number = Unsafe.As<TEnum, TNumber>(ref key);
            maxNumber |= number;

            var utf8Name = _valueToUtf8Name[key];
            sumNameLength += utf8Name.Length;

            numberUtf8Name[i] = (number, utf8Name);
        }

        var xxhToNumber = new Dictionary<int, TNumber>(values.Length);
        foreach (var pair in _xxhToValue)
        {
            var value = pair.Value;
            xxhToNumber.Add(pair.Key, Unsafe.As<TEnum, TNumber>(ref value));
        }
        _xxhToNumber = xxhToNumber.ToFrozenDictionary();
        _maxNumber = maxNumber;
        _numberUtf8Name = numberUtf8Name;
        _maxLength = sumNameLength + (sep.Length * (values.Length - 1));
        _sep = sep;
    }

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw NotString();
        if (reader.ValueIsEscaped) throw NotEscaped();
        if (reader.HasValueSequence)
        {
            var sequence = reader.ValueSequence;
            if (sequence.IsSingleSegment)
            {
                return TryReadSpan(sequence.First.Span, out var value, out var name) ? value : throw NotMapped(name ?? reader.GetString());
            }
            else
            {
                return TryReadSequence(sequence, out var value, out var name) ? value : throw NotMapped(name ?? reader.GetString());
            }
        }
        else
        {
            return TryReadSpan(reader.ValueSpan, out var value, out var name) ? value : throw NotMapped(name ?? reader.GetString());
        }
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (_valueToUtf8Name.TryGetValue(value, out var utf8Name))
        {
            writer.WriteStringValue(utf8Name);
        }
        else
        {
            TNumber numberValue = Unsafe.As<TEnum, TNumber>(ref value);

            if (numberValue > _maxNumber) throw NotMapped(value);

            bool status;
            scoped Span<byte> utf8Value;
            var length = _maxLength;

            if (length <= MaxStackallocBytes)
            {
                utf8Value = stackalloc byte[length];
                status = TryWrite(ref utf8Value, ref numberValue);
                if (status)
                {
#if DEBUG
                    System.Diagnostics.Debug.Assert(numberValue == default);
#endif
                    writer.WriteStringValue(utf8Value);
                }
            }
            else
            {
                var pool = ArrayPool<byte>.Shared;
                var rented = pool.Rent(length);
                utf8Value = rented.AsSpan(0, length);
                try
                {
                    status = TryWrite(ref utf8Value, ref numberValue);
                    if (status)
                    {
#if DEBUG
                        System.Diagnostics.Debug.Assert(numberValue == default);
#endif
                        writer.WriteStringValue(utf8Value);
                    }
                }
                finally
                {
                    pool.Return(rented);
                }
            }

            if (!status)
            {
#if DEBUG
                System.Diagnostics.Debug.Assert(numberValue != default);
#endif
                throw Unsafe.As<TEnum, TNumber>(ref value) != numberValue ? NotMappedBit(value, numberValue) : NotMapped(value);
            }
        }
    }

    private bool TryWrite(ref Span<byte> utf8Value, ref TNumber numberValue)
    {
        var length = utf8Value.Length;
        var start = length;
        var sep = _sep;
        var numberUtf8Name = _numberUtf8Name;
        for (var i = numberUtf8Name.Length - 1; i >= 0; i--)
        {
            (var number, var utf8Name) = numberUtf8Name[i];
            if (number == default) continue;

            if ((numberValue & number) == number)
            {
                if (start != length)
                {
                    start -= sep.Length;
                    sep.CopyTo(utf8Value.Slice(start));
                }

                start -= utf8Name.Length;
                utf8Name.CopyTo(utf8Value.Slice(start));

                numberValue &= ~number;
                if (numberValue == default)
                {
                    utf8Value = utf8Value.Slice(start);
                    return true;
                }
            }
        }
        utf8Value = utf8Value.Slice(start);
        return false;
    }

    private bool TryReadSpan(ReadOnlySpan<byte> span, out TEnum value, out string? name)
    {
        var index = span.IndexOf(_sep);
        if (index == -1)
        {
            name = null;
            return TryReadSpan(span, out value);
        }

        TNumber numberValue = default;
        TNumber number;
        var xxhToNumber = _xxhToNumber;
        var sep = _sep;
        var seplen = sep.Length;
        var maxNameLength = _maxNameLength;
        ReadOnlySpan<byte> utf8Name;
        do
        {
            utf8Name = span.Slice(0, index);
            if (utf8Name.Length > maxNameLength || !xxhToNumber.TryGetValue((int)XXH.HashToUInt32(utf8Name), out number))
                goto invalid;

            numberValue |= number;

            span = span.Slice(index + seplen);

            index = span.IndexOf(sep);

        } while (index > -1);

        utf8Name = span;

        if (utf8Name.Length > maxNameLength || !xxhToNumber.TryGetValue((int)XXH.HashToUInt32(utf8Name), out number))
            goto invalid;

        numberValue |= number;

        value = Unsafe.As<TNumber, TEnum>(ref numberValue);
        name = null;
        return true;

    invalid:
        value = default;
        name = Encoding.UTF8.GetString(utf8Name);
        return false;
    }

    private bool TryReadSequence(ReadOnlySequence<byte> sequence, out TEnum value, out string? name)
    {
        //TODO: static cache?
        name = null;
        var xxhAlg = new XXH(_seed);
        var position = sequence.Start;
        var length = 0;
        while (sequence.TryGet(ref position, out var memory))
        {
            var span = memory.Span;

            if (span.IndexOf(_sep) > -1)
            {
                throw new NotImplementedException();
            }

            length += memory.Length;

            if (length > _maxNameLength)
            {
                value = default;
                return false;
            }

            xxhAlg.Append(span);

            if (position.GetObject() == null) break;
        }

        var xxh = (int)xxhAlg.GetCurrentHashAsUInt32();
        return _xxhToValue.TryGetValue(xxh, out value);
    }

    private static JsonException NotMappedBit(TEnum value, TNumber bit) =>
        new($"The bit {bit} JSON enum '{value}' could not be mapped to any .NET member contained in type '{typeof(TEnum).FullName}'.");
}