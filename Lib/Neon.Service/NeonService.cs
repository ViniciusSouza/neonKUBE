﻿//-----------------------------------------------------------------------------
// FILE:	    NeonService.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2021 by neonFORGE LLC.  All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.Contracts;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Neon.Common;
using Neon.Cryptography;
using Neon.Diagnostics;
using Neon.IO;
using Neon.Net;
using Neon.Retry;
using Neon.Windows;

using DnsClient;
using Prometheus;
using Neon.Tasks;

namespace Neon.Service
{
    /// <summary>
    /// Handy base class for application services.  This class handles process termination signals when
    /// running on Linux, OS/X, and similar environments and also provides some features to help you run
    /// unit tests on your service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Basing your service implementations on the <see cref="Service"/> class will
    /// make them easier to test via integration with the <b>ServiceFixture</b> from
    /// the <b>Neon.Xunit</b> library by providing some useful abstractions over 
    /// service configuration, startup and shutdown including a <see cref="ProcessTerminator"/>
    /// to handle termination signals from Linux or Kubernetes.
    /// </para>
    /// <para>
    /// This class is pretty easy to use.  Simply derive your service class from <see cref="NeonService"/>
    /// and implement the <see cref="OnRunAsync"/> method.  <see cref="OnRunAsync"/> will be called when 
    /// your service is started.  This is where you'll implement your service.  You should perform any
    /// initialization and then call <see cref="SetRunningAsync"/> to indicate that the service is ready for
    /// business.
    /// </para>
    /// <note>
    /// Note that calling <see cref="SetRunningAsync()"/> after your service has initialized is important
    /// because the <b>NeonServiceFixture</b> won't allow tests to proceed until the service
    /// indicates that it's ready.  This is necessary to avoid unit test race conditions.
    /// </note>
    /// <para>
    /// Note that your <see cref="OnRunAsync"/> method should generally not return until the 
    /// <see cref="Terminator"/> signals it to stop.  Alternatively, you can throw a <see cref="ProgramExitException"/>
    /// with an optional process exit code to proactively exit your service.
    /// </para>
    /// <note>
    /// All services should properly handle <see cref="Terminator"/> stop signals so services deployed as
    /// containers will stop promptly and cleanly (this also applies to services running in unit tests).  
    /// Your terminate handler method must return within a set period of time (30 seconds by default) 
    /// to avoid killed by by Docker or Kubernetes.  This is probably the trickiest thing you'll need to implement.
    /// For asynchronous service implementations, you consider passing the <see cref="ProcessTerminator.CancellationToken"/>
    /// to all async method calls.
    /// </note>
    /// <note>
    /// This class uses the <b>DEV_WORKSTATION</b> environment variable to determine whether
    /// the service is running in test mode or not.  This variable will typically be defined
    /// on developer workstations as well as CI/CD machines.  This variable must never be
    /// defined for production environments.  You can use the <see cref="InProduction"/>
    /// or <see cref="InDevelopment"/> properties to check this.
    /// </note>
    /// <code source="..\..\Snippets\Snippets.NeonService\Program-Basic.cs" language="c#" title="Simple example showing a basic service implementation:"/>
    /// <para><b>CONFIGURATION</b></para>
    /// <para>
    /// Services are generally configured using environment variables and/or configuration
    /// files.  In production, environment variables will actually come from the environment
    /// after having been initialized by the container image or passed by Kubernetes when
    /// starting the service container.  Environment variables are retrieved by name
    /// (case sensitive).
    /// </para>
    /// <para>
    /// Configuration files work the same way.  They are either present in the service 
    /// container image or mounted to the container as a secret or config file by Kubernetes. 
    /// Configuration files are specified by their path (case sensitive) within the
    /// running container.
    /// </para>
    /// <para>
    /// This class provides some abstractions for managing environment variables and 
    /// configuration files so that services running in production or as a unit test
    /// can configure themselves using the same code for both environments. 
    /// </para>
    /// <para>
    /// Services should use the <see cref="GetEnvironmentVariable(string, string)"/> method to 
    /// retrieve important environment variables rather than using <see cref="Environment.GetEnvironmentVariable(string)"/>.
    /// In production, this simply returns the variable directly from the current process.
    /// For tests, the environment variable will be returned from a local dictionary
    /// that was expicitly initialized by calls to <see cref="SetEnvironmentVariable(string, string)"/>.
    /// This local dictionary allows the testing of multiple services at the same
    /// time with each being presented their own environment variables.
    /// </para>
    /// <para>
    /// You may also use the <see cref="LoadEnvironmentVariables(string, Func{string, string})"/>
    /// methods to load environment variables from a text file (potentially encrypted via
    /// <see cref="NeonVault"/>).  This will typically be done only for unit tests.
    /// </para>
    /// <para>
    /// Configuration files work similarily.  You'll use <see cref="GetConfigFilePath(string)"/>
    /// to map a logical file path to a physical path.  The logical file path is typically
    /// specified as the path where the configuration file will be located in production.
    /// This can be any valid path with in a running production container and since we're
    /// currently Linux centric, will typically be a Linux file path like <c>/etc/MYSERVICE.yaml</c>
    /// or <c>/etc/MYSERVICE/config.yaml</c>.
    /// </para>
    /// <para>
    /// For production, <see cref="GetConfigFilePath(string)"/> will simply return the file
    /// path passed so that the configuration file located there will referenced.  For
    /// testing, <see cref="GetConfigFilePath(string)"/> will return the path specified by
    /// an earlier call to <see cref="SetConfigFilePath(string, string, Func{string, string})"/> or to a
    /// temporary file initialized by previous calls to <see cref="SetConfigFile(string, string, bool)"/>
    /// or <see cref="SetConfigFile(string, byte[])"/>.  This indirection provides a 
    /// consistent way to run services in production as well as in tests, including tests
    /// running multiple services simultaneously.
    /// </para>
    /// <para><b>DISPOSE IMPLEMENTATION</b></para>
    /// <para>
    /// All services, especially those that create unmanaged resources like ASP.NET services,
    /// sockets, NATS clients, HTTP clients, thread etc. should override and implement 
    /// <see cref="Dispose(bool)"/>  to ensure that any of these resources are proactively 
    /// disposed.  Your method should call the base class version of the method first before 
    /// disposing these resources.
    /// </para>
    /// <code language="C#">
    /// protected override Dispose(bool disposing)
    /// {
    ///     base.Dispose(disposing);
    ///     
    ///     if (appHost != null)
    ///     {
    ///         appHost.Dispose();
    ///         appHost = null;
    ///     }
    /// }
    /// </code>
    /// <para>
    /// The <b>disposing</b> parameter is passed as <c>true</c> when the base <see cref="NeonService.Dispose()"/>
    /// method was called or <c>false</c> if the garbage collector is finalizing the instance
    /// before discarding it.  The difference is subtle and most services can safely ignore
    /// this parameter (other than passing it through to the base <see cref="Dispose(bool)"/>
    /// method).
    /// </para>
    /// <para>
    /// In the example above, the service implements an ASP.NET web service where <c>appHost</c>
    /// was initialized as the <c>IWebHost</c> actually implementing the web service.  The code
    /// ensures that the <c>appHost</c> isn't already disposed before disposing it.  This will
    /// stop the web service and release the underlying listening socket.  You'll want to do
    /// something like this for any other unmanaged resources your service might hold.
    /// </para>
    /// <note>
    /// <para>
    /// It's very important that you take care to dispose things like running web services and
    /// listening sockets within your <see cref="Dispose(bool)"/> method.  You also need to
    /// ensure that any threads you've created are terminated.  This means that you'll need
    /// a way to signal threads to exit and then wait for them to actually exit.
    /// </para>
    /// <para>
    /// This is important when testing your services with a unit testing framework like
    /// Xunit because frameworks like this run all tests within the same Test Runner
    /// process and leaving something like a listening socket open on a port (say port 80)
    /// may prevent a subsequent test from running successfully due to it not being able 
    /// to open its listening socket on port 80. 
    /// </para>
    /// </note>
    /// <para><b>LOGGING</b></para>
    /// <para>
    /// Each <see cref="NeonService"/> instance maintains its own <see cref="LogManager"/>
    /// instance with the a default logger created at <see cref="Log"/>.  The log manager
    /// is initialized using the <b>LOG_LEVEL</b> environment variable value which defaults
    /// to <b>info</b> when not present.  <see cref="LogLevel"/> for the possible values.
    /// </para>
    /// <para>
    /// Note that the <see cref="Neon.Diagnostics.LogManager.Default"/> log manager will
    /// also be initialized with the log level when the service is running in a production
    /// environment so that logging in production works completely as expected.
    /// </para>
    /// <para>
    /// For development environments, the <see cref="Neon.Diagnostics.LogManager.Default"/>
    /// instance's log level will not be modified.  This means that loggers created from
    /// <see cref="Neon.Diagnostics.LogManager.Default"/> may not use the same log
    /// level as the service itself.  This means that library classes that create their
    /// own loggers won't honor the service log level.  This is an unfortunate consequence
    /// of running emulated services in the same process.
    /// </para>
    /// <para>
    /// There are two ways to mitigate this.  First, any source code defined within the 
    /// service project should be designed to create loggers from the service's <see cref="LogManager"/>
    /// rather than using the global one.  Second, you can configure your unit test to
    /// set the desired log level like:
    /// </para>
    /// <code language="C#">
    /// LogManager.Default.SetLogLevel(LogLevel.Debug));
    /// </code>
    /// <note>
    /// Setting the global default log level like this will impact loggers created for all
    /// emulated services, but this shouldn't be a problem for more situations.
    /// </note>
    /// <para><b>HEALTH PROBES</b></para>
    /// <para>
    /// Hosting environments such as Kubernetes will often require service instances
    /// to be able to report their health via health probes.  These probes are typically
    /// implemented as a script that is called periodically by the hosting environment
    /// with the script return code indicating the service instance health.
    /// </para>
    /// <para>
    /// The <see cref="Neon.Service.NeonService"/> class supports this by optionally
    /// writing a text file with various strings indicating the health status.  This file
    /// will consist of a single line of text <b>without line ending characters</b>.  You'll
    /// need to specify the fully qualified path to this file as an optional parameter to the 
    /// <see cref="NeonService"/> constructor.
    /// </para>
    /// <para><b>SERVICE DEPENDENCIES</b></para>
    /// <para>
    /// Services often depend on other services to function, such as a database, rest API, etc.
    /// <see cref="NeonService"/> provides an easy to use integrated way to wait for other
    /// services to initialize themselves and become ready before your service will be allowed
    /// to start.  This is a great way to avoid a blizzard of service failures and restarts
    /// when starting a collection of related services on a platform like Kubernetes.
    /// </para>
    /// <para>
    /// You can use the <see cref="Dependencies"/> property to control this in code via the
    /// <see cref="ServiceDependencies"/> class or configure this via environment variables: 
    /// </para>
    /// <code>
    /// NEON_SERVICE_DEPENDENCIES_URIS=http://foo.com;tcp://10.0.0.55:1234
    /// NEON_SERVICE_DEPENDENCIES_TIMEOUT_SECONDS=30
    /// NEON_SERVICE_DEPENDENCIES_WAIT_SECONDS=5
    /// </code>
    /// <para>
    /// The basic idea is that the <see cref="RunAsync"/> call to start your service will
    /// need to successfully to establish socket connections to any service dependecy URIs 
    /// before your <see cref="OnRunAsync"/> method will be called.  Your service will be
    /// terminated if any of the services cannot be reached after the specified timeout.
    /// </para>
    /// <para>
    /// You can also specity an additional time to wait after all services are available
    /// to give them a chance to perform additional internal initialization.
    /// </para>
    /// <code source="..\..\Snippets\Snippets.NeonService\Program-Dependencies.cs" language="c#" title="Waiting for service dependencies:"/>
    /// <para><b>PROMETHEUS METRICS</b></para>
    /// <para>
    /// <see cref="NeonService"/> can enable services to publish Prometheus metrics with a
    /// single line of code; simply set <see cref="NeonService.MetricsOptions"/>.<see cref="MetricsOptions.Mode"/> to
    /// <see cref="MetricsMode.Scrape"/> before calling <see cref="RunAsync(bool)"/>.  This configures
    /// your service to publish metrics via HTTP via <b>http://0.0.0.0:</b><see cref="NetworkPorts.NeonPrometheusScrape"/><b>/metrics/</b>.
    /// We've resistered port <see cref="NetworkPorts.NeonPrometheusScrape"/> with Prometheus as a standard port
    /// to be used for micro services running in Kubernetes or on other container platforms to make it 
    /// easy configure scraping for a cluster.
    /// </para>
    /// <para>
    /// You can also configure a custom port and path or configure metrics push to a Prometheus
    /// Pushgateway using other <see cref="MetricsOptions"/> properties.  You can also fully customize
    /// your Prometheus configuration by leaving this disabled in <see cref="NeonService.MetricsOptions"/>
    /// and setting things up using the standard <b>prometheus-net</b> mechanisms before calling
    /// <see cref="RunAsync(bool)"/>.
    /// </para>
    /// <code source="..\..\Snippets\Snippets.NeonService\Program-Dependencies.cs" language="c#" title="Waiting for service dependencies:"/>
    /// <para><b>NETCORE Runtime METRICS</b></para>
    /// <para>
    /// We highly recommend that you also enable .NET Runtime related metrics for services targeting
    /// .NET Core 2.2 or greater.
    /// </para>
    /// <note>
    /// Although the .NET Core 2.2+ runtimes are supported, the runtime apparently has some issues that
    /// may prevent this from working properly, so that's not recommended.  Note that there's currently
    /// no support for any .NET Framework runtime.
    /// </note>
    /// <para>
    /// Adding support for this is easy, simply add a reference to the <a href="https://www.nuget.org/packages/prometheus-net.DotNetRuntime">prometheus-net.DotNetRuntime</a>
    /// package to your service project and then assign a function callback to <see cref="MetricsOptions.GetCollector"/>
    /// that configures runtime metrics collection, like:
    /// </para>
    /// <code source="..\..\Snippets\Snippets.NeonService\Program-Metrics.cs" language="c#" title="Service metrics example:"/>
    /// <para>
    /// You can also customize the the runtime metrics emitted like this:
    /// </para>
    /// <code source="..\..\Snippets\Snippets.NeonService\Program-RuntimeMetrics.cs" language="c#" title="Service and .NET Runtime metrics:"/>
    /// <para><b>SERVICE: FULL MEAL DEAL!</b></para>
    /// <para>
    /// Here's a reasonable template you can use to begin implementing your service projects with 
    /// all features enabled:
    /// </para>
    /// <code source="..\..\Snippets\Snippets.NeonService\Program-FullMealDeal.cs" language="c#" title="Full Neon.Service template:"/>
    /// </remarks>
    public abstract class NeonService : IDisposable
    {
        //---------------------------------------------------------------------
        // Private types

