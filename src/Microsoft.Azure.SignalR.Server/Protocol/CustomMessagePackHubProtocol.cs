// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Internal;
using Microsoft.AspNetCore.SignalR.Internal.Formatters;
using Microsoft.AspNetCore.SignalR.Internal.Protocol;
using Microsoft.Extensions.Options;
using MsgPack;
using MsgPack.Serialization;

namespace Microsoft.Azure.SignalR
{
    public class CustomMessagePackHubProtocol : IHubProtocol
    {
        private const int ErrorResult = 1;
        private const int VoidResult = 2;
        private const int NonVoidResult = 3;

        public static readonly string ProtocolName = "messagepack";

        public SerializationContext SerializationContext { get; }

        public string Name => ProtocolName;

        public ProtocolType Type => ProtocolType.Binary;

        public CustomMessagePackHubProtocol()
            : this(Options.Create(new MessagePackHubProtocolOptions()))
        { }

        public CustomMessagePackHubProtocol(IOptions<MessagePackHubProtocolOptions> options)
        {
            SerializationContext = options.Value.SerializationContext;
        }

        public bool TryParseMessages(ReadOnlySpan<byte> input, IInvocationBinder binder, out IList<HubMessage> messages)
        {
            messages = new List<HubMessage>();

            while (BinaryMessageParser.TryParseMessage(ref input, out var payload))
            {
                using (var memoryStream = new MemoryStream(payload.ToArray()))
                {
                    messages.Add(ParseMessage(memoryStream, binder));
                }
            }

            return messages.Count > 0;
        }

        private static HubMessage ParseMessage(Stream input, IInvocationBinder binder)
        {
            using (var unpacker = Unpacker.Create(input))
            {
                _ = ReadArrayLength(unpacker, "elementCount");
                var messageType = ReadInt32(unpacker, "messageType");

                switch (messageType)
                {
                    case HubProtocolConstants.InvocationMessageType:
                        return CreateInvocationMessage(unpacker);
                    case HubProtocolConstants.StreamInvocationMessageType:
                        return CreateStreamInvocationMessage(unpacker);
                    case HubProtocolConstants.StreamItemMessageType:
                        return CreateStreamItemMessage(unpacker);
                    case HubProtocolConstants.CompletionMessageType:
                        return CreateCompletionMessage(unpacker);
                    case HubProtocolConstants.CancelInvocationMessageType:
                        return CreateCancelInvocationMessage(unpacker);
                    case HubProtocolConstants.PingMessageType:
                        return PingMessage.Instance;
                    default:
                        throw new FormatException($"Invalid message type: {messageType}.");
                }
            }
        }

        private static InvocationMessage CreateInvocationMessage(Unpacker unpacker)
        {
            var invocationId = ReadInvocationId(unpacker);

            // For MsgPack, we represent an empty invocation ID as an empty string,
            // so we need to normalize that to "null", which is what indicates a non-blocking invocation.
            if (string.IsNullOrEmpty(invocationId))
            {
                invocationId = null;
            }

            var metadata = ReadMetadata(unpacker);
            var target = ReadString(unpacker, "target");

            try
            {
                var arguments = BindArguments(unpacker);
                return new InvocationMessage(invocationId, metadata, target, arguments: arguments);
            }
            catch (Exception ex)
            {
                return new InvocationMessage(invocationId, metadata, target, ExceptionDispatchInfo.Capture(ex));
            }
        }

        private static StreamInvocationMessage CreateStreamInvocationMessage(Unpacker unpacker)
        {
            var invocationId = ReadInvocationId(unpacker);
            var metadata = ReadMetadata(unpacker);
            var target = ReadString(unpacker, "target");
            try
            {
                var arguments = BindArguments(unpacker);
                return new StreamInvocationMessage(invocationId, metadata, target, arguments: arguments);
            }
            catch (Exception ex)
            {
                return new StreamInvocationMessage(invocationId, metadata, target, ExceptionDispatchInfo.Capture(ex));
            }
        }

        private static object[] BindArguments(Unpacker unpacker)
        {
            var argumentCount = ReadArrayLength(unpacker, "arguments");

