﻿using Dahomey.Json.Util;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dahomey.Json.Serialization.Converters
{
    public abstract class AbstractCollectionConverter<TC, TI> : JsonConverter<TC>
        where TC : IEnumerable<TI>
    {
        private readonly JsonConverter<TI> _itemConverter;
        private readonly ReferenceHandling _referenceHandling;

        public AbstractCollectionConverter(JsonSerializerOptions options)
        {
            _itemConverter = options.GetConverter<TI>();
            _referenceHandling = typeof(TI).IsStruct() ? ReferenceHandling.Default : options.GetReferenceHandling();
        }

        protected abstract ICollection<TI> InstantiateWorkingCollection();
        protected abstract TC InstantiateCollection(ICollection<TI> workingCollection);

        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert == typeof(TC);
        }

        public override TC Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return default(TC);
            }

            using (new DepthHandler(options))
            {
                string id = null;

                if (_referenceHandling == ReferenceHandling.Preserve)
                {
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        throw new JsonException("Expected start of object");
                    }

                    reader.Read();
                    ReadOnlySpan<byte> memberName = reader.GetRawString();

                    if (memberName.SequenceEqual(ReferenceHandler.ID_MEMBER_NAME))
                    {
                        reader.Read();
                        id = reader.GetString();

                        reader.Read();
                        memberName = reader.GetRawString();
                        if (!memberName.SequenceEqual(ReferenceHandler.VALUES_MEMBER_NAME))
                        {
                            throw new JsonException("Expected $values member name");
                        }
                        reader.Read();
                    }
                    else if (memberName.SequenceEqual(ReferenceHandler.REF_MEMBER_NAME))
                    {
                        reader.Read();
                        string @ref = reader.GetString();

                        object @object = SerializationContext.Current.ReferenceHandler.ResolveReference(@ref);

                        if (@object == null)
                        {
                            throw new JsonException($"Cannot resolve reference {@ref}");
                        }

                        if (reader.TokenType != JsonTokenType.EndObject)
                        {
                            throw new JsonException("Expected end of object");
                        }

                        return InstantiateCollection((ICollection<TI>)@object);
                    }
                    else
                    {
                        throw new JsonException($"Unexpected member name {Encoding.UTF8.GetString(memberName)}");
                    }
                }

                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    throw new JsonException("Expected start of array");
                }

                ICollection<TI> workingCollection = InstantiateWorkingCollection();

                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    TI item = _itemConverter.Read(ref reader, typeof(TI), options);
                    workingCollection.Add(item);
                }

                if (_referenceHandling == ReferenceHandling.Preserve)
                {
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.EndObject)
                    {
                        throw new JsonException("Expected end of object");
                    }
                }

                TC collection = InstantiateCollection(workingCollection);

                if (!string.IsNullOrEmpty(id))
                {
                    SerializationContext.Current.ReferenceHandler.AddReference(collection, id);
                }

                return collection;
            }
        }

        public override void Write(Utf8JsonWriter writer, TC value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            using (new DepthHandler(options))
            {
                ReferenceHandler referenceResolver = null;
                if (_referenceHandling == ReferenceHandling.Ignore)
                {
                    referenceResolver = SerializationContext.Current.ReferenceHandler;
                }
                else if (_referenceHandling == ReferenceHandling.Preserve)
                {
                    writer.WriteStartObject();

                    referenceResolver = SerializationContext.Current.ReferenceHandler;
                    string reference = referenceResolver.GetReference(value, out bool firstReference);

                    if (firstReference)
                    {
                        writer.WriteString(ReferenceHandler.ID_MEMBER_NAME, reference);
                        writer.WritePropertyName(ReferenceHandler.VALUES_MEMBER_NAME);
                    }
                    else
                    {
                        writer.WriteString(ReferenceHandler.REF_MEMBER_NAME, reference);
                        writer.WriteEndObject();
                        return;
                    }
                }

                writer.WriteStartArray();

                foreach (TI item in value)
                {
                    if (_referenceHandling == ReferenceHandling.Ignore)
                    {
                        if (referenceResolver.IsReferenced(item))
                        {
                            continue;
                        }
                        else
                        {
                            referenceResolver.AddReference(item);
                        }
                    }

                    _itemConverter.Write(writer, item, options);
                }

                writer.WriteEndArray();

                if (_referenceHandling == ReferenceHandling.Preserve)
                {
                    writer.WriteEndObject();
                }
            }
        }
    }
}
