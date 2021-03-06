// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Net.Test.Common;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace System.Net.Http.Functional.Tests
{
    public class HttpClientHandler_MaxResponseHeadersLength_Test : RemoteExecutorTestBase
    {
        [Fact]
        public void Default_MaxResponseHeadersLength()
        {
            using (var handler = new HttpClientHandler())
            {
                Assert.Equal(64 * 1024, handler.MaxResponseHeadersLength);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void InvalidValue_ThrowsException(int invalidValue)
        {
            using (var handler = new HttpClientHandler())
            {
                Assert.Throws<ArgumentOutOfRangeException>("value", () => handler.MaxResponseHeadersLength = invalidValue);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(65 * 1024)]
        [InlineData(int.MaxValue)]
        public void ValidValue_SetGet_Roundtrips(int validValue)
        {
            using (var handler = new HttpClientHandler())
            {
                handler.MaxResponseHeadersLength = validValue;
                Assert.Equal(validValue, handler.MaxResponseHeadersLength);
            }
        }

        [Fact]
        public async Task SetAfterUse_Throws()
        {
            using (var handler = new HttpClientHandler())
            using (var client = new HttpClient(handler))
            {
                handler.MaxResponseHeadersLength = int.MaxValue;
                await client.GetStreamAsync(HttpTestServers.RemoteEchoServer);
                Assert.Throws<InvalidOperationException>(() => handler.MaxResponseHeadersLength = int.MaxValue);
            }
        }

        [Theory]
        [InlineData("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n", 37, false)]
        [InlineData("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n", 38, true)]
        [InlineData("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n", 39, true)]
        public async Task ThresholdExceeded_ThrowsException(string responseHeaders, int maxResponseHeadersLength, bool shouldSucceed)
        {
            using (Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                s.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                s.Listen(1);
                var ep = (IPEndPoint)s.LocalEndPoint;

                using (var handler = new HttpClientHandler() { MaxResponseHeadersLength = maxResponseHeadersLength })
                using (var client = new HttpClient(handler))
                {
                    Task<HttpResponseMessage> getAsync = client.GetAsync($"http://{ep.Address}:{ep.Port}", HttpCompletionOption.ResponseHeadersRead);

                    using (Socket server = s.Accept())
                    using (Stream serverStream = new NetworkStream(server, ownsSocket: false))
                    using (StreamReader reader = new StreamReader(serverStream, Encoding.ASCII))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null && !string.IsNullOrEmpty(line)) ;

                        byte[] headerData = Encoding.ASCII.GetBytes(responseHeaders);
                        serverStream.Write(headerData, 0, headerData.Length);
                    }

                    if (shouldSucceed)
                    {
                        (await getAsync).Dispose();
                    }
                    else
                    {
                        await Assert.ThrowsAsync<HttpRequestException>(() => getAsync);
                    }
                }
            }
        }

    }
}
