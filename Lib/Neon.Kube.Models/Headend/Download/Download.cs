﻿//-----------------------------------------------------------------------------
// FILE:	    Download.cs
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
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;

using Newtonsoft.Json;

namespace Neon.Kube.Models.Headend
{
    /// <summary>
    /// Describes a download including its parts
    /// </summary>
    public class Download
    {
        /// <summary>
        /// Default constructor.
        /// </summary>
        public Download()
        {
        }

        /// <summary>
        /// Identifies the download by name.
        /// </summary>
        [JsonProperty(PropertyName = "Name")]
        public string Name { get; set; }

        /// <summary>
        /// The download version (this may be <c>null</c>).
        /// </summary>
        [JsonProperty(PropertyName = "Version")]
        public string Version { get; set; }

        /// <summary>
        /// The download parts.
        /// </summary>
        [JsonProperty(PropertyName = "Parts")]
        public List<DownloadPart> Parts { get; set; }
    }
}