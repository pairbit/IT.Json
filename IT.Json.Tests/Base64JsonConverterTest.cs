﻿using IT.Json.Converters;
using System;
using System.Text.Json;

namespace IT.Json.Tests;

internal class Base64JsonConverterTest
{
    public class Entity
    {
        public int Id { get; set; }

        public ArraySegment<byte> Data { get; set; }
    }

    [Test]
    public void Test()
    {
        var array = new byte[1000];
        Random.Shared.NextBytes(array);
        var entity = new Entity() { Id = 1, Data = new ArraySegment<byte>(array) };

        var jso = new JsonSerializerOptions();
        jso.Converters.Add(new Base64JsonConverterFactory());
        
        var bin = JsonSerializer.SerializeToUtf8Bytes(entity, jso);
        var str = System.Text.Encoding.UTF8.GetString(bin);

        var entityCopy = JsonSerializer.Deserialize<Entity>(bin, jso)!;

        Assert.That(entity.Data.AsSpan().SequenceEqual(entityCopy.Data.AsSpan()));
    }

    //[Test]
    //public async Task TestAsync()
    //{
    //    await JsonSerializer.DeserializeAsync(inputStream, context.ModelType, SerializerOptions);
    //}
}