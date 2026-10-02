using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HospitalManagementSystem.Tests.Helpers
{
    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _sentRequests = new();
        private Func<HttpRequestMessage, Task<HttpResponseMessage>>? _handlerFunc;

        public IReadOnlyList<HttpRequestMessage> SentRequests => _sentRequests;

        public MockHttpMessageHandler()
        {
        }

        public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handlerFunc)
        {
            _handlerFunc = handlerFunc;
        }

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc)
        {
            _handlerFunc = req => Task.FromResult(handlerFunc(req));
        }

        public void SetHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handlerFunc)
        {
            _handlerFunc = handlerFunc;
        }

        public void SetHandler(Func<HttpRequestMessage, HttpResponseMessage> handlerFunc)
        {
            _handlerFunc = req => Task.FromResult(handlerFunc(req));
        }

        public void SetJsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _handlerFunc = _ => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        public void SetErrorResponse(HttpStatusCode statusCode, string message = "Error")
        {
            _handlerFunc = _ => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(message, Encoding.UTF8, "text/plain")
            });
        }

        public void SetTimeout()
        {
            _handlerFunc = _ => throw new TaskCanceledException("The simulated request timed out.");
        }

        public void SetNetworkFailure(string message = "Connection refused")
        {
            _handlerFunc = _ => throw new HttpRequestException(message);
        }

        public HttpClient ToHttpClient(string? baseAddress = null)
        {
            var client = new HttpClient(this);
            if (!string.IsNullOrEmpty(baseAddress))
            {
                client.BaseAddress = new Uri(baseAddress);
            }
            return client;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _sentRequests.Add(request);

            if (_handlerFunc != null)
            {
                return await _handlerFunc(request);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        }
    }
}
