#Requires -Version 7.0
#------------------------------------------------------------------------------
# FILE:         deployment.ps1
# CONTRIBUTOR:  Jeff Lill
# COPYRIGHT:    Copyright (c) 2005-2021 by neonFORGE LLC.  All rights reserved.
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

#------------------------------------------------------------------------------
# IMPORTANT:
#
# This file defines GitHub related Powershell functions and is intended for use
# in GitHub actions and other deployment related scenarios.  This file is intended
# to be shared/included across multiple GitHub repos and should never include
# repo-specific code.
#
# After modifying this file, you should take care to push any changes to the
# other repos where this file is present.

# Load these assemblies from the [neon-assistant] installation folder
# to ensure we'll be compatible.

Add-Type -Path "$env:NEON_ASSISTANT_HOME\Neon.Common.dll"
Add-Type -Path "$env:NEON_ASSISTANT_HOME\Neon.Deployment.dll"

#------------------------------------------------------------------------------
# Returns a global [Neon.Deployment.ProfileClient] instance creating one if necessary.
# This can be used to query the [neon-assistant] installed on the workstation for
# secret passwords, secret values, as well as profile values.  The client is thread-safe,
# can be used multiple times, and does not need to be disposed.

$global:neonProfileClient = $null

function GetProfileClient
{
    if ($global:neonProfileClient -ne $null)
    {
        return $global:neonProfileClient
    }

    $global:neonProfileClient = New-Object "Neon.Deployment.ProfileClient"

    return $global:neonProfileClient
}

#------------------------------------------------------------------------------
# Returns the named profile value.
#
# ARGUMENTS:
#
#   name            - Specifies the profile value name
#   $nullOnNotFound - Optionally specifies that $null should be returned rather 
#                     than throwing an exception when the profile value does 
#                     not exist.

function GetProfileValue
{
    [CmdletBinding()]
    param (
        [Parameter(Position=0, Mandatory=1)]
        [string]$name,
        [Parameter(Position=1, Mandatory=0)]
        [bool]$nullOnNotFound = $false
    )

    $client = GetProfileClient

    return $client.GetProfileValue($name, $nullOnNotFound)
}

#------------------------------------------------------------------------------
# Returns the named secret password, optionally specifying a non-default vault.
#
# ARGUMENTS:
#
#   name            - Specifies the secret password name
#   vault           - Optionally overrides the default vault
#   masterPassword  - Optionally specifies the master 1Password (for automation)
#   $nullOnNotFound - Optionally specifies that $null should be returned rather 
#                     than throwing an exception when the secret does not exist.

function GetSecretPassword
{
    [CmdletBinding()]
    param (
        [Parameter(Position=0, Mandatory=1)]
        [string]$name,
        [Parameter(Position=1, Mandatory=0)]
        [string]$vault = $null,
        [Parameter(Position=2, Mandatory=0)]
        [string]$masterPassword = $null,
        [Parameter(Position=3, Mandatory=0)]
        [bool]$nullOnNotFound = $false
    )

    $client = GetProfileClient

    return $client.GetSecretPassword($name, $vault, $masterPassword, $nullOnNotFound)
}

#------------------------------------------------------------------------------
# Returns the named secret value, optionally specifying a non-default vault.
#
# ARGUMENTS:
#
#   name            - Specifies the secret value name
#   vault           - Optionally overrides the default vault
#   masterPassword  - Optionally specifies the master 1Password (for automation)
#   $nullOnNotFound - Optionally specifies that $null should be returned rather 
#                     than throwing an exception when the secret does not exist.

function GetSecretValue
{
    [CmdletBinding()]
    param (
        [Parameter(Position=0, Mandatory=1)]
        [string]$name,
        [Parameter(Position=1, Mandatory=0)]
        [string]$vault = $null,
        [Parameter(Position=2, Mandatory=0)]
        [string]$masterPassword = $null,
        [Parameter(Position=3, Mandatory=0)]
        [bool]$nullOnNotFound = $false
    )

    $client = GetProfileClient

    return $client.GetSecretValue($name, $vault, $masterPassword, $nullOnNotFound)
}

#------------------------------------------------------------------------------
# Retrieves the AWS access key ID and secret access key from from 1Password 
# and sets these enviroment variables for use by the AWS-CLI:
#
#   AWS_ACCESS_KEY_ID
#   AWS_SECRET_ACCESS_KEY
#
# ARGUMENTS:
#
#   awsAccessKeyId      - Optionally overrides the key ID password name
#   awsSecretAccessKey  - Optionally overrides the secret key password name
#   vault               - Optionally overrides the default vault
#   masterPassword      - Optionally specifies the master 1Password (for automation)

function GetAwsCliCredentials
{
    [CmdletBinding()]
    param (
        [Parameter(Position=0, Mandatory=0)]
        [string]$awsAccessKeyId = "AWS_ACCESS_KEY_ID",
        [Parameter(Position=1, Mandatory=0)]
        [string]$awsSecretAccessKey = "AWS_SECRET_ACCESS_KEY",
        [Parameter(Position=1, Mandatory=0)]
        [string]$vault = $null,
        [Parameter(Position=2, Mandatory=0)]
        [string]$masterPassword = $null
    )

    $client = GetProfileClient

    $env:AWS_ACCESS_KEY_ID     = $client.GetSecretPassword($awsAccessKeyId, $vault, $masterPassword)
    $env:AWS_SECRET_ACCESS_KEY = $client.GetSecretPassword($awsSecretAccessKey, $vault, $masterPassword)
}

#------------------------------------------------------------------------------
# Removes the AWS-CLI credential environment variables if present:
#
#   AWS_ACCESS_KEY_ID
#   AWS_SECRET_ACCESS_KEY

function ClearAwsCliCredentials
{
    $env:AWS_ACCESS_KEY_ID     = $null
    $env:AWS_SECRET_ACCESS_KEY = $null
}

#------------------------------------------------------------------------------
# Retrieves the GITHUB_PAT (personal access token) from from 1Password 
# and sets the GITHUB_PAT environment variable used by the GitHub-CLI
# as well as the [Neon.Deployment.GitHub] class.
#
# ARGUMENTS:
#
#   name            - Optionally overrides the default secret name (GITHUB_PAT)
#   vault           - Optionally overrides the default vault
#   masterPassword  - Optionally specifies the master 1Password (for automation)

function GetGitHubCredentials
{
    [CmdletBinding()]
    param (
        [Parameter(Position=0, Mandatory=0)]
        [string]$name = "GITHUB_PAT",
        [Parameter(Position=1, Mandatory=0)]
        [string]$vault = $null,
        [Parameter(Position=2, Mandatory=0)]
        [string]$masterPassword = $null
    )

    $client = GetProfileClient

    $env:GITHUB_PAT = $client.GetSecretPassword($name, $vault, $masterPassword)
}

#------------------------------------------------------------------------------
# Removes the GitHub credential environment variables if present:
#
#   GITHUB_PAT

function ClearGitHubCredentials
{
    $env:GITHUB_PAT = $null
}
