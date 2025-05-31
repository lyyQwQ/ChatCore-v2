using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;
// using ChatCore.SimpleJSON;
using Microsoft.Extensions.Logging;

namespace ChatCore.Utilities.BLive
{
	/// <summary>
	/// Bilibili WBI 签名工具类
	/// 基于 bilibili-api 项目的实现
	/// </summary>
	public static class WbiUtils
	{
		// 由于是静态类，暂时使用 Console 输出日志

		// OE 数组用于字符重排
		private static readonly int[] OE = {
			46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35, 27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13,
			37, 48, 7, 16, 24, 55, 40, 61, 26, 17, 0, 1, 60, 51, 30, 4, 22, 25, 54, 21, 56, 59, 6, 63, 57, 62, 11, 36, 20, 34, 44, 52
		};

		// 缓存混淆密钥，避免重复请求
		private static string? _cachedMixinKey = null;
		private static DateTime _cacheTime = DateTime.MinValue;
		private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
		
		// 失败标记，避免重复无效的重试
		private static DateTime _lastFailureTime = DateTime.MinValue;
		private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(5); // 失败后5分钟内不再重试

		/// <summary>
		/// 获取 WBI 混淆密钥（带重试机制）
		/// </summary>
		private static async Task<string> GetWbiMixinKeyAsync(string cookies = "")
		{
			// 检查缓存
			if (!string.IsNullOrEmpty(_cachedMixinKey) && DateTime.Now - _cacheTime < CacheDuration)
			{
				return _cachedMixinKey;
			}
			
			// 检查是否在失败冷却期内
			if (DateTime.Now - _lastFailureTime < FailureCooldown)
			{
				Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Still in failure cooldown period, returning empty string immediately");
				return "";
			}

			// 重试机制：最多重试3次
			Exception lastException = null;
			for (int attempt = 0; attempt < 3; attempt++)
			{
				if (attempt > 0)
				{
					var delaySeconds = Math.Pow(2, attempt - 1);
					Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Retry attempt {attempt + 1}/3 after {delaySeconds} seconds");
					await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
				}

				try
				{
				// 调用 nav API 获取 wbi_img 信息
				var apiUrl = "https://api.bilibili.com/x/web-interface/nav";
				Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Requesting nav API to get WBI keys");
				var httpClient = new HttpClientUtils();
				
				// 确保包含 opus-goback=1 cookie
				string cookieWithOpus = cookies ?? "";
				if (!string.IsNullOrEmpty(cookieWithOpus) && !cookieWithOpus.Contains("opus-goback"))
				{
					cookieWithOpus = cookieWithOpus.TrimEnd(';') + "; opus-goback=1";
				}
				else if (string.IsNullOrEmpty(cookieWithOpus))
				{
					cookieWithOpus = "opus-goback=1";
				}
				
				Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Sending request with cookie: {(string.IsNullOrEmpty(cookieWithOpus) ? "<empty>" : "<has_cookie>")}");
				var result = await httpClient.HttpClient(apiUrl, HttpMethod.Get, cookieWithOpus, null);

				if (result != null && result[0] == "OK")
				{
					var navData = JSONNode.Parse(result[1]);
					Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Nav API response code: {navData["code"]?.AsInt}");
					
					if (navData["code"] == 0)
					{
						// 检查是否登录
						var isLogin = navData["data"]["isLogin"]?.AsBool ?? false;
						Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Is logged in: {isLogin}");
						
						var wbiImg = navData["data"]["wbi_img"];
						if (wbiImg != null)
						{
							var imgUrl = wbiImg["img_url"]?.Value ?? "";
							var subUrl = wbiImg["sub_url"]?.Value ?? "";

							Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | img_url: {imgUrl}");
							Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | sub_url: {subUrl}");

							if (!string.IsNullOrEmpty(imgUrl) && !string.IsNullOrEmpty(subUrl))
							{
								// 提取文件名部分
								var imgKey = ExtractFileName(imgUrl);
								var subKey = ExtractFileName(subUrl);

								Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | imgKey: {imgKey}, subKey: {subKey}");

								// 拼接并重排
								var ae = imgKey + subKey;
								var mixinKey = new StringBuilder();

								foreach (var index in OE)
								{
									if (index < ae.Length)
									{
										mixinKey.Append(ae[index]);
									}
								}

								// 取前32位
								_cachedMixinKey = mixinKey.ToString().Substring(0, Math.Min(32, mixinKey.Length));
								_cacheTime = DateTime.Now;

								Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Mixin key retrieved successfully (length: {_cachedMixinKey.Length})");
								return _cachedMixinKey;
							}
							else
							{
								Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | img_url or sub_url is empty");
							}
						}
						else
						{
							Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | wbi_img is null in response");
						}
					}
					else
					{
						Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Nav API returned error code {navData["code"]}: {navData["message"]?.Value}");
						Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Full response: {result[1]}");
					}
				}
				else
				{
					Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Nav API request failed: {(result == null ? "null response" : result[0])}");
					if (result != null && result.Count > 1)
					{
						Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Error details: {result[1]}");
					}
				}
			}
				catch (Exception ex)
				{
					lastException = ex;
					Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Attempt {attempt + 1} failed - {ex.GetType().Name}: {ex.Message}");
					
					// 如果不是最后一次重试，继续下一次尝试
					if (attempt < 2)
					{
						continue;
					}
				}
			}

			// 所有重试都失败了
			Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | All retry attempts failed");
			if (lastException != null)
			{
				Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Last exception: {lastException.Message}");
				Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Stack trace: {lastException.StackTrace}");
			}
			
			// 标记失败时间，避免后续请求重复重试
			_lastFailureTime = DateTime.Now;
			Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Marked failure time, will not retry for {FailureCooldown.TotalMinutes} minutes");
			
			// 返回空字符串作为降级处理
			Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Returning empty string for degraded mode");
			return "";
		}

