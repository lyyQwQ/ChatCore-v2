using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ChatCore.Services.Twitch;
using ChatCore.Utilities;
using Microsoft.Extensions.Logging;
using System.Timers;
using System.Linq;
using System.Text.RegularExpressions;

namespace ChatCore.Services.Bilibili
{
	public class BilibiliLoginProvider
	{
		private readonly ILogger _logger;
		private readonly MainSettingsProvider _mainSettingsProvider;
		private readonly SemaphoreSlim _loginLock;

		private readonly string BilibiliRequestQRCodeApi = @"https://passport.bilibili.com/x/passport-login/web/qrcode/generate";
		private readonly string BilibiliQRCodeStatusApi = @"https://passport.bilibili.com/x/passport-login/web/qrcode/poll?qrcode_key=";

		private string qr_secret = string.Empty;
		public string qr_url = string.Empty;
		public string status = "ready";
		public string cookie = string.Empty;
		private Dictionary<string, string> parsedCookies = new Dictionary<string, string>();
		private int retry = 36;
	
		// 重要的Cookie字段（基于Python版本分析）
		private readonly string[] ImportantCookieFields = {
			"SESSDATA",         // 会话数据，最重要
			"bili_jct",         // CSRF Token
			"DedeUserID",       // 用户ID
			"DedeUserID__ckMd5", // 用户ID的MD5
			"buvid3",           // 设备指纹3
			"buvid4",           // 设备指纹4
			"sid",              // 会话ID
			"fingerprint",      // 浏览器指纹
			"_uuid",            // UUID
			"CURRENT_BLACKGAP", // 当前黑名单间隔
			"CURRENT_FNVAL"     // 当前功能值
		};
	
		// 必须的Cookie字段 - 基于Python版本成功案例
		private readonly string[] RequiredCookieFields = {
			"SESSDATA",         // 会话数据，最重要
			"bili_jct",         // CSRF Token
			"DedeUserID",       // 用户ID
			"buvid3"            // 设备指纹，风控检测关键字段
		};

		private System.Timers.Timer heatBeatTimer = new System.Timers.Timer();

		public BilibiliLoginProvider(ILogger<TwitchDataProvider> logger, MainSettingsProvider mainSettingsProvider)
		{
			_logger = logger;
			_mainSettingsProvider = mainSettingsProvider;
			_loginLock = new SemaphoreSlim(1, 1);
		}

		public async Task Login()
		{
			await _loginLock.WaitAsync();
			try
			{
				await GetQRCodeAsync();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $"[BilibiliLoginProvider] | [Login] | An exception occurred while trying to login Bilibili.");
			}
			finally
			{
				_loginLock.Release();
			}
		}

		private async Task<bool> GetQRCodeAsync()
		{
			qr_secret = string.Empty;
			qr_url = string.Empty;
			status = "qr_fetch_busy";
			cookie = string.Empty;
			retry = 36;

			setTimer();
			try
			{
				var apiResult = await (new HttpClientUtils()).HttpClient(BilibiliRequestQRCodeApi, HttpMethod.Get, null, null);
				if (apiResult != null && apiResult[0] == "OK")
				{
					var NewQRInfo = JSONNode.Parse(apiResult[1]);
					if (NewQRInfo["code"].AsInt == 0)
					{
						qr_url = NewQRInfo["data"]["url"].Value;
						qr_secret = NewQRInfo["data"]["qrcode_key"].Value;
						status = "qr_fetch_done";
						setTimer();
						return true;
					}
				}
				else
				{
					status = "qr_fetch_failed_api_error";
					_logger.LogInformation($"[BilibiliLoginProvider] | [GetQRCodeAsync] | Get QR Code Info failed. ({(apiResult == null ? "connection failed" : apiResult[0])})");
				}

			}
			catch
			{
				status = "qr_fetch_failed_http_client_error";
				_logger.LogInformation($"[BilibiliLoginProvider] | [GetQRCodeAsync] | Get QR Code Info failed. (Exception)");
			}
			status = "qr_fetch_failed";
			return false;
		}

