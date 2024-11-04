﻿using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IT.Json.Converters;

public class StrictEnumJsonConverterFactory : JsonConverterFactory
{
    private readonly JsonNamingPolicy? _namingPolicy;

    public StrictEnumJsonConverterFactory(JsonNamingPolicy? namingPolicy)
    {
        _namingPolicy = namingPolicy;
    }

    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (!typeToConvert.IsEnum) throw new ArgumentOutOfRangeException(nameof(typeToConvert), typeToConvert, "type not supported");

        var type = typeToConvert.GetCustomAttribute<FlagsAttribute>() != null
            ? typeof(StrictEnumFlagsJsonConverter<>)
            : typeof(StrictEnumJsonConverter<>);

        return (JsonConverter?)Activator.CreateInstance(type.MakeGenericType(typeToConvert), _namingPolicy);
    }
}