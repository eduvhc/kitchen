// The same request, notification and pass-through behavior written once per library. Handlers return cached
// responses so the numbers are the mediators' own cost, not the handlers'.

namespace MediatingKit.Benchmarks.Kit
{
    public sealed record Ping(int Value) : MediatingKit.IRequest<Pong>;

    public sealed record Pong(int Value);

    public sealed record Pinged(int Value) : MediatingKit.INotification;

    public sealed class PingHandler : MediatingKit.IRequestHandler<Ping, Pong>
    {
        public static readonly Pong Response = new(1);

        public ValueTask<Pong> Handle(Ping request, CancellationToken ct) => new(Response);
    }

    public sealed class FirstPingedHandler : MediatingKit.INotificationHandler<Pinged>
    {
        public ValueTask Handle(Pinged notification, CancellationToken ct) => default;
    }

    public sealed class SecondPingedHandler : MediatingKit.INotificationHandler<Pinged>
    {
        public ValueTask Handle(Pinged notification, CancellationToken ct) => default;
    }

    public sealed class PassThrough<TRequest, TResponse> : MediatingKit.IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public ValueTask<TResponse> Handle(TRequest request, MediatingKit.PipelineNext<TRequest, TResponse> next, CancellationToken ct) =>
            next(request, ct);
    }
}

namespace MediatingKit.Benchmarks.MediatR
{
    public sealed record Ping(int Value) : global::MediatR.IRequest<Pong>;

    public sealed record Pong(int Value);

    public sealed record Pinged(int Value) : global::MediatR.INotification;

    public sealed class PingHandler : global::MediatR.IRequestHandler<Ping, Pong>
    {
        private static readonly Task<Pong> Response = Task.FromResult(new Pong(1));

        public Task<Pong> Handle(Ping request, CancellationToken cancellationToken) => Response;
    }

    public sealed class FirstPingedHandler : global::MediatR.INotificationHandler<Pinged>
    {
        public Task Handle(Pinged notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class SecondPingedHandler : global::MediatR.INotificationHandler<Pinged>
    {
        public Task Handle(Pinged notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class PassThrough<TRequest, TResponse> : global::MediatR.IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        public Task<TResponse> Handle(TRequest request, global::MediatR.RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken) =>
            next(cancellationToken);
    }
}

namespace MediatingKit.Benchmarks.Mediator
{
    public sealed record Ping(int Value) : global::Mediator.IRequest<Pong>;

    public sealed record Pong(int Value);

    public sealed record Pinged(int Value) : global::Mediator.INotification;

    public sealed class PingHandler : global::Mediator.IRequestHandler<Ping, Pong>
    {
        private static readonly Pong Response = new(1);

        public ValueTask<Pong> Handle(Ping request, CancellationToken cancellationToken) => new(Response);
    }

    public sealed class FirstPingedHandler : global::Mediator.INotificationHandler<Pinged>
    {
        public ValueTask Handle(Pinged notification, CancellationToken cancellationToken) => default;
    }

    public sealed class SecondPingedHandler : global::Mediator.INotificationHandler<Pinged>
    {
        public ValueTask Handle(Pinged notification, CancellationToken cancellationToken) => default;
    }

    public sealed class PassThrough<TMessage, TResponse> : global::Mediator.IPipelineBehavior<TMessage, TResponse>
        where TMessage : notnull, global::Mediator.IMessage
    {
        public ValueTask<TResponse> Handle(TMessage message, global::Mediator.MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken) =>
            next(message, cancellationToken);
    }
}
