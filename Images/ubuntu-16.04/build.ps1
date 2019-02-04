#------------------------------------------------------------------------------
# FILE:         build.ps1
# CONTRIBUTOR:  Jeff Lill
# COPYRIGHT:    Copyright (c) 2016-2019 by neonFORGE, LLC.  All rights reserved.
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# Builds the base Ubuntu 16.04 image by applying all current package updates to the 
# base Ubuntu image and then adds some handy packages.
#
# Usage: powershell -file build.ps1 REGISTRY TAG
 
param 
(
	[parameter(Mandatory=$True,Position=1)][string] $registry,
	[parameter(Mandatory=$True,Position=2)][string] $tag
)

"   "
"======================================="
"* UBUNTU-16.04"
"======================================="

# Build the image.

Exec { docker build -t "${registry}:$tag" --build-arg "TINI_URL=$tini_url" . }

if ($latest)
{
	Exec { docker tag "${registry}:$tag" "${registry}:latest" }
}
