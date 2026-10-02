using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace HospitalManagementSystem.Tests.Helpers
{
    public class TestHubContext<THub> : IHubContext<THub> where THub : Hub
    {
        public IHubClients Clients { get; } = new TestHubClients();
        public IGroupManager Groups { get; } = new TestGroupManager();
    }

    public class TestHubClients : IHubClients
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TestClientProxy> _groupProxies = new();
        public TestClientProxy DefaultProxy { get; } = new TestClientProxy();

        public IClientProxy All => DefaultProxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => DefaultProxy;
        public IClientProxy Client(string connectionId) => DefaultProxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => DefaultProxy;
        public IClientProxy Group(string groupName) => _groupProxies.GetOrAdd(groupName, _ => new TestClientProxy());
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => DefaultProxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => DefaultProxy;
        public IClientProxy User(string userId) => DefaultProxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => DefaultProxy;

        public TestClientProxy GetGroupProxy(string groupName) => (TestClientProxy)Group(groupName);
    }

    public class TestClientProxy : IClientProxy
    {
        public List<(string Method, object?[] Args)> Invocations { get; } = new();

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Invocations.Add((method, args));
            return Task.CompletedTask;
        }
    }

    public class TestGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public class TestServiceScopeFactory : IServiceScopeFactory, IServiceScope
    {
        private readonly IServiceProvider _serviceProvider;

        public TestServiceScopeFactory(IServiceProvider? serviceProvider = null)
        {
            _serviceProvider = serviceProvider ?? new ServiceCollection().BuildServiceProvider();
        }

        public IServiceScope CreateScope() => this;
        public IServiceProvider ServiceProvider => _serviceProvider;
        public void Dispose() { }
    }
}
