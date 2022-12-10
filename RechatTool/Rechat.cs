// --------------------------------------------------------------------------------
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

namespace RechatTool;

public static class Rechat {
	public static void DownloadFile(long videoId, string path, bool overwrite = false, DownloadProgressCallback progressCallback = null) {
		if (File.Exists(path) && !overwrite) {
			throw new Exception("Output file already exists.");
		}
		const string baseQuery = """[{"operationName":"VideoCommentsByOffsetOrCursor","variables":{},"extensions":{"persistedQuery":{"version":1,"sha256Hash":"b70a3591ff0f4e0313d126c6a1502d79a1c02baebb288227c582044aa76adf6a"}}}]""";
		string nextCursor = null;
		int segmentCount = 0;
		JObject firstComment = null;
		JObject lastComment = null;
		bool finishedDownload = false;
		try {
			using TwitchApiClient apiClient = new();
			using JsonTextWriter writer = new(new StreamWriter(path, false, new UTF8Encoding(true)));
			writer.WriteStartArray();
			do {
				List<string> queryVariables = new() {
					$"\"videoID\":\"{videoId}\"",
					nextCursor == null ?
						"\"contentOffsetSeconds\":0" :
						$"\"cursor\":\"{nextCursor}\""
				};
				JArray response = JArray.Parse(apiClient.Request(
					"https://gql.twitch.tv/gql",
					baseQuery.Replace("\"variables\":{}", $"\"variables\":{{{queryVariables.StringJoin(",")}}}")
				));
				nextCursor = null;
				foreach (JObject commentEdge in (JArray)response[0]["data"]["video"]["comments"]["edges"]) {
					JObject comment = (JObject)commentEdge["node"];
					comment.WriteTo(writer);
					firstComment ??= comment;
					lastComment = comment;
					nextCursor = (string)commentEdge["cursor"];
				}
				segmentCount++;
				progressCallback?.Invoke(segmentCount, TryGetContentOffset(lastComment));
			}
			while (!String.IsNullOrEmpty(nextCursor));
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

	public static string TimestampToString(TimeSpan value) {
		return $"{(int)value.TotalHours:00}:{value:mm}:{value:ss}";
	}

	private static string ToReadableString(RechatMessage m, bool showBadges) {
		string userBadges = $"{(m.UserIsAdmin || m.UserIsStaff ? "*" : "")}{(m.UserIsBroadcaster ? "#" : "")}{(m.UserIsModerator || m.UserIsGlobalModerator ? "@" : "")}{(m.UserIsSubscriber ? "+" : "")}";
		string userName = m.UserLogin == null ? "???" : String.Equals(m.UserDisplayName, m.UserLogin, StringComparison.OrdinalIgnoreCase) ? m.UserDisplayName : $"{m.UserDisplayName} ({m.UserLogin})";
		return $"[{TimestampToString(m.ContentOffset)}] {(showBadges ? userBadges : "")}{userName}: {m.MessageText}";
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

		public string MessageText => (Message.Fragments?.Select(f => f.Text) ?? Enumerable.Empty<string>()).StringJoin("");

		public string UserLogin => Commenter?.Login;

		public string UserDisplayName => Commenter?.DisplayName.TrimEnd(' ');

		public bool UserIsAdmin => HasBadge("admin");

		public bool UserIsStaff => HasBadge("staff");

		public bool UserIsGlobalModerator => HasBadge("global_mod");

		public bool UserIsBroadcaster => HasBadge("broadcaster");

		public bool UserIsModerator => HasBadge("moderator");

		public bool UserIsSubscriber => HasBadge("subscriber");

		public IEnumerable<UserBadge> UserBadges => Message.UserBadges?.Select(n => n.ToUserBadge()) ?? Enumerable.Empty<UserBadge>();

		private bool HasBadge(string id) => Message.UserBadges?.Any(n => n.SetId.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? false;

		private class JsonComment {
			[JsonProperty("createdAt")]
			public DateTime CreatedAt { get; set; }
			[JsonProperty("contentOffsetSeconds")]
			public double ContentOffsetSeconds { get; set; }
			[JsonProperty("commenter")]
			public JsonCommentCommenter Commenter { get; set; }
			[JsonProperty("message")]
			public JsonCommentMessage Message { get; set; }
		}

		private class JsonCommentCommenter {
			[JsonProperty("displayName")]
			public string DisplayName { get; set; }
			[JsonProperty("login")]
			public string Login { get; set; }
		}

		private class JsonCommentMessage {
			[JsonProperty("fragments")]
			public JsonCommentFragment[] Fragments { get; set; }
			[JsonProperty("userBadges")]
			public JsonCommentUserBadge[] UserBadges { get; set; }
		}

		private class JsonCommentFragment {
			[JsonProperty("text")]
			public string Text { get; set; }
		}

		private class JsonCommentUserBadge {
			[JsonProperty("setID")]
			public string SetId { get; set; }
			[JsonProperty("version")]
			public string Version { get; set; }

			public UserBadge ToUserBadge() {
				return new UserBadge {
					SetId = SetId,
					Version = Version
				};
			}
		}

		public class UserBadge {
			internal UserBadge() { }

			public string SetId { get; internal set; }
			public string Version { get; internal set; }
		}
	}

	public delegate void DownloadProgressCallback(int segmentCount, TimeSpan? contentOffset);
}
