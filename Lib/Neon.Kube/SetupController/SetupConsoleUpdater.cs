﻿//-----------------------------------------------------------------------------
// FILE:	    SetupConsoleUpdater.cs
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
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Neon.Common;

namespace Neon.Kube
{
    /// <summary>
    /// Used internally to update .NET console window without flickering.
    /// </summary>
    public class SetupConsoleUpdater
    {
        private string          previousText  = null;
        private List<string>    previousLines = new List<string>();

        /// <summary>
        /// Writes the text passed to the <see cref="Console"/> without flickering.
        /// </summary>
        /// <param name="text">The text to be written.</param>
        public void Update(string text)
        {
            text ??= string.Empty;

            var newLines = text.Split('\n')
                .Select(line => line.TrimEnd())
                .ToList();

            if (previousText == null)
            {
                // This is the first Update() has been called so we need configure
                // and clear the console.

                Console.CursorVisible = false;
                Console.Clear();
            }

            if (text == previousText)
            {
                return;     // The text hasn't changed
            }

            // We're going to write the new lines by comparing them against the previous lines and rewriting
            // only the lines that are different. 

            for (int lineIndex = 0; lineIndex < Math.Max(previousLines.Count, newLines.Count); lineIndex++)
            {
                var previousLine = lineIndex < previousLines.Count ? previousLines[lineIndex] : string.Empty;
                var newLine      = lineIndex < newLines.Count ? newLines[lineIndex] : string.Empty;

                // When the new line is shorter than the previous one, we need to append enough spaces
                // to the new line such that the previous line will be completely overwritten.

                if (newLine.Length < previousLine.Length)
                {
                    newLine += new string(' ', previousLine.Length - newLine.Length);
                }

                if (newLine != previousLine)
                {
                    Console.SetCursorPosition(0, lineIndex);
                    Console.Write(newLine);
                }
            }

            previousLines = newLines;
            previousText  = text;
        }
    }
}