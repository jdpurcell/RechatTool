using System;
using System.IO;
using System.Net.Http;

namespace RechatTool;

internal class TwitchApiClient : IDisposable {
	private readonly HttpClient _httpClient = new();

	public void Dispose() {
		_httpClient.Dispose();
	}

	public string Request(string url, string content) {
		using HttpRequestMessage request = new(HttpMethod.Post, url);
		request.Headers.Add("Client-ID", "kimne78kx3ncx6brgo4mv6wki5h1ko");
		request.Content = new StringContent(content);
		using HttpResponseMessage response =
#if NETFRAMEWORK
			AsyncHelper.RunSync(() => _httpClient.SendAsync(request));
#else
			_httpClient.Send(request);
#endif
		using StreamReader responseStream =
#if NETFRAMEWORK
			new(AsyncHelper.RunSync(() => response.Content.ReadAsStreamAsync()));
#else
			new(response.Content.ReadAsStream());
#endif
		return responseStream.ReadToEnd();
	}
}
