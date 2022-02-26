using System;
using System.IO;

#if NETFRAMEWORK
using System.Net;
#else
using System.Net.Http;
#endif

namespace RechatTool {
	internal class TwitchApi5Client : IDisposable {
#if !NETFRAMEWORK
		private readonly HttpClient _httpClient = new();
#endif

		public void Dispose() {
#if !NETFRAMEWORK
			_httpClient.Dispose();
#endif
		}

		public string Request(string url) {
#if NETFRAMEWORK
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			request.Accept = "application/vnd.twitchtv.v5+json";
			request.Headers.Add("Client-ID", "kimne78kx3ncx6brgo4mv6wki5h1ko");
			using HttpWebResponse response = (HttpWebResponse)request.GetResponse();
			using StreamReader responseStream = new(response.GetResponseStream());
			return responseStream.ReadToEnd();
#else
			using HttpRequestMessage request = new(HttpMethod.Get, url);
			request.Headers.Add("Accept", "application/vnd.twitchtv.v5+json");
			request.Headers.Add("Client-ID", "kimne78kx3ncx6brgo4mv6wki5h1ko");
			using HttpResponseMessage response = _httpClient.Send(request);
			using StreamReader responseStream = new(response.Content.ReadAsStream());
			return responseStream.ReadToEnd();
#endif
		}
	}
}
