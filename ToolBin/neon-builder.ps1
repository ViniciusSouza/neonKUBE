#Requires -Version 7.0
#------------------------------------------------------------------------------
# FILE:         neon-builder.ps1
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

# Performs a clean build of the neonKUBE solution and publishes binaries
# to the [$/build] folder.  This can also optionally build the neonKUBE
# Desktop installer.
#
# USAGE: pwsh -file ./neon-builder.ps1 [OPTIONS]
#
# OPTIONS:
#
#       -tools        - Builds the command line tools
#       -codedoc      - Builds the code documentation
#       -all          - Builds with all of the options above

param 
(
    [switch]$tools   = $false,
    [switch]$codedoc = $false,
    [switch]$all     = $false
)

if ($all)
{
    $tools   = $true
    $codedoc = $true
}

# Import the global project include file.

. $env:NF_ROOT/Powershell/includes.ps1

# Initialize

$msbuild        = "C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\amd64\MSBuild.exe"
$nfRoot         = "$env:NF_ROOT"
$nfSolution     = "$nfRoot\neonKUBE.sln"
$nfBuild        = "$env:NF_BUILD"
$nfLib          = "$nfRoot\Lib"
$nfTools        = "$nfRoot\Tools"
$nfToolBin      = "$nfRoot\ToolBin"
$config         = "Release"
$buildConfig    = "-p:Configuration=Release"
$env:PATH      += ";$nfBuild"

$libraryVersion = $(& "$nfToolBin\neon-build" read-version "$nfLib\Neon.Common\Build.cs" NeonLibraryVersion)
ThrowOnExitCode

#------------------------------------------------------------------------------
# Publishes a .NET Core project to the repo's build folder.
#
# ARGUMENTS:
#
#   $projectPath    - The relative project folder PATH
#   $targetName     - Name of the target executable

function PublishCore
{
    [CmdletBinding()]
    param (
        [Parameter(Position=0, Mandatory=1)]
        [string]$projectPath,
        [Parameter(Position=1, Mandatory=1)]
        [string]$targetName
    )

    # Ensure that the NF_BUILD folder exists:

    [System.IO.Directory]::CreateDirectory($nfBuild)

    # Locate the published output folder (note that we need to handle apps targeting different versions of .NET):

    $projectPath = [System.IO.Path]::Combine($nfRoot, $projectPath)
    $targetPath  = [System.IO.Path]::Combine($nfRoot, [System.IO.Path]::GetDirectoryName($projectPath), "bin", $config, "net5.0-windows")
    
    if (![System.IO.Directory]::Exists($targetPath))
    {
        $targetPath = [System.IO.Path]::Combine($nfRoot, [System.IO.Path]::GetDirectoryName($projectPath), "bin", $config, "net5.0")

        if (![System.IO.Directory]::Exists($targetPath))
        {
            Write-Error "Cannot locate publish folder for: $projectPath"
            Exit 1
        }
    }

    # Copy the binary files to a new build folder subdirectory named for the target and
    # generate the batch file to launch the program.

    ""
    "**********************************************************"
    "*** PUBLISH: $targetName"
    "**********************************************************"

    $binaryFolder = [System.IO.Path]::Combine($nfBuild, $targetName)

    if ([System.IO.Directory]::Exists($binaryFolder))
    {
        [System.IO.Directory]::Delete($binaryFolder, $true)
    }

    [System.IO.Directory]::CreateDirectory($binaryFolder)
    Copy-Item -Path "$targetPath/*" -Destination $binaryFolder -Recurse

    $cmdPath = [System.IO.Path]::Combine($nfBuild, "$targetName.cmd")

    [System.IO.File]::WriteAllText($cmdPath, "@echo off`r`n")
    [System.IO.File]::AppendAllText($cmdPath, "%~dp0\$targetName\$targetName.exe %*`r`n")
}

#------------------------------------------------------------------------------

# We see somewhat random build problems when Visual Studio has the solution open,
# so have the user close Visual Studio instances first.

Get-Process -Name devenv -ErrorAction SilentlyContinue | Out-Null

if ($?)
{
    Write-Error -Message "ERROR: Please close all Visual Studio instances before building."
    exit 1
}

Push-Location $nfRoot

# Build the solution.

if (-not $nobuild)
{
    # Clear the NF_BUILD folder and delete any [bin] or [obj] folders
    # to be really sure we're doing a clean build.  I've run into 
    # situations where I've upgraded SDKs or Visual Studio and Files
    # left over from previous builds that caused build trouble.

    & $nfToolBin\neon-build clean "$nfRoot"

    # Clean and build the solution.

    ""
    "**********************************************************"
    "***                   CLEAN SOLUTION                   ***"
    "**********************************************************"
    ""

    & "$msbuild" "$nfSolution" $buildConfig -t:Clean -m -verbosity:quiet

    if (-not $?)
    {
        ""
        "*** CLEAN FAILED ***"
        ""
        exit 1
    }

    ""
    "**********************************************************"
    "***                   BUILD SOLUTION                   ***"
    "**********************************************************"
    ""

    & "$msbuild" "$nfSolution" $buildConfig -restore -m -verbosity:quiet

    if (-not $?)
    {
        ""
        "*** BUILD FAILED ***"
        ""
        exit 1
    }
}

# Build the Neon tools.

if ($tools)
{
    # Publish the Windows .NET Core tool binaries to the build folder.

    PublishCore "Tools\neon-cli\neon-cli.csproj"    "neon"
    PublishCore "Tools\unix-text\unix-text.csproj"  "unix-text"
 }

# Build the code documentation if requested.

if ($codedoc)
{
    ""
    "**********************************************************"
    "***                CODE DOCUMENTATION                  ***"
    "**********************************************************"
    ""

    # Remove some pesky aliases:

    del alias:rm
    del alias:cp
    del alias:mv

    if (-not $?)
    {
        ""
        "*** ERROR: Cannot remove: $nfBuild\codedoc"
        ""
        exit 1
    }

    & "$msbuild" "$nfSolution" -p:Configuration=CodeDoc -restore -m -verbosity:quiet

    if (-not $?)
    {
        ""
        "*** BUILD FAILED ***"
        ""
        exit 1
    }

    # Copy the CHM file to a more convenient place for adding to the GitHub release
    # and generate the SHA512 for it.

    $nfDocOutput = "$nfroot\Websites\CodeDoc\bin\CodeDoc"

    & cp "$nfDocOutput\neon.chm" "$nfbuild"
    ThrowOnExitCode

    ""
    "Generating neon.chm SHA512..."
	""

    & cat "$nfBuild\neon.chm" | openssl dgst -sha512 -binary | neon-build hexdump > "$nfBuild\neon.chm.sha512.txt"
    ThrowOnExitCode

    # Move the documentation build output.
	
    & rm -r --force "$nfBuild\codedoc"
    ThrowOnExitCode
    & mv "$nfDocOutput" "$nfBuild\codedoc"
    ThrowOnExitCode

    # Munge the SHFB generated documentation site:
    #
    #   1. Insert the Google Analytics [gtag.js] scripts
    #   2. Munge and relocate HTML files for better site
    #      layout and friendlier permalinks.

    ""
    "Tweaking Layout and Enabling Google Analytics..."
	""

    & neon-build shfb --gtag="$nfroot\Websites\CodeDoc\gtag.js" --styles="$nfRoot\WebSites\CodeDoc\styles" "$nfRoot\WebSites\CodeDoc" "$nfBuild\codedoc"
    ThrowOnExitCode
}

Pop-Location