        /// <summary>
        /// Holds information about configuration files.
        /// </summary>
        private sealed class FileInfo : IDisposable
        {
            /// <summary>
            /// The physical path to the configuration file.
            /// </summary>
            public string PhysicalPath { get; set; }

            /// <summary>
            /// The file data as bytes or as a string encoded as UTF-8 encode bytes.
            /// </summary>
            public byte[] Data { get; set; }

            /// <summary>
            /// Set if the physical file is temporary.
            /// </summary>
            public TempFile TempFile { get; set; }

            /// <summary>
            /// Dispose the file.
            /// </summary>
            public void Dispose()
            {
                if (TempFile != null)
                {
                    TempFile.Dispose();
                    TempFile = null;
                }
            }
        }

        //---------------------------------------------------------------------
        // Static members

        private static readonly char[]  equalArray = new char[] { '=' };
        private static readonly Gauge   infoGauge  = Metrics.CreateGauge("neon_service_info", "Describes your service version.", "version");

        /// <summary>
        /// This controls whether any <see cref="NeonService"/> instances will use the global
        /// <see cref="LogManager.Default"/> log manager for logging or maintain its own
        /// log manager.  This defaults to <c>true</c> which will be appropriate for most
        /// production situations.  It may be useful to disable this for some unit tests.
        /// </summary>
        public static bool GlobalLogging = true;

