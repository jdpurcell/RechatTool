// --------------------------------------------------------------------------------
// Copyright (c) J.D. Purcell
//
// Licensed under the MIT License (see LICENSE.txt)
// --------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RechatTool {
	public static class Rechat {
		public static void DownloadFile(long videoId, string path, bool overwrite = false, Action<int> progressCallback = null) {
			if (File.Exists(path) && !overwrite) {
				throw new Exception("Output file already exists.");
			}
			string baseUrl = $"{"https"}://api.twitch.tv/v5/videos/{videoId}/comments";
			var segments = new List<JArray>();
			string nextCursor = null;
			do {
				string url = nextCursor == null ?
					$"{baseUrl}?content_offset_seconds=0" :
					$"{baseUrl}?cursor={nextCursor}";
				JObject response = JObject.Parse(DownloadUrlAsString(url, withRequest: AddTwitchApiHeaders));
				segments.Add((JArray)response["comments"]);
				nextCursor = (string)response["_next"];
				progressCallback?.Invoke(segments.Count);
			}
			while (nextCursor != null);
			JArray combined = new JArray(segments.SelectMany(s => s));
			File.WriteAllText(path, combined.ToString(Formatting.None), new UTF8Encoding(true));
		}

		private static string DownloadUrlAsString(string url, Action<HttpWebRequest> withRequest = null) {
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			withRequest?.Invoke(request);
			using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
			using (StreamReader responseStream = new StreamReader(response.GetResponseStream())) {
				return responseStream.ReadToEnd();
			}
		}

		private static void AddTwitchApiHeaders(HttpWebRequest request) {
			request.Accept = "application/vnd.twitchtv.v5+json";
			request.Headers.Add("Client-ID", "jzkbprff40iqj646a697cyrvl0zt2m6");
		}

		public static void ProcessFile(string pathIn, string pathOut = null, bool overwrite = false, bool showBadges = false) {
			if (pathOut == null) {
				bool isAlreadyTxt = pathIn.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
				pathOut = Path.Combine(
					Path.GetDirectoryName(pathIn),
					Path.GetFileNameWithoutExtension(pathIn) + (isAlreadyTxt ? "-p" : "") + ".txt");
			}
			if (File.Exists(pathOut) && !overwrite) {
				throw new Exception("Output file already exists.");
			}
			IEnumerable<string> lines = ParseMessages(pathIn)
				.Select(n => ToReadableString(n, showBadges))
				.Where(n => n != null);
			File.WriteAllLines(pathOut, lines, new UTF8Encoding(true));
		}

		public static List<RechatMessage> ParseMessages(string path) {
			return JArray.Parse(File.ReadAllText(path))
				.Cast<JObject>()
				.Select(n => new RechatMessage(n))
				.ToList();
		}

		private static string ToReadableString(RechatMessage m, bool showBadges) {
			string userBadges = $"{(m.UserIsBroadcaster ? "#" : "")}{(m.UserIsModerator ? "@" : "")}{(m.UserIsSubscriber ? "+" : "")}";
			string userName = m.UserDisplayName.Equals(m.UserName, StringComparison.OrdinalIgnoreCase) ? m.UserDisplayName : $"{m.UserDisplayName} ({m.UserName})";
			return $"[{m.ContentOffset:hh\\:mm\\:ss\\.fff}] {(showBadges ? userBadges : "")}{userName}{(m.IsAction ? "" : ":")} {m.MessageText}";
		}

		public class RechatMessage {
			public JObject SourceJson { get; }

			private JsonComment Comment { get; }
			private JsonCommentCommenter Commenter => Comment.Commenter;
			private JsonCommentMessage Message => Comment.Message;

			public RechatMessage(JObject sourceJson) {
				SourceJson = sourceJson;
				Comment = sourceJson.ToObject<JsonComment>();
			}

			public DateTime CreatedAt => Comment.CreatedAt;

			public TimeSpan ContentOffset => TimeSpan.FromSeconds(Comment.ContentOffsetSeconds);

			// User said something with "/me"
			public bool IsAction => Message.IsAction;

			// Not from the live chat (i.e. user posted a comment on the VOD)
			public bool IsNonChat => !Comment.Source.Equals("chat", StringComparison.OrdinalIgnoreCase);

			public string MessageText => Message.Body;

			public string UserName => Commenter.Name;

			public string UserDisplayName => Commenter.DisplayName.TrimEnd(' ');

			public bool UserIsBroadcaster => HasBadge("broadcaster");

			public bool UserIsModerator => HasBadge("moderator");

			public bool UserIsSubscriber => HasBadge("subscriber");

			public IEnumerable<UserBadge> UserBadges => Message.UserBadges?.Select(n => n.ToUserBadge()) ?? Enumerable.Empty<UserBadge>();

			private bool HasBadge(string id) => Message.UserBadges?.Any(n => n.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? false;

			private class JsonComment {
				[JsonProperty("created_at")]
				public DateTime CreatedAt { get; set; }
				[JsonProperty("content_offset_seconds")]
				public double ContentOffsetSeconds { get; set; }
				[JsonProperty("source")]
				public string Source { get; set; }
				[JsonProperty("commenter")]
				public JsonCommentCommenter Commenter { get; set; }
				[JsonProperty("message")]
				public JsonCommentMessage Message { get; set; }
			}

			private class JsonCommentCommenter {
				[JsonProperty("display_name")]
				public string DisplayName { get; set; }
				[JsonProperty("name")]
				public string Name { get; set; }
			}

			private class JsonCommentMessage {
				[JsonProperty("body")]
				public string Body { get; set; }
				[JsonProperty("is_action")]
				public bool IsAction { get; set; }
				[JsonProperty("user_badges")]
				public JsonCommentUserBadge[] UserBadges { get; set; }
			}

			private class JsonCommentUserBadge {
				[JsonProperty("_id")]
				public string Id { get; set; }
				[JsonProperty("version")]
				public int Version { get; set; }

				public UserBadge ToUserBadge() {
					return new UserBadge {
						Id = Id,
						Version = Version
					};
				}
			}

			public class UserBadge {
				internal UserBadge() { }

				public string Id { get; internal set; }
				public int Version { get; internal set; }
			}
		}
	}
}
