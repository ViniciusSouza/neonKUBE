﻿//-----------------------------------------------------------------------------
// FILE:	    LinuxPath.cs
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
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Neon.Common;

namespace Neon.IO
{
    /// <summary>
    /// Implements functionality much like <see cref="Path"/>, except for
    /// this class is oriented towards handling Linux-style paths on
    /// a remote (possibly a Windows) machine.
    /// </summary>
    public static class LinuxPath
    {
        /// <summary>
        /// Ensures that the path passed is suitable for non-Windows platforms
        /// by conmverting any backslashes to forward slashes.
        /// </summary>
        /// <param name="path">The input path (or <c>null</c>).</param>
        /// <returns>The normalized path.</returns>
        private static string Normalize(string path)
        {
            if (path == null || NeonHelper.IsWindows)
            {
                return path;
            }

            return path.Replace('\\', '/');
        }

        /// <summary>
        /// Converts a Windows style path to Linux.
        /// </summary>
        /// <param name="path">The source path.</param>
        /// <returns>The converted path.</returns>
        private static string ToLinux(this string path)
        {
            return path.Replace('\\', '/');
        }

        /// <summary>
        /// Changes the file extension.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <param name="extension">The new extension.</param>
        /// <returns>The modified path.</returns>
        public static string ChangeExtension(string path, string extension)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(path), nameof(path));
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(extension), nameof(extension));

            return Path.ChangeExtension(Normalize(path), extension).ToLinux();
        }

        /// <summary>
        /// Combines an array of strings into a path.
        /// </summary>
        /// <param name="paths">The paths.</param>
        /// <returns>The combined paths.</returns>
        public static string Combine(params string[] paths)
        {
            Covenant.Requires<ArgumentNullException>(paths != null, nameof(paths));

            return Path.Combine(paths).ToLinux();
        }

        /// <summary>
        /// Extracts the directory portion of a path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>The directory portion.</returns>
        public static string GetDirectoryName(string path)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(path), nameof(path));

            return Path.GetDirectoryName(Normalize(path)).ToLinux();
        }

        /// <summary>
        /// Returns the file extension from a path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>The file extension.</returns>
        public static string GetExtension(string path)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(path), nameof(path));

            return Path.GetExtension(Normalize(path));
        }

        /// <summary>
        /// Returns the file name and extension from a path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>The file name and extension.</returns>
        public static string GetFileName(string path)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(path), nameof(path));

            return Path.GetFileName(Normalize(path));
        }

        /// <summary>
        /// Returns the file name from a path without the extension.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>The file name without the extension.</returns>
        public static string GetFileNameWithoutExtension(string path)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(path), nameof(path));

            return Path.GetFileNameWithoutExtension(Normalize(path));
        }

        /// <summary>
        /// Determines whether a path has a file extension.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns><c>true</c> if the path has an extension.</returns>
        public static bool HasExtension(string path)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(path), nameof(path));

            return Path.HasExtension(Normalize(path));
        }

        /// <summary>
        /// Determines whether the path is rooted.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns><c>true</c> ifc the path is rooted.</returns>
        public static bool IsPathRooted(string path)
        {
            Covenant.Requires<ArgumentNullException>(!string.IsNullOrEmpty(path), nameof(path));

            return Normalize(path).ToLinux().StartsWith("/");
        }
    }
}
