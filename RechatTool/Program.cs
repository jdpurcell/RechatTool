// --------------------------------------------------------------------------------
// Copyright (c) J.D. Purcell
//
// Licensed under the MIT License (see LICENSE.txt)
// --------------------------------------------------------------------------------
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace RechatTool {
	internal class Program {
		private static int Main(string[] args) {
			int iArg = 0;
			string GetArg(bool optional = false) =>
				iArg < args.Length ? args[iArg++] : optional ? (string)null : throw new InvalidArgumentException();

			try {
				string mode = GetArg();
				if (mode == "-d" || mode == "-D") {
					if (!Int64.TryParse(GetArg(), out long videoId)) {
						throw new InvalidArgumentException();
					}
					string path = GetArg(true) ?? $"{videoId}.json";;
					void UpdateProgress(int downloaded, int total) {
						Console.Write($"\rDownloaded {downloaded} of {total}");
					}
					DownloadFile(videoId, path, false, UpdateProgress);
					if (mode == "-D") {
						ProcessFile(path);
					}
					Console.WriteLine();
					Console.WriteLine("Done!");
				}
				else if (mode == "-p") {
					string path = GetArg();
					if (path.Contains('*') || path.Contains('?')) {
						string[] paths = Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path));
						foreach (string p in paths) {
							ProcessFile(p);
						}
					}
					else {
						ProcessFile(path);
						Console.WriteLine("Done!");
					}
				}
				else {
					throw new InvalidArgumentException();
				}

				return 0;
			}
			catch (InvalidArgumentException) {
				Console.WriteLine("Modes:");
				Console.WriteLine("   -d videoid [path]");
				Console.WriteLine("      Downloads chat replay for the specified videoid. If path is not");
				Console.WriteLine("      specified, the output is saved to the current directory.");
				Console.WriteLine("   -D videoid [path]");
				Console.WriteLine("      Downloads and processes chat replay (combines -d and -p).");
				Console.WriteLine("   -p path");
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

		private static void DownloadFile(long videoId, string path, bool overwrite, Action<int, int> progressCallback) {
			const int timestampStep = 30;
			const int threadCount = 6;
			if (File.Exists(path) && !overwrite) {
				throw new Exception("Output file already exists.");
			}
			string MakeUrl(long timestamp) => $"https://rechat.twitch.tv/rechat-messages?video_id=v{videoId}&start={timestamp}";
			string videoInfo = (string)JObject.Parse(DownloadUrlAsString(MakeUrl(0), true))["errors"][0]["detail"];
			string[] videoInfoSplit = videoInfo.Split(' ');
			if (!videoInfo.StartsWith("0 is not between ", StringComparison.Ordinal) || videoInfoSplit.Length != 7) {
				throw new Exception("Unrecognized response: " + videoInfo);
			}
			long firstTimestamp = Int64.Parse(videoInfoSplit[4]);
			long lastTimestamp = Int64.Parse(videoInfoSplit[6]);
			int segmentCount = ((int)(lastTimestamp - firstTimestamp) / timestampStep) + 1;
			object syncObj = new object();
			JArray[] segments = new JArray[segmentCount];
			int downloadedSegmentCount = 0;
			void DownloadSegment(int iSegment) {
				long segmentTimestamp = firstTimestamp + (iSegment * timestampStep);
				JArray segment = (JArray)JObject.Parse(DownloadUrlAsString(MakeUrl(segmentTimestamp)))["data"];
				lock (syncObj) {
					segments[iSegment] = segment;
					downloadedSegmentCount++;
					progressCallback?.Invoke(downloadedSegmentCount, segmentCount);
				}
			}
			progressCallback?.Invoke(0, segmentCount);
			Parallel.ForEach(
				Enumerable.Range(0, segmentCount),
				new ParallelOptions { MaxDegreeOfParallelism = threadCount },
				DownloadSegment);
			JArray combined = new JArray(segments.SelectMany(s => s).ToArray());
			File.WriteAllText(path, combined.ToString(Formatting.None), new UTF8Encoding(true));
		}

		private static string DownloadUrlAsString(string url, bool allowErrors = false) {
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			HttpWebResponse response;
			try {
				response = (HttpWebResponse)request.GetResponse();
			}
			catch (WebException ex) when (allowErrors) {
				response = (HttpWebResponse)ex.Response;
			}
			using (response)
			using (StreamReader responseStream = new StreamReader(response.GetResponseStream())) {
				return responseStream.ReadToEnd();
			}
		}

		private static void ProcessFile(string pathIn, string pathOut = null, bool overwrite = false) {
			if (pathOut == null) {
				bool isAlreadyTxt = Path.GetExtension(pathIn).Equals(".txt", StringComparison.OrdinalIgnoreCase);
				pathOut = Path.Combine(
					Path.GetDirectoryName(pathIn),
					Path.GetFileNameWithoutExtension(pathIn) + (isAlreadyTxt ? "-p" : "") + ".txt");
			}
			if (File.Exists(pathOut) && !overwrite) {
				throw new Exception("Output file already exists.");
			}
			JArray items = JArray.Parse(File.ReadAllText(pathIn));
			List<Message> messages = items
				.OfType<JObject>()
				.Where(n => (string)n["type"] == "rechat-message")
				.Select(n => new Message(n.ToObject<RawMessage>()))
				.ToList();
			IEnumerable<string> lines = messages
				.Select(m => $"[{m.VideoOffset:hh\\:mm\\:ss\\.fff}] {m.UserDisplayName}: {m.MessageText ?? "<message deleted>"}");
			File.WriteAllLines(pathOut, lines, new UTF8Encoding(true));
		}

		private class Message {
			private static readonly DateTime BaseTime = new DateTime(1970, 1, 1);

			private RawMessage Main { get; }
			private RawMessageAttributes Attributes => Main.Attributes;
			private RawMessageTags Tags => Main.Attributes.Tags;

			public Message(RawMessage main) {
				Main = main;
			}

			public DateTime Timestamp => BaseTime.AddMilliseconds(Attributes.Timestamp);
	
			public TimeSpan VideoOffset => TimeSpan.FromMilliseconds(Attributes.VideoOffset);

			public string MessageText => Attributes.Deleted ? null : Attributes.Message;

			public string UserDisplayName => Tags.DisplayName;

			public bool UserIsModerator => Tags.Mod;

			public bool UserIsSubscriber => Tags.Subscriber;
		}

		private class RawMessage {
			[JsonProperty("attributes")]
			public RawMessageAttributes Attributes { get; set; }
		}

		private class RawMessageAttributes {
			[JsonProperty("timestamp")]
			public long Timestamp { get; set; }
			[JsonProperty("video-offset")]
			public int VideoOffset { get; set; }
			[JsonProperty("deleted")]
			public bool Deleted { get; set; }
			[JsonProperty("message")]
			public string Message { get; set; }
			[JsonProperty("tags")]
			public RawMessageTags Tags { get; set; }
		}

		private class RawMessageTags {
			[JsonProperty("display-name")]
			public string DisplayName { get; set; }
			[JsonProperty("mod")]
			public bool Mod { get; set; }
			[JsonProperty("subscriber")]
			public bool Subscriber { get; set; }
		}

		private class InvalidArgumentException : Exception { }
	}
}