        // WARNING:
        //
        // The code below should be manually synchronized with similar code in [KubeHelper]
        // if neonKUBE related folder names ever change in the future.

        private static string testFolder;
        private static string cachedNeonKubeUserFolder;
        private static string cachedPasswordsFolder;

        /// <summary>
        /// Returns <c>true</c> if the service is running in test mode.
        /// </summary>
        private static bool IsTestMode
        {
            get
            {
                if (testFolder != null)
                {
                    return true;
                }

                testFolder = Environment.GetEnvironmentVariable(NeonHelper.TestModeFolderVar);

                return testFolder != null;
            }
        }

        /// <summary>
        /// Returns the path the folder holding user-specific Kubernetes files.
        /// </summary>
        /// <returns>The folder path.</returns>
        private static string GetNeonKubeUserFolder()
        {
            if (cachedNeonKubeUserFolder != null)
            {
                return cachedNeonKubeUserFolder;
            }

            if (IsTestMode)
            {
                cachedNeonKubeUserFolder = Path.Combine(testFolder, ".neonkube");

                Directory.CreateDirectory(cachedNeonKubeUserFolder);

                return cachedNeonKubeUserFolder;
            }

            if (NeonHelper.IsWindows)
            {
                var path = Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE"), ".neonkube");

                Directory.CreateDirectory(path);

                try
                {
                    NeonHelper.EncryptFile(path);
                }
                catch
                {
                    // Encryption is not available on all platforms (e.g. Windows Home, or non-NTFS
                    // file systems).  The secrets won't be encrypted for these situations.
                }

                return cachedNeonKubeUserFolder = path;
            }
            else if (NeonHelper.IsLinux || NeonHelper.IsOSX)
            {
                var path = Path.Combine(Environment.GetEnvironmentVariable("HOME"), ".neonkube");

                Directory.CreateDirectory(path);

                return cachedNeonKubeUserFolder = path;
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Returns path to the folder holding the encryption passwords.
        /// </summary>
        /// <returns>The folder path.</returns>
        private static string PasswordsFolder
        {
            get
            {
                if (cachedPasswordsFolder != null)
                {
                    return cachedPasswordsFolder;
                }

                var path = Path.Combine(GetNeonKubeUserFolder(), "passwords");

                Directory.CreateDirectory(path);

                return cachedPasswordsFolder = path;
            }
        }

        /// <summary>
        /// Looks up a password given its name.
        /// </summary>
        /// <param name="passwordName">The password name.</param>
        /// <returns>The password value.</returns>
        /// <exception cref="KeyNotFoundException">Thrown if the password doesn't exist.</exception>
        private static string LookupPassword(string passwordName)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(passwordName), nameof(passwordName));

            var path = Path.Combine(PasswordsFolder, passwordName);

            if (!File.Exists(path))
            {
                throw new KeyNotFoundException(passwordName);
            }

            return File.ReadAllText(path).Trim();
        }

