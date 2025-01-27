﻿//-----------------------------------------------------------------------------
// FILE:	    ClusterStopCommand.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2022 by neonFORGE LLC.  All rights reserved.
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
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using k8s;
using k8s.Models;

using Microsoft.Extensions.DependencyInjection;

using Neon.Common;
using Neon.Cryptography;
using Neon.Deployment;
using Neon.IO;
using Neon.Kube;
using Neon.Net;
using Neon.Retry;
using Neon.SSH;
using Neon.Time;

namespace NeonCli
{
    /// <summary>
    /// Implements the <b>cluster stop</b> command.
    /// </summary>
    [Command]
    public class ClusterStopCommand : CommandBase
    {
        private const string usage = @"
Starts a stopped or paused cluster.  This is not supported by all environments.

USAGE:

    neon cluster stop [--turnoff] [--force]

OPTIONS:

    --turnoff   - Turns the nodes off immediately without allowing
                  a graceful shutdown.  This may cause data loss.

    --force     - forces cluster stop without user confirmation

REMARKS:

This command will not work on a locked clusters as a safety measure.  The idea
it to add some friction to avoid impacting production clusters by accident.

NOTE: [--force] DOES NOT OVERRIDE THE LOCK

All clusters besides neon-desktop built-in clusters are locked by default when
they're deployed.  You can disable this by setting [IsLocked=false] in your
cluster definition or by executing this command on your cluster:

    neon cluster unlock

";
        /// <inheritdoc/>
        public override string[] Words => new string[] { "cluster", "stop" };

        /// <inheritdoc/>
        public override string[] ExtendedOptions => new string[] { "--turnoff", "--force" };

        /// <inheritdoc/>
        public override void Help()
        {
            Console.WriteLine(usage);
        }

        /// <inheritdoc/>
        public override async Task RunAsync(CommandLine commandLine)
        {
            if (commandLine.HasHelpOption)
            {
                Help();
                Program.Exit(0);
            }

            var context = KubeHelper.CurrentContext;

            if (context == null)
            {
                Console.Error.WriteLine($"*** ERROR: There is no current cluster.");
                Program.Exit(1);
            }

            // $todo(jefflill): Temporarily disabling this command:
            //
            //      https://github.com/nforgeio/neonKUBE/issues/1281

            Console.Error.WriteLine("*** ERROR: The [neon cluster stop] command is not fully implemented yet.");
            Program.Exit(1);

            var turnoff = commandLine.HasOption("--turnoff");
            var force   = commandLine.HasOption("--force");

            using (var cluster = new ClusterProxy(context, new HostingManagerFactory()))
            {
                var status = await cluster.GetClusterStatusAsync();

                switch (status.State)
                {
                    case ClusterState.Healthy:
                    case ClusterState.Unhealthy:

                        var isLocked = await cluster.IsLockedAsync();

                        if (!isLocked.HasValue)
                        {
                            Console.Error.WriteLine($"*** ERROR: [{cluster.Name}] lock status is unknown.");
                            Program.Exit(1);
                        }

                        if (isLocked.Value)
                        {
                            Console.Error.WriteLine($"*** ERROR: [{cluster.Name}] is locked.");
                            Program.Exit(1);
                        }

                        var capabilities = cluster.Capabilities;

                        if ((capabilities & HostingCapabilities.Stoppable) == 0)
                        {
                            Console.Error.WriteLine($"*** ERROR: Cluster is not stoppable.");
                            Program.Exit(1);
                        }

                        if (!force)
                        {
                            if (!Program.PromptYesNo($"Are you sure you want to stop: {cluster.Name}?"))
                            {
                                Program.Exit(0);
                            }
                        }

                        await cluster.StopAsync(turnoff ? StopMode.TurnOff : StopMode.Graceful);
                        Console.WriteLine($"Cluster stopped: {cluster.Name}");
                        break;

                    default:

                        Console.Error.WriteLine($"*** ERROR: Cluster is not running.");
                        Program.Exit(1);
                        break;
                }
            }
        }
    }
}
