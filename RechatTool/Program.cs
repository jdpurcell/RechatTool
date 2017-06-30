// --------------------------------------------------------------------------------
// Copyright (c) J.D. Purcell
//
// Licensed under the MIT License (see LICENSE.txt)
// --------------------------------------------------------------------------------
using System;
using System.IO;
using System.Linq;

namespace RechatTool {
	internal class Program {
		public const string Version = "1.2.0.0";

		private static int Main(string[] args) {
			int iArg = 0;
			string PeekArg() =>
				iArg < args.Length ? args[iArg] : null;
			string GetArg(bool optional = false) =>
				iArg < args.Length ? args[iArg++] : optional ? (string)null : throw new InvalidArgumentException();

			try {
				string arg = GetArg();
				if (arg == "-d" || arg == "-D") {
					bool processFile = arg == "-D";
					string videoIdStr = GetArg();
					long videoId = videoIdStr.TryParseInt64() ??
						TryParseVideoIdFromUrl(videoIdStr) ??
						throw new InvalidArgumentException();
					string path = PeekArg()?.StartsWith("-") == false ? GetArg() : $"{videoId}.json";
					bool overwrite = false;
					int? threadCount = null;
					while ((arg = GetArg(true)) != null) {
						if (arg == "-o") {
							overwrite = true;
						}
						else if (arg == "-t") {
							threadCount = GetArg().TryParseInt32() ?? throw new InvalidArgumentException();
							if (threadCount < 1 || threadCount > 8) {
								throw new InvalidArgumentException();
							}
						}
						else {
							throw new InvalidArgumentException();
						}
					}
					void UpdateProgress(int downloaded, int total) {
						Console.Write($"\rDownloaded {downloaded} of {total} segments ({(double)downloaded / total * 100.0:0.0}%)");
					}
					Rechat.DownloadFile(videoId, path, overwrite, threadCount, UpdateProgress);
					if (processFile) {
						Rechat.ProcessFile(path, overwrite: overwrite);
					}
					Console.WriteLine();
					Console.WriteLine("Done!");
				}
				else if (arg == "-p") {
					string[] paths = { GetArg() };
					if (paths[0].IndexOfAny(new[] { '*', '?'}) != -1) {
						paths = Directory.GetFiles(Path.GetDirectoryName(paths[0]), Path.GetFileName(paths[0]));
					}
					bool overwrite = false;
					while ((arg = GetArg(true)) != null) {
						if (arg == "-o") {
							overwrite = true;
						}
						else {
							throw new InvalidArgumentException();
						}
					}
					foreach (string p in paths) {
						Console.WriteLine("Processing " + Path.GetFileName(p));
						Rechat.ProcessFile(p, overwrite: overwrite);
					}
					Console.WriteLine("Done!");
				}
				else {
					throw new InvalidArgumentException();
				}

				return 0;
			}
			catch (InvalidArgumentException) {
				Console.WriteLine($"RechatTool v{new Version(Version).ToDisplayString()}");
				Console.WriteLine();
				Console.WriteLine("Modes:");
				Console.WriteLine("   -d videoid [path] [-o] [-t num]");
				Console.WriteLine("      Downloads chat replay for the specified videoid. If path is not");
				Console.WriteLine("      specified, output is saved to the current directory. -o overwrites");
				Console.WriteLine("      existing output file. -t specifies number of download threads (1 to 8),");
				Console.WriteLine("      otherwise defaults to 4.");
				Console.WriteLine("   -D (same parameters as -d)");
				Console.WriteLine("      Downloads and processes chat replay (combines -d and -p).");
				Console.WriteLine("   -p path [-o]");
				Console.WriteLine("      Processes a JSON chat replay file and outputs a human-readable text file.");
				Console.WriteLine("      Output is written to same folder as the input file with the extension");
				Console.WriteLine("      changed to .txt.");
				return 1;
			}
			catch (Exception ex) {
				Console.WriteLine("\rError: " + ex.Message);
				return 1;
			}
		}

		private static long? TryParseVideoIdFromUrl(string s) {
			string[] hosts = { "twitch.tv", "www.twitch.tv" };
			if (!s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) {
				s = "https://" + s;
			}
			if (!Uri.TryCreate(s, UriKind.Absolute, out Uri uri)) return null;
			if (!hosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase))) return null;
			if (!uri.AbsolutePath.StartsWith("/videos/", StringComparison.Ordinal)) return null;
			return uri.AbsolutePath.Substring(8).TryParseInt64();
		}

		private class InvalidArgumentException : Exception { }
	}
}