		private async void GetQRCodeLoginStatusAsync()
		{
			try
			{
				if (qr_secret == string.Empty)
				{
					return;
				}

				try
				{
					var apiResult = await (new HttpClientUtils()).HttpClient($"{BilibiliQRCodeStatusApi}{qr_secret}", HttpMethod.Get, null, null);
					if (apiResult != null && apiResult[0] == "OK")
					{
						var NewLoginInfo = JSONNode.Parse(apiResult[1]);
						if (NewLoginInfo["code"].AsInt == 0 && NewLoginInfo["data"] != null)
						{
							var bilibiliData = NewLoginInfo["data"];
							var code = bilibiliData["code"].AsInt;

							switch (code)
							{
								case 0:
									qr_secret = string.Empty;
				
									if (apiResult.Count == 3)
									{
										cookie = apiResult[2];
										
										// 🔥 从重定向URL获取额外的Cookie字段（如buvid3、b_nut）
										try
										{
											// 从响应体中提取重定向URL
											var redirectUrl = bilibiliData["url"]?.Value ?? string.Empty;
											if (!string.IsNullOrEmpty(redirectUrl))
											{
												_logger.LogInformation($"[BilibiliLoginProvider] | [GetQRCodeLoginStatusAsync] | Found redirect URL, extracting additional cookies");
												
												// 获取重定向URL的额外cookie
												var additionalCookies = await ExtractCookiesFromUrl(redirectUrl);
												
												if (additionalCookies.Count > 0)
												{
													// 合并原有cookie和额外cookie
													var originalCookieDict = new Dictionary<string, string>();
													var cookiePairs = cookie.Split(';');
													foreach (var pair in cookiePairs)
													{
														var trimmedPair = pair.Trim();
														var equalIndex = trimmedPair.IndexOf('=');
														if (equalIndex > 0 && equalIndex < trimmedPair.Length - 1)
														{
															var name = trimmedPair.Substring(0, equalIndex).Trim();
															var value = trimmedPair.Substring(equalIndex + 1).Trim();
															if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
															{
																originalCookieDict[name] = value;
															}
														}
													}
													
													// 添加额外的cookie（不覆盖已有的）
													foreach (var kvp in additionalCookies)
													{
														if (!originalCookieDict.ContainsKey(kvp.Key))
														{
															originalCookieDict[kvp.Key] = kvp.Value;
															_logger.LogInformation($"[BilibiliLoginProvider] | [GetQRCodeLoginStatusAsync] | Added additional cookie: {kvp.Key}");
														}
													}
													
													// 重新组装cookie字符串
													var mergedCookiePairs = originalCookieDict.Select(kvp => $"{kvp.Key}={kvp.Value}");
													cookie = string.Join("; ", mergedCookiePairs);
													
													_logger.LogInformation($"[BilibiliLoginProvider] | [GetQRCodeLoginStatusAsync] | Merged cookies: total {originalCookieDict.Count} fields");
												}
											}
										}
										catch (Exception ex)
										{
											_logger.LogError(ex, $"[BilibiliLoginProvider] | [GetQRCodeLoginStatusAsync] | Failed to extract additional cookies, continuing with original cookies");
										}
										
										// 🔥 新增：主动获取缺失的关键Cookie字段
										try
										{
											var missingCookies = await GetMissingCriticalCookies();
											if (missingCookies.Count > 0)
											{
												// 解析当前Cookie为字典
												var currentCookieDict = new Dictionary<string, string>();
												var cookiePairs = cookie.Split(';');
												foreach (var pair in cookiePairs)
												{
													var trimmedPair = pair.Trim();
													var equalIndex = trimmedPair.IndexOf('=');
													if (equalIndex > 0 && equalIndex < trimmedPair.Length - 1)
													{
														var name = trimmedPair.Substring(0, equalIndex).Trim();
														var value = trimmedPair.Substring(equalIndex + 1).Trim();
														if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
														{
															currentCookieDict[name] = value;
														}
													}
												}
												
												// 添加缺失的字段（不覆盖已有的）
												foreach (var kvp in missingCookies)
												{
													if (!currentCookieDict.ContainsKey(kvp.Key))
													{
														currentCookieDict[kvp.Key] = kvp.Value;
														_logger.LogInformation($"[BilibiliLoginProvider] | [GetQRCodeLoginStatusAsync] | 补充关键字段: {kvp.Key}={kvp.Value.Substring(0, Math.Min(12, kvp.Value.Length))}...");
													}
												}
												
												// 重新组装完整的Cookie字符串
												var finalCookiePairs = currentCookieDict.Select(kvp => $"{kvp.Key}={kvp.Value}");
												cookie = string.Join("; ", finalCookiePairs);
												
												_logger.LogInformation($"[BilibiliLoginProvider] | [GetQRCodeLoginStatusAsync] | 最终Cookie包含 {currentCookieDict.Count} 个字段");
											}
										}
										catch (Exception ex)
										{
											_logger.LogError(ex, $"[BilibiliLoginProvider] | [GetQRCodeLoginStatusAsync] | 获取缺失Cookie字段失败，继续使用原始Cookie");
										}
										
										// 🔥 标准化Cookie顺序（与Python版本保持一致）
										_logger.LogInformation($"[BilibiliLoginProvider] | [GetQRCodeLoginStatusAsync] | 🍪 Cookie before standardization: {cookie.Length} chars");
										cookie = ChatCore.Utilities.CookieOrderHelper.StandardizeCookieOrder(cookie, _logger);
										_logger.LogInformation($"[BilibiliLoginProvider] | [GetQRCodeLoginStatusAsync] | 🍪 Cookie after standardization: {cookie.Length} chars");
										
										// 🔥 详细记录最终Cookie内容以便调试
										LogDetailedCookieComparison(cookie);
										
										// 解析和验证Cookie
										if (ParseAndValidateCookies(cookie))
										{
											status = "login_done";
											_logger.LogInformation($"[BilibiliLoginProvider] | [GetQRCodeLoginStatusAsync] | Login successful, acquired {parsedCookies.Count} cookies");
											LogImportantCookies();
										}
										else
										{
											status = "login_failed_invalid_cookies";
											_logger.LogWarning($"[BilibiliLoginProvider] | [GetQRCodeLoginStatusAsync] | Login failed: Cookie validation failed");
										}
									}
									else
									{
										status = "login_failed_internal_error";
										_logger.LogError($"[BilibiliLoginProvider] | [GetQRCodeLoginStatusAsync] | Login failed: Invalid API response format");
									}
									disposeTimer();
									break;
								case 86038:
									status = "login_failed_expired";
									break;
								case 86090:
									status = "qr_scan_done";
									break;
								case 86101:
									status = "qr_scan_busy";
									break;
							}
						}
					}
					else
					{
						status = "qr_status_failed_api_error";
						_logger.LogInformation($"[BilibiliLoginProvider] | [GetQRCodeLoginStatusAsync] | Get QR code status failed. ({(apiResult == null ? "connection failed" : apiResult[0])})");
					}

				}
				catch
				{
					status = "qr_status_failed_http_client_error";
					_logger.LogInformation($"[BilibiliLoginProvider] | [GetQRCodeLoginStatusAsync] | Get QR code status failed. (Exception)");
				}
			}
			catch (Exception ex)
			{
				status = "status_failed" + ex.Message;
			}
		}