            try
            {
                var arguments = new object[argumentCount];
                for (var i = 0; i < argumentCount; i++)
                {
                    arguments[i] = DeserializeObject(unpacker, typeof(object), "argument");
                }

                return arguments;
            }
            catch (Exception ex)
            {
                throw new FormatException("Error binding arguments. Make sure that the types of the provided values match the types of the hub method being invoked.", ex);
            }
        }

        private static StreamItemMessage CreateStreamItemMessage(Unpacker unpacker)
        {
            var invocationId = ReadInvocationId(unpacker);
            var metadata = ReadMetadata(unpacker);
            var value = DeserializeObject(unpacker, typeof(object), "item");
            return new StreamItemMessage(invocationId, value, metadata);
        }

        private static CompletionMessage CreateCompletionMessage(Unpacker unpacker)
        {
            var invocationId = ReadInvocationId(unpacker);
            var metadata = ReadMetadata(unpacker);
            var resultKind = ReadInt32(unpacker, "resultKind");

            string error = null;
            object result = null;
            var hasResult = false;

            switch (resultKind)
            {
                case ErrorResult:
                    error = ReadString(unpacker, "error");
                    break;
                case NonVoidResult:
                    result = DeserializeObject(unpacker, typeof(object), "argument");
                    hasResult = true;
                    break;
                case VoidResult:
                    hasResult = false;
                    break;
                default:
                    throw new FormatException("Invalid invocation result kind.");
            }

            return new CompletionMessage(invocationId, metadata, error, result, hasResult);
        }

        private static CancelInvocationMessage CreateCancelInvocationMessage(Unpacker unpacker)
        {
            var invocationId = ReadInvocationId(unpacker);
            var metadata = ReadMetadata(unpacker);
            return new CancelInvocationMessage(invocationId, metadata);
        }

        public void WriteMessage(HubMessage message, Stream output)
        {
            using (var memoryStream = new MemoryStream())
            {
                WriteMessageCore(message, memoryStream);
                BinaryMessageFormatter.WriteMessage(new ReadOnlySpan<byte>(memoryStream.ToArray()), output);
            }
        }

        private void WriteMessageCore(HubMessage message, Stream output)
        {
            // PackerCompatibilityOptions.None prevents from serializing byte[] as strings
            // and allows extended objects
            var packer = Packer.Create(output, PackerCompatibilityOptions.None);
            switch (message)
            {
                case InvocationMessage invocationMessage:
                    WriteInvocationMessage(invocationMessage, packer);
                    break;
                case StreamInvocationMessage streamInvocationMessage:
                    WriteStreamInvocationMessage(streamInvocationMessage, packer);
                    break;
                case StreamItemMessage streamItemMessage:
                    WriteStreamingItemMessage(streamItemMessage, packer);
                    break;
                case CompletionMessage completionMessage:
                    WriteCompletionMessage(completionMessage, packer);
                    break;
                case CancelInvocationMessage cancelInvocationMessage:
                    WriteCancelInvocationMessage(cancelInvocationMessage, packer);
                    break;
                case PingMessage pingMessage:
                    WritePingMessage(pingMessage, packer);
                    break;
                default:
                    throw new FormatException($"Unexpected message type: {message.GetType().Name}");
            }
        }

        private void WriteInvocationMessage(InvocationMessage invocationMessage, Packer packer)
        {
            packer.PackArrayHeader(5);
            packer.Pack(HubProtocolConstants.InvocationMessageType);
            if (string.IsNullOrEmpty(invocationMessage.InvocationId))
            {
                packer.PackNull();
            }
            else
            {
                packer.PackString(invocationMessage.InvocationId);
            }
            packer.PackDictionary(invocationMessage.Metadata, SerializationContext);
            packer.PackString(invocationMessage.Target);
            packer.PackObject(invocationMessage.Arguments, SerializationContext);
        }

        private void WriteStreamInvocationMessage(StreamInvocationMessage streamInvocationMessage, Packer packer)
        {
            packer.PackArrayHeader(5);
            packer.Pack(HubProtocolConstants.StreamInvocationMessageType);
            packer.PackString(streamInvocationMessage.InvocationId);
            packer.PackDictionary(streamInvocationMessage.Metadata, SerializationContext);
            packer.PackString(streamInvocationMessage.Target);
            packer.PackObject(streamInvocationMessage.Arguments, SerializationContext);
        }

