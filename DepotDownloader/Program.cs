﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using SteamKit2;
using System.ComponentModel;

namespace DepotDownloader
{
    class Program
    {
        static void Main( string[] args )
        {
            if ( args.Length == 0 )
            {
                PrintUsage();
                return;
            }

            DebugLog.Enabled = false;

            ConfigStore.LoadFromFile(Path.Combine(Environment.CurrentDirectory, "DepotDownloader.config"));

            bool bDumpManifest = HasParameter( args, "-manifest-only" );
            bool bVerbose = HasParameter(args, "-v");
            uint appId = GetParameter<uint>( args, "-app", ContentDownloader.INVALID_APP_ID );
            uint depotId = GetParameter<uint>( args, "-depot", ContentDownloader.INVALID_DEPOT_ID );
            ContentDownloader.Config.ManifestId = GetParameter<ulong>( args, "-manifest", ContentDownloader.INVALID_MANIFEST_ID );

            if ( appId == ContentDownloader.INVALID_APP_ID )
            {
                Console.WriteLine( "Error: -app not specified!" );
                return;
            }

            if (depotId == ContentDownloader.INVALID_DEPOT_ID && ContentDownloader.Config.ManifestId != ContentDownloader.INVALID_MANIFEST_ID)
            {
                Console.WriteLine("Error: -manifest requires -depot to be specified");
                return;
            }

            ContentDownloader.Config.DownloadManifestOnly = bDumpManifest;
            ContentDownloader.Config.Verbose = bVerbose;

            int cellId = GetParameter<int>(args, "-cellid", -1);
            if (cellId == -1)
            {
                cellId = 0;
            }

            ContentDownloader.Config.CellID = cellId;
            ContentDownloader.Config.BetaPassword = GetParameter<string>(args, "-betapassword");

            string fileList = GetParameter<string>(args, "-filelist");
            string[] files = null;

            if ( fileList != null )
            {
                try
                {
                    string fileListData = File.ReadAllText( fileList );
                    files = fileListData.Split( new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries );

                    ContentDownloader.Config.UsingFileList = true;
                    ContentDownloader.Config.FilesToDownload = new List<string>();
                    ContentDownloader.Config.FilesToDownloadRegex = new List<Regex>();

                    foreach (var fileEntry in files)
                    {
                        try
                        {
                            Regex rgx = new Regex(fileEntry, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                            ContentDownloader.Config.FilesToDownloadRegex.Add(rgx);
                        }
                        catch
                        {
                            ContentDownloader.Config.FilesToDownload.Add(fileEntry);
                            continue;
                        }
                    }

                    Console.WriteLine( "Using filelist: '{0}'.", fileList );
                }
                catch ( Exception ex )
                {
                    Console.WriteLine( "Warning: Unable to load filelist: {0}", ex.ToString() );
                }
            }

            string username = GetParameter<string>(args, "-username") ?? GetParameter<string>(args, "-user");
            string password = GetParameter<string>(args, "-password") ?? GetParameter<string>(args, "-pass");
            ContentDownloader.Config.InstallDirectory = GetParameter<string>(args, "-dir");
            ContentDownloader.Config.DownloadAllPlatforms = HasParameter(args, "-all-platforms");
            ContentDownloader.Config.VerifyAll = HasParameter(args, "-verify-all") || HasParameter(args, "-verify_all") || HasParameter(args, "-validate");
            ContentDownloader.Config.MaxServers = GetParameter<int>(args, "-max-servers", 8);
            ContentDownloader.Config.MaxDownloads = GetParameter<int>(args, "-max-downloads", 4);
            string branch = GetParameter<string>(args, "-branch") ?? GetParameter<string>(args, "-beta") ?? "Public";
            var forceDepot = HasParameter(args, "-force-depot");

            ContentDownloader.Config.MaxServers = Math.Max(ContentDownloader.Config.MaxServers, ContentDownloader.Config.MaxDownloads);

            if (username != null && password == null)
            {
                Console.Write("Enter account password for \"{0}\": ", username);
                password = Util.ReadPassword();
                Console.WriteLine();
            }
            else if (username == null)
            {
                Console.WriteLine("No username given. Using anonymous account with dedicated server subscription.");
            }

            int retCode = 0;

            if (appId == 0) // we will get apps and depots from list
            {
                if (!File.Exists("depot_list.txt"))
                {
                    Console.WriteLine("depot_list.txt does not exist, aborting.");
                    Environment.Exit(10);
                }

                Console.WriteLine("Bulk downloading apps and depots...");

                ContentDownloader.InitializeSteam3(username, password);

                string[] lines = System.IO.File.ReadAllLines(@"depot_list.txt");

                // Display the file contents by using a foreach loop.
                foreach (string line in lines)
                {
                    string[] tuple = line.Split(',');
                    if (tuple.Length < 2)
                        continue;

                    appId = UInt32.Parse(tuple[0]);
                    depotId = UInt32.Parse(tuple[1]);

                    Console.WriteLine("App: {0} |  Depot: {1}", appId, depotId);
                    retCode = ContentDownloader.DownloadApp(appId, depotId, branch, forceDepot);
                    Console.Write("\n\n\n");
                }

            }
            else
            {
                ContentDownloader.InitializeSteam3(username, password);
                retCode = ContentDownloader.DownloadApp(appId, depotId, branch, forceDepot);
            }
            ContentDownloader.ShutdownSteam3();

            Environment.Exit(retCode);
        }

        static int IndexOfParam( string[] args, string param )
        {
            for ( int x = 0 ; x < args.Length ; ++x )
            {
                if ( args[ x ].Equals( param, StringComparison.OrdinalIgnoreCase ) )
                    return x;
            }
            return -1;
        }
        static bool HasParameter( string[] args, string param )
        {
            return IndexOfParam( args, param ) > -1;
        }

        static T GetParameter<T>(string[] args, string param, T defaultValue = default(T))
        {
            int index = IndexOfParam(args, param);

            if (index == -1 || index == (args.Length - 1))
                return defaultValue;

            string strParam = args[index + 1];

            var converter = TypeDescriptor.GetConverter(typeof(T));
            if( converter != null )
            {
                return (T)converter.ConvertFromString(strParam);
            }
            
            return default(T);
        }

        static void PrintUsage()
        {
            Console.WriteLine( "\nUsage: depotdownloader <parameters> [optional parameters]\n" );

            Console.WriteLine( "Parameters:" );
            Console.WriteLine("\t-app <#>\t\t\t\t- the AppID to download.");            
            Console.WriteLine();

            Console.WriteLine( "Optional Parameters:" );
            Console.WriteLine( "\t-depot <#>\t\t\t- the DepotID to download." );
            Console.WriteLine( "\t-cellid <#>\t\t\t- the overridden CellID of the content server to download from." );
            Console.WriteLine( "\t-username <user>\t\t\t- the username of the account to login to for restricted content." );
            Console.WriteLine( "\t-password <pass>\t\t\t- the password of the account to login to for restricted content." );
            Console.WriteLine( "\t-dir <installdir>\t\t\t- the directory in which to place downloaded files." );
            Console.WriteLine( "\t-filelist <filename.txt>\t\t- a list of files to download (from the manifest). Can optionally use regex to download only certain files." );
            Console.WriteLine( "\t-all-platforms\t\t\t- downloads all platform-specific depots when -app is used." );
            Console.WriteLine( "\t-manifest-only\t\t\t- downloads a human readable manifest for any depots that would be downloaded." );
            Console.WriteLine( "\t-beta <branchname>\t\t\t\t- download from specified branch if available (default: Public)." );
            Console.WriteLine( "\t-betapassword <pass>\t\t\t- branch password if applicable." );
            Console.WriteLine( "\t-manifest <id>\t\t\t- manifest id of content to download (requires -depot, default: current for branch)." );
            Console.WriteLine( "\t-max-servers <#>\t\t\t- maximum number of content servers to use. (default: 8)." );
            Console.WriteLine( "\t-max-downloads <#>\t\t\t- maximum number of chunks to download concurrently. (default: 4)." );
            Console.WriteLine( "\t-v\t\t\t- be verbose, write more information.");
        }
    }
}
