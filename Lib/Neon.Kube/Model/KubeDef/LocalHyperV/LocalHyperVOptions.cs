﻿//-----------------------------------------------------------------------------
// FILE:	    LocalHyperVOptions.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.

using System;
using System.ComponentModel;
using System.Diagnostics.Contracts;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Neon.Kube
{
    /// <summary>
    /// Specifies hosting settings for the local Microsoft Hyper-V hypervisor.
    /// </summary>
    public class LocalHyperVOptions
    {
        private const string defaultHostVhdxUri = "https://s3-us-west-2.amazonaws.com/neonforge/neoncluster/neon-Ubuntu-18.04.latest.vhdx";

        /// <summary>
        /// Default constructor.
        /// </summary>
        public LocalHyperVOptions()
        {
        }

        /// <summary>
        /// <para>
        /// URI to the zipped VHDX image with the base Docker host operating system.  This defaults to
        /// <b>https://s3-us-west-2.amazonaws.com/neonforge/neoncluster/neon-Ubuntu-18.04.latest.vhdx</b>
        /// which is the latest supported Ubuntu 16.04 image.
        /// </para>
        /// <note>
        /// Production cluster definitions should be configured with an VHDX with a specific version
        /// of the host operating system to ensure that cluster nodes are provisioned with the same
        /// operating system version.
        /// </note>
        /// </summary>
        [JsonProperty(PropertyName = "HostVhdxUri", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(defaultHostVhdxUri)]
        public string HostVhdxUri { get; set; } = defaultHostVhdxUri;

        /// <summary>
        /// Validates the options and also ensures that all <c>null</c> properties are
        /// initialized to their default values.
        /// </summary>
        /// <param name="kubeDefinition">The cluster definition.</param>
        /// <exception cref="KubeDefinitionException">Thrown if the definition is not valid.</exception>
        [Pure]
        public void Validate(KubeDefinition kubeDefinition)
        {
            Covenant.Requires<ArgumentNullException>(kubeDefinition != null);

            if (!kubeDefinition.Network.StaticIP)
            {
                throw new KubeDefinitionException($"[{nameof(NetworkOptions)}.{nameof(NetworkOptions.StaticIP)}] must be [true] when deploying to Hyper-V.");
            }

            if (string.IsNullOrEmpty(HostVhdxUri) || !Uri.TryCreate(HostVhdxUri, UriKind.Absolute, out Uri uri))
            {
                throw new KubeDefinitionException($"[{nameof(LocalHyperVOptions)}.{nameof(HostVhdxUri)}] is required when deploying to Hyper-V.");
            }

            kubeDefinition.ValidatePrivateNodeAddresses();                                          // Private node IP addresses must be assigned and valid.
            kubeDefinition.Hosting.ValidateHypervisor(kubeDefinition, remoteHypervisors: false);    // Hypervisor options must be valid.
        }
    }
}
