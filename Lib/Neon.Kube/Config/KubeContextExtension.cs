﻿//-----------------------------------------------------------------------------
// FILE:	    KubeContextExtension.cs
// CONTRIBUTOR: Jeff Lill
// COPYRIGHT:	Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using YamlDotNet.Serialization;

using Neon.Common;
using Neon.Cryptography;

namespace Neon.Kube
{
    /// <summary>
    /// Holds extended cluster information such as the cluster definition and
    /// node SSH credentials.  These records are persisted as files to the 
    /// <b>$HOME/.neonkube/contexts</b> folder in YAML files named like
    /// <b><i>NAME</i>.context.yaml</b>, where <i>NAME</i> identifies the 
    /// associated Kubernetes context.
    /// </summary>
    public class KubeContextExtension
    {
        private string path;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <param name="path">Optionally specifies the path to the extension file.</param>
        public KubeContextExtension(string path = null)
        {
            this.path = path;
        }

        /// <summary>
        /// The cluster definition.
        /// </summary>
        [JsonProperty(PropertyName = "clusterDefinition", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [YamlMember(Alias = "clusterDefinition", ApplyNamingConventions = false)]
        [DefaultValue(null)]
        public ClusterDefinition ClusterDefinition { get; set; }

        /// <summary>
        /// Indicates whether provisioning is complete but setup is still
        /// pending for this cluster
        /// </summary>
        [JsonProperty(PropertyName = "SetupPending", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [YamlMember(Alias = "SetupPending", ApplyNamingConventions = false)]
        [DefaultValue(false)]
        public bool SetupPending { get; set; } = false;

        /// <summary>
        /// The SSH root username.
        /// </summary>
        [JsonProperty(PropertyName = "SshUsername", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [YamlMember(Alias = "SshUsername", ApplyNamingConventions = false)]
        [DefaultValue(null)]
        public string SshUsername { get; set; }

        /// <summary>
        /// The SSH root password.
        /// </summary>
        [JsonProperty(PropertyName = "SshPassword", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [YamlMember(Alias = "SshPassword", ApplyNamingConventions = false)]
        [DefaultValue(null)]
        public string SshPassword { get; set; }

        /// <summary>
        /// Returns a <see cref="SshCredentials"/> instance suitable for connecting to
        /// a clsueter node.
        /// </summary>
        [JsonIgnore]
        [YamlIgnore]
        public SshCredentials SshCredentials => SshCredentials.FromUserPassword(SshUsername, SshPassword);

        /// <summary>
        /// Indicates whether a strong host SSH password was generated for the cluster.
        /// This defaults to <c>false</c>.
        /// </summary>
        [JsonProperty(PropertyName = "HasStrongSshPassword", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [YamlMember(Alias = "HasStrongSshPassword", ApplyNamingConventions = false)]
        [DefaultValue(false)]
        public bool HasStrongSshPassword { get; set; }

        /// <summary>
        /// The SSH RSA private key fingerprint used to secure the cluster nodes.  This is a
        /// MD5 hash encoded as hex bytes separated by colons.
        /// </summary>
        [JsonProperty(PropertyName = "SshNodeFingerprint")]
        [YamlMember(Alias = "SshNodeFingerprint", ApplyNamingConventions = false)]
        [DefaultValue(false)]
        public string SshNodeFingerprint { get; set; }

        /// <summary>
        /// The SSH RSA private key used to secure the cluster nodes.
        /// </summary>
        [JsonProperty(PropertyName = "SshNodePrivateKey")]
        [YamlMember(Alias = "SshNodePrivateKey", ApplyNamingConventions = false)]
        [DefaultValue(false)]
        public string SshNodePrivateKey { get; set; }

        /// <summary>
        /// The SSH RSA private key used to secure the cluster nodes.
        /// </summary>
        [JsonProperty(PropertyName = "SshNodePublicKey")]
        [YamlMember(Alias = "SshNodePublicKey", ApplyNamingConventions = false)]
        [DefaultValue(false)]
        public string SshNodePublicKey { get; set; }

        /// <summary>
        /// The public and private parts of the SSH client key used to
        /// authenticate an SSH session with a cluster node.
        /// </summary>
        [JsonProperty(PropertyName = "SshClientKey", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [YamlMember(Alias = "SshClientKey", ApplyNamingConventions = false)]
        [DefaultValue(null)]
        public SshClientKey SshClientKey { get; set; }

        /// <summary>
        /// The token required to join a node to the cluster.
        /// </summary>
        [JsonProperty(PropertyName = "ClusterJoinToken", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [YamlMember(Alias = "ClusterJoinToken", ApplyNamingConventions = false)]
        [DefaultValue(null)]
        public string ClusterJoinToken { get; set; }

        /// <summary>
        /// <para>
        /// Persists the extension data.
        /// </para>
        /// <note>
        /// A valid path must have been passed to the constructor for this to work.
        /// </note>
        /// </summary>
        public void Save()
        {
            File.WriteAllText(path, NeonHelper.YamlSerialize(this));
        }
    }
}