		private void setTimer()
		{
			disposeTimer();
			heatBeatTimer = new System.Timers.Timer();
			heatBeatTimer.Interval = 5000;
			heatBeatTimer.AutoReset = true;
			heatBeatTimer.Elapsed += (sender, e) =>
			{
				if (retry > 0)
				{
					retry--;
					GetQRCodeLoginStatusAsync();
				}
				else
				{
					heatBeatTimer.AutoReset = false;
					disposeTimer();
				}
			};
			heatBeatTimer.Start();
		}

		private void disposeTimer()
		{
			heatBeatTimer?.Stop();
			heatBeatTimer?.Dispose();
		}
	
		/// <summary>
		/// 解析Cookie字符串为键值对字典
		/// </summary>
		/// <param name="cookieString">原始Cookie字符串</param>
		/// <returns>是否解析成功</returns>
		private bool ParseAndValidateCookies(string cookieString)
		{
			if (string.IsNullOrEmpty(cookieString))
			{
				_logger.LogWarning($"[BilibiliLoginProvider] | [ParseAndValidateCookies] | Cookie string is null or empty");
				return false;
			}
	
			parsedCookies.Clear();
	
			try
			{
				// 解析Cookie字符串
				// Cookie格式：name1=value1; name2=value2; ...
				var cookiePairs = cookieString.Split(';');
				
				foreach (var pair in cookiePairs)
				{
					var trimmedPair = pair.Trim();
					if (string.IsNullOrEmpty(trimmedPair))
						continue;
	
					var equalIndex = trimmedPair.IndexOf('=');
					if (equalIndex > 0 && equalIndex < trimmedPair.Length - 1)
					{
						var name = trimmedPair.Substring(0, equalIndex).Trim();
						var value = trimmedPair.Substring(equalIndex + 1).Trim();
						
						if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
						{
							parsedCookies[name] = value;
						}
					}
				}
	
				_logger.LogInformation($"[BilibiliLoginProvider] | [ParseAndValidateCookies] | Parsed {parsedCookies.Count} cookie pairs");
	
				// 验证必须的Cookie字段
				var missingFields = new List<string>();
				foreach (var requiredField in RequiredCookieFields)
				{
					if (!parsedCookies.ContainsKey(requiredField) || string.IsNullOrEmpty(parsedCookies[requiredField]))
					{
						missingFields.Add(requiredField);
					}
				}
	
				if (missingFields.Count > 0)
				{
					_logger.LogWarning($"[BilibiliLoginProvider] | [ParseAndValidateCookies] | Missing required cookie fields: {string.Join(", ", missingFields)}");
					return false;
				}
	
				return true;
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $"[BilibiliLoginProvider] | [ParseAndValidateCookies] | Exception while parsing cookies");
				return false;
			}
		}
	
		/// <summary>
		/// 记录获取到的重要Cookie字段
		/// </summary>
		private void LogImportantCookies()
		{
			var importantCookies = new List<string>();
			
			foreach (var field in ImportantCookieFields)
			{
				if (parsedCookies.ContainsKey(field))
				{
					// 对于敏感字段，只记录前几位字符
					var value = parsedCookies[field];
					var maskedValue = value.Length > 8 ? value.Substring(0, 8) + "..." : value;
					importantCookies.Add($"{field}={maskedValue}");
				}
			}
	
			if (importantCookies.Count > 0)
			{
				_logger.LogInformation($"[BilibiliLoginProvider] | [LogImportantCookies] | Important cookies acquired: {string.Join(", ", importantCookies)}");
			}
	
			// 检查是否获取到用户ID
			if (parsedCookies.ContainsKey("DedeUserID"))
			{
				_logger.LogInformation($"[BilibiliLoginProvider] | [LogImportantCookies] | User ID acquired: {parsedCookies["DedeUserID"]}");
			}
		}
	
		/// <summary>
		/// 获取解析后的Cookie字典
		/// </summary>
		/// <returns>Cookie字典</returns>
		public Dictionary<string, string> GetParsedCookies()
		{
			return new Dictionary<string, string>(parsedCookies);
		}
	
		/// <summary>
		/// 获取指定Cookie字段的值
		/// </summary>
		/// <param name="fieldName">字段名</param>
		/// <returns>字段值，不存在则返回null</returns>
		public string? GetCookieValue(string fieldName)
		{
			return parsedCookies.ContainsKey(fieldName) ? parsedCookies[fieldName] : null;
		}
	
		/// <summary>
		/// 检查Cookie是否包含所有必须字段
		/// </summary>
		/// <returns>是否有效</returns>
		public bool IsCookieValid()
		{
			foreach (var requiredField in RequiredCookieFields)
			{
				if (!parsedCookies.ContainsKey(requiredField) || string.IsNullOrEmpty(parsedCookies[requiredField]))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// 详细记录Cookie内容以便与Python版本对比调试
		/// </summary>
		/// <param name="cookieString">最终的Cookie字符串</param>
		private void LogDetailedCookieComparison(string cookieString)
		{
			try
			{
				_logger.LogInformation($"[BilibiliLoginProvider] | [LogDetailedCookieComparison] | 🔍 开始Cookie详细分析");
				
				// 解析Cookie
				var cookieDict = new Dictionary<string, string>();
				var cookiePairs = cookieString.Split(';');
				foreach (var pair in cookiePairs)
				{
					var trimmedPair = pair.Trim();
					if (string.IsNullOrEmpty(trimmedPair)) continue;
					
					var equalIndex = trimmedPair.IndexOf('=');
					if (equalIndex > 0 && equalIndex < trimmedPair.Length - 1)
					{
						var name = trimmedPair.Substring(0, equalIndex).Trim();
						var value = trimmedPair.Substring(equalIndex + 1).Trim();
						if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
						{
							cookieDict[name] = value;
						}
					}
				}
				
				// Python版本的期望字段
				var pythonExpectedFields = new[] { "SESSDATA", "buvid3", "b_nut", "bili_jct", "DedeUserID", "DedeUserID__ckMd5", "sid" };
				
				_logger.LogInformation($"[BilibiliLoginProvider] | [LogDetailedCookieComparison] | 📊 ChatCore获取字段总数: {cookieDict.Count}");
				_logger.LogInformation($"[BilibiliLoginProvider] | [LogDetailedCookieComparison] | 📊 Python期望字段总数: {pythonExpectedFields.Length}");
				
				// 记录字段对比
				var presentFields = new List<string>();
				var missingFields = new List<string>();
				
				foreach (var field in pythonExpectedFields)
				{
					if (cookieDict.ContainsKey(field))
					{
						presentFields.Add(field);
						var value = cookieDict[field];
						var preview = value.Length > 12 ? value.Substring(0, 12) + "..." : value;
						_logger.LogInformation($"[BilibiliLoginProvider] | [LogDetailedCookieComparison] | ✅ {field}: {preview}");
					}
					else
					{
						missingFields.Add(field);
						_logger.LogWarning($"[BilibiliLoginProvider] | [LogDetailedCookieComparison] | ❌ 缺失字段: {field}");
					}
				}
				
				// 记录额外字段
				var extraFields = cookieDict.Keys.Where(k => !pythonExpectedFields.Contains(k)).ToList();
				if (extraFields.Count > 0)
				{
					_logger.LogInformation($"[BilibiliLoginProvider] | [LogDetailedCookieComparison] | 🆕 额外字段: {string.Join(", ", extraFields)}");
				}
				
				// 最终Cookie字符串顺序
				var fieldOrder = cookiePairs.Where(p => p.Contains("="))
					.Select(p => p.Trim().Split('=')[0].Trim())
					.Where(name => !string.IsNullOrEmpty(name))
					.ToList();
				_logger.LogInformation($"[BilibiliLoginProvider] | [LogDetailedCookieComparison] | 🔄 实际字段顺序: {string.Join(" → ", fieldOrder)}");
				_logger.LogInformation($"[BilibiliLoginProvider] | [LogDetailedCookieComparison] | 🎯 Python期望顺序: {string.Join(" → ", pythonExpectedFields)}");
				
				// 最终报告
				if (missingFields.Count == 0)
				{
					_logger.LogInformation($"[BilibiliLoginProvider] | [LogDetailedCookieComparison] | 🎉 Cookie完整性检查通过！所有Python期望字段都存在");
				}
				else
				{
					_logger.LogWarning($"[BilibiliLoginProvider] | [LogDetailedCookieComparison] | ⚠️  Cookie完整性检查失败！缺失 {missingFields.Count} 个字段: {string.Join(", ", missingFields)}");
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $"[BilibiliLoginProvider] | [LogDetailedCookieComparison] | Cookie详细分析失败");
			}
		}

		/// <summary>
		/// 主动获取缺失的关键Cookie字段，特别是buvid3和b_nut
		/// </summary>
		/// <returns>补充的Cookie字典</returns>
		private async Task<Dictionary<string, string>> GetMissingCriticalCookies()
		{
			var missingCookies = new Dictionary<string, string>();
			
			try
			{
				_logger.LogInformation($"[BilibiliLoginProvider] | [GetMissingCriticalCookies] | 🔍 开始获取缺失的关键Cookie字段");
				
				// 方法1：使用B站SPI接口获取buvid3和buvid4（Python版本使用的方法）
				try
				{
					var spiUrl = "https://api.bilibili.com/x/frontend/finger/spi";
					var httpUtils = new HttpClientUtils();
					var spiResult = await httpUtils.HttpClient(spiUrl, HttpMethod.Get, null, null);
					
					if (spiResult != null && spiResult[0] == "OK")
					{
						_logger.LogInformation($"[BilibiliLoginProvider] | [GetMissingCriticalCookies] | 📡 SPI接口响应: {spiResult[1].Substring(0, Math.Min(200, spiResult[1].Length))}...");
						
						// 解析SPI接口返回的JSON
						var spiJson = JSONNode.Parse(spiResult[1]);
						if (spiJson != null && spiJson["code"].AsInt == 0 && spiJson["data"] != null)
						{
							var spiData = spiJson["data"];
							
							// 获取buvid3和buvid4
							if (!string.IsNullOrEmpty(spiData["b_3"]?.Value))
							{
								missingCookies["buvid3"] = spiData["b_3"].Value;
								_logger.LogInformation($"[BilibiliLoginProvider] | [GetMissingCriticalCookies] | ✅ 从SPI接口获取到buvid3: {spiData["b_3"].Value.Substring(0, Math.Min(12, spiData["b_3"].Value.Length))}...");
							}
							
							if (!string.IsNullOrEmpty(spiData["b_4"]?.Value))
							{
								missingCookies["buvid4"] = spiData["b_4"].Value;
								_logger.LogInformation($"[BilibiliLoginProvider] | [GetMissingCriticalCookies] | ✅ 从SPI接口获取到buvid4: {spiData["b_4"].Value.Substring(0, Math.Min(12, spiData["b_4"].Value.Length))}...");
							}
						}
						else
						{
							_logger.LogWarning($"[BilibiliLoginProvider] | [GetMissingCriticalCookies] | ⚠️  SPI接口返回错误: code={spiJson?["code"]?.AsInt}, message={spiJson?["message"]?.Value}");
						}
					}
					else
					{
						_logger.LogWarning($"[BilibiliLoginProvider] | [GetMissingCriticalCookies] | ⚠️  SPI接口请求失败: {(spiResult == null ? "null" : spiResult[0])}");
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, $"[BilibiliLoginProvider] | [GetMissingCriticalCookies] | SPI接口调用失败");
				}
				
				// 方法2：如果还没有buvid3，尝试调用用户信息接口
				if (!missingCookies.ContainsKey("buvid3"))
				{
					var navUrl = "https://api.bilibili.com/x/web-interface/nav";
					var httpUtils = new HttpClientUtils();
					var navResult = await httpUtils.HttpClient(navUrl, HttpMethod.Get, null, null);
					
					if (navResult != null && navResult.Count >= 3 && navResult[0] == "OK")
					{
						var navCookies = navResult[2];
						if (!string.IsNullOrEmpty(navCookies))
						{
							_logger.LogInformation($"[BilibiliLoginProvider] | [GetMissingCriticalCookies] | 🔗 Nav接口返回Cookie: {navCookies.Length} chars");
							
							var cookiePairs = navCookies.Split(';');
							foreach (var pair in cookiePairs)
							{
								var trimmedPair = pair.Trim();
								if (string.IsNullOrEmpty(trimmedPair)) continue;
								
								var equalIndex = trimmedPair.IndexOf('=');
								if (equalIndex > 0 && equalIndex < trimmedPair.Length - 1)
								{
									var name = trimmedPair.Substring(0, equalIndex).Trim();
									var value = trimmedPair.Substring(equalIndex + 1).Trim();
									
									if ((name == "buvid3" || name == "b_nut") && !missingCookies.ContainsKey(name))
									{
										missingCookies[name] = value;
										_logger.LogInformation($"[BilibiliLoginProvider] | [GetMissingCriticalCookies] | ✅ 从Nav接口获取到: {name}={value.Substring(0, Math.Min(12, value.Length))}...");
									}
								}
							}
						}
					}
				}
				
				// 方法3：如果还是没有，生成一个b_nut时间戳
				if (!missingCookies.ContainsKey("b_nut"))
				{
					var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
					missingCookies["b_nut"] = currentTime.ToString();
					_logger.LogInformation($"[BilibiliLoginProvider] | [GetMissingCriticalCookies] | 🕒 生成b_nut时间戳: {currentTime}");
				}
				
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $"[BilibiliLoginProvider] | [GetMissingCriticalCookies] | 获取缺失Cookie失败");
			}
			
			_logger.LogInformation($"[BilibiliLoginProvider] | [GetMissingCriticalCookies] | 🎯 成功获取 {missingCookies.Count} 个关键字段");
			return missingCookies;
		}

		/// <summary>
		/// 从重定向URL获取额外的Cookie字段
		/// 模拟Python版本的实现，用于获取buvid3、b_nut等关键字段
		/// </summary>
		/// <param name="redirectUrl">登录成功后的重定向URL</param>
		/// <returns>额外的Cookie字典</returns>
		private async Task<Dictionary<string, string>> ExtractCookiesFromUrl(string redirectUrl)
		{
			var additionalCookies = new Dictionary<string, string>();
			
			if (string.IsNullOrEmpty(redirectUrl))
			{
				_logger.LogWarning($"[BilibiliLoginProvider] | [ExtractCookiesFromUrl] | Redirect URL is null or empty");
				return additionalCookies;
			}

			try
			{
				_logger.LogInformation($"[BilibiliLoginProvider] | [ExtractCookiesFromUrl] | Extracting cookies from redirect URL: {redirectUrl}");
				
				// 使用HttpClientUtils访问重定向URL
				var httpUtils = new HttpClientUtils();
				var result = await httpUtils.HttpClient(redirectUrl, HttpMethod.Get, null, null);
				
				if (result != null && result[0] == "OK" && result.Count >= 3)
				{
					// result[2] 包含从重定向URL获取的cookie
					var redirectCookies = result[2];
					
					if (!string.IsNullOrEmpty(redirectCookies))
					{
						_logger.LogInformation($"[BilibiliLoginProvider] | [ExtractCookiesFromUrl] | Got redirect cookies: {redirectCookies.Length} chars");
						
						// 解析重定向URL返回的cookie
						var cookiePairs = redirectCookies.Split(';');
						foreach (var pair in cookiePairs)
						{
							var trimmedPair = pair.Trim();
							if (string.IsNullOrEmpty(trimmedPair))
								continue;

							var equalIndex = trimmedPair.IndexOf('=');
							if (equalIndex > 0 && equalIndex < trimmedPair.Length - 1)
							{
								var name = trimmedPair.Substring(0, equalIndex).Trim();
								var value = trimmedPair.Substring(equalIndex + 1).Trim();
								
								if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(value))
								{
									additionalCookies[name] = value;
									_logger.LogDebug($"[BilibiliLoginProvider] | [ExtractCookiesFromUrl] | Added cookie: {name}={value.Substring(0, Math.Min(8, value.Length))}...");
								}
							}
						}
					}
				}
				else
				{
					_logger.LogWarning($"[BilibiliLoginProvider] | [ExtractCookiesFromUrl] | Failed to get redirect response: {(result == null ? "null" : result[0])}");
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, $"[BilibiliLoginProvider] | [ExtractCookiesFromUrl] | Exception while extracting cookies from redirect URL");
			}

			_logger.LogInformation($"[BilibiliLoginProvider] | [ExtractCookiesFromUrl] | Extracted {additionalCookies.Count} additional cookies");
			return additionalCookies;
		}
	}
	}
