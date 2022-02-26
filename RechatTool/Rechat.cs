﻿// --------------------------------------------------------------------------------
// Copyright (c) J.D. Purcell
//
// Licensed under the MIT License (see LICENSE.txt)
// --------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

#if NETFRAMEWORK
using System.Net;
#else
using System.Net.Http;
#endif

namespace RechatTool {
	public static class Rechat {
		public static void DownloadFile(long videoId, string path, bool overwrite = false, DownloadProgressCallback progressCallback = null) {
			if (File.Exists(path) && !overwrite) {
				throw new Exception("Output file already exists.");
			}
			string baseUrl = $"https://api.twitch.tv/v5/videos/{videoId}/comments";
			string nextCursor = null;
			int segmentCount = 0;
			JObject firstComment = null;
			JObject lastComment = null;
			bool finishedDownload = false;
			try {
				using JsonTextWriter writer = new(new StreamWriter(path, false, new UTF8Encoding(true)));
				writer.WriteStartArray();
				do {
					string url = nextCursor == null ?
						$"{baseUrl}?content_offset_seconds=0" :
						$"{baseUrl}?cursor={nextCursor}";
					JObject response = JObject.Parse(DownloadUrlAsString(url, withRequest: AddTwitchApiHeaders));
					foreach (JObject comment in (JArray)response["comments"]) {
						comment.WriteTo(writer);
						firstComment ??= comment;
						lastComment = comment;
					}
					nextCursor = (string)response["_next"];
					segmentCount++;
					progressCallback?.Invoke(segmentCount, TryGetContentOffset(lastComment));
				}
				while (nextCursor != null);
				writer.WriteEndArray();
				finishedDownload = true;
			}
			finally {
				if (!finishedDownload) {
					try {
						File.Delete(path);
					}
					catch { }
				}
			}
			if (firstComment != null) {
				try {
					RechatMessage firstMessage = new(firstComment);
					RechatMessage lastMessage = new(lastComment);
					File.SetCreationTimeUtc(path, firstMessage.CreatedAt - firstMessage.ContentOffset);
					File.SetLastWriteTimeUtc(path, lastMessage.CreatedAt);
				}
				catch (Exception ex) {
					throw new WarningException("Unable to set file created/modified time.", ex);
				}
			}
		}

#if NETFRAMEWORK
		private static string DownloadUrlAsString(string url, Action<HttpWebRequest> withRequest = null) {
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			withRequest?.Invoke(request);
			using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			using StreamReader responseStream = new(response.GetResponseStream());
			return responseStream.ReadToEnd();
		}

		private static void AddTwitchApiHeaders(HttpWebRequest request) {
			request.Accept = "application/vnd.twitchtv.v5+json";
			request.Headers.Add("Client-ID", "jzkbprff40iqj646a697cyrvl0zt2m6");
		}
#else
		private static string DownloadUrlAsString(string url, Action<HttpRequestMessage> withRequest = null) {
			using HttpClient client = new();
			using HttpRequestMessage request = new(HttpMethod.Get, url);
			withRequest?.Invoke(request);
			using HttpResponseMessage response = client.Send(request);
			using StreamReader responseStream = new(response.Content.ReadAsStream());
			return responseStream.ReadToEnd();
		}

		private static void AddTwitchApiHeaders(HttpRequestMessage request) {
			request.Headers.Add("Accept", "application/vnd.twitchtv.v5+json");
			request.Headers.Add("Client-ID", "jzkbprff40iqj646a697cyrvl0zt2m6");
		}
#endif

		private static TimeSpan? TryGetContentOffset(JObject comment) {
			try {
				return comment == null ? null : new RechatMessage(comment).ContentOffset;
			}
			catch {
				return null;
			}
		}

		public static void ProcessFile(string pathIn, string pathOut = null, bool overwrite = false, bool showBadges = false) {
			if (!File.Exists(pathIn)) {
				throw new Exception("Input file does not exist.");
			}
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
			try {
				File.SetCreationTimeUtc(pathOut, File.GetCreationTimeUtc(pathIn));
				File.SetLastWriteTimeUtc(pathOut, File.GetLastWriteTimeUtc(pathIn));
			}
			catch (Exception ex) {
				throw new WarningException("Unable to set file created/modified time.", ex);
			}
		}

		public static IEnumerable<RechatMessage> ParseMessages(string path) {
			using JsonTextReader reader = new(File.OpenText(path));
			while (reader.Read()) {
				if (reader.TokenType != JsonToken.StartObject) continue;
				yield return new RechatMessage(JObject.Load(reader));
			}
		}

		public static string TimestampToString(TimeSpan value, bool showMilliseconds) {
			return $"{(int)value.TotalHours:00}:{value:mm}:{value:ss}{(showMilliseconds ? $".{value:fff}" : "")}";
		}

		private static string ToReadableString(RechatMessage m, bool showBadges) {
			string userBadges = $"{(m.UserIsAdmin || m.UserIsStaff ? "*" : "")}{(m.UserIsBroadcaster ? "#" : "")}{(m.UserIsModerator || m.UserIsGlobalModerator ? "@" : "")}{(m.UserIsSubscriber ? "+" : "")}";
			string userName = m.UserName == null ? "???" : String.Equals(m.UserDisplayName, m.UserName, StringComparison.OrdinalIgnoreCase) ? m.UserDisplayName : $"{m.UserDisplayName} ({m.UserName})";
			return $"[{TimestampToString(m.ContentOffset, true)}] {(showBadges ? userBadges : "")}{userName}{(m.IsAction ? "" : ":")} {m.MessageText}";
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

			public TimeSpan ContentOffset => new TimeSpan((long)Math.Round(Comment.ContentOffsetSeconds * 1000.0) * TimeSpan.TicksPerMillisecond);

			// User said something with "/me"
			public bool IsAction => Message.IsAction;

			// Not from the live chat (i.e. user posted a comment on the VOD)
			public bool IsNonChat => !Comment.Source.Equals("chat", StringComparison.OrdinalIgnoreCase);

			public string MessageText => Message.Body;

			public string UserName => Commenter?.Name;

			public string UserDisplayName => Commenter?.DisplayName.TrimEnd(' ');

			public bool UserIsAdmin => HasBadge("admin");

			public bool UserIsStaff => HasBadge("staff");

			public bool UserIsGlobalModerator => HasBadge("global_mod");

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
				public string Version { get; set; }

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
				public string Version { get; internal set; }
			}
		}

		public delegate void DownloadProgressCallback(int segmentCount, TimeSpan? contentOffset);
	}
}
