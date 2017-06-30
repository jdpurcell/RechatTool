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

namespace RechatTool {
	public static class Rechat {
		static Rechat() {
			ServicePointManager.DefaultConnectionLimit = 8;
			ServicePointManager.SecurityProtocol |=
				SecurityProtocolType.Tls11 |
				SecurityProtocolType.Tls12;
		}

		public static void DownloadFile(long videoId, string path, bool overwrite = false, int? threadCount = null, Action<int, int> progressCallback = null) {
			const int timestampStep = 30;
			const int defaultThreadCount = 4;
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
			int totalSegmentCount = ((int)(lastTimestamp - firstTimestamp) / timestampStep) + 1;
			object syncObj = new object();
			JArray[] segments = new JArray[totalSegmentCount];
			int startedSegmentCount = 0;
			int finishedSegmentCount = 0;
			void DownloadSegment() {
				int iSegment;
				lock (syncObj) {
					iSegment = startedSegmentCount++;
				}
				long segmentTimestamp = firstTimestamp + (iSegment * timestampStep);
				JArray segment = (JArray)JObject.Parse(DownloadUrlAsString(MakeUrl(segmentTimestamp)))["data"];
				lock (syncObj) {
					segments[iSegment] = segment;
					finishedSegmentCount++;
					progressCallback?.Invoke(finishedSegmentCount, totalSegmentCount);
				}
			}
			progressCallback?.Invoke(0, totalSegmentCount);
			Enumerable.Repeat(0, totalSegmentCount)
				.AsParallel()
				.WithDegreeOfParallelism(threadCount ?? defaultThreadCount)
				.ForAll(n => DownloadSegment());
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

		public static void ProcessFile(string pathIn, string pathOut = null, bool overwrite = false) {
			if (pathOut == null) {
				bool isAlreadyTxt = Path.GetExtension(pathIn).Equals(".txt", StringComparison.OrdinalIgnoreCase);
				pathOut = Path.Combine(
					Path.GetDirectoryName(pathIn),
					Path.GetFileNameWithoutExtension(pathIn) + (isAlreadyTxt ? "-p" : "") + ".txt");
			}
			if (File.Exists(pathOut) && !overwrite) {
				throw new Exception("Output file already exists.");
			}
			IEnumerable<string> lines = ParseMessages(pathIn)
				.Select(ToReadableString)
				.Where(n => n != null);
			File.WriteAllLines(pathOut, lines, new UTF8Encoding(true));
		}

		public static List<RechatMessage> ParseMessages(string path) {
			return JArray.Parse(File.ReadAllText(path))
				.OfType<JObject>()
				.Where(n => (string)n["type"] == "rechat-message")
				.Select(n => new RechatMessage(n))
				.ToList();
		}

		private static string ToReadableString(RechatMessage m) {
			if (m.IsRoomState) return null;
			return $"[{m.VideoOffset:hh\\:mm\\:ss\\.fff}] {m.UserName}{(m.IsAction ? "" : ":")} {(m.IsDeleted ? "<message deleted>" : m.MessageText)}";
		}

		public class RechatMessage {
			private static readonly DateTime BaseTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

			public JObject SourceJson { get; }

			private JsonMessage Message { get; }
			private JsonMessageAttributes Attributes => Message.Attributes;
			private JsonMessageTags Tags => Message.Attributes.Tags;

			public RechatMessage(JObject sourceJson) {
				SourceJson = sourceJson;
				Message = sourceJson.ToObject<JsonMessage>();
			}

			public DateTime Timestamp => BaseTime.AddMilliseconds(Attributes.Timestamp);

			public TimeSpan VideoOffset => TimeSpan.FromMilliseconds(Attributes.VideoOffset);

			// State changes such as followers-only, subs-only, or slow mode
			public bool IsRoomState => Attributes.Command?.Equals("ROOMSTATE", StringComparison.OrdinalIgnoreCase) == true;

			// Channel subscription notices
			public bool IsUserNotice => Attributes.Command?.Equals("USERNOTICE", StringComparison.OrdinalIgnoreCase) == true;

			// User said something with "/me"
			public bool IsAction => Tags.Style?.Equals("action", StringComparison.OrdinalIgnoreCase) == true;

			public bool IsDeleted => Attributes.Deleted;

			public string MessageText => Attributes.Message;

			public string UserName => Tags.DisplayName.NullIfEmpty() ?? Attributes.From;

			public bool UserIsModerator => Tags.Mod;

			public bool UserIsSubscriber => Tags.Subscriber;

			private class JsonMessage {
				[JsonProperty("attributes")]
				public JsonMessageAttributes Attributes { get; set; }
			}

			private class JsonMessageAttributes {
				[JsonProperty("command")]
				public string Command { get; set; }
				[JsonProperty("timestamp")]
				public long Timestamp { get; set; }
				[JsonProperty("video-offset")]
				public int VideoOffset { get; set; }
				[JsonProperty("deleted")]
				public bool Deleted { get; set; }
				[JsonProperty("message")]
				public string Message { get; set; }
				[JsonProperty("from")]
				public string From { get; set; }
				[JsonProperty("tags")]
				public JsonMessageTags Tags { get; set; }
			}

			private class JsonMessageTags {
				[JsonProperty("display-name")]
				public string DisplayName { get; set; }
				[JsonProperty("mod")]
				public bool Mod { get; set; }
				[JsonProperty("subscriber")]
				public bool Subscriber { get; set; }
				[JsonProperty("style")]
				public string Style { get; set; }
			}
		}
	}
}
