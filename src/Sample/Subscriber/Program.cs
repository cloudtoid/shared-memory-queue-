using Cloudtoid.Interprocess;

namespace Subscriber;

internal static partial class Program
{
    internal static void Main()
    {
        // Set up an optional logger factory to redirect the traces to he console

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("Subscriber");

        // Create the queue factory. If you are not interested in tracing the internals of
        // the queue then don't pass in a loggerFactory

        var factory = new QueueFactory(loggerFactory);

        // Create a message queue publisher

        var options = new QueueOptions(
            queueName: "sample-queue",
            capacity: 1024 * 1024);

        using var subscriber = factory.CreateSubscriber(options);

        // Dequeue messages
        var messageBuffer = new byte[1];

        while (true)
        {
            if (subscriber.TryDequeue(messageBuffer, default, out var message))
                LogDequeue(logger, messageBuffer[0]);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Dequeue #{i}")]
    private static partial void LogDequeue(ILogger logger, int i);
}