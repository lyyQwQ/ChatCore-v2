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
	
		// 必须的Cookie字段
		private readonly string[] RequiredCookieFields = {
			"SESSDATA",
			"bili_jct"
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
	}
	}
