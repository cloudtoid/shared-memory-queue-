﻿using Microsoft.Extensions.Logging;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess
{
    internal sealed class Subscriber : Queue, ISubscriber
    {
        private const long BeingCreated = (long)MessageState.BeingCreated;
        private const long LockedToBeConsumed = (long)MessageState.LockedToBeConsumed;
        private const long ReadyToBeConsumed = (long)MessageState.ReadyToBeConsumed;
        private readonly IInterprocessSemaphoreWaiter signal;

        internal Subscriber(QueueOptions options, ILogger logger)
            : base(options, logger)
        {
            signal = InterprocessSemaphore.CreateWaiter(identifier, logger);
        }

        public override void Dispose()
        {
            signal.Dispose();
            base.Dispose();
        }

        public ValueTask<bool> TryDequeueAsync(CancellationToken cancellation, out ReadOnlyMemory<byte> message)
            => TryDequeueAsync(default(Memory<byte>?), cancellation, out message);

        public ValueTask<bool> TryDequeueAsync(
            Memory<byte> resultBuffer,
            CancellationToken cancellation,
            out ReadOnlyMemory<byte> message)
            => TryDequeueAsync((Memory<byte>?)resultBuffer, cancellation, out message);

        public ValueTask<ReadOnlyMemory<byte>> DequeueAsync(CancellationToken cancellation)
            => DequeueAsync(default(Memory<byte>?), cancellation);

        public ValueTask<ReadOnlyMemory<byte>> DequeueAsync(Memory<byte> resultBuffer, CancellationToken cancellation)
            => DequeueAsync((Memory<byte>?)resultBuffer, cancellation);

        private unsafe ValueTask<bool> TryDequeueAsync(
            Memory<byte>? resultBuffer,
            CancellationToken cancellation,
            out ReadOnlyMemory<byte> message)
        {
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();

                var header = Header;
                var headOffset = header->HeadOffset;

                // is this is an empty queue?
                if (headOffset == header->TailOffset)
                {
                    message = ReadOnlyMemory<byte>.Empty;
                    return new ValueTask<bool>(false);
                }

                var state = (long*)buffer.GetPointer(headOffset);

                if (*state == LockedToBeConsumed)
                    continue; // some other receiver got to this message before us

                // is the message still being written/created?
                if (*state != ReadyToBeConsumed)
                    continue; // message is not ready to be consumed yet

                // take a lock so no other thread can start processing this message
                if (Interlocked.CompareExchange(ref *state, LockedToBeConsumed, ReadyToBeConsumed) != ReadyToBeConsumed)
                    continue; // some other receiver got to this message before us

                // read the message body from the queue buffer
                var bodyOffset = GetMessageBodyOffset(headOffset);
                var bodyLength = ReadMessageBodyLength(headOffset);
                message = buffer.Read(bodyOffset, bodyLength, resultBuffer);

                // zero out the entire message block
                long messageLength = GetMessageLength(bodyLength);
                buffer.ZeroBlock(headOffset, messageLength);

                // updating the queue header to point the head of the queue to the next available message 
                var newHeadOffset = SafeIncrementMessageOffset(headOffset, messageLength);
                var currentHeadOffset = (long*)header;
                Interlocked.Exchange(ref *currentHeadOffset, newHeadOffset);

                return new ValueTask<bool>(true);
            }
        }

        private async ValueTask<ReadOnlyMemory<byte>> DequeueAsync(
            Memory<byte>? resultBuffer,
            CancellationToken cancellation)
        {
            bool wait = false;

            while (true)
            {
                if (wait)
                    signal.WaitOne(millisecondsTimeout: 100);
                else
                    wait = false;

                if (await TryDequeueAsync(resultBuffer, cancellation, out var message))
                    return message;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long ReadMessageBodyLength(long messageHeaderOffset)
            => buffer.ReadInt64(messageHeaderOffset + sizeof(long));
    }
}
