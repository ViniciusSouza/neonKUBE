﻿//-----------------------------------------------------------------------------
// FILE:	    KubeSetupProperty.cs
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

using Neon.Common;

using k8s;

namespace Neon.Kube
{
    /// <summary>
    /// Identifies the cluster setup state available in an <see cref="ISetupController"/>.
    /// </summary>
    public static class KubeSetupProperty
    {
        /// <summary>
        /// <para>
        /// Property name for accessing a <c>bool</c> that indicates that we're preparing a cluster vs.
        /// setting one up.
        /// </para>
        /// </summary>
        public const string Preparing = "preparing";

        /// <summary>
        /// <para>
        /// Property name for accessing a <c>bool</c> that indicates that we're running cluster prepare/setup in <b>debug mode</b>.
        /// In debug mode, setup works like it did in the past, where we deployed the base node image first and then 
        /// configured the node from that, rather than starting with the node image with assets already prepositioned.
        /// </para>
        /// <para>
        /// This mode is useful when debugging cluster setup or adding new features.
        /// </para>
        /// </summary>
        public const string DebugMode = "debug-setup";

        /// <summary>
        /// <para>
        /// Property name for accessing a <c>bool</c> that indicates that we're running cluster prepare/setup in <b>release mode</b>.
        /// </para>
        /// </summary>
        public const string ReleaseMode = "release-setup";

        /// <summary>
        /// <para>
        /// Property name for accessing a <c>bool</c> that indicates that we're running cluster prepare/setup in <b>maintainer mode</b>.
        /// </para>
        /// </summary>
        public const string MaintainerMode = "maintainer-setup";

        /// <summary>
        /// Property name for a <c>bool</c> that identifies the base image name to be used for preparing
        /// a cluster in <b>debug mode</b>.  This is the name of the base image file as persisted to our
        /// public S3 bucket.  This will not be set for cluster setup.
        /// </summary>
        public const string BaseImageName = "base-image-name";

        /// <summary>
        /// Property name for determining the current hosting environment: <see cref="Kube.HostingEnvironment"/>,
        /// </summary>
        public const string HostingEnvironment = "hosting-environment";

        /// <summary>
        /// Property name for accessing the <see cref="SetupController{NodeMetadata}"/>'s <see cref="Kube.ClusterProxy"/> property.
        /// </summary>
        public const string ClusterProxy = "cluster-proxy";

        /// <summary>
        /// Property name for accessing the <see cref="SetupController{NodeMetadata}"/>'s <see cref="Kube.ClusterLogin"/> property.
        /// </summary>
        public const string ClusterLogin = "cluster-login";

        /// <summary>
        /// Property name for accessing the <see cref="SetupController{NodeMetadata}"/>'s <see cref="IHostingManager"/> property.
        /// </summary>
        public const string HostingManager = "hosting-manager";

        /// <summary>
        /// Property name for accessing the <see cref="SetupController{NodeMetadata}"/>'s <see cref="Kubernetes"/> client property.
        /// </summary>
        public const string K8sClient = "k8sclient";

        /// <summary>
        /// Property name for accessing the <see cref="SetupController{NodeMetadata}"/>'s <see cref="KubeClusterAdvice"/> client property.
        /// </summary>
        public const string ClusterAdvice = "setup-advice";

        /// <summary>
        /// Property name for accessing the fully qualified path to the local folder for the current
        /// clusterspace where cluster deployment operation state will be persisted, such as the Kubernetes 
        /// config file, neonKUBE cluster login and operation logs.
        /// </summary>
        /// <remarks>
        /// <note>
        /// <para>
        /// Clusterspaces are used by the <b>neon cluster prepare/setup</b> commands using the
        /// <b>--clusterspace</b> option as well as clusters provisioned for unit testing via <b>ClusterFixture</b>.
        /// This will be set to <c>null</c> for normal cluster deployments performed by neonKUBE or <b>neon-cli</b>
        /// without the <b>--clusterspace</b> option.
        /// </para>
        /// <para>
        /// Clusterspaces are used to workaround the restriction that allow only a single cluster to
        /// be logged in at any time and also that there be no logged-in cluster before a new cluster
        /// may be provisioned.  This allows cluster related CI/CD operations or unit tests to be able
        /// to execute without impacting normal user clusters.
        /// </para>
        /// </note>
        /// </remarks>
        public const string ClusterspaceFolder = "clusterspace-folder";

        /// <summary>
        /// Property name for accessing the neonCLOUD headend service base URI. This can be overridden
        /// for development purposes.
        /// </summary>
        public const string NeonCloudHeadendUri = "neoncloud-headend-uri";

        /// <summary>
        /// Property name for a boolean indicating that the node image has already been downloaded
        /// (e.g. by neonDESKTOP) and does not need to be downloaded hosting managers during cluster
        /// provisioning.  Image downloading should be considered to be enabled when this property
        /// is not present.
        /// </summary>
        public const string DisableImageDownload = "image-download-disabled";

        /// <summary>
        /// Property name for the IP address of the cluster. This is used to create the <b>neoncluster.io</b> 
        /// DNS subdomain pointing to the cluster.
        /// </summary>
        public const string ClusterIp = "cluster-ip";

        /// <summary>
        /// Property name for a boolean indicating whether secrets should be redacted when logging
        /// during cluster setup.  This should be generally set to <c>true</c> for production
        /// deployments.
        /// </summary>
        public const string Redact = "redact";

        /// <summary>
        /// <para>
        /// Property name for a <see cref="Credentials"/> object holding the username and password
        /// to be used to authenticate <b>podman</b> on the cluster node with the local Harbor
        /// registry.
        /// </para>
        /// <note>
        /// Token based credentials are not supported.
        /// </note>
        /// </summary>
        public const string HarborCredentials = "harbor-credentials";
    }
}
