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

		/// <summary>
		/// 获取 WBI 混淆密钥
		/// </summary>
		private static async Task<string> GetWbiMixinKeyAsync(string cookies = "")
		{
			// 检查缓存
			if (!string.IsNullOrEmpty(_cachedMixinKey) && DateTime.Now - _cacheTime < CacheDuration)
			{
				return _cachedMixinKey;
			}

			try
			{
				// 调用 nav API 获取 wbi_img 信息
				var apiUrl = "https://api.bilibili.com/x/web-interface/nav";
				var httpClient = new HttpClientUtils();
				var result = await httpClient.HttpClient(apiUrl, HttpMethod.Get, cookies, null);

				if (result != null && result[0] == "OK")
				{
					var navData = JSONNode.Parse(result[1]);
					if (navData["code"] == 0)
					{
						var wbiImg = navData["data"]["wbi_img"];
						var imgUrl = wbiImg["img_url"]?.Value ?? "";
						var subUrl = wbiImg["sub_url"]?.Value ?? "";

						// 提取文件名部分
						var imgKey = ExtractFileName(imgUrl);
						var subKey = ExtractFileName(subUrl);

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

						// Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Mixin key retrieved successfully");
						return _cachedMixinKey;
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[WbiUtils] | [GetWbiMixinKeyAsync] | Failed to get mixin key: {ex.Message}");
			}

			// 返回空字符串作为降级处理
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
				Console.WriteLine("[WbiUtils] | [SignParametersAsync] | No mixin key available, returning unsigned parameters");
				return signedParams;
			}

			// 参数排序并编码
			var sortedParams = signedParams.OrderBy(kv => kv.Key);
			var queryString = string.Join("&", sortedParams.Select(kv => $"{kv.Key}={HttpUtility.UrlEncode(kv.Value)}"));

			// 计算 MD5 签名
			using (var md5 = MD5.Create())
			{
				var input = Encoding.UTF8.GetBytes(queryString + mixinKey);
				var hash = md5.ComputeHash(input);
				var wRid = BitConverter.ToString(hash).Replace("-", "").ToLower();
				signedParams["w_rid"] = wRid;
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
		}
	}
}