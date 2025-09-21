using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;
using ChatCore.Interfaces;
using ChatCore.Models;
using ChatCore.Services.Bilibili;
using ChatCore.Utilities;
using Microsoft.Extensions.Logging;

namespace ChatCore.Services
{
	public class WebLoginProvider : IWebLoginProvider
	{
		private readonly ILogger _logger;
		private readonly IUserAuthProvider _authManager;
		private readonly MainSettingsProvider _settings;
		private readonly IPathProvider _pathProvider;
		private readonly BilibiliLoginProvider _bilibiliLoginProvider;

		private HttpListener? _listener;
		private CancellationTokenSource? _cancellationToken;
		private static string? _pageData, _overlayPageData;
		private static bool _bilibiliOnly = false;

		private readonly SemaphoreSlim _requestLock = new SemaphoreSlim(1, 1);

		public WebLoginProvider(ILogger<WebLoginProvider> logger, IUserAuthProvider authManager, MainSettingsProvider settings, IPathProvider pathProvider, BilibiliLoginProvider bilibiliLoginProvider)
		{
			_logger = logger;
			_authManager = authManager;
			_settings = settings;
			_pathProvider = pathProvider;
			_bilibiliLoginProvider	= bilibiliLoginProvider;
		}

		public void Start(bool bilibiliOnly = false)
		{
			_bilibiliOnly = bilibiliOnly;
			if (_pageData == null)
			{
				using var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("ChatCore.Resources.Web.index.html")!);
				_pageData = reader.ReadToEnd();
			}

			if (_overlayPageData == null)
			{
				using var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("ChatCore.Resources.Web.overlay.html")!);
				_overlayPageData = reader.ReadToEnd();
			}

			if (_listener != null)
			{
				return;
			}

			_cancellationToken = new CancellationTokenSource();
			_listener = new HttpListener { Prefixes = { $"http://localhost:{MainSettingsProvider.WEB_APP_PORT}/" } };
			_listener.Start();

			_logger.Log(LogLevel.Information, $"[WebLoginProvider] | [Start] | Listening on {string.Join(", ", _listener.Prefixes)}");

