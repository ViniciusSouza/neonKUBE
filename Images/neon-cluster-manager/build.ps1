﻿#------------------------------------------------------------------------------
# FILE:         build.ps1
# CONTRIBUTOR:  Marcus Bowyer
# COPYRIGHT:    Copyright (c) 2005-2021 by neonFORGE LLC.  All rights reserved.
#
# Builds the Neon [neon-cluster-manager] image.
#
# Usage: powershell -file build.ps1 REGISTRY VERSION TAG

param 
(
	[parameter(Mandatory=$True,Position=1)][string] $registry,
	[parameter(Mandatory=$True,Position=2)][string] $tag
)

Log-ImageBuild $registry $tag

$appname      = "neon-cluster-manager"
$organization = LibraryRegistryOrg
$base_organization = KubeBaseRegistryOrg

# Build and publish the app to a local [bin] folder.

DeleteFolder bin

Exec { mkdir bin }
Exec { dotnet publish "$nfServices\\$appname\\$appname.csproj" -c Release -o "$pwd\bin" }

# Split the build binaries into [__app] (application) and [__dep] dependency subfolders
# so we can tune the image layers.

Exec { core-layers $appname "$pwd\bin" }

# Build the image.

Exec { docker build -t "${registry}:$tag" --build-arg "ORGANIZATION=$organization" --build-arg "BASE_ORGANIZATION=$base_organization" --build-arg "CLUSTER_VERSION=neonkube-$neonKUBE_Version" --build-arg "APPNAME=$appname" . }

# Clean up

DeleteFolder bin

