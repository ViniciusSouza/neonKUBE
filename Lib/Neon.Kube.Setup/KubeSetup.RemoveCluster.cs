﻿//-----------------------------------------------------------------------------
// FILE:	    KubeSetup.RemoveCluster.cs
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
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using ICSharpCode.SharpZipLib.Zip;
using k8s;
using k8s.Models;
using Microsoft.Rest;
using Newtonsoft.Json.Linq;

using Neon.Collections;
using Neon.Common;
using Neon.Cryptography;
using Neon.IO;
using Neon.Retry;
using Neon.SSH;
using Neon.Tasks;

namespace Neon.Kube
{
    public static partial class KubeSetup
    {
        /// <summary>
        /// Constructs the <see cref="ISetupController"/> to be used for deprovisioning a cluster.
        /// </summary>
        /// <param name="clusterLogin">The cluster login.</param>
        /// <param name="maxParallel">
        /// Optionally specifies the maximum number of node operations to be performed in parallel.
        /// This <b>defaults to 500</b> which is effectively infinite.
        /// </param>
        /// <param name="unredacted">
        /// Optionally indicates that sensitive information <b>won't be redacted</b> from the setup logs 
        /// (typically used when debugging).
        /// </param>
        /// <returns>The <see cref="ISetupController"/>.</returns>
        /// <exception cref="NeonKubeException">Thrown when there's a problem.</exception>
        public static ISetupController CreateClusterRemoveController(
            ClusterLogin        clusterLogin,
            int                 maxParallel = 500,
            bool                unredacted  = false)
        {
            Covenant.Requires<ArgumentNullException>(clusterLogin != null, nameof(clusterLogin));
            Covenant.Requires<ArgumentException>(maxParallel > 0, nameof(maxParallel));

            // Determine where the log files should go.

            var logFolder = KubeHelper.LogFolder;

            // Initialize the cluster proxy.

            var cluster = new ClusterProxy(
                clusterDefinition:      clusterLogin.ClusterDefinition,
                hostingManagerFactory:  new HostingManagerFactory(() => HostingLoader.Initialize()),
                operation:              ClusterProxy.Operation.LifeCycle,
                nodeProxyCreator:       (nodeName, nodeAddress) =>
                {
                    var logStream      = new FileStream(Path.Combine(logFolder, $"{nodeName}.log"), FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite);
                    var logWriter      = new StreamWriter(logStream);
                    var sshCredentials = SshCredentials.FromUserPassword(KubeConst.SysAdminUser, KubeConst.SysAdminPassword);

                    return new NodeSshProxy<NodeDefinition>(nodeName, nodeAddress, sshCredentials, logWriter: logWriter);
                });

            if (unredacted)
            {
                cluster.SecureRunOptions = RunOptions.None;
            }

            // Configure the setup controller.

            var controller = new SetupController<NodeDefinition>($"Removing [{cluster.Definition.Name}] cluster infrastructure", cluster.Nodes, KubeHelper.LogFolder, disableConsoleOutput: true)
            {
                MaxParallel     = maxParallel,
                LogBeginMarker  = "# CLUSTER-BEGIN-REMOVE ###########################################################",
                LogEndMarker    = "# CLUSTER-END-REMOVE-SUCCESS #####################################################",
                LogFailedMarker = "# CLUSTER-END-REMOVE-FAILED ######################################################"
            };

            // Configure the hosting manager.

            var hostingManager = new HostingManagerFactory(() => HostingLoader.Initialize()).GetManagerWithNodeImageUri(cluster, logFolder);

            if (hostingManager == null)
            {
                throw new NeonKubeException($"No hosting manager for the [{cluster.Definition.Hosting.Environment}] environment could be located.");
            }

            // Configure the setup controller state.

            controller.Add(KubeSetupProperty.ReleaseMode, KubeHelper.IsRelease);
            controller.Add(KubeSetupProperty.DebugMode, false);
            controller.Add(KubeSetupProperty.MaintainerMode, !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NC_ROOT")));
            controller.Add(KubeSetupProperty.ClusterProxy, cluster);
            controller.Add(KubeSetupProperty.ClusterLogin, clusterLogin);
            controller.Add(KubeSetupProperty.HostingManager, hostingManager);
            controller.Add(KubeSetupProperty.HostingEnvironment, hostingManager.HostingEnvironment);

            // Add the hosting manager's deprovisioning steps.

            hostingManager.AddDeprovisoningSteps(controller);

            return controller;
        }
    }
}
