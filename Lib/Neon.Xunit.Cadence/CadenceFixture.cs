﻿//-----------------------------------------------------------------------------
// FILE:	    CadenceFixture.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2020 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Neon.Cadence;
using Neon.Cadence.Internal;
using Neon.Common;
using Neon.Data;
using Neon.Diagnostics;
using Neon.Retry;
using Neon.Net;

using Newtonsoft.Json.Linq;
using Xunit;

namespace Neon.Xunit.Cadence
{
    /// <summary>
    /// Used to run the Cadence Docker compose application while performing Cadence
    /// related unit tests.
    /// </summary>
    /// <remarks>
    /// <note>
    /// <para>
    /// <b>IMPORTANT:</b> The base Neon <see cref="TestFixture"/> implementation <b>DOES NOT</b>
    /// support parallel test execution because fixtures may impact global machine state
    /// like starting a Docker container, modifying the local DNS <b>hosts</b> file, or 
    /// configuring a test database.
    /// </para>
    /// <para>
    /// You should explicitly disable parallel execution in all test assemblies that
    /// rely on test fixtures by adding a C# file called <c>AssemblyInfo.cs</c> with:
    /// </para>
    /// <code language="csharp">
    /// [assembly: CollectionBehavior(DisableTestParallelization = true, MaxParallelThreads = 1)]
    /// </code>
    /// </note>
    /// <para>
    /// This fixture assumes that Cadence is not currently running on the
    /// local workstation or as a container named <b>cadence-dev</b>.
    /// You may see port conflict errors if either of these conditions
    /// are not true.
    /// </para>
    /// <para>
    /// See <see cref="Start(CadenceSettings, string, string, string, LogLevel, bool, bool, string, bool, bool)"/>
    /// for more information about how this works.
    /// </para>
    /// </remarks>
    /// <threadsafety instance="true"/>
    public sealed class CadenceFixture : DockerComposeFixture
    {
        /// <summary>
        /// The default Docker compose file text used to spin up Temporal and its related services
        /// by the <see cref="CadenceFixture"/>.
        /// </summary>
        public const string DefaultComposeFile =
@"version: '3'
services:
  cassandra:
    image: cassandra:3.11
    ports:
      - 9042:9042
    environment:
      - HEAP_NEWSIZE=1M
      - MAX_HEAP_SIZE=1024M
    deploy:
      resources:
        limits:
          memory: 1536M
  statsd:
    image: graphiteapp/graphite-statsd
    ports:
      - 8080:80
      - 2003:2003
      - 8125:8125
      - 8126:8126
  cadence:
    image: ubercadence/server:master-auto-setup
    ports:
     - 7933:7933
     - 7934:7934
     - 7935:7935
     - 7939:7939
    environment:
      - CASSANDRA_SEEDS=cassandra
      - STATSD_ENDPOINT=statsd:8125
      - DYNAMIC_CONFIG_FILE_PATH=config/dynamicconfig/development.yaml
    depends_on:
      - cassandra
      - statsd
  cadence-web:
    image: ubercadence/web:latest
    environment:
      - CADENCE_TCHANNEL_PEERS=cadence:7933
    ports:
      - 8088:8088
    depends_on:
      - cadence
";
        private CadenceSettings     settings;
        private CadenceClient       client;
        private bool                reconnect;
        private bool                noReset;

        /// <summary>
        /// The default domain configured for <see cref="CadenceFixture"/> clients.
        /// </summary>
        public const string DefaultDomain = "test-domain";

        /// <summary>
        /// Constructs the fixture.
        /// </summary>
        public CadenceFixture()
        {
        }