        //---------------------------------------------------------------------
        // Instance members

        private readonly object                 syncLock   = new object();
        private readonly AsyncMutex             asyncMutex = new AsyncMutex();
        private bool                            isRunning;
        private bool                            isDisposed;
        private bool                            stopPending;
        private string                          version;
        private Dictionary<string, string>      environmentVariables;
        private Dictionary<string, FileInfo>    configFiles;
        private string                          statusFilePath;
        private MetricServer                    metricServer;
        private MetricPusher                    metricPusher;
        private IDisposable                     metricCollector;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="name">The name of this service within <see cref="ServiceMap"/>.</param>
        /// <param name="version">
        /// Optionally specifies the version of your service formatted as a valid <see cref="SemanticVersion"/>.
        /// This will default to <b>"unknown"</b> when not set or when the value passed is invalid.
        /// </param>
        /// <param name="statusFilePath">
        /// Optionally specifies the path where the service will update its status (for external health probes).
        /// See the class documentation for more information <see cref="Neon.Service"/>.
        /// </param>
        /// <param name="serviceMap">
        /// Optionally specifies a service map describing this service and potentially other services.
        /// Service maps can be used to run services locally on developer workstations via <b>Neon.Xunit.NeonServiceFixture</b>
        /// or other means to avoid port conflicts or to emulate a cluster of services without Kubernetes
        /// or containers.  This is a somewhat advanced topic that needs documentation.
        /// </param>
        /// <exception cref="KeyNotFoundException">
        /// Thrown if there is no service description for <paramref name="name"/>
        /// within the <see cref="ServiceMap"/>.
        /// </exception>
        public NeonService(
            string      name, 
            string      version        = null,
            string      statusFilePath = null,
            ServiceMap  serviceMap     = null)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(name), nameof(name));

            version = version ?? string.Empty;

            if (serviceMap != null)
            {
                if (!serviceMap.TryGetValue(name, out var description))
                {
                    throw new KeyNotFoundException($"The service map does not include a service definition for [{name}].");
                }
                else
                {
                    if (name != description.Name)
                    {
                        throw new ArgumentException($"Service [name={name}] does not match [description.Name={description.Name}.");
                    }

                    this.Description = description;
                }
            }

            if (string.IsNullOrEmpty(statusFilePath))
            {
                statusFilePath = null;
            }

            this.Name                 = name;
            this.InProduction         = !NeonHelper.IsDevWorkstation;
            this.Terminator           = new ProcessTerminator();
            this.version              = global::Neon.Diagnostics.LogManager.VersionRegex.IsMatch(version) ? version : "unknown";
            this.environmentVariables = new Dictionary<string, string>();
            this.configFiles          = new Dictionary<string, FileInfo>();
            this.statusFilePath       = statusFilePath;
            this.ServiceMap           = serviceMap;

            // Update the Prometheus metrics port from the service description if present.

            if (Description != null)
            {
                MetricsOptions.Port = Description.MetricsPort;
            }

            // Initialize the [neon_service_info] gauge.

