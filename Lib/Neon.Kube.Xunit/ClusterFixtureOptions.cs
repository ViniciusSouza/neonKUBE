﻿//-----------------------------------------------------------------------------
// FILE:	    ClusterFixtureOptions.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2005-2022 by neonFORGE LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using k8s;
using k8s.KubeConfigModels;

using Newtonsoft.Json.Linq;

using Xunit;
using Xunit.Abstractions;

using Neon.Common;
using Neon.Data;
using Neon.Deployment;
using Neon.IO;
using Neon.Retry;
using Neon.Net;
using Neon.SSH;
using Neon.Xunit;
using Neon.Tasks;

namespace Neon.Kube.Xunit
{
    /// <summary>
    /// Options for the <see cref="ClusterFixture"/>.
    /// </summary>
    public class ClusterFixtureOptions
    {
        /// <summary>
        /// Constructor.  This initializes the properties to reasonable defaults.
        /// </summary>
        public ClusterFixtureOptions()
        {
        }

        /// <summary>
        /// Specifies the options that <see cref="ClusterFixture.ResetCluster()"/> will use when
        /// resetting the target cluster.  This defaults to the stock <see cref="ClusterResetOptions"/>
        /// which performs a full cluster reset.
        /// </summary>
        public ClusterResetOptions ResetOptions { get; set; } = new ClusterResetOptions();

        /// <summary>
        /// Optionally disables the redaction of potentially sensitive information from cluster
        /// deployment logs.  This defaults to <c>false</c>.
        /// </summary>
        public bool Unredacted { get; set; } = false;

        /// <summary>
        /// Forces the removal of any existing cluster when the associated <see cref="ClusterFixture"/> 
        /// is started.  This defaults to <c>false</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For hypervisor hosted clusters, setting this to <c>true</c> turn off and remove all VMs 
        /// that have the same name prefix as is specified by the test cluster definition and for cloud 
        /// hosted clusters, the specified resource group will be removed.  This ensures that even 
        /// partially deployed clusters can be removed.
        /// </para>s
        /// <note>
        /// An existing cluster that cannot be managed via an existing kubecontext and cluster login
        /// will always be removed.  For clusters that can be managed, <see cref="ClusterFixture"/>
        /// will compare the cluster definition of the existing cluster with the definition for the
        /// specified test cluster and when these are different, <see cref="ClusterFixture"/> will
        /// remove the existing cluster when <see cref="RemoveClusterOnStart"/> is <c>true</c> or
        /// throw a <see cref="NeonKubeException"/> when <see cref="RemoveClusterOnStart"/> is
        /// <c>false</c>.
        /// </note>
        /// </remarks>
        public bool RemoveClusterOnStart { get; set; } = false;

        /// <summary>
        /// Controls whether <see cref="ClusterFixture"/> will remove the cluster after unit test
        /// have finished executing or whether it will delete it.  This defaults to <c>false</c>.
        /// </summary>
        public bool RemoveClusterOnDispose { get; set; } = false;

        /// <summary>
        /// Optionally specifies a <see cref="ITestOutputHelper"/> where <see cref="ClusterFixture"/>
        /// can write diagnostics including the cluster deployment logs.
        /// </summary>
        public ITestOutputHelper TestOutputHelper { get; set; } = null;

        /// <summary>
        /// Optionally specifies the URI or file path to the node image to be used when deploying the cluster.
        /// This is used to override the default published node image.  This default to <c>null</c>.
        /// </summary>
        public string ImageUriOrPath { get; set; } = null;

        /// <summary>
        /// Optionally overrides the default neonCLOUD headend URI.  This defaults to <c>null</c>.
        /// </summary>
        public string NeonCloudHeadendUri { get; set; } = null;

        /// <summary>
        /// Optionally write the cluster deployment logs to <see cref="TestOutputHelper"/>.
        /// This defaults to <c>true</c>.
        /// </summary>
        public bool CaptureDeploymentLogs { get; set; } = true;

        /// <summary>
        /// Optionally specifies the maximum number of operations to be performed in parallel.
        /// This defaults to <c>500</c> which is effectively infinite.
        /// </summary>
        public int MaxParallel { get; set; } = 500;
    }
}
