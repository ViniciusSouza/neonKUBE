﻿//-----------------------------------------------------------------------------
// FILE:	    TaskQueueStatus.cs
// CONTRIBUTOR: John C. Burns
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
using System.Text;

using Newtonsoft.Json;

namespace Neon.Temporal
{
    public class TaskQueueStatus
    {
        [JsonProperty(PropertyName = "backlog_count_hint")]
        public long BackLogCountHint { get; set; }

        [JsonProperty(PropertyName = "read_level")]
        public long ReadLevel { get; set; }

        [JsonProperty(PropertyName = "ack_level")]
        public long AckLevel { get; set; }

        [JsonProperty(PropertyName = "rate_per_second")]
        public double RatePerSecond { get; set; }

        [JsonProperty(PropertyName = "task_id_block")]
        public TaskIdBlock TaskIdBlock { get; set; }
    }
}