            infoGauge.WithLabels(version).Set(1);
        }

        /// <summary>
        /// Used to specify other services that must be reachable via the network before a
        /// <see cref="NeonService"/> will be allowed to start.  This is exposed via the
        /// <see cref="NeonService.Dependencies"/> where these values can be configured in
        /// code before <see cref="NeonService.RunAsync(bool)"/> is called or they can
        /// also be configured via environment variables as described in <see cref="ServiceDependencies"/>.
        /// </summary>
        public ServiceDependencies Dependencies { get; set; } = new ServiceDependencies();

        /// <summary>
        /// Finalizer.
        /// </summary>
        ~NeonService()
        {
            Dispose(false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Releases all associated resources.
        /// </summary>
        /// <param name="disposing">Pass <c>true</c> if we're disposing, <c>false</c> if we're finalizing.</param>
        protected virtual void Dispose(bool disposing)
        {
            lock (syncLock)
            {
                if (isDisposed)
                {
                    return;
                }

                isDisposed = true;
            }

            Stop();

            lock(syncLock)
            {
                foreach (var item in configFiles.Values)
                {
                    item.Dispose();
                }

                if (disposing)
                {
                    GC.SuppressFinalize(this);
                }
            }
        }

        /// <summary>
        /// Returns <c>true</c> when the service is running in production,
        /// when the <b>DEV_WORKSTATION</b> environment variable is
        /// <b>not defined</b>.  The <c>NeonServiceFixure</c> will set this
        /// to <c>true</c> explicitly as well.
        /// </summary>
        public bool InProduction { get; internal set; }

        /// <summary>
        /// Returns <c>true</c> when the service is running in development
        /// or test mode, when the <b>DEV_WORKSTATION</b> environment variable 
        /// is <b>defined</b>.
        /// </summary>
        public bool InDevelopment => !InProduction;

        /// <summary>
        /// Returns the service name.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Returns the service map (if any).
        /// </summary>
        public ServiceMap ServiceMap { get; private set; }

        /// <summary>
        /// Returns the service description for this service (if any).
        /// </summary>
        public ServiceDescription Description { get; private set; }

        /// <summary>
        /// Returns GIT branch and commit the service was built from as
        /// well as an optional indication the the build branch had 
        /// uncomitted changes (e.g. was dirty).
        /// </summary>
        public string GitVersion { get; private set; }

        /// <summary>
        /// Returns the dictionary mapping case sensitive service endpoint names to endpoint information.
        /// </summary>
        public Dictionary<string, ServiceEndpoint> Endpoints => Description.Endpoints;

        /// <summary>
        /// <para>
        /// For services with exactly one network endpoint, this returns the base
        /// URI to be used to access the service.
        /// </para>
        /// <note>
        /// This will throw a <see cref="InvalidOperationException"/> if the service
        /// defines no endpoints or has multiple endpoints.
        /// </note>
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the service does not define exactly one endpoint or <see cref="Description"/> is not set.
        /// </exception>
        public Uri BaseUri
        {
            get
            {
                if (Description == null)
                {
                    throw new InvalidOperationException($"The {nameof(BaseUri)} property requires that [{nameof(Description)} be set and have exactly one endpoint.");
                }

                if (Description.Endpoints.Count == 1)
                {
                    return Description.Endpoints.First().Value.Uri;
                }
                else
                {
                    throw new InvalidOperationException($"The {nameof(BaseUri)} property requires that the service be defined with exactly one endpoint.");
                }
            }
        }

        /// <summary>
        /// <para>
        /// Prometheus metrics options.  To enable metrics collection for non-ASPNET applications,
        /// we recommend that you simply set <see cref="MetricsOptions.Mode"/><c>==</c><see cref="MetricsMode.Scrape"/>
        /// before calling <see cref="OnRunAsync"/>.
        /// </para>
        /// <para>
        /// See <see cref="MetricsOptions"/> for more details.
        /// </para>
        /// </summary>
        public MetricsOptions MetricsOptions { get; set; } = new MetricsOptions();

        /// <summary>
        /// Returns the service's log manager.
        /// </summary>
        public ILogManager LogManager { get; private set; }

        /// <summary>
        /// Returns the service's default logger.
        /// </summary>
        public INeonLogger Log { get; private set; }

        /// <summary>
        /// Returns the service's <see cref="ProcessTerminator"/>.  This can be used
        /// to handle termination signals.
        /// </summary>
        public ProcessTerminator Terminator { get; private set; }

        /// <summary>
        /// Returns the list of command line arguments passed to the service.  This
        /// defaults to an empty list.
        /// </summary>
        public List<string> Arguments { get; private set; } = new List<string>();

        /// <summary>
        /// Returns the service current running status.
        /// </summary>
        public NeonServiceStatus Status { get; private set; }

        /// <summary>
        /// Updates the service status.  This is typically called internally by this
        /// class but service code may set this to <see cref="NeonServiceStatus.Unhealthy"/>
        /// when there's a problem and back to <see cref="NeonServiceStatus.Running"/>
        /// when the service is healthy again.
        /// </summary>
        /// <param name="status">The new status.</param>
        public async Task SetStatusAsync(NeonServiceStatus status)
        {
            using (await asyncMutex.AcquireAsync())
            {
                if (this.Status == status)
                {
                    return;
                }

                this.Status = status;

                if (status == NeonServiceStatus.Unhealthy)
                {
                    Log.LogWarn($"[{Name}] status is now: {status}");
                }
                else
                {
                    Log.LogInfo($"[{Name}] status is now: {status}");
                }

                if (statusFilePath != null)
                {
                    // We're going to use a retry policy to handle the rare situations
                    // where the health poll and this method try to access this file 
                    // at the exact same moment.

                    var policy = new LinearRetryPolicy(e => e is IOException, maxAttempts: 10, retryInterval: TimeSpan.FromMilliseconds(100));

                    await policy.InvokeAsync(
                        async () =>
                        {
                            using (var output = new FileStream(statusFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                            {
                                await output.WriteAsync(Encoding.UTF8.GetBytes(NeonHelper.EnumToString(status)));
                            }
                        });
                }
            }
        }

        /// <summary>
        /// Returns the exit code returned by the service.
        /// </summary>
        public int ExitCode { get; private set; }

        /// <summary>
        /// Returns any abnormal exception thrown by the derived <see cref="OnRunAsync"/> method.
        /// </summary>
        public Exception ExitException { get; private set; }

        /// <summary>
        /// Initializes <see cref="Arguments"/> with the command line arguments passed.
        /// </summary>
        /// <param name="args">The arguments.</param>
        /// <returns>The service instance so developers can chain fluent style calls.</returns>
        public NeonService SetArguments(IEnumerable<string> args)
        {
            Covenant.Requires<ArgumentNullException>(args != null, nameof(args));

            Arguments.Clear();

            foreach (var arg in args)
            {
                Arguments.Add(arg);
            }

            return this;
        }

        /// <summary>
        /// Called by <see cref="OnRunAsync"/> implementation after they've completed any
        /// initialization and are ready for traffic.  This sets <see cref="Status"/> to
        /// <see cref="NeonServiceStatus.Running"/>.
        /// </summary>
        public async Task SetRunningAsync()
        {
            await SetStatusAsync(NeonServiceStatus.Running);
        }

        /// <summary>
        /// Starts the service if it's not already running.  This will call <see cref="OnRunAsync"/>,
        /// which is your code that actually implements the service.  Note that any service dependencies
        /// specified by <see cref="Dependencies"/> will be verified as ready before <see cref="OnRunAsync"/>
        /// will be called.
        /// </summary>
        /// <param name="disableProcessExit">
        /// Optionally specifies that the hosting process should not be terminated 
        /// when the service exists.  This is typically used for testing or debugging.
        /// This defaults to <c>false</c>.
        /// </param>
        /// <remarks>
        /// <note>
        /// For production, this method will not return until the service is expicitly 
        /// stopped via a call to <see cref="Stop"/> or the <see cref="Terminator"/> 
        /// handles a stop signal.  For test environments, this method will call
        /// <see cref="OnRunAsync"/> on a new thread and returns immediately while the
        /// service continues to run in parallel.
        /// </note>
        /// <para>
        /// Service implementations must honor <see cref="Terminator"/> termination
        /// signals by exiting the <see cref="OnRunAsync"/> method reasonably quickly (within
        /// 30 seconds by default) when these occur.  They can do this by passing 
        /// <see cref="ProcessTerminator.CancellationToken"/> for <c>async</c> calls
        /// and then catching the <see cref="TaskCanceledException"/> and returning
        /// from <see cref="OnRunAsync"/>.
        /// </para>
        /// <para>
        /// Another technique for synchronous code is to explicitly check the 
        /// <see cref="ProcessTerminator.CancellationToken"/> token's  
        /// <see cref="CancellationToken.IsCancellationRequested"/> property and 
        /// return from your <see cref="OnRunAsync"/> method when this is <c>true</c>.
        /// You'll need to perform this check frequently so you may need
        /// to use timeouts to prevent blocking code from blocking for too long.
        /// </para>
        /// </remarks>
        /// <returns>The service exit code.</returns>
        /// <remarks>
        /// <note>
        /// It is not possible to restart a service after it's been stopped.
        /// </note>
        /// </remarks>
        public async virtual Task<int> RunAsync(bool disableProcessExit = false)
        {
            lock (syncLock)
            {
                if (isRunning)
                {
                    throw new InvalidOperationException($"Service [{Name}] is already running.");
                }

                if (isDisposed)
                {
                    throw new InvalidOperationException($"Service [{Name}] cannot be restarted after it's been stopped.");
                }

                isRunning = true;
            }

            // [disableProcessExit] will be typically passed as true when testing or
            // debugging.  We'll let the terminator know so it won't actually terminate
            // the current process (which will actually be the unit test framework).

            if (disableProcessExit)
            {
                Terminator.DisableProcessExit = true;
            }

            // Initialize the log manager.

            if (GlobalLogging)
            {
                LogManager          = global::Neon.Diagnostics.LogManager.Default;
                LogManager.Version = version;
            }
            else
            {
                LogManager = new LogManager(parseLogLevel: false, version: this.version);
            }

            LogManager.SetLogLevel(GetEnvironmentVariable("LOG_LEVEL", "info"));

            Log = LogManager.GetLogger();

            if (!string.IsNullOrEmpty(version))
            {
                Log.LogInfo(() => $"Starting [{Name}:{version}]");
            }
            else
            {
                Log.LogInfo(() => $"Starting [{Name}]");
            }

            // Initialize Prometheus metrics when enabled.

            MetricsOptions = MetricsOptions ?? new MetricsOptions();
            MetricsOptions.Validate();

            try
            {
                switch (MetricsOptions.Mode)
                {
                    case MetricsMode.Disabled:

                        break;

                    case MetricsMode.Scrape:
                    case MetricsMode.ScrapeIgnoreErrors:

                        metricServer = new MetricServer(MetricsOptions.Port, MetricsOptions.Path);
                        metricServer.Start();
                        break;

                    case MetricsMode.Push:

                        metricPusher = new MetricPusher(MetricsOptions.PushUrl, job: Name, intervalMilliseconds: 100 /* (long)MetricsOptions.PushInterval.TotalMilliseconds */, additionalLabels: MetricsOptions.PushLabels);
                        metricPusher.Start();
                        break;

                    default:

                        throw new NotImplementedException();
                }

                if (MetricsOptions.GetCollector != null)
                {
                    metricCollector = MetricsOptions.GetCollector();
                }
            }
            catch (NotImplementedException)
            {
                throw;
            }
            catch
            {
                if (MetricsOptions.Mode != MetricsMode.ScrapeIgnoreErrors)
                {
                    throw;
                }
            }

            // Verify that any required service dependencies are ready.

            var dnsOptions = new LookupClientOptions()
            {
                ContinueOnDnsError      = false,
                ContinueOnEmptyResponse = false,
                Retries                 = 0,
                ThrowDnsErrors          = false,
                Timeout                 = TimeSpan.FromSeconds(2),
                UseTcpFallback          = true
            };

            var dnsClient         = new LookupClient(dnsOptions);
            var dnsAvailable      = Dependencies.DisableDnsCheck;
            var readyServices     = new HashSet<Uri>();
            var notReadyUri       = (Uri)null;
            var notReadyException = (Exception)null;

            try
            {
                await NeonHelper.WaitForAsync(
                    async () =>
                    {
                        // Verify DNS availability first because services won't be available anyway
                        // when there's no DNS.

                        if (!dnsAvailable)
                        {
                            try
                            {
                                await dnsClient.QueryAsync(ServiceDependencies.DnsCheckHostName, QueryType.A);

                                dnsAvailable = true;
                            }
                            catch (DnsResponseException e)
                            {
                                return e.Code == DnsResponseCode.ConnectionTimeout;
                            }
                        }

                        // Verify the service dependencies next.

                        foreach (var uri in Dependencies.Uris)
                        {
                            if (readyServices.Contains(uri))
                            {
                                continue;   // This one is already ready
                            }

                            switch (uri.Scheme.ToUpperInvariant())
                            {
                                case "HTTP":
                                case "HTTPS":
                                case "TCP":

                                    using (var socket = new Socket(SocketType.Stream, ProtocolType.Tcp))
                                    {
                                        try
                                        {
                                            var addresses = await Dns.GetHostAddressesAsync(uri.Host);

                                            socket.Connect(new IPEndPoint(addresses.First(), uri.Port));
                                        }
                                        catch (SocketException e)
                                        {
                                            // Remember these so we can log something useful if we end up timing out.

                                            notReadyUri = uri;
                                            notReadyException = e;

                                            return false;
                                        }
                                    }
                                    break;

                                default:

                                    Log.LogWarn($"Service Dependency: [{uri}] has an unsupported scheme and will be ignored.  Only HTTP, HTTPS, and TCP URIs are allowed.");
                                    readyServices.Add(uri);     // Add the bad URI so we won't try it again.
                                    break;
                            }
                        }

                        return true;
                    },
                    timeout: Dependencies.TestTimeout ?? Dependencies.Timeout,
                    pollInterval: TimeSpan.FromSeconds(1));
            }
            catch (TimeoutException)
            {
                // Report the problem and exit the service.

                Log.LogError($"Service Dependency: [{notReadyUri}] is still not ready after waiting [{Dependencies.Timeout}].", notReadyException);

                if (metricServer != null)
                {
                    await metricServer.StopAsync();
                    metricServer = null;
                }

                if (metricPusher != null)
                {
                    await metricPusher.StopAsync();
                    metricPusher = null;
                }

                if (metricCollector != null)
                {
                    metricCollector.Dispose();
                    metricCollector = null;
                }

                return ExitCode = 1;
            }

            await Task.Delay(Dependencies.Wait);

            // Start and run the service.

            try
            {
                await OnRunAsync();

                ExitCode = 0;
            }
            catch (TaskCanceledException)
            {
                // Ignore these as a normal consequence of a service
                // being signalled to terminate.

                ExitCode = 0;
            }
            catch (ProgramExitException e)
            {
                // Don't override a non-zero ExitCode that was set earlier
                // with a zero exit code.

                if (e.ExitCode != 0)
                {
                    ExitCode = e.ExitCode;
                }
            }
            catch (Exception e)
            {
                // We're going to consider any exceptions caught here to be errors
                // and return a non-zero exit code.  The service's [main()] method
                // can examine the [ExceptionException] property to decide whether
                // the exception should be considered an error or whether to return
                // a custom error code.

                ExitException = e;
                ExitCode      = 1;

                Log.LogError(e);
            }

            // Perform last rights for the service before it passes away.

            Log.LogInfo(() => $"Exiting [{Name}] with [exitcode={ExitCode}].");

            if (metricServer != null)
            {
                await metricServer.StopAsync();
                metricServer = null;
            }

            if (metricPusher != null)
            {
                await metricPusher.StopAsync();
                metricPusher = null;
            }

            if (metricCollector != null)
            {
                metricCollector.Dispose();
                metricCollector = null;
            }

            Terminator.ReadyToExit();

            await SetStatusAsync(NeonServiceStatus.Terminated);

            return ExitCode;
        }

        /// <summary>
        /// <para>
        /// Stops the service if it's not already stopped.  This is intended to be called by
        /// external things like unit test fixtures and is not intended to be called by the
        /// service itself.
        /// </para>
        /// </summary>
        /// <exception cref="TimeoutException">
        /// Thrown if the service did not exit gracefully in time before it would have 
        /// been killed (e.g. by Kubernetes or Docker).
        /// </exception>
        /// <remarks>
        /// <note>
        /// It is not possible to restart a service after it's been stopped.
        /// </note>
        /// <para>
        /// This is intended for internal use or managing unit test execution and is not intended 
        /// for use by the service to stop itself.
        /// </para>
        /// </remarks>
        public virtual void Stop()
        {
            lock (syncLock)
            {
                if (stopPending || !isRunning)
                {
                    return;
                }

                stopPending = true;
            }

            Terminator.Signal();
        }

        /// <summary>
        /// Used by services to stop themselves, specifying an optional process exit code.
        /// </summary>
        /// <param name="exitCode">The optional exit code (defaults to <b>0</b>).</param>
        /// <remarks>
        /// This works by setting <see cref="ExitCode"/> if <paramref name="exitCode"/> is non-zero,
        /// signalling process termination on another thread and then throwing a <see cref="ProgramExitException"/> 
        /// on the current thread.  This will generally cause the current thread or task to terminate
        /// immediately and any other properly implemented threads and tasks to terminate gracefully
        /// when they receive the termination signal.
        /// </remarks>
        public virtual void Exit(int exitCode = 0)
        {
            lock (syncLock)
            {
                if (exitCode != 0)
                {
                    ExitCode = exitCode;
                }

                new Thread(
                    new ThreadStart(
                        () =>
                        {
                            // $hack(jefflill):
                            //
                            // Give the Exit() method a bit of time to throw the 
                            // ProgramExitException to make termination handling
                            // a bit more deterministic.

                            Thread.Sleep(TimeSpan.FromSeconds(0.5));

                            try
                            {
                                Stop();
                            }
                            catch
                            {
                                // Ignoring any errors.
                            }

                        })).Start();

                throw new ProgramExitException(ExitCode);
            }
        }

        /// <summary>
        /// Called to actually implement the service.
        /// </summary>
        /// <returns>The the progam exit code.</returns>
        /// <remarks>
        /// <para>
        /// Services should perform any required initialization and then they must call <see cref="SetRunningAsync()"/>
        /// to indicate that the service should transition into the <see cref="NeonServiceStatus.Running"/>
        /// state.  This is very important because the service test fixture requires the service to be
        /// in the running state before it allows tests to proceed.  This is necessary to avoid unit test 
        /// race conditions.
        /// </para>
        /// <para>
        /// This method should return the program exit code or throw a <see cref="ProgramExitException"/>
        /// to exit with the program exit code.
        /// </para>
        /// </remarks>
        protected abstract Task<int> OnRunAsync();

        /// <summary>
        /// <para>
        /// Loads environment variables formatted as <c>NAME=VALUE</c> from a text file as service
        /// environment variables.  The file will be decrypted using <see cref="NeonVault"/> if necessary.
        /// </para>
        /// <note>
        /// Blank lines and lines beginning with '#' will be ignored.
        /// </note>
        /// </summary>
        /// <param name="path">The input file path.</param>
        /// <param name="passwordProvider">
        /// Optionally specifies the password provider function to be used to locate the
        /// password required to decrypt the source file when necessary.  The password will 
        /// use a default password provider <paramref name="passwordProvider"/> is <c>null</c>.
        /// See the remarks below.
        /// </param>
        /// <exception cref="FileNotFoundException">Thrown if the file doesn't exist.</exception>
        /// <exception cref="FormatException">Thrown for file formatting problems.</exception>
        /// <remarks>
        /// <para>
        /// The default password provider assumes that you have neonDESKTOP installed and may be
        /// specifying passwords in the `~/.neonkube/passwords` folder (relative to the current
        /// user's home directory).  This will be harmless if you don't have neonDESKTOP installed;
        /// it just probably won't find any passwords.
        /// </para>
        /// <para>
        /// Implement a custom password provider function if you need something different.
        /// </para>
        /// </remarks>
        public void LoadEnvironmentVariables(string path, Func<string, string> passwordProvider = null)
        {
            passwordProvider = passwordProvider ?? LookupPassword;

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"File not found: {path}");
            }

            var vault = new NeonVault(passwordProvider);
            var bytes = vault.Decrypt(path);

            using (var ms = new MemoryStream(bytes))
            {
                using (var reader = new StreamReader(ms))
                {
                    var lineNumber = 1;

                    foreach (var rawLine in reader.Lines())
                    {
                        var line = rawLine.Trim();

                        if (line.Length == 0 || line.StartsWith("#"))
                        {
                            continue;
                        }

                        var fields = line.Split(equalArray, 2);

                        if (fields.Length != 2)
                        {
                            throw new FormatException($"[{path}:{lineNumber}]: Invalid input: {line}");
                        }

                        var name  = fields[0].Trim();
                        var value = fields[1].Trim();

                        if (name.Length == 0)
                        {
                            throw new FormatException($"[{path}:{lineNumber}]: Setting name cannot be blank.");
                        }

                        SetEnvironmentVariable(name, value);
                    }
                }
            }
        }

        /// <summary>
        /// Sets or deletes a service environment variable.
        /// </summary>
        /// <param name="name">The variable name (case sensitive).</param>
        /// <param name="value">The variable value or <c>null</c> to remove the variable.</param>
        /// <returns>The service instance so developers can chain fluent style calls.</returns>
        /// <remarks>
        /// <note>
        /// Environment variable names are to be considered to be case sensitive since
        /// this is how Linux treats them and it's very common to be deploying services
        /// to Linux.
        /// </note>
        /// </remarks>
        public NeonService SetEnvironmentVariable(string name, string value)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(name), nameof(name));

            lock (syncLock)
            {
                if (value == null)
                {
                    if (environmentVariables.ContainsKey(name))
                    {
                        environmentVariables.Remove(name);
                    }
                }
                else
                {
                    environmentVariables[name] = value;
                }
            }

            return this;
        }

        /// <summary>
        /// Returns the value of an environment variable.
        /// </summary>
        /// <param name="name">The environment variable name (case sensitive).</param>
        /// <param name="def">The value to be returned when the environment variable doesn't exist (defaults to <c>null</c>).</param>
        /// <returns>The variable value or <paramref name="def"/> if the variable doesn't exist.</returns>
        public string GetEnvironmentVariable(string name, string def = null)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(name), nameof(name));

            lock (syncLock)
            {
                if (InProduction)
                {
                    return Environment.GetEnvironmentVariable(name) ?? def;
                }

                if (environmentVariables.TryGetValue(name, out var value))
                {
                    return value;
                }
                else
                {
                    return def;
                }
            }
        }

        /// <summary>
        /// Maps a logical configuration file path to an actual file on the
        /// local machine.  This is used for unit testing to map a file on
        /// the local workstation to the path where the service expects the
        /// find to be.
        /// </summary>
        /// <param name="logicalPath">The logical file path (typically expressed as a Linux path).</param>
        /// <param name="physicalPath">The physical path to the file on the local workstation.</param>
        /// <param name="passwordProvider">
        /// Optionally specifies the password provider function to be used to locate the
        /// password required to decrypt the source file when necessary.  The password will 
        /// use a default password provider <paramref name="passwordProvider"/> is <c>null</c>.
        /// See the remarks below.
        /// </param>
        /// <returns>The service instance so developers can chain fluent style calls.</returns>
        /// <exception cref="FileNotFoundException">Thrown if there's no file at <paramref name="physicalPath"/>.</exception>
        /// <remarks>
        /// <para>
        /// The default password provider assumes that you have neonDESKTOP installed and may be
        /// specifying passwords in the `~/.neonkube/passwords` folder (relative to the current
        /// user's home directory).  This will be harmless if you don't have neonDESKTOP installed;
        /// it just probably won't find any passwords.
        /// </para>
        /// <para>
        /// Implement a custom password provider function if you need something different.
        /// </para>
        /// </remarks>
        public NeonService SetConfigFilePath(string logicalPath, string physicalPath, Func<string, string> passwordProvider = null)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(logicalPath), nameof(logicalPath));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(physicalPath), nameof(physicalPath));

            if (!File.Exists(physicalPath))
            {
                throw new FileNotFoundException($"Physical configuration file [{physicalPath}] does not exist.");
            }

            passwordProvider = passwordProvider ?? LookupPassword;

            var vault = new NeonVault(passwordProvider);
            var bytes = vault.Decrypt(physicalPath);

            SetConfigFile(logicalPath, bytes);

            return this;
        }

        /// <summary>
        /// Maps a logical configuration file path to a temporary file holding the
        /// string contents passed encoded as UTF-8.  This is typically used for
        /// initializing confguration files for unit testing.
        /// </summary>
        /// <param name="logicalPath">The logical file path (typically expressed as a Linux path).</param>
        /// <param name="contents">The content string.</param>
        /// <param name="linuxLineEndings">
        /// Optionally convert any Windows style line endings (CRLF) into Linux 
        /// style endings (LF).  This defaults to <c>false</c>.
        /// </param>
        /// <returns>The service instance so developers can chain fluent style calls.</returns>
        public NeonService SetConfigFile(string logicalPath, string contents, bool linuxLineEndings = false)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(logicalPath), nameof(logicalPath));
            Covenant.Requires<ArgumentNullException>(contents != null, nameof(contents));

            if (linuxLineEndings)
            {
                contents = contents.Replace("\r\n", "\n");
            }

            lock (syncLock)
            {
                if (configFiles.TryGetValue(logicalPath, out var fileInfo))
                {
                    fileInfo.Dispose();
                }

                var tempFile = new TempFile();

                File.WriteAllText(tempFile.Path, contents);

                configFiles[logicalPath] = new FileInfo()
                {
                    PhysicalPath = tempFile.Path,
                    TempFile     = tempFile
                };
            }

            return this;
        }

        /// <summary>
        /// Maps a logical configuration file path to a temporary file holding the
        /// byte contents passed.  This is typically used initializing confguration
        /// files for unit testing.
        /// </summary>
        /// <param name="logicalPath">The logical file path (typically expressed as a Linux path).</param>
        /// <param name="contents">The content bytes.</param>
        /// <returns>The service instance so developers can chain fluent style calls.</returns>
        public NeonService SetConfigFile(string logicalPath, byte[] contents)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(logicalPath), nameof(logicalPath));
            Covenant.Requires<ArgumentNullException>(contents != null, nameof(contents));

            lock (syncLock)
            {
                if (configFiles.TryGetValue(logicalPath, out var fileInfo))
                {
                    fileInfo.Dispose();
                }

                var tempFile = new TempFile();

                File.WriteAllBytes(tempFile.Path, contents);

                configFiles[logicalPath] = new FileInfo()
                {
                    PhysicalPath = tempFile.Path,
                    TempFile     = tempFile
                };
            }

            return this;
        }

        /// <summary>
        /// Returns the physical path for the confguration file whose logical path is specified.
        /// </summary>
        /// <param name="logicalPath">The logical file path (typically expressed as a Linux path).</param>
        /// <returns>The physical path for the configuration file or <c>null</c> if the logical file path is not present.</returns>
        /// <remarks>
        /// <note>
        /// This method does not verify that the physical file actually exists.
        /// </note>
        /// </remarks>
        public string GetConfigFilePath(string logicalPath)
        {
            lock (syncLock)
            {
                if (InProduction)
                {
                    return logicalPath;
                }

                if (!configFiles.TryGetValue(logicalPath, out var fileInfo))
                {
                    return null;
                }

                return fileInfo.PhysicalPath;
            }
        }
    }
}
