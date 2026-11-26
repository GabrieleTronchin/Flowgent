namespace Flowgent.Testing;

using Yarp.ReverseProxy.Forwarder;

/// <summary>Makes the gateway forward to in-process services through a <see cref="HostRoutingHandler"/>.</summary>
public sealed class TestForwarderHttpClientFactory(HostRoutingHandler router) : IForwarderHttpClientFactory
{
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(router, disposeHandler: false);
}
