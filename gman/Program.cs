//-----------------------------------------------------------------------
// <copyright file="filename.cs" company="marshl">
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
namespace gman
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Sockets;
    using System.Text.RegularExpressions;
    using Mono.Options;
    using Oracle.ManagedDataAccess.Client;
    using Oracle.ManagedDataAccess.Types;
    using System.Xml;
    using System.Xml.Serialization;

    class Program
    {
        public static void Main(string[] args)
        {
            bool show_help = false;
            string username = "XVIEWMGR";
            int port = 1521;
            string password = null;
            string hostname = null;
            string sid = null;
            string directory = null;


            var p = new OptionSet() {
                { "u|username=",
                    "the username of {USERNAME} to log in as.\n" +
                        "default xviewmgr.",
                  (string v) => username = v },
                { "r|port=",
                    "the port of {PORT} to log in as.\n" +
                        "default 1521.",
                  (int v) => port = v },
                 { "p|password=",
                    "the password of {PORT} to log in as.\n",
                  (string v) => password = v },
                 { "o|host=",
                    "the host of {PORT} to log in as.\n",
                  (string v) => hostname = v },
                 { "s|sid=",
                    "the sid of {SID} to log in as.\n",
                  (string v) => sid = v },
                { "d|directory=",
                    "the sid of {SID} to log in as.\n",
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
            //CheckXviews(con, codeSourceDirectory);
            CheckFileDefinitions(con, codeSourceDirectory);
            //PackageCheck(con, codeSourceDirectory);

            con.Close();
        }

        static void PatchCheck(OracleConnection con, DirectoryInfo codeSourceDirectory)
        {
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
                    Regex r = new Regex(@"(\D+?)(\d+?) \(.+?\).sql");
                    Match m = r.Match(patchFile.Name);
                    Debug.Assert(m.Groups.Count > 0);
                    string patchType = m.Groups[1].Value;
                    string patchNum = m.Groups[2].Value;
                    //Console.WriteLine(patchType + " " + patchNum);
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


        static void PackageCheck(OracleConnection con, DirectoryInfo codeSourceDirectory)
        {
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
                            packageType = "PROCEDURE";// "PROCOBJ";
                            break;
                        default:
                            Debug.Assert(false);
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
                        result = "";
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

                    /*if (!reader.Read())
                    {
                        Console.WriteLine($"No data found on clob retrieval of when executing command: {cmd.CommandText}");
                        return;
                    }*/
                    //

                    string databaseContents = NormalisePackage(result, packageName);

                    string fileContents = File.ReadAllText(fileInfo.FullName);
                    fileContents = NormalisePackage(fileContents, packageName);

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

        static void CheckFileDefinitions(OracleConnection con, DirectoryInfo codeSourceDirectory)
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

                ProcessFolderDefinition(fd, con, codeSourceDirectory);
            }
        }

        static void ProcessFolderDefinition(FolderDefinition fd, OracleConnection con, DirectoryInfo codeSourceDirectory)
        {
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

                string databaseContents = CleanXml(reader.GetOracleClob(0).Value);
                string fileContents = CleanXml(File.ReadAllText(fileInfo.FullName));

                if (databaseContents != fileContents)
                {
                    Console.WriteLine($"{fd.Name} {fileInfo.Name} will be updated.");
                }

                reader.Close();
            }
        }

        /*
        static void CheckXviews(OracleConnection con, DirectoryInfo codeSourceDirectory)
        {
            DirectoryInfo xviewDirectory = new DirectoryInfo(Path.Combine(codeSourceDirectory.FullName, "XviewDefinitions"));

            OracleCommand xviewCommand = con.CreateCommand();
            xviewCommand.CommandText = @"
SELECT x.xview_metadata.getClobVal()
FROM xviewmgr.xview_definition_metadata x
WHERE file_name = :file_name
";

            foreach (FileInfo xviewFile in xviewDirectory.GetFiles("*.xml"))
            {
                xviewCommand.Parameters.Clear();
                xviewCommand.Parameters.Add("filename", xviewFile.Name);
                OracleDataReader reader = xviewCommand.ExecuteReader();
                if (!reader.Read())
                {
                    Console.WriteLine($"Xview {xviewFile.Name} will be added.");
                    continue;
                }

                string databaseContents = CleanXml(reader.GetOracleClob(0).Value);
                string fileContents = CleanXml(File.ReadAllText(xviewFile.FullName));

                if (databaseContents != fileContents)
                {
                    Console.WriteLine($"Xview is different {xviewFile.Name}");
                }
            }
        }*/

        static void ShowHelp(OptionSet p)
        {
            Console.WriteLine("Usage: greet [OPTIONS]+ message");
            Console.WriteLine("Greet a list of individuals with an optional message.");
            Console.WriteLine("If no message is specified, a generic greeting is used.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }

        /*static void Debug(string format, params object[] args)
        {
            
        }*/

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

        static string TrimLines(string str)
        {
            const string matchPattern = @"^[ \t]*(.+?)[ \t]*$";
            const string replacePattern = @"$1";
            str = Regex.Replace(str, @"^\s+$[\r\n]*", "", RegexOptions.Multiline);
            return Regex.Replace(str, matchPattern, replacePattern, RegexOptions.Multiline);
        }

        static string NormalisePackage(string package, string packageName)
        {
            package = TrimLines(package);
            //package = Regex.Replace(package, @"\s+?(.+)\\?\s+?", "$1", RegexOptions.Multiline);
            //package = Regex.Replace(package, @"(CREATE OR REPLACE\s+)?(EDITIONABLE\s+)?PACKAGE\s+(BODY\s+)?""?(.+?)""?\.""?(.+?)""? IS", @"CREATE OR REPLACE PACKAGE $3$4.$5 IS", RegexOptions.Multiline);
            //package = Regex.Replace(package, "(CREATE OR REPLACE)?.*?PACKAGE.+?(BODY)?.+", "");
            //package = Regex.Replace(package, @"^(CREATE OR REPLACE\s+?)?(FORCE\s+?)?(EDITIONABLE\s+?)?(PACKAGE BODY|VIEW).+?(""?\S+?""?\.)?""?\S+?\""?\s+?(IS|AS)", "", RegexOptions.Multiline);
            //package = Regex.Replace(package, @"PACKAGE( BODY)?.+?IS.*?", "");

            package = Regex.Replace(package, @"(.*?) *--.+?$", "$1", RegexOptions.Multiline);
            package = TrimLines(package);
            //package = Regex.Replace(package, @"^((CREATE OR REPLACE)|(PACKAGE( BODY)?)).+?\s(AS|IS)\s", "", RegexOptions.Multiline);
            package = Regex.Replace(package, @"^.*?" + packageName + @".*?\s(AS|IS)\s", "", RegexOptions.Singleline);
            package = Regex.Replace(package, @".+?\s(AS|IS)\s", "", RegexOptions.Multiline);
            package = package.Trim();
            package = package.TrimEnd('/');
            package = package.Trim();
            package = Regex.Replace(package, "^--.*?[\r\n]+", "", RegexOptions.Multiline);
            package = TrimLines(package);

            return package;
        }

        static string CleanXml(string xml)
        {
            xml = Regex.Replace(xml, @"\s", "");
            xml = Regex.Replace(xml, @"<!--.*?-->", "");
            xml = Regex.Replace(xml, @"<\?.*?\?>", "");
            return xml;
        }

    }
}
