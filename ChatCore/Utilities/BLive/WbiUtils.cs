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
	/// 🔥 完全移植Python成功实现版本
	/// </summary>
	public static class WbiUtils
	{
		// 🔥 Python版本的MIXIN_KEY_ENC_TAB（完全一致）
		private static readonly int[] MIXIN_KEY_ENC_TAB = {
			46, 47, 18, 2, 53, 8, 23, 32, 15, 50,
			10, 31, 58, 3, 45, 35, 27, 43, 5, 49,
			33, 9, 42, 19, 29, 28, 14, 39, 12, 38,
			41, 13, 37, 48, 7, 16, 24, 55, 40, 61,
			26, 17, 0, 1, 60, 51, 30, 4, 22, 25,
			54, 21, 56, 59, 6, 63, 57, 62, 11, 36,
			20, 34, 44, 52
		};

		// 🔥 完全移植Python版本的缓存机制
		private static string? _imgKey = null;
		private static string? _subKey = null;
		private static DateTime _lastUpdateTime = DateTime.MinValue;
		private static readonly TimeSpan UpdateInterval = TimeSpan.FromMinutes(30); // 30分钟更新一次
		
		// 🔥 新的缓存变量（与Python版本对齐）
		private static string? _cachedMixinKey = null;
		private static DateTime _cacheTime = DateTime.MinValue;
		private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
		
		// 🔥 失败重试控制（与Python版本对齐）
		private static DateTime _lastFailureTime = DateTime.MinValue;
		private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(5);
		
		/// <summary>
		/// 🔥 从文件URL中提取文件名（不含扩展名）
		/// 对应Python版本：img_key = img_url.split("/")[-1].split(".")[0]
		/// </summary>
		private static string ExtractFileName(string url)
		{
			if (string.IsNullOrEmpty(url)) return "";
			
			// 分割URL，取最后一部分
			var parts = url.Split('/');
			var fileName = parts[parts.Length - 1];
			
			// 去掉扩展名
			var dotIndex = fileName.LastIndexOf('.');
			if (dotIndex > 0)
			{
				fileName = fileName.Substring(0, dotIndex);
			}
			
			return fileName;
		}
		
		/// <summary>
		/// 🔥 获取用户导航信息（完全复制Python版本）
		/// 对应Python版本的get_user_nav方法
		/// </summary>
		private static async Task<bool> GetUserNavAsync(string cookies = "")
		{
			try
			{
				// 检查缓存是否有效
				if (!string.IsNullOrEmpty(_imgKey) && !string.IsNullOrEmpty(_subKey) &&
					DateTime.Now - _lastUpdateTime < UpdateInterval)
				{
					return true;
				}
				
				var apiUrl = "https://api.bilibili.com/x/web-interface/nav";
				Console.WriteLine($"[WbiUtils] | [GetUserNavAsync] | 🔄 正在获取WBI参数...");
				
				var httpClient = new HttpClientUtils(useWbiUserAgent: true);
				
				// 🔥 关键修复：Python版本的nav接口不添加opus-goback=1
				// 直接使用原始cookies，不做任何修改
				string cookieWithOpus = cookies ?? "";
				
				// 🔥 标准化Cookie顺序（与Python版本保持一致）
				if (!string.IsNullOrEmpty(cookieWithOpus))
				{
					Console.WriteLine($"[WbiUtils] | [GetUserNavAsync] | 🍪 Original cookie length: {cookieWithOpus.Length}");
					cookieWithOpus = CookieOrderHelper.StandardizeCookieOrder(cookieWithOpus, null);
					Console.WriteLine($"[WbiUtils] | [GetUserNavAsync] | 🍪 Standardized cookie length: {cookieWithOpus.Length}");
				}
				
				var result = await httpClient.HttpClient(apiUrl, HttpMethod.Get, cookieWithOpus, null);
				
				if (result != null && result[0] == "OK")
				{
					var navData = JSONNode.Parse(result[1]);
					
					if (navData["code"] == 0)
					{
						var wbiImg = navData["data"]["wbi_img"];
						if (wbiImg != null)
						{
							var imgUrl = wbiImg["img_url"]?.Value ?? "";
							var subUrl = wbiImg["sub_url"]?.Value ?? "";
							
							if (!string.IsNullOrEmpty(imgUrl) && !string.IsNullOrEmpty(subUrl))
							{
								_imgKey = ExtractFileName(imgUrl);
								_subKey = ExtractFileName(subUrl);
								_lastUpdateTime = DateTime.Now;
								
								Console.WriteLine($"[WbiUtils] | [GetUserNavAsync] | ✅ WBI参数获取成功");
								return true;
							}
						}
					}
					else
					{
						Console.WriteLine($"[WbiUtils] | [GetUserNavAsync] | ❌ API返回错误: {navData["code"]} - {navData["message"]?.Value}");
					}
				}
				else
				{
					Console.WriteLine($"[WbiUtils] | [GetUserNavAsync] | ❌ HTTP请求失败");
				}
				
				return false;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[WbiUtils] | [GetUserNavAsync] | ❌ 异常: {ex.Message}");
				return false;
			}
		}
		
		/// <summary>
		/// 🔥 生成混合密钥（完全复制Python版本）
		/// 对应Python版本的get_mixin_key方法
		/// </summary>
		private static string GetMixinKey()
		{
			if (string.IsNullOrEmpty(_imgKey) || string.IsNullOrEmpty(_subKey))
			{
				return "";
			}
			
			// 拼接 img_key 和 sub_key
			var ae = _imgKey + _subKey;
			var mixinKey = new StringBuilder();
			
			// 按照MIXIN_KEY_ENC_TAB的顺序重排字符
			foreach (var index in MIXIN_KEY_ENC_TAB)
			{
				if (index < ae.Length)
				{
					mixinKey.Append(ae[index]);
				}
			}
			
			// 取前32位
			return mixinKey.ToString().Substring(0, Math.Min(32, mixinKey.Length));
		}
		
		/// <summary>
		/// 🔥 生成WBI签名（完全复制Python版本）
		/// 对应Python版本的get_wbi_sign方法
		/// </summary>
		private static string GetWbiSign(Dictionary<string, string> parameters)
		{
			// 获取混合密钥
			var mixinKey = GetMixinKey();
			if (string.IsNullOrEmpty(mixinKey))
			{
				Console.WriteLine($"[WbiUtils] | [GetWbiSign] | ⚠️ 混合密钥为空，无法生成签名");
				return "";
			}
			
			// 排序参数（按键名排序）
			var sortedParams = parameters
				.Where(kv => !string.IsNullOrEmpty(kv.Value) && kv.Key != "w_rid") // 排除空值和w_rid本身
				.OrderBy(kv => kv.Key)
				.ToList();
			
			// 构建查询字符串
			var queryString = string.Join("&", sortedParams.Select(kv =>
				$"{kv.Key}={HttpUtility.UrlEncode(kv.Value)}"));
			
			// 拼接混合密钥
			var signString = queryString + mixinKey;
			
			// 计算MD5
			using (var md5 = MD5.Create())
			{
				var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(signString));
				var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
				
				Console.WriteLine($"[WbiUtils] | [GetWbiSign] | 🔐 WBI签名生成: {hashString.Substring(0, 8)}...");
				return hashString;
			}
		}
		
		/// <summary>
		/// 🔥 确保WBI参数有效（完全复制Python版本）
		/// 对应Python版本的ensure_wbi_params方法
		/// </summary>
		private static async Task<bool> EnsureWbiParamsAsync(string cookies)
		{
			// 检查缓存是否有效
			if (!string.IsNullOrEmpty(_imgKey) && !string.IsNullOrEmpty(_subKey) &&
				DateTime.Now - _lastUpdateTime < UpdateInterval)
			{
				return true;
			}
			
			// 获取新的WBI参数
			return await GetUserNavAsync(cookies);
		}
	
		/// <summary>
		/// 获取 WBI 混淆密钥（带重试机制）- 保持向后兼容
		/// </summary>
		private static async Task<string> GetWbiMixinKeyAsync(string cookies = "")
		{
			// 🔥 使用新的实现方式
			if (await EnsureWbiParamsAsync(cookies))
			{
				return GetMixinKey();
			}
			
			return "";
		}

			/// <summary>
			/// 🔥 创建带WBI签名的参数字典（完全复制Python版本）
			/// 对应Python版本的create_signed_params方法
			/// </summary>
			public static async Task<Dictionary<string, string>> CreateSignedParamsAsync(
				Dictionary<string, string> parameters,
				string cookies,
				bool addWts = true,
				string webLocation = "444.8")
			{
				// 确保WBI参数有效
				if (!await EnsureWbiParamsAsync(cookies))
				{
					throw new InvalidOperationException("无法获取WBI参数");
				}
				
				// 复制原始参数
				var signedParams = new Dictionary<string, string>(parameters);
				
				// 添加时间戳（对应Python版本的wts参数）
				if (addWts)
				{
					signedParams["wts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
				}
				
				// 添加web_location参数
				if (!string.IsNullOrEmpty(webLocation))
				{
					signedParams["web_location"] = webLocation;
				}
				
				// 生成WBI签名
				var wRid = GetWbiSign(signedParams);
				signedParams["w_rid"] = wRid;
				
				Console.WriteLine($"[WbiUtils] | [CreateSignedParamsAsync] | ✅ WBI签名生成成功: w_rid={wRid.Substring(0, Math.Min(8, wRid.Length))}...");
				
				return signedParams;
			}
	
			/// <summary>
			/// 🔥 生成获取弹幕信息API的签名参数（完全复制Python版本）
			/// 对应Python版本的get_danmu_info_params方法
			/// </summary>
			public static async Task<Dictionary<string, string>> GetDanmuInfoParamsAsync(string roomId, string cookies)
			{
				var baseParams = new Dictionary<string, string>
				{
					["id"] = roomId,
					["type"] = "0"
				};
				
				return await CreateSignedParamsAsync(baseParams, cookies);
			}
	
			/// <summary>
			/// 🔥 对参数进行 WBI 签名（保持向后兼容的公共接口）
			/// </summary>
			/// <param name="parameters">要签名的参数字典</param>
			/// <param name="cookies">可选的 Cookie 字符串</param>
			/// <returns>签名后的参数字典</returns>
			public static async Task<Dictionary<string, string>> SignParametersAsync(Dictionary<string, string> parameters, string cookies = "")
			{
				// 🔥 直接调用新的完全移植版本
				return await CreateSignedParamsAsync(parameters, cookies);
			}
	
			/// <summary>
			/// 🔥 测试WBI功能是否正常工作（完全复制Python版本）
			/// 对应Python版本的test_wbi_functionality方法
			/// </summary>
			public static async Task<bool> TestWbiFunctionalityAsync(string cookies)
			{
				try
				{
					Console.WriteLine($"[WbiUtils] | [TestWbiFunctionalityAsync] | 开始测试WBI功能...");
					
					// 1. 测试获取WBI参数
					if (!await GetUserNavAsync(cookies))
					{
						Console.WriteLine($"[WbiUtils] | [TestWbiFunctionalityAsync] | ❌ 获取WBI参数失败");
						return false;
					}
					
					// 2. 测试签名生成
					var testParams = new Dictionary<string, string>
					{
						["id"] = "12345",
						["type"] = "0"
					};
					
					var signedParams = await CreateSignedParamsAsync(testParams, cookies);
					
					if (!signedParams.ContainsKey("w_rid"))
					{
						Console.WriteLine($"[WbiUtils] | [TestWbiFunctionalityAsync] | ❌ WBI签名生成失败");
						return false;
					}
					
					Console.WriteLine($"[WbiUtils] | [TestWbiFunctionalityAsync] | ✅ WBI签名生成成功: w_rid={signedParams["w_rid"].Substring(0, Math.Min(8, signedParams["w_rid"].Length))}...");
					Console.WriteLine($"[WbiUtils] | [TestWbiFunctionalityAsync] | 📋 完整参数: {string.Join(", ", signedParams.Select(kv => $"{kv.Key}={kv.Value}"))}");
					
					return true;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[WbiUtils] | [TestWbiFunctionalityAsync] | ❌ WBI功能测试失败: {ex.Message}");
					return false;
				}
			}
	
			/// <summary>
			/// 🔥 清除缓存的WBI参数（更新方法名以匹配新实现）
			/// </summary>
			public static void ClearCache()
			{
				_imgKey = null;
				_subKey = null;
				_lastUpdateTime = DateTime.MinValue;
				Console.WriteLine("[WbiUtils] | [ClearCache] | WBI参数缓存已清除");
			}
		}
	}