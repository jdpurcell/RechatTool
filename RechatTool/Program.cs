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
		public const string Version = "1.5.0.3";

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
					string path = PeekArg()?.StartsWith("-", StringComparison.Ordinal) == false ? GetArg() : $"{videoId}.json";
					bool overwrite = false;
					while ((arg = GetArg(true)) != null) {
						if (arg == "-o") {
							overwrite = true;
						}
						else {
							throw new InvalidArgumentException();
						}
					}
					void UpdateProgress(int segmentCount, TimeSpan? contentOffset) {
						string message = $"Downloaded {segmentCount} segment{(segmentCount == 1 ? "" : "s")}";
						if (contentOffset != null) {
							message += $", offset = {Rechat.TimestampToString(contentOffset.Value, false)}";
						}
						Console.Write($"\r{message}");
					}
					try {
						Rechat.DownloadFile(videoId, path, overwrite, UpdateProgress);
						Console.WriteLine();
					}
					catch (Rechat.WarningException ex) {
						Console.WriteLine();
						Console.WriteLine($"Warning: {ex.Message}");
					}
					if (processFile) {
						try {
							Console.WriteLine("Processing file");
							Rechat.ProcessFile(path, overwrite: overwrite);
						}
						catch (Rechat.WarningException ex) {
							Console.WriteLine($"Warning: {ex.Message}");
						}
					}
					Console.WriteLine("Done!");
				}
				else if (arg == "-p") {
					string[] paths = { GetArg() };
					string outputPath = null;
					if (paths[0].IndexOfAny(new[] { '*', '?'}) != -1) {
						paths = Directory.GetFiles(Path.GetDirectoryName(paths[0]), Path.GetFileName(paths[0]));
					}
					else if (PeekArg()?.StartsWith("-", StringComparison.Ordinal) == false) {
						outputPath = GetArg();
					}
					bool overwrite = false;
					bool showBadges = false;
					while ((arg = GetArg(true)) != null) {
						if (arg == "-o") {
							overwrite = true;
						}
						else if (arg == "-b") {
							showBadges = true;
						}
						else {
							throw new InvalidArgumentException();
						}
					}
					foreach (string p in paths) {
						Console.WriteLine($"Processing {Path.GetFileName(p)}");
						try {
							Rechat.ProcessFile(p, pathOut: outputPath, overwrite: overwrite, showBadges: showBadges);
						}
						catch (Rechat.WarningException ex) {
							Console.WriteLine($"Warning: {ex.Message}");
						}
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
				Console.WriteLine("   -d videoid [filename] [-o]");
				Console.WriteLine("      Downloads chat replay for the specified videoid.");
				Console.WriteLine("        filename: Output location as relative or absolute filename, otherwise");
				Console.WriteLine("          defaults to the current directory and named as videoid.json.");
				Console.WriteLine("        -o: Overwrite the existing output file.");
				Console.WriteLine("   -D (same parameters as -d)");
				Console.WriteLine("      Downloads and processes chat replay (combines -d and -p).");
				Console.WriteLine("   -p filename [output_filename] [-o] [-b]");
				Console.WriteLine("      Processes a JSON chat replay file and outputs a human-readable text file.");
				Console.WriteLine("        output_filename: Output location as relative or absolute filename,");
				Console.WriteLine("            otherwise defaults to the same location as the input file with the");
				Console.WriteLine("            extension changed to .txt.");
				Console.WriteLine("        -o: Overwrite the existing output file. ");
				Console.WriteLine("        -b: Show user badges (e.g. moderator/subscriber).");
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
