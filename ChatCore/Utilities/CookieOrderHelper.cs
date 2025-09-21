using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace ChatCore.Utilities
{
	/// <summary>
	/// Cookie顺序处理工具类
	/// 确保Cookie顺序与Python版本一致，以避免B站风控检测
	/// </summary>
	public static class CookieOrderHelper
	{
		// Python版本的Cookie顺序（成功的顺序）
		private static readonly string[] PythonCookieOrder = new[]
		{
			"SESSDATA",
			"buvid3",
			"b_nut",
			"bili_jct",
			"DedeUserID",
			"DedeUserID__ckMd5",
			"sid"
		};

		// 必需的Cookie字段
		private static readonly string[] RequiredCookieFields = new[]
		{
			"SESSDATA",
			"bili_jct",
			"DedeUserID",
			"DedeUserID__ckMd5",
			"buvid3",    // 设备指纹，B站风控检测的关键字段
			"b_nut"      // 时间戳相关字段，通过风控检测必需
		};

		/// <summary>
		/// 解析并重新排序Cookie字符串
		/// </summary>
		/// <param name="cookieString">原始Cookie字符串</param>
		/// <param name="logger">日志记录器（可选）</param>
		/// <returns>重新排序后的Cookie字符串</returns>
		public static string StandardizeCookieOrder(string cookieString, ILogger? logger = null)
		{
			if (string.IsNullOrEmpty(cookieString))
			{
				logger?.LogWarning("[CookieOrderHelper] Cookie string is null or empty");
				return cookieString;
			}

			try
			{
				// 解析Cookie字符串为字典
				var cookieDict = ParseCookieString(cookieString);
				logger?.LogInformation($"[CookieOrderHelper] Parsed {cookieDict.Count} cookies from input");

				// 检查必需字段
				var missingFields = RequiredCookieFields.Where(field => !cookieDict.ContainsKey(field)).ToList();
				if (missingFields.Any())
				{
					logger?.LogWarning($"[CookieOrderHelper] Missing required cookie fields: {string.Join(", ", missingFields)}");
				}

				// 按照Python版本的顺序重新组装Cookie
				var orderedCookies = new List<string>();
				
				// 首先添加Python顺序中的Cookie
				foreach (var cookieName in PythonCookieOrder)
				{
					if (cookieDict.TryGetValue(cookieName, out var value))
					{
						orderedCookies.Add($"{cookieName}={value}");
						cookieDict.Remove(cookieName); // 移除已处理的Cookie
					}
				}

				// 然后添加剩余的Cookie（如sec_ck等）
				foreach (var kvp in cookieDict)
				{
					// 跳过一些不需要的字段
					if (kvp.Key.Equals("opus-goback", StringComparison.OrdinalIgnoreCase))
					{
						logger?.LogInformation($"[CookieOrderHelper] Skipping opus-goback cookie");
						continue;
					}
					orderedCookies.Add($"{kvp.Key}={kvp.Value}");
				}

				var result = string.Join("; ", orderedCookies);
				logger?.LogInformation($"[CookieOrderHelper] Reordered cookies: {orderedCookies.Count} fields, length: {result.Length}");
				
				// 记录Cookie顺序（用于调试）
				if (logger != null && logger.IsEnabled(LogLevel.Debug))
				{
					var orderInfo = string.Join(" → ", orderedCookies.Select(c => c.Split('=')[0]));
					logger.LogDebug($"[CookieOrderHelper] Cookie order: {orderInfo}");
				}

				return result;
			}
			catch (Exception ex)
			{
				logger?.LogError(ex, "[CookieOrderHelper] Failed to standardize cookie order");
				return cookieString; // 失败时返回原始字符串
			}
		}

		/// <summary>
		/// 解析Cookie字符串为字典
		/// </summary>
		/// <param name="cookieString">Cookie字符串</param>
		/// <returns>Cookie字典</returns>
		private static Dictionary<string, string> ParseCookieString(string cookieString)
		{
			var cookieDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			
			// 分割Cookie字符串
			var cookiePairs = cookieString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
			
			foreach (var pair in cookiePairs)
			{
				var trimmedPair = pair.Trim();
				var equalIndex = trimmedPair.IndexOf('=');
				
				if (equalIndex > 0 && equalIndex < trimmedPair.Length - 1)
				{
					var name = trimmedPair.Substring(0, equalIndex).Trim();
					var value = trimmedPair.Substring(equalIndex + 1).Trim();
					
					// 跳过Path、Domain、Expires等属性
					if (!name.Equals("Path", StringComparison.OrdinalIgnoreCase) &&
						!name.Equals("Domain", StringComparison.OrdinalIgnoreCase) &&
						!name.Equals("Expires", StringComparison.OrdinalIgnoreCase) &&
						!name.Equals("Max-Age", StringComparison.OrdinalIgnoreCase) &&
						!name.Equals("Secure", StringComparison.OrdinalIgnoreCase) &&
						!name.Equals("HttpOnly", StringComparison.OrdinalIgnoreCase) &&
						!name.Equals("SameSite", StringComparison.OrdinalIgnoreCase))
					{
						cookieDict[name] = value;
					}
				}
			}
			
			return cookieDict;
		}

		/// <summary>
		/// 检查Cookie是否包含所有必需字段
		/// </summary>
		/// <param name="cookieString">Cookie字符串</param>
		/// <returns>是否有效</returns>
		public static bool ValidateCookieFields(string cookieString)
		{
			if (string.IsNullOrEmpty(cookieString))
				return false;
				
			var cookieDict = ParseCookieString(cookieString);
			return RequiredCookieFields.All(field => cookieDict.ContainsKey(field));
		}

		/// <summary>
		/// 从QR码登录响应中提取并排序Cookie
		/// </summary>
		/// <param name="qrLoginCookies">QR码登录返回的Cookie</param>
		/// <param name="logger">日志记录器</param>
		/// <returns>标准化的Cookie字符串</returns>
		public static string ProcessQRLoginCookies(Dictionary<string, string> qrLoginCookies, ILogger? logger = null)
		{
			var cookiePairs = qrLoginCookies.Select(kvp => $"{kvp.Key}={kvp.Value}");
			var rawCookieString = string.Join("; ", cookiePairs);
			return StandardizeCookieOrder(rawCookieString, logger);
		}
	}
}