			Task.Run(async () =>
			{
				await Task.Delay(1000);
				while (true)
				{
					try
					{
						//_logger.LogInformation("[WebLoginProvider] | [Start] | Waiting for incoming request...");
						var httpListenerContext = await _listener.GetContextAsync().ConfigureAwait(false);
						//_logger.LogWarning("[WebLoginProvider] | [Start] | Request received");
						await OnContext(httpListenerContext).ConfigureAwait(false);
					}
					catch (Exception e)
					{
						_logger.LogError(e, "[WebLoginProvider] | [Start] | WebLoginProvider errored.");
					}
				}

				// ReSharper disable once FunctionNeverReturns
			}).ConfigureAwait(false);
		}

		private async Task OnContext(HttpListenerContext ctx)
		{
			string[] resource_file_list = {
				"/Statics/Css/default.css",
				"/Statics/Css/overlay.css",
				"/Statics/Css/Material+Symbols+Outlined.css",
				"/Statics/Css/materialize.min.css",
				"/Statics/Fonts/kJF1BvYX7BgnkSrUwT8OhrdQw4oELdPIeeII9v6oDMzByHX9rA6RzazHD_dY43zj-jCxv3fzvRNU22ZXGJpEpjC_1v-p_4MrImHCIJIZrDCvHOej.woff2",

				"/Statics/Js/anime.min.js",
				"/Statics/Js/default.js",
				"/Statics/Js/overlay.js",
				"/Statics/Js/tts.js",
				"/Statics/Js/materialize.min.js",
				"/Statics/Js/jquery-3.7.1.min.js",
				"/Statics/Js/qrcode.min.js",

				"/Statics/Lang/en.json",
				"/Statics/Lang/zh.json",
				"/Statics/Lang/ja.json",

				"/Statics/Images/BilibiliDefaultAvatar.jpg",
				"/Statics/Images/BilibiliLiveBroadcaster.png",
				"/Statics/Images/BilibiliLiveModerator.png",
				"/Statics/Images/BilibiliLiveGuard1.png",
				"/Statics/Images/BilibiliLiveGuard2.png",
				"/Statics/Images/BilibiliLiveGuard3.png",
				"/Statics/Images/BilibiliLiveGuard1_full.png",
				"/Statics/Images/BilibiliLiveGuard2_full.png",
				"/Statics/Images/BilibiliLiveGuard3_full.png",
				"/Statics/Images/Blive/Close.png",
				"/Statics/Images/Blive/Close01.png",
				"/Statics/Images/Blive/Ellipse.png",
				"/Statics/Images/Blive/question_mark.png",
				"/Statics/Images/Blive/round.png",
				"/Statics/Images/Blive/tv.png",
				"/Statics/Images/Blive/Vector.png",
				"/Statics/Images/TwitchDefaultAvatar.png",
			};
			await _requestLock.WaitAsync();
			try
			{
				var request = ctx.Request;
				var response = ctx.Response;

				if (request.HttpMethod == "POST")
				{
					if (request.Url.AbsolutePath == "/submit")
					{
						await Submit(request, response).ConfigureAwait(false);
					}
					else
					{
						response.StatusCode = 404;
					}
				} else if (request.HttpMethod == "GET")
				{
					if (Array.IndexOf(resource_file_list, request.Url.AbsolutePath) > -1) // Get Resouces Files
					{
						// Load resources
						response.StatusCode = 200;
						var Ext = Path.GetExtension(request.Url.AbsolutePath);
						if (Ext == ".html")
						{
							response.ContentType = "text/html; charset=utf-8";
						}
						else if (Ext == ".css")
						{
							response.ContentType = "text/css; charset=utf-8";
						}
						else if (Ext == ".js")
						{
							response.ContentType = "application/javascript; charset=utf-8";
						}
						else if (Ext == ".json")
						{
							response.ContentType = "application/json; charset=utf-8";
						}
						else if (Ext == ".woff2")
						{
							response.ContentType = "font/woff2; charset=utf-8";
						}
						else if (Ext == ".png")
						{
							response.ContentType = "image/png";
						}
						else if (Ext == ".jpg")
						{
							response.ContentType = "image/jpeg";
						}

						// _logger.Log(LogLevel.Information, "Trying to get resource: " + "ChatCore.Resources.Web" + request.Url.AbsolutePath.Replace("/", "."));
						var buffer = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("ChatCore.Resources.Web" + request.Url.AbsolutePath.Replace("/", "."))!);
						buffer.BaseStream.CopyTo(response.OutputStream);
					}
					else if (request.Url.AbsolutePath == "/clean/cache/images") // Clean Image Cache
					{
						var targetDirectories = new List<string>() {
						_pathProvider.GetAvatarImagePath(),
						_pathProvider.GetBadgesImagePath()
					};

						foreach (var dir in targetDirectories)
						{
							if (Directory.Exists(dir))
							{
								Directory.Delete(dir, true);
							}
						}
						response.StatusCode = 200;
					}
					else if ((request.Url.AbsolutePath.StartsWith("/Badges/") || request.Url.AbsolutePath.StartsWith("/Avatars/")) && !request.Url.AbsolutePath.Contains("../")) // Get Image cache
					{
						var buffer = new StreamReader(HttpUtility.UrlDecode(Path.Combine(_pathProvider.GetImagePath(), request.Url.AbsolutePath.Replace("/", "\\"))));
						buffer.BaseStream.CopyTo(response.OutputStream);
						var Ext = Path.GetExtension(request.Url.AbsolutePath);
						if (Ext == ".png")
						{
							response.ContentType = "image/png";
						}
						else if (Ext == ".jpg")
						{
							response.ContentType = "image/jpeg";
						}
					}
					else if (request.Url.AbsolutePath == "/" && request.Url.Query.StartsWith("?url="))  // Image Proxier
					{
						var path = request.Url.Query.Substring("?url=".Length, request.Url.Query.Length - "?url=".Length);
						var Ext = Path.GetExtension(path);
						if (Ext == ".png")
						{
							response.ContentType = "image/png";
						}
						else if (Ext == ".jpg")
						{
							response.ContentType = "image/jpeg";
						}
						else if (Ext == ".bmp")
						{
							response.ContentType = "image/bmp";
						}
						else if (Ext == ".gif")
						{
							response.ContentType = "image/gif";
						}
						else if (Ext == ".webp")
						{
							response.ContentType = "image/webp";
						}
						else if (Ext == ".svg")
						{
							response.ContentType = "image/svg+xml";
						}

						if (path.StartsWith("http://") || path.StartsWith("https://"))
						{
							var buffer = new StreamReader(new WebClient().OpenRead(path));
							buffer.BaseStream.CopyTo(response.OutputStream);
						}
						else if (!request.Url.Query.Contains("../") && (path.Replace("/", "\\").StartsWith("file:\\\\\\" + _pathProvider.GetBadgesImagePath().Replace("/", "\\")) || path.Replace("/", "\\").StartsWith("file:\\\\\\" + _pathProvider.GetAvatarImagePath().Replace("/", "\\"))))
						{
							path = HttpUtility.UrlDecode(path.Substring("file:\\\\\\".Length, path.Length - "file:\\\\\\".Length).Replace("/", "\\"));
							var buffer = new StreamReader(path);
							buffer.BaseStream.CopyTo(response.OutputStream);
						}
					}
					else if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/" && request.Url.Query == "") // Get Config Data
					{
						_settings.Reload();
						_authManager.Reload();
						var settingsJson = _settings.GetSettingsAsJson();
						settingsJson["twitch_oauth_token"] = new JSONString(_authManager.Credentials.Twitch_OAuthToken);
						settingsJson["twitch_channels"] = new JSONArray(_authManager.Credentials.Twitch_Channels);
						settingsJson["bilibili_room_id"] = new JSONNumber(_authManager.Credentials.Bilibili_room_id);
						settingsJson["bilibili_identity_code"] = new JSONString(_authManager.Credentials.Bilibili_identity_code);
						// TODO: update identity code from blive sdk
						settingsJson["bilibili_identity_code_save"] = new JSONBool(_authManager.Credentials.Bilibili_identity_code_save);
						settingsJson["bilibili_cookies"] = new JSONString(_authManager.Credentials.Bilibili_cookies);

						var pageBuilder = new StringBuilder(_pageData);
						pageBuilder.Replace("{libVersion}", FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion);
						//pageBuilder.Replace("{libVersion}", ChatCoreInstance.Version.ToString(3));
#if OPENBLIVE
					// pageBuilder.Replace("var data = {};", $"var data = {settingsJson}; var bilibili_version = true;");
#else
						pageBuilder.Replace("var data = {};", $"var data = {settingsJson}; var bilibili_version = false;");
#endif

						if (_bilibiliOnly)
						{
							pageBuilder.Replace("var bilibili_only = false;", $"var bilibili_only = true;");
						}

						var data = Encoding.UTF8.GetBytes(pageBuilder.ToString());
						response.ContentType = "text/html";
						response.ContentEncoding = Encoding.UTF8;
						response.ContentLength64 = data.LongLength;
						await response.OutputStream.WriteAsync(data, 0, data.Length);
					}
					else if (request.Url.AbsolutePath == "/overlay" || request.Url.AbsolutePath == "/overlay/") // Get Overlay
					{
						_settings.Reload();
						_authManager.Reload();
						var settingsJson = _settings.GetSettingsAsJson();
						settingsJson["bilibili_room_id"] = new JSONNumber(_authManager.Credentials.Bilibili_room_id);
						var pageBuilder = new StringBuilder(_overlayPageData);
						pageBuilder.Replace("var config_data = {};", $"var config_data = {settingsJson};");

						var data = Encoding.UTF8.GetBytes(pageBuilder.ToString());
						response.ContentType = "text/html; charset=utf-8";
						response.ContentEncoding = Encoding.UTF8;
						response.ContentLength64 = data.LongLength;
						await response.OutputStream.WriteAsync(data, 0, data.Length);
					} else if (request.Url.AbsolutePath == "/config" || request.Url.AbsolutePath == "/config/") // Get config
					{
						_settings.Reload();
						_authManager.Reload();
						var settingsJson = _settings.GetSettingsAsJson();
						settingsJson["bilibili_room_id"] = new JSONNumber(_authManager.Credentials.Bilibili_room_id);
						var data = Encoding.UTF8.GetBytes(settingsJson.ToString());
						await response.OutputStream.WriteAsync(data, 0, data.Length);
					}
					else if (request.Url.AbsolutePath == "/overlay/custom.js" || request.Url.AbsolutePath == "/overlay/custom.css") // Get Overlay Custom js/css
					{
						var path = Path.Combine(_pathProvider.GetDataPath(), request.Url.AbsolutePath.Substring("/overlay/".Length, request.Url.AbsolutePath.Length - "/overlay/".Length));
						try
						{
							var Ext = Path.GetExtension(path);
							if (Ext == ".css")
							{
								response.ContentType = "text/css; charset=utf-8";
							}
							else if (Ext == ".js")
							{
								response.ContentType = "application/javascript; charset=utf-8";
							}

							if (!File.Exists(path))
							{
								var f = File.Create(path);
								f.Close();
							}

							Console.WriteLine("Load " + request.Url.AbsolutePath);
							if (new FileInfo(path).Length != 0)
							{
								var buffer = new StreamReader(path);
								if (buffer.BaseStream.Length != 0)
								{
									Console.WriteLine("Read " + request.Url.AbsolutePath);
									buffer.BaseStream.CopyTo(response.OutputStream);
								}
								else
								{
									Console.WriteLine("Empty " + request.Url.AbsolutePath);
								}
								buffer.BaseStream.Close();
								Console.WriteLine("Close " + request.Url.AbsolutePath);
							}
							else
							{
								Console.WriteLine("Skip " + request.Url.AbsolutePath);
							}
						}
						catch (Exception ex)
						{
							Console.WriteLine(path);
							Console.WriteLine(ex.ToString());
							response.StatusCode = 404;
						}
					}
					else if (request.Url.AbsolutePath == "/bilibili_qr_request")
					{
						await _bilibiliLoginProvider.Login();
						var resultJson = new JSONObject();
						resultJson["url"] = new JSONString(_bilibiliLoginProvider.qr_url);
						resultJson["status"] = new JSONString(_bilibiliLoginProvider.status);
						var data = Encoding.UTF8.GetBytes(resultJson.ToString());
						await response.OutputStream.WriteAsync(data, 0, data.Length);
					}
					else if (request.Url.AbsolutePath == "/bilibili_qr_status")
					{
						var resultJson = new JSONObject();
						resultJson["status"] = new JSONString(_bilibiliLoginProvider.status);
						resultJson["cookies"] = new JSONString(_bilibiliLoginProvider.cookie);
						var data = Encoding.UTF8.GetBytes(resultJson.ToString());
						await response.OutputStream.WriteAsync(data, 0, data.Length);
					}
					else if (request.Url.AbsolutePath == "/favicon.ico")
					{
						response.StatusCode = 404;
					}
					else // return 403
					{
						response.StatusCode = 403;
					}
				} else
				{
					// NOT SUPPORT
					response.StatusCode = 501;
				}

				response.Close();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "[WebLoginProvider] | [OnContext] | Exception occurred during webapp request.");
			}
			finally
			{
				_requestLock.Release();
			}
		}

		private async Task Submit(HttpListenerRequest request, HttpListenerResponse response)
		{
			try
			{
				using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
				var postStr = await reader.ReadToEndAsync().ConfigureAwait(false);

				var responseJson = JSON.Parse(postStr) as JSONObject;
				if (responseJson == null)
				{
					return;
				}

				var authChanged = false;
				if (responseJson.HasKey("twitch_oauth_token"))
				{
					var token = responseJson["twitch_oauth_token"].Value;
					if (!token.StartsWith("oauth:"))
					{
						token = !string.IsNullOrWhiteSpace(token) ? $"oauth:{token}" : string.Empty;
					}

					if (_authManager.Credentials.Twitch_OAuthToken != token)
					{
						_authManager.Credentials.Twitch_OAuthToken = token;
						authChanged = true;
					}

					responseJson.Remove("twitch_oauth_token");
				}

				if (responseJson.HasKey("twitch_channels"))
				{
					var channelsFromResponse = responseJson["twitch_channels"].AsArray?.Children.Select(channelName => channelName.Value).ToList();
					if (channelsFromResponse != null &&
						(channelsFromResponse.Count != _authManager.Credentials.Twitch_Channels.Count ||
						channelsFromResponse.Any(name => !_authManager.Credentials.Twitch_Channels.Contains(name))))
					{
						_authManager.Credentials.Twitch_Channels.Clear();
						_authManager.Credentials.Twitch_Channels.AddRange(channelsFromResponse);

						authChanged = true;
					}

					responseJson.Remove("twitch_channels");
				}

				if (authChanged)
				{
					_authManager.SaveTwitch();
				}

				authChanged = false;
				// Console.WriteLine(postStr);

				if (responseJson.HasKey("bilibili_room_id") && (_authManager.Credentials.Bilibili_room_id != responseJson["bilibili_room_id"].AsInt))
				{
					_authManager.Credentials.Bilibili_room_id = responseJson["bilibili_room_id"].AsInt;
					authChanged = true;
					responseJson.Remove("bilibili_room_id");
				}

				if (responseJson.HasKey("bilibili_identity_code_save") && (_authManager.Credentials.Bilibili_identity_code_save != responseJson["bilibili_identity_code_save"].AsBool))
				{
					_authManager.Credentials.Bilibili_identity_code_save = responseJson["bilibili_identity_code_save"].AsBool;
					authChanged = true;
					responseJson.Remove("bilibili_identity_code_save");
				}

				if (responseJson.HasKey("bilibili_identity_code") && (_authManager.Credentials.Bilibili_identity_code != responseJson["bilibili_identity_code"]))
				{
					_authManager.Credentials.Bilibili_identity_code = (!string.IsNullOrWhiteSpace(responseJson["bilibili_identity_code"].Value) && _authManager.Credentials.Bilibili_identity_code_save) ? responseJson["bilibili_identity_code"].Value : string.Empty;
					authChanged = true;
					responseJson.Remove("bilibili_identity_code");
				}

				if (responseJson.HasKey("bilibili_cookies") && (_authManager.Credentials.Bilibili_cookies != responseJson["bilibili_cookies"]))
				{
					// 🔥 标准化Cookie顺序（与Python版本保持一致）
					var rawCookies = responseJson["bilibili_cookies"].Value;
					var standardizedCookies = ChatCore.Utilities.CookieOrderHelper.StandardizeCookieOrder(rawCookies, null);
					
					_authManager.Credentials.Bilibili_cookies = standardizedCookies;
					authChanged = true;
					responseJson.Remove("bilibili_cookies");
					if (_authManager.Credentials.Bilibili_cookies == _bilibiliLoginProvider.cookie)
					{
						_bilibiliLoginProvider.cookie = string.Empty;
					}
				}

				if (!_authManager.Credentials.Bilibili_identity_code_save && !string.IsNullOrWhiteSpace(_authManager.Credentials.Bilibili_identity_code) )
				{
					_authManager.Credentials.Bilibili_identity_code = string.Empty;
					authChanged = true;
				}

				if (responseJson.HasKey("danmuku_service_method") && _settings.danmuku_service_method != responseJson["danmuku_service_method"] && (_settings.danmuku_service_method == "OpenBLive" || responseJson["danmuku_service_method"] == "OpenBLive")){
					authChanged = true;
				}

				if (authChanged)
				{
					_authManager.SaveBilibili();
				}

				var TwitchRequireUpdate = responseJson.HasKey("EnableTwitch") && (_settings.EnableTwitch != responseJson["EnableTwitch"].AsBool);
				var BilibiliRequireUpdate = (responseJson.HasKey("EnableBilibili") && (_settings.EnableBilibili != responseJson["EnableBilibili"].AsBool)) || (responseJson.HasKey("danmuku_service_method") && _settings.danmuku_service_method != responseJson["danmuku_service_method"]);

				_settings.SetFromDictionary(responseJson);
				_settings.Save();

				if (TwitchRequireUpdate)
				{
					_settings.updateTwitch(responseJson["EnableTwitch"].AsBool);
				}

				if (BilibiliRequireUpdate)
				{
					_settings.updateBilibili(responseJson["EnableBilibili"].AsBool);
				}

				response.StatusCode = 204;
			}
			catch
			{
				response.StatusCode = 500;
			}
		}

		public void Stop()
		{
			if (_cancellationToken is null)
			{
				return;
			}

			_listener?.Stop();
			_cancellationToken.Cancel();
			_logger.LogInformation("[WebLoginProvider] | [Stop] | Stopped");
		}
	}
}