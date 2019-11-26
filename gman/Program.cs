//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="marshl">
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
//
// "The right man in the wrong place can make all the difference in the world"
//
//-----------------------------------------------------------------------
namespace GMan
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Sockets;
    using System.Text.RegularExpressions;
    using System.Xml;
    using System.Xml.Serialization;
    using Mono.Options;
    using Oracle.ManagedDataAccess.Client;
    using Oracle.ManagedDataAccess.Types;

    /// <summary>
    /// The entry class of the program.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// The entry point of the program
        /// </summary>
        /// <param name="args">The command line arguments to the program.</param>
        public static void Main(string[] args)
        {
            bool show_help = false;
            string username = "XVIEWMGR";
            int port = 1521;
            string password = null;
            string hostname = null;
            string sid = null;
            string directory = null;

            var p = new OptionSet()
            {
                { "u|username=",
                    "the username to log in as.\n" +
                        "default xviewmgr.",
                  (string v) => username = v },
                { "r|port=",
                    "the port to connect to\n" +
                        "default 1521.",
                  (int v) => port = v },
                 { "p|password=",
                    "the password of the user\n",
                  (string v) => password = v },
                 { "o|host=",
                    "the host to connect to\n",
                  (string v) => hostname = v },
                 { "s|sid=",
                    "the Oracle Service ID\n",
                  (string v) => sid = v },
                { "d|directory=",
                    "the CodeSource directory to compare with.\n",
                  (string v) => directory = v },
                { "h|help",  "show this message and exit",
                  v => show_help = v != null },
            };

            List<string> extra;
            try
            {
                extra = p.Parse(args);
            }
            catch (OptionException e)
            {
                Console.Write("gman: ");
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `gman --help' for more information.");
                return;
            }

            if (show_help)
            {
                ShowHelp(p);
                return;
            }

            string message;
            if (extra.Count > 0)
            {
                message = string.Join(" ", extra.ToArray());
                Console.WriteLine("Unknown argument(s): {0},", message);
                return;
            }

            if (password == null || hostname == null || sid == null || directory == null)
            {
                ShowHelp(p);
                return;
            }

            OracleConnection con = CreateConnection(username, password, hostname, port, sid);
            if (con == null)
            {
                return;
            }

            DirectoryInfo codeSourceDirectory = new DirectoryInfo(directory);

            PatchCheck(con, codeSourceDirectory);
            CheckALlFolderDefinitions(con, codeSourceDirectory);

            // PackageCheck(con, codeSourceDirectory);
            con.Close();
        }

        /// <summary>
        /// Displays a help message for the given OptionSet
        /// </summary>
        /// <param name="optionSet">The option set to display the help for.</param>
        private static void ShowHelp(OptionSet optionSet)
        {
            Console.WriteLine("Usage: greet [OPTIONS]+ message");
            Console.WriteLine("Greet a list of individuals with an optional message.");
            Console.WriteLine("If no message is specified, a generic greeting is used.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            optionSet.WriteOptionDescriptions(Console.Out);
        }

        /// <summary>
        /// Performs a patch check for the database.
        /// </summary>
        /// <param name="con">The oracle connection to use.</param>
        /// <param name="codeSourceDirectory">The Code Source directory to start from.</param>
        private static void PatchCheck(OracleConnection con, DirectoryInfo codeSourceDirectory)
        {
            Console.WriteLine(@"
#################################################
# Patches
# ");

            OracleCommand patchCommand = con.CreateCommand();
            patchCommand.CommandText = @"
SELECT pr.id FROM promotemgr.patch_runs pr
WHERE pr.patch_label = :patch_label
AND pr.patch_number = :patch_number
AND pr.ignore_flag IS NULL";

            DirectoryInfo patchDirectory = new DirectoryInfo(Path.Combine(codeSourceDirectory.FullName, "DatabasePatches"));
            foreach (DirectoryInfo subDir in patchDirectory.GetDirectories())
            {
                if (subDir.Name.Contains("NoDeploy"))
                {
                    continue;
                }

                foreach (FileInfo patchFile in subDir.GetFiles("*.sql"))
                {
                    Regex regex = new Regex(@"(\D+?)(\d+?) \(.+?\).sql");
                    Match match = regex.Match(patchFile.Name);
                    Debug.Assert(match.Groups.Count > 0, "The given file does not meet the patch naming guidelines");
                    string patchType = match.Groups[1].Value;
                    string patchNum = match.Groups[2].Value;

                    patchCommand.Parameters.Clear();
                    patchCommand.Parameters.Add("patch_label", patchType);
                    patchCommand.Parameters.Add("patch_number", patchNum);
                    OracleDataReader reader = patchCommand.ExecuteReader();
                    if (!reader.Read())
                    {
                        Console.WriteLine($"Patch {patchFile} will be run.");
                    }

                    reader.Close();
                }
            }

            patchCommand.Dispose();
        }

        /// <summary>
        /// Compares the packages on the database with those in the CodeSource folder.
        /// </summary>
        /// <param name="con">The Oracle connection to check with.</param>
        /// <param name="codeSourceDirectory">The Code Source directory to start from.</param>
        private static void PackageCheck(OracleConnection con, DirectoryInfo codeSourceDirectory)
        {
            Console.WriteLine(@"
#################################################
# Database Source
# ");

            DirectoryInfo datasouceDirectory = new DirectoryInfo(Path.Combine(codeSourceDirectory.FullName, "DatabaseSource", "CoreSource"));

            foreach (DirectoryInfo dirInfo in datasouceDirectory.GetDirectories())
            {
                string packageOwner = dirInfo.Name;
                foreach (FileInfo fileInfo in dirInfo.GetFiles())
                {
                    string packageName = Path.GetFileNameWithoutExtension(fileInfo.Name);
                    string packageType = null;
                    switch (fileInfo.Extension)
                    {
                        case ".pks":
                            packageType = "PACKAGE";
                            break;
                        case ".pkb":
                            packageType = "PACKAGE_BODY";
                            break;
                        case ".vw":
                            packageType = "VIEW";
                            break;
                        case ".tps":
                            packageType = "TYPE";
                            break;
                        case ".trg":
                            packageType = "TRIGGER";
                            break;
                        case ".fnc":
                            packageType = "FUNCTION";
                            break;
                        case ".tpb":
                            packageType = "TYPE_BODY";
                            break;
                        case ".prc":
                            packageType = "PROCEDURE";
                            break;
                        default:
                            Debug.Assert(false, $"Unknown extension {fileInfo.Extension}");
                            break;
                    }

                    string result;
                    OracleCommand cmd = con.CreateCommand();
                    if (packageType == "PACKAGE" || packageType == "PACKAGE_BODY")
                    {
                        cmd.CommandText = $@"SELECT text 
                        FROM dba_source
                        WHERE type = '{(packageType == "PACKAGE" ? "PACKAGE" : "PACKAGE BODY")}'
                        AND owner = '{packageOwner}'
                        AND name = '{packageName}'
                        ORDER BY line ASC";
                        OracleDataReader reader = cmd.ExecuteReader();
                        result = string.Empty;
                        while (reader.Read())
                        {
                            result += reader.GetString(0);
                        }

                        reader.Close();
                    }
                    else
                    {
                        cmd.CommandText = $"SELECT DBMS_METADATA.GET_DDL( object_type => '{packageType}', name => '{packageName.ToUpper()}', schema => '{packageOwner.ToUpper()}' ) FROM DUAL";
                        try
                        {
                            OracleDataReader reader = cmd.ExecuteReader();
                            reader.Read();
                            OracleClob clob = reader.GetOracleClob(0);
                            result = clob.Value;
                            reader.Close();
                        }
                        catch (OracleException ex)
                        {
                            if (ex.Number == 31603)
                            {
                                Console.WriteLine($"Adding new package {packageName}");
                                continue;
                            }

                            throw;
                        }
                    }

                    string databaseContents = CleanPackageSource(result, packageName);

                    string fileContents = File.ReadAllText(fileInfo.FullName);
                    fileContents = CleanPackageSource(fileContents, packageName);

                    if (databaseContents != fileContents)
                    {
                        File.WriteAllText("database.sql", databaseContents);
                        File.WriteAllText("file.sql", fileContents);
                        Console.WriteLine($"{packageType} object {fileInfo.Name} is different.");
                    }

                    cmd.Dispose();
                }
            }
        }

        /// <summary>
        /// Compares all folder definitions with the database.
        /// </summary>
        /// <param name="con">The Oracle connection to connect with.</param>
        /// <param name="codeSourceDirectory">The CodeSource directory to diff against.</param>
        private static void CheckALlFolderDefinitions(OracleConnection con, DirectoryInfo codeSourceDirectory)
        {
            DirectoryInfo definitionDirectory = new DirectoryInfo("gman");
            foreach (FileInfo file in definitionDirectory.GetFiles("*.xml"))
            {
                XmlSerializer xmls = new XmlSerializer(typeof(FolderDefinition));
                FolderDefinition fd;
                StreamReader reader = new StreamReader(file.FullName);
                XmlReader xmlReader = XmlReader.Create(reader);
                fd = (FolderDefinition)xmls.Deserialize(xmlReader);
                xmlReader.Close();
                reader.Close();

                string fullpath = Path.Combine(codeSourceDirectory.FullName, fd.Directory);
                if (!Directory.Exists(fullpath))
                {
                    Console.WriteLine($"The folder {fullpath} could not be found");
                    continue;
                }

                ProcessFolderDefinition(fd, con, codeSourceDirectory);
            }
        }

        /// <summary>
        /// Processes the given folder definition object, printing any warnings.
        /// </summary>
        /// <param name="fd">The folder definition to use.</param>
        /// <param name="con">The Oracle connection to use.</param>
        /// <param name="codeSourceDirectory">The CodeSource directory to compare with.</param>
        private static void ProcessFolderDefinition(FolderDefinition fd, OracleConnection con, DirectoryInfo codeSourceDirectory)
        {
            Console.WriteLine(@"
#################################################
# " + fd.Name + @"
# ");

            DirectoryInfo dirInfo = new DirectoryInfo(Path.Combine(codeSourceDirectory.FullName, fd.Directory));
            OracleCommand command = con.CreateCommand();
            command.CommandText = fd.LoadStatement;

            foreach (FileInfo fileInfo in dirInfo.GetFiles(fd.Extension, SearchOption.AllDirectories))
            {
                command.Parameters.Clear();
                command.Parameters.Add("filename", fileInfo.Name);
                OracleDataReader reader = command.ExecuteReader();
                if (!reader.Read())
                {
                    Console.WriteLine($"{fd.Name} {fileInfo.Name} is new.");
                    reader.Close();
                    continue;
                }

                string databaseContents = CleanXmlSource(reader.GetOracleClob(0).Value);
                string fileContents = CleanXmlSource(File.ReadAllText(fileInfo.FullName));

                if (databaseContents != fileContents)
                {
                    Console.WriteLine($"{fd.Name} {fileInfo.Name} will be updated.");
                }

                reader.Close();
            }
        }

        /// <summary>
        /// Opens a new connection to an Oracle Database using the given parameters.
        /// </summary>
        /// <param name="username">The username to connect as.</param>
        /// <param name="password">The password of the user.</param>
        /// <param name="hostname">The IP address of the database to connect to.</param>
        /// <param name="port">The port to connect with.</param>
        /// <param name="sid">The Oracle SID to connect to.</param>
        /// <returns>The connection object if the connection was successful, otherwise false.</returns>
        private static OracleConnection CreateConnection(string username, string password, string hostname, int port, string sid)
        {
            try
            {
                OracleConnection con = new OracleConnection();
                con.ConnectionString = "User Id=" + username
                    + (string.IsNullOrEmpty(password) ? null : ";Password=" + password)
                    + ";Data Source=(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)("
                    + $"HOST={hostname})"
                    + $"(PORT={port})))(CONNECT_DATA="
                    + $"(SID={sid})(SERVER=DEDICATED)))";

                con.Open();
                return con;
            }
            catch (Exception e) when (e is InvalidOperationException || e is OracleException || e is ArgumentException || e is SocketException)
            {
                Console.WriteLine($"Connection to Oracle failed: {e}");
                return null;
            }
        }

        /// <summary>
        /// Removes all trailing and leading whitespace from the lines in the given string.
        /// </summary>
        /// <param name="str">The string to trim.</param>
        /// <returns>The trimmed string.</returns>
        private static string TrimLines(string str)
        {
            const string MatchPattern = @"^[ \t]*(.+?)[ \t]*$";
            const string ReplacePattern = @"$1";
            str = Regex.Replace(str, @"^\s+$[\r\n]*", string.Empty, RegexOptions.Multiline);
            return Regex.Replace(str, MatchPattern, ReplacePattern, RegexOptions.Multiline);
        }

        /// <summary>
        /// Cleans the source of the given package, so it can be diffed without unnecessary changes.
        /// </summary>
        /// <param name="package">The package source to clean down.</param>
        /// <param name="packageName">The name of the package that is being cleaned.</param>
        /// <returns>The cleaned down version of the package.</returns>
        private static string CleanPackageSource(string package, string packageName)
        {
            package = TrimLines(package);

            // package = Regex.Replace(package, @"\s+?(.+)\\?\s+?", "$1", RegexOptions.Multiline);
            // package = Regex.Replace(package, @"(CREATE OR REPLACE\s+)?(EDITIONABLE\s+)?PACKAGE\s+(BODY\s+)?""?(.+?)""?\.""?(.+?)""? IS", @"CREATE OR REPLACE PACKAGE $3$4.$5 IS", RegexOptions.Multiline);
            // package = Regex.Replace(package, "(CREATE OR REPLACE)?.*?PACKAGE.+?(BODY)?.+", "");
            // package = Regex.Replace(package, @"^(CREATE OR REPLACE\s+?)?(FORCE\s+?)?(EDITIONABLE\s+?)?(PACKAGE BODY|VIEW).+?(""?\S+?""?\.)?""?\S+?\""?\s+?(IS|AS)", "", RegexOptions.Multiline);
            // package = Regex.Replace(package, @"PACKAGE( BODY)?.+?IS.*?", "");
            package = Regex.Replace(package, @"(.*?) *--.+?$", "$1", RegexOptions.Multiline);
            package = TrimLines(package);

            // package = Regex.Replace(package, @"^((CREATE OR REPLACE)|(PACKAGE( BODY)?)).+?\s(AS|IS)\s", "", RegexOptions.Multiline);
            package = Regex.Replace(package, @"^.*?" + packageName + @".*?\s(AS|IS)\s", string.Empty, RegexOptions.Singleline);
            package = Regex.Replace(package, @".+?\s(AS|IS)\s", string.Empty, RegexOptions.Multiline);
            package = package.Trim();
            package = package.TrimEnd('/');
            package = package.Trim();
            package = Regex.Replace(package, "^--.*?[\r\n]+", string.Empty, RegexOptions.Multiline);
            package = TrimLines(package);

            return package;
        }

        /// <summary>
        /// Strips down the given xml string to be able to diff them with.
        /// </summary>
        /// <param name="xml">The xml string to process.</param>
        /// <returns>The stripped down version of the xml.</returns>
        private static string CleanXmlSource(string xml)
        {
            xml = Regex.Replace(xml, @"\s", string.Empty);
            xml = Regex.Replace(xml, @"<!--.*?-->", string.Empty);
            xml = Regex.Replace(xml, @"<\?.*?\?>", string.Empty);
            return xml;
        }
    }
}