        private void WriteStreamingItemMessage(StreamItemMessage streamItemMessage, Packer packer)
        {
            packer.PackArrayHeader(4);
            packer.Pack(HubProtocolConstants.StreamItemMessageType);
            packer.PackString(streamItemMessage.InvocationId);
            packer.PackObject(streamItemMessage.Metadata, SerializationContext);
            packer.PackObject(streamItemMessage.Item, SerializationContext);
        }

        private void WriteCompletionMessage(CompletionMessage completionMessage, Packer packer)
        {
            var resultKind =
                completionMessage.Error != null ? ErrorResult :
                completionMessage.HasResult ? NonVoidResult :
                VoidResult;

            packer.PackArrayHeader(4 + (resultKind != VoidResult ? 1 : 0));

            packer.Pack(HubProtocolConstants.CompletionMessageType);
            packer.PackString(completionMessage.InvocationId);
            packer.PackDictionary(completionMessage.Metadata);
            packer.Pack(resultKind);
            switch (resultKind)
            {
                case ErrorResult:
                    packer.PackString(completionMessage.Error);
                    break;
                case NonVoidResult:
                    packer.PackObject(completionMessage.Result, SerializationContext);
                    break;
            }
        }

        private void WriteCancelInvocationMessage(CancelInvocationMessage cancelInvocationMessage, Packer packer)
        {
            packer.PackArrayHeader(3);
            packer.Pack(HubProtocolConstants.CancelInvocationMessageType);
            packer.PackString(cancelInvocationMessage.InvocationId);
            packer.PackDictionary(cancelInvocationMessage.Metadata);
        }

        private void WritePingMessage(PingMessage pingMessage, Packer packer)
        {
            packer.PackArrayHeader(1);
            packer.Pack(HubProtocolConstants.PingMessageType);
        }

        private static string ReadInvocationId(Unpacker unpacker)
        {
            return ReadString(unpacker, "invocationId");
        }

        private static IDictionary<string, string> ReadMetadata(Unpacker unpacker)
        {
            Exception msgPackException = null;
            try
            {
                return unpacker.Unpack<IDictionary<string, string>>();
            }
            catch (Exception e)
            {
                msgPackException = e;
            }

            throw new FormatException($"Reading message metadata failed.", msgPackException);
        }

        private static int ReadInt32(Unpacker unpacker, string field)
        {
            Exception msgPackException = null;
            try
            {
                if (unpacker.ReadInt32(out var value))
                {
                    return value;
                }
            }
            catch (Exception e)
            {
                msgPackException = e;
            }

            throw new FormatException($"Reading '{field}' as Int32 failed.", msgPackException);
        }

        private static string ReadString(Unpacker unpacker, string field)
        {
            Exception msgPackException = null;
            try
            {
                if (unpacker.Read())
                {
                    if (unpacker.LastReadData.IsNil)
                    {
                        return null;
                    }
                    else
                    {
                        return unpacker.LastReadData.AsString();
                    }
                }
            }
            catch (Exception e)
            {
                msgPackException = e;
            }

            throw new FormatException($"Reading '{field}' as String failed.", msgPackException);
        }

        private static long ReadArrayLength(Unpacker unpacker, string field)
        {
            Exception msgPackException = null;
            try
            {
                if (unpacker.ReadArrayLength(out var value))
                {
                    return value;
                }
            }
            catch (Exception e)
            {
                msgPackException = e;
            }

            throw new FormatException($"Reading array length for '{field}' failed.", msgPackException);
        }

        private static object DeserializeObject(Unpacker unpacker, Type type, string field)
        {
            Exception msgPackException = null;
            try
            {
                if (unpacker.Read())
                {
                    var serializer = MessagePackSerializer.Get(type);
                    return serializer.UnpackFrom(unpacker);
                }
            }
            catch (Exception ex)
            {
                msgPackException = ex;
            }

            throw new FormatException($"Deserializing object of the `{type.Name}` type for '{field}' failed.", msgPackException);
        }
    }
}