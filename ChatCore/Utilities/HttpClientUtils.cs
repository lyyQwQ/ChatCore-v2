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
 // 🔥 关键修复：确保User-Agent与Python版本(wbi_manager.py:63)完全一致
 public static string UserAgent = @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/81.0.4044.138 Safari/537.36";
 public static string WbiUserAgent = @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/81.0.4044.138 Safari/537.36";

        private readonly string _userAgent;

        public HttpClientUtils(bool useWbiUserAgent = false)
        {
            _userAgent = useWbiUserAgent ? WbiUserAgent : UserAgent;
        }

        public async Task<List<string>> HttpClient(string url, HttpMethod httpMethod, string? cookieVal, HttpContent? content)
        {
			Console.WriteLine($"[HttpClientUtils] | [HttpClient] | 🚀 START REQUEST: {httpMethod} {url}");
			Console.WriteLine($"[HttpClientUtils] | [HttpClient] | 🚀 Using Python-aligned configuration");
			// 🔥 关键修复：优化HttpClient配置以匹配Python aiohttp行为
			var handler = new HttpClientHandler()
			{
				UseCookies = false,  // 🔥 禁用自动cookie处理，手动设置
				AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
				// 确保SSL行为与Python一致
				ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
			};
			
			var client = new HttpClient(handler);
			// 设置超时时间与Python版本一致（10秒）
			client.Timeout = TimeSpan.FromSeconds(10);
            var result = new List<string>();
			try
			{
				var httpRequestMessage = new HttpRequestMessage(httpMethod, url);
				// 🔥 强制使用HTTP/1.1（Python requests默认使用HTTP/1.1）
				httpRequestMessage.Version = new Version(1, 1);
				httpRequestMessage.Headers.Add("User-Agent", _userAgent);
				
				// 🔥 关键修复：完全照搬Python成功实现的请求头配置
				// 参考 auth_validator.py:28-33，只保留Python版本中的核心请求头
				httpRequestMessage.Headers.Add("Accept", "application/json, text/plain, */*");
				httpRequestMessage.Headers.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
				httpRequestMessage.Headers.Add("Referer", "https://www.bilibili.com/");
				httpRequestMessage.Headers.Add("Origin", "https://www.bilibili.com");
				// 🔥 禁用Keep-Alive，Python requests默认不使用持久连接
				httpRequestMessage.Headers.Add("Connection", "close");
				
				// 调试：输出关键请求头（与Python版本对齐）
				Console.WriteLine($"[HttpClientUtils] | [HttpClient] | 🔥 Python-aligned Request Headers:");
				Console.WriteLine($"[HttpClientUtils] | [HttpClient] | User-Agent: {_userAgent}");
				Console.WriteLine($"[HttpClientUtils] | [HttpClient] | Accept: application/json, text/plain, */*");
				Console.WriteLine($"[HttpClientUtils] | [HttpClient] | Accept-Language: zh-CN,zh;q=0.9,en;q=0.8");
				Console.WriteLine($"[HttpClientUtils] | [HttpClient] | Referer: https://www.bilibili.com/");
				Console.WriteLine($"[HttpClientUtils] | [HttpClient] | Origin: https://www.bilibili.com");
				
				// 🔥 关键修复：优化Cookie处理，确保与Python版本(auth_validator.py:47-48)一致
				if (cookieVal != null && cookieVal.Trim() != "")
				{
					httpRequestMessage.Headers.Add("Cookie", cookieVal);
					Console.WriteLine($"[HttpClientUtils] | [HttpClient] | 🍪 Cookie length: {cookieVal.Length} chars");
					// 调试：输出Cookie前50字符以便验证格式
					var cookiePreview = cookieVal.Length > 50 ? cookieVal.Substring(0, 50) + "..." : cookieVal;
					Console.WriteLine($"[HttpClientUtils] | [HttpClient] | 🍪 Cookie preview: {cookiePreview}");
				}
				else
				{
					Console.WriteLine($"[HttpClientUtils] | [HttpClient] | ⚠️  No Cookie provided");
				}
				if (httpMethod == HttpMethod.Post && content != null)
				{
					httpRequestMessage.Content = content;
				}
				// 🔥 关键修复：增强响应处理和日志记录
				HttpResponseMessage response = await client.SendAsync(httpRequestMessage);
				var responseBody = await response.Content.ReadAsStringAsync();
				
				// 详细的响应日志，便于与Python版本对比
				Console.WriteLine($"[HttpClientUtils] | [HttpClient] | 📋 Response Status: {response.StatusCode} ({(int)response.StatusCode})");
				Console.WriteLine($"[HttpClientUtils] | [HttpClient] | 📋 Response Length: {responseBody.Length} chars");
				
				// 检查是否是352错误（关键诊断点）
				if (responseBody.Contains("-352") || responseBody.Contains("\"code\":-352"))
				{
					Console.WriteLine($"[HttpClientUtils] | [HttpClient] | ❌ DETECTED 352 ERROR in response!");
					var responsePreview = responseBody.Length > 200 ? responseBody.Substring(0, 200) + "..." : responseBody;
					Console.WriteLine($"[HttpClientUtils] | [HttpClient] | ❌ Response preview: {responsePreview}");
				}
				
				// 🔥 调试：打印-101错误的完整响应
				if (responseBody.Contains("-101") || responseBody.Contains("\"code\":-101"))
				{
					Console.WriteLine($"[HttpClientUtils] | [HttpClient] | ❌ DETECTED -101 ERROR in response!");
					Console.WriteLine($"[HttpClientUtils] | [HttpClient] | ❌ Full response: {responseBody}");
				}
				
				IEnumerable<string> cookies = response.Headers.SingleOrDefault(header => header.Key == "Set-Cookie").Value;
				switch (response.StatusCode)
				{
					case HttpStatusCode.OK:
						result.Add("OK");
						result.Add(responseBody);
						break;
					default:
						Console.WriteLine($"[HttpClientUtils] | [HttpClient] | ❌ HTTP Error {response.StatusCode}: {responseBody.Substring(0, Math.Min(200, responseBody.Length))}...");
						result.Add("error");
						result.Add($"{response.StatusCode}");
						break;
				}
				result.Add(cookies == null ? string.Empty : new HttpClientUtils().RemoveExpiredTimeAndPath(string.Join("; ", cookies.ToList())));
			}
			catch (Exception e)
			{
				Console.WriteLine($"[HttpClientUtils] | [HttpClient] | 💥 Exception: {e.GetType().Name} - {e.Message}");
				Console.WriteLine($"[HttpClientUtils] | [HttpClient] | 💥 Stack trace: {e.StackTrace}");
				
				// 特别检查网络相关异常
				if (e is HttpRequestException || e is TaskCanceledException)
				{
					Console.WriteLine($"[HttpClientUtils] | [HttpClient] | 💥 Network-related exception detected");
				}
				
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
			try
			{
				if (string.IsNullOrEmpty(cookies))
				{
					Console.WriteLine($"[HttpClientUtils] | [RemoveExpiredTimeAndPath] | Input cookies is null or empty");
					return "";
				}
				
				Console.WriteLine($"[HttpClientUtils] | [RemoveExpiredTimeAndPath] | Processing cookies (length: {cookies.Length})");
				
				var cookies_arr = cookies.Split(';');
				Console.WriteLine($"[HttpClientUtils] | [RemoveExpiredTimeAndPath] | Found {cookies_arr.Length} cookie segments");
				
				var new_cookies_list = new List<string>();
				foreach (var cookie_item in cookies_arr)
				{
					var value = cookie_item.Trim();
					Console.WriteLine($"[HttpClientUtils] | [RemoveExpiredTimeAndPath] | Processing cookie segment: '{(value.Length > 50 ? value.Substring(0, 50) + "..." : value)}'");
					
					if (value.StartsWith("Path=") || value.StartsWith("Domain=") || value.StartsWith("Expires=") || value.IndexOf("=") == -1)
					{
						Console.WriteLine($"[HttpClientUtils] | [RemoveExpiredTimeAndPath] | Skipping non-value segment: '{value.Split('=')[0]}'");
						continue;
					}
					new_cookies_list.Add(value);
				}

				var result = string.Join("; ", new_cookies_list);
				Console.WriteLine($"[HttpClientUtils] | [RemoveExpiredTimeAndPath] | Returning {new_cookies_list.Count} cookie values");
				return result;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[HttpClientUtils] | [RemoveExpiredTimeAndPath] | Exception: {ex.GetType().Name}: {ex.Message}");
				Console.WriteLine($"[HttpClientUtils] | [RemoveExpiredTimeAndPath] | Stack trace: {ex.StackTrace}");
				return "";
			}
		}
    }
}
