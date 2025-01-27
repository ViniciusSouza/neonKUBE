﻿//-----------------------------------------------------------------------------
// FILE:	    FeatureOptions.cs
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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using YamlDotNet.Serialization;

using Neon.Common;
using Neon.IO;

namespace Neon.Kube
{
    /// <summary>
    /// Specifies which optional cluster features should be deployed.
    /// </summary>
    public class FeatureOptions
    {
        //---------------------------------------------------------------------
        // Local types

        /// <summary>
        /// Specifies which optional Harbor related components to be deployed.
        /// </summary>
        public class HarborOptions
        {
            /// <summary>
            /// Optionally installs the Harbor Chart Museum.  This defaults to <c>false</c>.
            /// </summary>
            [JsonProperty(PropertyName = "ChartMuseum", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
            [YamlMember(Alias = "chartMuseum", ApplyNamingConventions = false)]
            [DefaultValue(false)]
            public bool ChartMuseum { get; set; } = false;

            /// <summary>
            /// Optionally installs the Harbor JobService.  This defaults to <c>false</c>.
            /// </summary>
            [JsonProperty(PropertyName = "JobService", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
            [YamlMember(Alias = "jobService", ApplyNamingConventions = false)]
            [DefaultValue(false)]
            public bool JobService { get; set; } = false;

            /// <summary>
            /// Optionally installs the Harbor NotaryServer.  This defaults to <c>false</c>.
            /// </summary>
            [JsonProperty(PropertyName = "NotaryServer", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
            [YamlMember(Alias = "notaryServer", ApplyNamingConventions = false)]
            [DefaultValue(false)]
            public bool NotaryServer { get; set; } = false;

            /// <summary>
            /// Optionally installs the Harbor NotarySigner.  This defaults to <c>false</c>.
            /// </summary>
            [JsonProperty(PropertyName = "NotarySigner", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
            [YamlMember(Alias = "notarySigner", ApplyNamingConventions = false)]
            [DefaultValue(false)]
            public bool NotarySigner { get; set; } = false;

            /// <summary>
            /// Validates the options.
            /// </summary>
            /// <param name="clusterDefinition">The cluster definition.</param>
            /// <exception cref="ClusterDefinitionException">Thrown if the definition is not valid.</exception>
            [Pure]
            internal void Validate(ClusterDefinition clusterDefinition)
            {
                Covenant.Requires<ArgumentNullException>(clusterDefinition != null, nameof(clusterDefinition));
            }
        }

        //---------------------------------------------------------------------
        // Implementation

        /// <summary>
        /// Specifies optional Harbor related components to be installed in the cluster.
        /// </summary>
        [JsonProperty(PropertyName = "Harbor", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [YamlMember(Alias = "harbor", ApplyNamingConventions = false)]
        [DefaultValue(null)]
        public HarborOptions Harbor { get; set; } = new HarborOptions();

        /// <summary>
        /// Optionally installs the Node Problem Detector.  This defaults to <c>false</c>.
        /// </summary>
        [JsonProperty(PropertyName = "NodeProblemDetector", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [YamlMember(Alias = "nodeProblemDetector", ApplyNamingConventions = false)]
        [DefaultValue(false)]
        public bool NodeProblemDetector { get; set; } = false;

        /// <summary>
        /// Optionally enables the Istio service mesh.  This defaults to <c>false</c>.
        /// </summary>
        [JsonProperty(PropertyName = "ServiceMesh", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [YamlMember(Alias = "serviceMesh", ApplyNamingConventions = false)]
        [DefaultValue(false)]
        public bool ServiceMesh { get; set; } = false;

        /// <summary>
        /// Optionally installs Tempo.  This defaults to <c>false</c>.
        /// </summary>
        [JsonProperty(PropertyName = "Tempo", Required = Required.Default, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [YamlMember(Alias = "tempo", ApplyNamingConventions = false)]
        [DefaultValue(false)]
        public bool Tracing { get; set; } = false;

        /// <summary>
        /// Validates the options.
        /// </summary>
        /// <param name="clusterDefinition">The cluster definition.</param>
        /// <exception cref="ClusterDefinitionException">Thrown if the definition is not valid.</exception>
        [Pure]
        internal void Validate(ClusterDefinition clusterDefinition)
        {
            Covenant.Requires<ArgumentNullException>(clusterDefinition != null, nameof(clusterDefinition));
        }
    }
}