        /// <summary>
        /// <para>
        /// Starts a Cadence container if it's not already running.  You'll generally want
        /// to call this in your test class constructor instead of <see cref="ITestFixture.Start(Action)"/>.
        /// </para>
        /// <note>
        /// You'll need to call <see cref="StartAsComposed(CadenceSettings, string, string, string, LogLevel, bool, bool, string, bool, bool)"/>
        /// instead when this fixture is being added to a <see cref="ComposedFixture"/>.
        /// </note>
        /// </summary>
        /// <param name="settings">Optional Cadence settings.</param>
        /// <param name="name">Optionally specifies the Cadence container name (defaults to <c>cadence-dev</c>).</param>
        /// <param name="composeFile">
        /// <para>
        /// Optionally specifies the Temporal Docker compose file text.  This defaults to
        /// <see cref="DefaultComposeFile"/> which configures Temporal server to start with
        /// a new Cassandra database instance listening on port <b>9042</b> as well as the
        /// Temporal web UI running on port <b>8088</b>.  Temporal server is listening on
        /// its standard gRPC port <b>7233</b>.
        /// </para>
        /// <para>
        /// You may specify your own Docker compose text file to customize this by configuring
        /// a different backend database, etc.
        /// </para>
        /// </param>
        /// <param name="defaultDomain">Optionally specifies the default domain for the fixture's client.  This defaults to <b>test-domain</b>.</param>
        /// <param name="logLevel">Specifies the Cadence log level.  This defaults to <see cref="LogLevel.None"/>.</param>
        /// <param name="reconnect">
        /// Optionally specifies that a new Cadence connection <b>should</b> be established for each
        /// unit test case.  By default, the same connection will be reused which will save about a 
        /// second per test.
        /// </param>
        /// <param name="keepOpen">
        /// Optionally indicates that the container should remain running after the fixture is disposed.
        /// This is handy for using the Temporal web UI for port mortems after tests have completed.
        /// </param>
        /// <param name="hostInterface">
        /// Optionally specifies the host interface where the container public ports will be
        /// published.  This defaults to <see cref="ContainerFixture.DefaultHostInterface"/>
        /// but may be customized.  This needs to be an IPv4 address.
        /// </param>
        /// <param name="noClient">
        /// Optionally disables establishing a client connection when <c>true</c>
        /// is passed.  The <see cref="Client"/> and <see cref="HttpClient"/> properties
        /// will be set to <c>null</c> in this case.
        /// </param>
        /// <param name="noReset">
        /// Optionally prevents the fixture from calling <see cref="CadenceClient.Reset()"/> to
        /// put the Cadence client library into its initial state before the fixture starts as well
        /// as when the fixture itself is reset.
        /// </param>
        /// <returns>
        /// <see cref="TestFixtureStatus.Started"/> if the fixture wasn't previously started and
        /// this method call started it or <see cref="TestFixtureStatus.AlreadyRunning"/> if the 
        /// fixture was already running.
        /// </returns>
        /// <remarks>
        /// <note>
        /// Some of the <paramref name="settings"/> properties will be ignored including 
        /// <see cref="CadenceSettings.Servers"/>.  This will be replaced by the local
        /// endpoint for the Cadence container.  Also, the fixture will connect to the 
        /// <b>default</b> Cadence domain by default (unless another is specified).
        /// </note>
        /// <note>
        /// A fresh Cadence client <see cref="Client"/> will be established every time this
        /// fixture is started, regardless of whether the fixture has already been started.  This
        /// ensures that each unit test will start with a client in the default state.
        /// </note>
        /// </remarks>
        public TestFixtureStatus Start(
            CadenceSettings     settings      = null,
            string              name          = "cadence-dev",
            string              composeFile   = DefaultComposeFile,
            string              defaultDomain = DefaultDomain,
            LogLevel            logLevel      = LogLevel.None,
            bool                reconnect     = false,
            bool                keepOpen      = false,
            string              hostInterface = null,
            bool                noClient      = false,
            bool                noReset       = false)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(composeFile), nameof(composeFile));

