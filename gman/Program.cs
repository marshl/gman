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
    using System.Net.Sockets;
    using Mono.Options;
    using Oracle.ManagedDataAccess.Client;

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
                Debug("Unknown argument(s): {0}", message);
                return;
            }

            if (password == null || hostname == null || sid == null)
            {
                ShowHelp(p);
                return;
            }

            OracleConnection con = CreateConnection(username, password, hostname, port, sid);
            if (con == null)
            {
                return;
            }
        }

        static void ShowHelp(OptionSet p)
        {
            Console.WriteLine("Usage: greet [OPTIONS]+ message");
            Console.WriteLine("Greet a list of individuals with an optional message.");
            Console.WriteLine("If no message is specified, a generic greeting is used.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }

        static void Debug(string format, params object[] args)
        {
            Console.Write("# ");
            Console.WriteLine(format, args);
        }

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

    }
}
