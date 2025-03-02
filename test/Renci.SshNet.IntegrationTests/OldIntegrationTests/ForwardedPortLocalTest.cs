﻿using System.Diagnostics;
#if !NET
using System.Net;
#endif

using Renci.SshNet.Common;

namespace Renci.SshNet.IntegrationTests.OldIntegrationTests
{
    /// <summary>
    /// Provides functionality for local port forwarding
    /// </summary>
    [TestClass]
    public class ForwardedPortLocalTest : IntegrationTestBase
    {
        [TestMethod]
        [WorkItem(713)]
        [Owner("Kenneth_aa")]
        [TestCategory("PortForwarding")]
        [Description("Test if calling Stop on ForwardedPortLocal instance causes wait.")]
        public void Test_PortForwarding_Local_Stop_Hangs_On_Wait()
        {
            using (var client = new SshClient(SshServerHostName, SshServerPort, User.UserName, User.Password))
            {
                client.Connect();

                using var port1 = new ForwardedPortLocal("localhost", 8085, "www.google.com", 80);
                client.AddForwardedPort(port1);
                port1.Exception += delegate (object sender, ExceptionEventArgs e)
                {
                    Assert.Fail(e.Exception.ToString());
                };

                port1.Start();

                var hasTestedTunnel = false;

                _ = ThreadPool.QueueUserWorkItem(delegate (object state)
                {
                    try
                    {
                        var url = "http://www.google.com/";
                        Debug.WriteLine("Starting web request to \"" + url + "\"");

#if NET
                        var httpClient = new HttpClient();
                        var response = httpClient.GetAsync(url)
                                                 .ConfigureAwait(false)
                                                 .GetAwaiter()
                                                 .GetResult();
#else
                        var request = (HttpWebRequest)WebRequest.Create(url);
                        var response = (HttpWebResponse)request.GetResponse();
#endif // NET

                        Assert.IsNotNull(response);

                        Debug.WriteLine("Http Response status code: " + response.StatusCode.ToString());

                        response.Dispose();

                        hasTestedTunnel = true;
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail(ex.ToString());
                    }
                });

                // Wait for the web request to complete.
                while (!hasTestedTunnel)
                {
                    Thread.Sleep(1000);
                }

                try
                {
                    // Try stop the port forwarding, wait 3 seconds and fail if it is still started.
                    _ = ThreadPool.QueueUserWorkItem(delegate (object state)
                    {
                        Debug.WriteLine("Trying to stop port forward.");
                        port1.Stop();
                        Debug.WriteLine("Port forwarding stopped.");
                    });

                    Thread.Sleep(3000);
                    if (port1.IsStarted)
                    {
                        Assert.Fail("Port forwarding not stopped.");
                    }
                }
                catch (Exception ex)
                {
                    Assert.Fail(ex.ToString());
                }
                client.RemoveForwardedPort(port1);
                client.Disconnect();
                Debug.WriteLine("Success.");
            }
        }

        [TestMethod]
        public void Test_PortForwarding_Local_Without_Connecting()
        {
            using (var client = new SshClient(SshServerHostName, SshServerPort, User.UserName, User.Password))
            {
                using var port1 = new ForwardedPortLocal("localhost", 8084, "www.renci.org", 80);
                Assert.ThrowsException<SshConnectionException>(() => client.AddForwardedPort(port1));
            }
        }
    }
}
