namespace RESPite.Transports;

/// <summary>
/// Marker interface for multiplexed connections supporting synchronous and asynchronous access.
/// </summary>
public interface IMultiplexedTransport : IAsyncMultiplexedTransport, ISyncMultiplexedTransport, IMessageTransport
{
} // diamond

/// <summary>
/// Marker interface for asynchronous multiplexed connections.
/// </summary>
public interface IAsyncMultiplexedTransport : IAsyncMessageTransport
{
}

/// <summary>
/// Marker interface for synchronous multiplexed connections.
/// </summary>
public interface ISyncMultiplexedTransport : ISyncMessageTransport
{
}

/// <summary>
/// Base marker interface for multiplexed connections.
/// </summary>
public interface IMultiplexedBase : IMessageTransportBase
{
}
