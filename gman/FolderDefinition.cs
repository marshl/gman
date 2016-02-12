﻿//-----------------------------------------------------------------------
// <copyright file="FolderDefinition.cs" company="marshl">
// Copyright 2016, Liam Marshall, marshl.
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
// </copyright>
//-----------------------------------------------------------------------

namespace GMan
{
    /// <summary>
    /// Used to define a folder in the Code Source directory
    /// </summary>
    public class FolderDefinition
    {
        /// <summary>
        /// Gets or sets the name of the definition (for logging purposes only)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the directory in the CodeSource to use
        /// </summary>
        public string Directory { get; set; }

        /// <summary>
        /// Gets or sets the file extensions to restrict to.
        /// </summary>
        public string Extension { get; set; }

        /// <summary>
        /// Gets or sets the SQL statement to use to get the CLOB data of any given file.
        /// </summary>
        public string LoadStatement { get; set; }
    }
}
