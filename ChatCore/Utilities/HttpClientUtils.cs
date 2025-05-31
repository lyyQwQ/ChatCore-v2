using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ChatCore.Utilities
{
    public class HttpClientUtils
    {
		public static string UserAgent = @"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0";

        public async Task<List<string>> HttpClient(string url, HttpMethod httpMethod, string? cookieVal, HttpContent? content)
        {
			Console.WriteLine($"[HttpClientUtils] | [HttpClient] | {httpMethod} {url}");
			var client = new HttpClient(new HttpClientHandler() { UseCookies = false, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
            var result = new List<string>();
			try
			{
				var httpRequestMessage = new HttpRequestMessage(httpMethod, url);
				httpRequestMessage.Headers.Add("User-Agent", UserAgent);
				
				// 添加 Bilibili 需要的标准请求头
				httpRequestMessage.Headers.Add("Referer", "https://live.bilibili.com");
				httpRequestMessage.Headers.Add("Origin", "https://live.bilibili.com");
				httpRequestMessage.Headers.Add("Accept", "application/json, text/plain, */*");
				httpRequestMessage.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
				
				if (cookieVal != null && cookieVal.Trim() != "")
				{
					httpRequestMessage.Headers.Add("Cookie", cookieVal);
				}
				if (httpMethod == HttpMethod.Post && content != null)
				{
					httpRequestMessage.Content = content;
				}
				HttpResponseMessage response = await client.SendAsync(httpRequestMessage);
				var responseBody = await response.Content.ReadAsStringAsync();
				IEnumerable<string> cookies = response.Headers.SingleOrDefault(header => header.Key == "Set-Cookie").Value;
				switch (response.StatusCode)
				{
					case HttpStatusCode.OK:
						result.Add("OK");
						result.Add(responseBody);
						break;
					default:
						Console.WriteLine($"[HttpClientUtils] | [HttpClient] | Response {response.StatusCode}: {responseBody}");
						result.Add("error");
						result.Add($"{response.StatusCode}");
						break;
				}
				result.Add(cookies == null ? string.Empty : new HttpClientUtils().RemoveExpiredTimeAndPath(string.Join("; ", cookies.ToList())));
			}
			catch (Exception e)
			{
				Console.WriteLine($"[HttpClientUtils] | [HttpClient] | Exception: {e.GetType().Name} - {e.Message}");
				result.Add("error");
				result.Add($"{e.Message}");
			}
            return result;
        }

        public async Task<string> GetRedirectedUrl(string url)
        {
            //this allows you to set the settings so that we can get the redirect url
            var handler = new HttpClientHandler()
            {
                AllowAutoRedirect = false
			};
			var redirectedUrl = string.Empty;

            using (var client = new HttpClient(handler))
            using (HttpResponseMessage response = await client.GetAsync(url))
            using (HttpContent content = response.Content)
            {
                // ... Read the response to see if we have the redirected url
                if (response.StatusCode == System.Net.HttpStatusCode.Found)
                {
                    HttpResponseHeaders headers = response.Headers;
                    if (headers != null && headers.Location != null)
                    {
                        redirectedUrl = headers.Location.AbsoluteUri;
                    }
                }
            }

            return redirectedUrl;
        }

		public string RemoveExpiredTimeAndPath(string cookies) {
			var cookies_arr = cookies.Split(';');
			var new_cookies_list = new List<string>();
			foreach (var cookie_item in cookies_arr)
			{
				var value = cookie_item.Trim();
				if (value.StartsWith("Path=") || value.StartsWith("Domain=") || value.StartsWith("Expires=") || value.IndexOf("=") == -1)
				{
					continue;
				}
				new_cookies_list.Add(value);
			}

			return string.Join("; ", new_cookies_list);
		}
    }
}
