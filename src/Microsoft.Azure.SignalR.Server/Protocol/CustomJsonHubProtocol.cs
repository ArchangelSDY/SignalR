// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.SignalR
{
    public class CustomJsonHubProtocol : IHubProtocol
    {
        private const string ResultPropertyName = "result";
        private const string ItemPropertyName = "item";
        private const string InvocationIdPropertyName = "invocationId";
        private const string TypePropertyName = "type";
        private const string ErrorPropertyName = "error";
        private const string TargetPropertyName = "target";
        private const string ArgumentsPropertyName = "arguments";
        private const string PayloadPropertyName = "payload";
        private const string MetadataPropertyName = "meta";

        public static readonly string ProtocolName = "json";

        // ONLY to be used for application payloads (args, return values, etc.)
        public JsonSerializer PayloadSerializer { get; }

        public CustomJsonHubProtocol() : this(Options.Create(new JsonHubProtocolOptions()))
        {
        }

        public CustomJsonHubProtocol(IOptions<JsonHubProtocolOptions> options)
        {
            PayloadSerializer = JsonSerializer.Create(options.Value.PayloadSerializerSettings);
        }

        public string Name => ProtocolName;

        public ProtocolType Type => ProtocolType.Text;

        public bool TryParseMessages(ReadOnlySpan<byte> input, IInvocationBinder binder, out IList<HubMessage> messages)
        {
            messages = new List<HubMessage>();

            while (TextMessageParser.TryParseMessage(ref input, out var payload))
            {
                // TODO: Need a span-native JSON parser!
                using (var memoryStream = new MemoryStream(payload.ToArray()))
                {
                    messages.Add(ParseMessage(memoryStream));
                }
            }

            return messages.Count > 0;
        }

        public void WriteMessage(HubMessage message, Stream output)
        {
            using (var memoryStream = new MemoryStream())
            {
                WriteMessageCore(message, memoryStream);
                memoryStream.Flush();

                TextMessageFormatter.WriteMessage(memoryStream.ToArray(), output);
            }
        }

        private HubMessage ParseMessage(Stream input)
        {
            using (var reader = new JsonTextReader(new StreamReader(input)))
            {
                try
                {
                    // PERF: Could probably use the JsonTextReader directly for better perf and fewer allocations
                    var token = JToken.ReadFrom(reader);

                    if (token == null || token.Type != JTokenType.Object)
                    {
                        throw new InvalidDataException($"Unexpected JSON Token Type '{token?.Type}'. Expected a JSON Object.");
                    }

                    var json = (JObject)token;

                    // Determine the type of the message
                    var type = JsonUtils.GetRequiredProperty<int>(json, TypePropertyName, JTokenType.Integer);
                    switch (type)
                    {
                        case HubProtocolConstants.InvocationMessageType:
                            return BindInvocationMessage(json);
                        case HubProtocolConstants.StreamInvocationMessageType:
                            return BindStreamInvocationMessage(json);
                        case HubProtocolConstants.StreamItemMessageType:
                            return BindStreamItemMessage(json);
                        case HubProtocolConstants.CompletionMessageType:
                            return BindCompletionMessage(json);
                        case HubProtocolConstants.CancelInvocationMessageType:
                            return BindCancelInvocationMessage(json);
                        case HubProtocolConstants.PingMessageType:
                            return PingMessage.Instance;
                        default:
                            throw new InvalidDataException($"Unknown message type: {type}");
                    }
                }
                catch (JsonReaderException jrex)
                {
                    throw new InvalidDataException("Error reading JSON.", jrex);
                }
            }
        }

        private void WriteMessageCore(HubMessage message, Stream stream)
        {
            using (var writer = new JsonTextWriter(new StreamWriter(stream)))
            {
                switch (message)
                {
                    case InvocationMessage m:
                        WriteInvocationMessage(m, writer);
                        break;
                    case StreamInvocationMessage m:
                        WriteStreamInvocationMessage(m, writer);
                        break;
                    case StreamItemMessage m:
                        WriteStreamItemMessage(m, writer);
                        break;
                    case CompletionMessage m:
                        WriteCompletionMessage(m, writer);
                        break;
                    case CancelInvocationMessage m:
                        WriteCancelInvocationMessage(m, writer);
                        break;
                    case PingMessage m:
                        WritePingMessage(m, writer);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported message type: {message.GetType().FullName}");
                }
            }
        }

        private void WriteCompletionMessage(CompletionMessage message, JsonTextWriter writer)
        {
            writer.WriteStartObject();
            WriteHubInvocationMessageCommon(message, writer, HubProtocolConstants.CompletionMessageType);
            if (!string.IsNullOrEmpty(message.Error))
            {
                writer.WritePropertyName(ErrorPropertyName);
                writer.WriteValue(message.Error);
            }
            else if (message.HasResult)
            {
                writer.WritePropertyName(ResultPropertyName);
                PayloadSerializer.Serialize(writer, message.Result);
            }
            writer.WriteEndObject();
        }

        private void WriteCancelInvocationMessage(CancelInvocationMessage message, JsonTextWriter writer)
        {
            writer.WriteStartObject();
            WriteHubInvocationMessageCommon(message, writer, HubProtocolConstants.CancelInvocationMessageType);
            writer.WriteEndObject();
        }

        private void WriteStreamItemMessage(StreamItemMessage message, JsonTextWriter writer)
        {
            writer.WriteStartObject();
            WriteHubInvocationMessageCommon(message, writer, HubProtocolConstants.StreamItemMessageType);
            writer.WritePropertyName(ItemPropertyName);
            PayloadSerializer.Serialize(writer, message.Item);
            writer.WriteEndObject();
        }

        private void WriteInvocationMessage(InvocationMessage message, JsonTextWriter writer)
        {
            writer.WriteStartObject();
            WriteHubInvocationMessageCommon(message, writer, HubProtocolConstants.InvocationMessageType);
            writer.WritePropertyName(TargetPropertyName);
            writer.WriteValue(message.Target);

            WriteArguments(message.Arguments, writer);

            writer.WriteEndObject();
        }

        private void WriteStreamInvocationMessage(StreamInvocationMessage message, JsonTextWriter writer)
        {
            writer.WriteStartObject();
            WriteHubInvocationMessageCommon(message, writer, HubProtocolConstants.StreamInvocationMessageType);
            writer.WritePropertyName(TargetPropertyName);
            writer.WriteValue(message.Target);

            WriteArguments(message.Arguments, writer);

            writer.WriteEndObject();
        }

        private void WriteArguments(object[] arguments, JsonTextWriter writer)
        {
            writer.WritePropertyName(ArgumentsPropertyName);
            writer.WriteStartArray();
            foreach (var argument in arguments)
            {
                PayloadSerializer.Serialize(writer, argument);
            }
            writer.WriteEndArray();
        }

        private void WritePingMessage(PingMessage m, JsonTextWriter writer)
        {
            writer.WriteStartObject();
            WriteMessageType(writer, HubProtocolConstants.PingMessageType);
            writer.WriteEndObject();
        }

        private static void WriteHubInvocationMessageCommon(HubInvocationMessage message, JsonTextWriter writer, int type)
        {
            if (!string.IsNullOrEmpty(message.InvocationId))
            {
                writer.WritePropertyName(InvocationIdPropertyName);
                writer.WriteValue(message.InvocationId);
            }
            WriteMessageType(writer, type);
            WriteMessageMetadata(writer, message.Metadata);
        }

        private static void WriteMessageType(JsonTextWriter writer, int type)
        {
            writer.WritePropertyName(TypePropertyName);
            writer.WriteValue(type);
        }

        private static void WriteMessageMetadata(JsonTextWriter writer, IDictionary<string, string> metadata)
        {
            if (metadata == null || !metadata.Any()) return;
            writer.WritePropertyName(MetadataPropertyName);
            writer.WriteStartObject();
            foreach (var kvp in metadata)
            {
                writer.WritePropertyName(kvp.Key);
                writer.WriteValue(kvp.Value);
            }
            writer.WriteEndObject();
        }

        private InvocationMessage BindInvocationMessage(JObject json)
        {
            var invocationId = JsonUtils.GetOptionalProperty<string>(json, InvocationIdPropertyName, JTokenType.String);
            var metadata = JsonUtils.GetOptionalMetadataDictionary(json, MetadataPropertyName);

            var target = JsonUtils.GetRequiredProperty<string>(json, TargetPropertyName, JTokenType.String);
            var args = JsonUtils.GetRequiredProperty<JArray>(json, ArgumentsPropertyName, JTokenType.Array);

            try
            {
                return new InvocationMessage(invocationId, metadata, target, arguments: args.Select(t => (object)t).ToArray());
            }
            catch (Exception ex)
            {
                return new InvocationMessage(invocationId, metadata, target, ExceptionDispatchInfo.Capture(ex));
            }
        }

        private StreamInvocationMessage BindStreamInvocationMessage(JObject json)
        {
            var invocationId = JsonUtils.GetRequiredProperty<string>(json, InvocationIdPropertyName, JTokenType.String);
            var metadata = JsonUtils.GetOptionalMetadataDictionary(json, MetadataPropertyName);

            var target = JsonUtils.GetRequiredProperty<string>(json, TargetPropertyName, JTokenType.String);
            var args = JsonUtils.GetRequiredProperty<JArray>(json, ArgumentsPropertyName, JTokenType.Array);

            try
            {
                return new StreamInvocationMessage(invocationId, metadata, target, arguments: args.Select(t => (object)t));
            }
            catch (Exception ex)
            {
                return new StreamInvocationMessage(invocationId, metadata, target, ExceptionDispatchInfo.Capture(ex));
            }
        }

        private StreamItemMessage BindStreamItemMessage(JObject json)
        {
            var invocationId = JsonUtils.GetRequiredProperty<string>(json, InvocationIdPropertyName, JTokenType.String);
            var metadata = JsonUtils.GetOptionalMetadataDictionary(json, MetadataPropertyName);
            var result = JsonUtils.GetRequiredProperty<JToken>(json, ItemPropertyName);

            return new StreamItemMessage(invocationId, result, metadata);
        }

        private CompletionMessage BindCompletionMessage(JObject json)
        {
            var invocationId = JsonUtils.GetRequiredProperty<string>(json, InvocationIdPropertyName, JTokenType.String);
            var metadata = JsonUtils.GetOptionalMetadataDictionary(json, MetadataPropertyName);
            var error = JsonUtils.GetOptionalProperty<string>(json, ErrorPropertyName, JTokenType.String);
            var resultProp = json.Property(ResultPropertyName);

            if (error != null && resultProp != null)
            {
                throw new InvalidDataException("The 'error' and 'result' properties are mutually exclusive.");
            }

            return new CompletionMessage(invocationId, metadata, error, result: resultProp?.Value, hasResult: resultProp != null);
        }

        private CancelInvocationMessage BindCancelInvocationMessage(JObject json)
        {
            var invocationId = JsonUtils.GetRequiredProperty<string>(json, InvocationIdPropertyName, JTokenType.String);
            var metadata = JsonUtils.GetOptionalMetadataDictionary(json, MetadataPropertyName);
            return new CancelInvocationMessage(invocationId, metadata);
        }
    }
}