            return base.Start(
                () =>
                {
                    StartAsComposed(
                        settings:        settings, 
                        name:            name,
                        composeFile:     composeFile,
                        defaultDomain:   defaultDomain, 
                        logLevel:        logLevel,
                        reconnect:       reconnect,
                        keepOpen:        keepOpen, 
                        hostInterface:   hostInterface,
                        noClient:        noClient, 
                        noReset:         noReset);
                });
        }

        /// <summary>
        /// Used to start the fixture within a <see cref="ComposedFixture"/>.
        /// </summary>
        /// <param name="settings">Optional Cadence settings.</param>
        /// <param name="name">Optionally specifies the Cadence container name (defaults to <c>cadence-dev</c>).</param>
        /// <param name="composeFile">
        /// <para>
        /// Optionally specifies the Temporal Docker compose file text.  This defaults to
        /// <see cref="DefaultComposeFile"/> which configures Temporal server to start with
        /// a new Cassandra database instance listening on port <b>9042</b> as well as the
        /// Temporal web UI running on port <b>8088</b>.  Temporal server is listening on
        /// its standard gRPC port <b>7233</b>.
        /// </para>
        /// <para>
        /// You may specify your own Docker compose text file to customize this by configuring
        /// a different backend database, etc.
        /// </para>
        /// </param>
        /// <param name="defaultDomain">Optionally specifies the default domain for the fixture's client.  This defaults to <b>test-domain</b>.</param>
        /// <param name="logLevel">Specifies the Cadence log level.  This defaults to <see cref="LogLevel.None"/>.</param>
        /// <param name="reconnect">
        /// Optionally specifies that a new Cadence connection <b>should</b> be established for each
        /// unit test case.  By default, the same connection will be reused which will save about a 
        /// second per test case.
        /// </param>
        /// <param name="keepOpen">
        /// Optionally indicates that the container should remain running after the fixture is disposed.
        /// This is handy for using the Temporal web UI for port mortems after tests have completed.
        /// </param>
        /// <param name="hostInterface">
        /// Optionally specifies the host interface where the container public ports will be
        /// published.  This defaults to <see cref="ContainerFixture.DefaultHostInterface"/>
        /// but may be customized.  This needs to be an IPv4 address.
        /// </param>
        /// <param name="noClient">
        /// Optionally disables establishing a client connection when <c>true</c>
        /// is passed.  The <see cref="Client"/> and <see cref="HttpClient"/> properties
        /// will be set to <c>null</c> in this case.
        /// </param>
        /// <param name="noReset">
        /// Optionally prevents the fixture from calling <see cref="CadenceClient.Reset()"/> to
        /// put the Cadence client library into its initial state before the fixture starts as well
        /// as when the fixture itself is reset.
        /// </param>
        /// <remarks>
        /// <note>
        /// A fresh Cadence client <see cref="Client"/> will be established every time this
        /// fixture is started, regardless of whether the fixture has already been started.  This
        /// ensures that each unit test will start with a client in the default state.
        /// </note>
        /// </remarks>
        public void StartAsComposed(
            CadenceSettings     settings      = null,
            string              name          = "cadence-dev",
            string              composeFile   = DefaultComposeFile,
            string              defaultDomain = DefaultDomain,
            LogLevel            logLevel      = LogLevel.None,
            bool                reconnect     = false,
            bool                keepOpen      = false,
            string              hostInterface = null,
            bool                noClient      = false,
            bool                noReset       = false)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(composeFile), nameof(composeFile));

            base.CheckWithinAction();

            if (!IsRunning)
            {
                // $hack(jefflill):
                //
                // The [temporal-dev] Docker compose app may be running from a previous test run.  
                // We need to stop this to avoid network port conflicts.  We're just going to
                // force the removal of the stack's Docker containers.
                //
                // This is somewhat fragile because it hardcodes the container names and won't
                // remove any other stack assets like networks.

                NeonHelper.ExecuteCapture(NeonHelper.DockerCli, new object[] { "rm", "--force",
                    new string[]
                    {
                        "temporal-admin-tools",
                        "temporal-web",
                        "temporal-dev_temporal_1",
                        "temporal-postgresql"
                    } });


                // Select the network interface where Cadence will listen.

                if (string.IsNullOrEmpty(hostInterface))
                {
                    hostInterface = ContainerFixture.DefaultHostInterface;
                }
                else
                {
                    Covenant.Requires<ArgumentException>(NetHelper.TryParseIPv4Address(hostInterface, out var address) && address.AddressFamily == AddressFamily.InterNetwork, nameof(hostInterface), $"[{hostInterface}] is not a valid IPv4 address.");
                }

                // Reset CadenceClient to its initial state.

                this.noReset = noReset;

                if (!noReset)
                {
                    CadenceClient.Reset();
                }

                // Start the Cadence container.

                base.StartAsComposed(name, composeFile, keepOpen);

                // It can take Cadence server some time to start.  Rather than relying on [cadence-proxy]
                // to handle retries (which may take longer than the connect timeout), we're going to wait
                // up to 4 minutes for Temporal to start listening on its RPC socket.

                var retry = new LinearRetryPolicy(e => true, maxAttempts: int.MaxValue, retryInterval: TimeSpan.FromSeconds(0.5), timeout: TimeSpan.FromMinutes(4));

                retry.Invoke(
                    () =>
                    {
                        // The [socket.Connect()] calls below will throw [SocketException] until
                        // Temporal starts listening on its RPC socket.

                        var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);

                        socket.Connect(IPAddress.IPv6Loopback, NetworkPorts.Cadence);
                        socket.Close();
                    });

                Thread.Sleep(TimeSpan.FromSeconds(5));  // Wait a bit longer for luck!

                // Initialize the settings.

                settings = settings ?? new CadenceSettings()
                {
                    CreateDomain  = true,
                    DefaultDomain = defaultDomain,
                    LogLevel      = logLevel
                };

                settings.Servers.Clear();
                settings.Servers.Add($"http://10.0.0.2:{NetworkPorts.Cadence}");

                this.settings  = settings;
                this.reconnect = reconnect;

                if (!noClient)
                {
                    // Establish the Cadence connection.

                    Client = CadenceClient.ConnectAsync(settings).Result;

                    HttpClient = new HttpClient()
                    {
                        BaseAddress = Client.ListenUri
                    };
                }
            }
        }

        /// <summary>
        /// Returns the settings used to connect to the Cadence cluster.
        /// </summary>
        public CadenceSettings Settings => settings;

        /// <summary>
        /// Returns the <see cref="CadenceClient"/> to be used to interact with Cadence.
        /// </summary>
        public CadenceClient Client
        {
            get
            {
                if (client == null)
                {
                    throw new Exception("Cadence client could not be connected to the Cadence cluster.");
                }

                return client;
            }

            set => client = value;
        }

        /// <summary>
        /// Returns a <see cref="System.Net.Http.HttpClient"/> suitable for submitting requests to the
        /// <see cref="HttpClient"/> instance web server.
        /// </summary>
        public HttpClient HttpClient { get; private set; }

        /// <summary>
        /// <para>
        /// Returns a <see cref="System.Net.Http.HttpClient"/> suitable for submitting requests to the
        /// associated <b>cadence-proxy</b> process.
        /// </para>
        /// <note>
        /// This will return <c>null</c> if the <b>cadence-proxy</b> process was disabled by
        /// the settings.
        /// </note>
        /// </summary>
        public HttpClient ProxyClient { get; private set; }

        /// <summary>
        /// Closes the existing Cadence connection and restarts the Cadence
        /// server and then establishes a new connection.
        /// </summary>
        public new void Restart()
        {
            // Disconnect.

            Client.Dispose();
            Client = null;

            HttpClient.Dispose();
            HttpClient = null;

            // Restart the Cadence container.

            base.Restart();

            // Reconnect.

            Client = CadenceClient.ConnectAsync(settings).Result;

            HttpClient = new HttpClient()
            {
                BaseAddress = Client.ListenUri
            };
        }

        /// <summary>
        /// This method completely resets the fixture by removing the Cadence 
        /// container from Docker.  Use <see cref="ContainerFixture.Restart"/> 
        /// if you just want to restart a fresh Cadence instance.
        /// </summary>
        public override void Reset()
        {
            if (!noReset)
            {
                CadenceClient.Reset();
            }

            if (Client != null)
            {
                Client.Dispose();
                Client = null;
            }

            // Dispose the clients.

            if (HttpClient != null)
            {
                HttpClient.Dispose();
                HttpClient = null;
            }

            if (ProxyClient != null)
            {
                ProxyClient.Dispose();
                ProxyClient = null;
            }

            base.Reset();
        }

        /// <summary>
        /// Called when an already started fixture is being restarted.  This 
        /// establishes a fresh Cadence connection.
        /// </summary>
        public override void OnRestart()
        {
            if (reconnect)
            {
                // We're going to continue using the same connection.

                return;
            }

            // Close any existing connection related objects.

            if (Client != null)
            {
                Client.Dispose();
                Client = null;
            }

            if (HttpClient != null)
            {
                HttpClient.Dispose();
                HttpClient = null;
            }

            // Establish fresh connections.

            Client = CadenceClient.ConnectAsync(settings).Result;

            HttpClient = new HttpClient()
            {
                BaseAddress = Client.ListenUri
            };
        }
    }
}
