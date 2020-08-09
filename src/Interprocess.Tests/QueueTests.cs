﻿using FluentAssertions;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Cloudtoid.Interprocess.Tests
{
    public class QueueTests
    {
        private const string DefaultQueueName = "qn";
        private static readonly byte[] byteArray1 = new byte[] { 100, };
        private static readonly byte[] byteArray2 = new byte[] { 100, 110 };
        private static readonly byte[] byteArray3 = new byte[] { 100, 110, 120 };

        [Fact]
        public async Task CanEnqueueAndDequeue()
        {
            using var p = CreatePublisher(24, createOrOverride: true);
            using var s = CreateSubscriber(24);

            (await p.TryEnqueueAsync(byteArray3, default)).Should().BeTrue();
            var message = await s.DequeueAsync(default);
            message.ToArray().Should().BeEquivalentTo(byteArray3);

            (await p.TryEnqueueAsync(byteArray3, default)).Should().BeTrue();
            message = await s.DequeueAsync(default);
            message.ToArray().Should().BeEquivalentTo(byteArray3);
        }

        [Fact]
        public async Task CannotEnqueuePastCapacity()
        {
            using var p = CreatePublisher(24, createOrOverride: true);

            (await p.TryEnqueueAsync(byteArray3, default)).Should().BeTrue();
            (await p.TryEnqueueAsync(byteArray1, default)).Should().BeFalse();
        }

        [Fact]
        public async Task DisposeShouldNotThrow()
        {
            var p = CreatePublisher(24, createOrOverride: true);
            (await p.TryEnqueueAsync(byteArray3, default)).Should().BeTrue();

            using var s = CreateSubscriber(24);
            p.Dispose();

            var message = await s.DequeueAsync(default);
            message.ToArray().Should().BeEquivalentTo(byteArray3);
        }

        [Fact]
        public async Task CannotReadAfterProducerIsDisposed()
        {
            var p = CreatePublisher(24, createOrOverride: true);
            (await p.TryEnqueueAsync(byteArray3, default)).Should().BeTrue();
            using (var s = CreateSubscriber(24))
                p.Dispose();

            using (CreatePublisher(24))
            using (var s = CreateSubscriber(24))
            {
                (await s.TryDequeueAsync(default, out var message)).Should().BeFalse();
            }
        }

        private static IPublisher CreatePublisher(long capacity, bool createOrOverride = false)
            => InterprocessQueue.CreatePublisher(new QueueOptions(DefaultQueueName, Environment.CurrentDirectory, capacity, createOrOverride));

        private static ISubscriber CreateSubscriber(long capacity, bool createOrOverride = false)
            => InterprocessQueue.CreateSubscriber(new QueueOptions(DefaultQueueName, Environment.CurrentDirectory, capacity, createOrOverride));
    }
}