		/// <summary>
		/// 从 URL 中提取文件名（不含扩展名）
		/// </summary>
		private static string ExtractFileName(string url)
		{
			if (string.IsNullOrEmpty(url)) return "";

			var lastSlash = url.LastIndexOf('/');
			var fileName = lastSlash >= 0 ? url.Substring(lastSlash + 1) : url;
			var lastDot = fileName.LastIndexOf('.');
			return lastDot >= 0 ? fileName.Substring(0, lastDot) : fileName;
		}

		/// <summary>
		/// 对参数进行 WBI 签名
		/// </summary>
		/// <param name="parameters">要签名的参数字典</param>
		/// <param name="cookies">可选的 Cookie 字符串</param>
		/// <returns>签名后的参数字典</returns>
		public static async Task<Dictionary<string, string>> SignParametersAsync(Dictionary<string, string> parameters, string cookies = "")
		{
			var signedParams = new Dictionary<string, string>(parameters);

			// 移除可能存在的旧签名
			signedParams.Remove("w_rid");

			// 添加时间戳
			signedParams["wts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

			// 如果没有 web_location 参数，添加默认值
			if (!signedParams.ContainsKey("web_location"))
			{
				signedParams["web_location"] = "1550101";
			}

			// 获取混淆密钥
			var mixinKey = await GetWbiMixinKeyAsync(cookies);
			if (string.IsNullOrEmpty(mixinKey))
			{
				// 修复：签名失败时抛出异常而非返回未签名参数
				var errorMsg = "[WbiUtils] | [SignParametersAsync] | Failed to obtain WBI mixin key - nav API might be blocked or cookies invalid";
				Console.WriteLine(errorMsg);
				throw new InvalidOperationException(errorMsg);
			}

			// 参数排序并编码
			var sortedParams = signedParams.OrderBy(kv => kv.Key);
			var queryString = string.Join("&", sortedParams.Select(kv => $"{kv.Key}={HttpUtility.UrlEncode(kv.Value)}"));

			Console.WriteLine($"[WbiUtils] | [SignParametersAsync] | Query string for signing: {queryString}");

			// 计算 MD5 签名
			using (var md5 = MD5.Create())
			{
				var input = Encoding.UTF8.GetBytes(queryString + mixinKey);
				var hash = md5.ComputeHash(input);
				var wRid = BitConverter.ToString(hash).Replace("-", "").ToLower();
				signedParams["w_rid"] = wRid;
				
				Console.WriteLine($"[WbiUtils] | [SignParametersAsync] | Generated w_rid: {wRid}");
			}

			return signedParams;
		}

		/// <summary>
		/// 清除缓存的混淆密钥
		/// </summary>
		public static void ClearCache()
		{
			_cachedMixinKey = null;
			_cacheTime = DateTime.MinValue;
			Console.WriteLine("[WbiUtils] | [ClearCache] | Cache cleared");
		}
	}
}