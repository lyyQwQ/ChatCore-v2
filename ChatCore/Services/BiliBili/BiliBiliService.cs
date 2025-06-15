using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Xml.Linq;
using ChatCore.Interfaces;
using ChatCore.Models;
using ChatCore.Models.Bilibili;
using ChatCore.Models.Twitch;
using ChatCore.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenBLive.Runtime.Data;
using Timer = System.Timers.Timer;

namespace ChatCore.Services.Bilibili
{
	public class BilibiliService : ChatServiceBase, IChatService
	{
		private readonly ConcurrentDictionary<Assembly, Action<IChatService, string>> _rawMessageReceivedCallbacks;
		private readonly ConcurrentDictionary<string, IChatChannel> _channels;
		private static readonly string BilibiliChannelInfoApi = "https://api.live.bilibili.com/xlive/web-room/v1/index/getInfoByRoom?room_id=";
		private static readonly string BilibiliGiftRoomInfoApi = "https://api.live.bilibili.com/xlive/web-room/v1/giftPanel/giftConfig?platform=pc&room_id=";
		private static readonly string BilibiliWealthApi = "https://api.live.bilibili.com/xlive/general-interface/v1/content/get?key=wealth";
		private static readonly string BilibiliChatTokenApi = "https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?type=0&id=";
		private static readonly string BilibiliEmotionsApi = "https://api.live.bilibili.com/xlive/web-ucenter/v2/emoticon/GetEmoticons?platform=pc&room_id=";
		private static readonly string BilibiliFingerprintApi = "https://api.bilibili.com/x/frontend/finger/spi";
        private static readonly string BilibiliValidateLoginInfoApi = "https://api.vc.bilibili.com/link_setting/v1/link_setting/get";

		private readonly ILogger _logger;
		private readonly IWebSocketService _websocketService;
		private readonly IUserAuthProvider _authManager;
		private readonly MainSettingsProvider _settings;
		private readonly IOpenBLiveProvider _openBLiveProvider;
		private readonly IWebSocketServerService _socketServerService;

		private readonly object _messageReceivedLock;
		private readonly object _initLock;
		private readonly object _chatTokenLock;

		private bool _isStarted;
		public bool _enable { get; private set; }

		private int _currentMessageCount;
		private DateTime _lastResetTime = DateTime.UtcNow;
		private readonly ConcurrentQueue<KeyValuePair<Assembly, string>> _textMessageQueue = new ConcurrentQueue<KeyValuePair<Assembly, string>>();

		public BilibiliChatUser? LoggedInUser { get; internal set; }
		private int RoomID => _authManager.Credentials.Bilibili_room_id;
		private string IdentityCode => string.IsNullOrEmpty(_authManager.Credentials.Bilibili_identity_code) ? string.Empty : _authManager.Credentials.Bilibili_identity_code;
		public static int _roomID { get; internal set; } = 0;
		public static long _userID { get; internal set; } = 0;
		private string _wssLink => _openBLiveProvider.getWssLink();
		private string _auth_body => _openBLiveProvider.getAuthBody();

		private long _randomUid = 0;

		private string _chatToken = "";
		private string _buvid3 = "";
		private string _cookies = "";
		private bool _cookie_valid = false;

		private readonly System.Timers.Timer packetTimer;

		// 重连延迟机制相关字段
		private int _reconnectAttempts = 0;
		private DateTime _lastReconnectTime = DateTime.MinValue;
		private readonly object _reconnectLock = new object();

		public ReadOnlyDictionary<string, IChatChannel> Channels { get; }

		public static Dictionary<string, string> bilibiliGiftInfo { get; internal set; } = new Dictionary<string, string>();
		public static Dictionary<string, string> bilibiliGiftCoinType { get; internal set; } = new Dictionary<string, string>();
		public static Dictionary<string, double> bilibiliGiftPrice { get; internal set; } = new Dictionary<string, double>();
		public static Dictionary<string, string> bilibiliGiftName { get; internal set; } = new Dictionary<string, string>();
		public static Dictionary<string, string> bilibiliuserInfo { get; set; } = new Dictionary<string, string>();
		public static Dictionary<string, string> bilibiliWealth { get; set; } = new Dictionary<string, string>();
		public static Dictionary<string, Dictionary<string, string>> bilibiliEmoticons { get; set; } = new Dictionary<string, Dictionary<string, string>>();

		private static Dictionary<string, BilibiliGiftTimer> giftTimerDict = new Dictionary<string, BilibiliGiftTimer>();

		private static readonly string[] danmuku = { "DANMU_MSG", "danmuku", "danmuku_motion", "LIVE_OPEN_PLATFORM_DM" };
		private static readonly string[] sc = { "SUPER_CHAT_MESSAGE", "SUPER_CHAT_MESSAGE_JPN", "super_chat", "super_chat_japanese", "LIVE_OPEN_PLATFORM_SUPER_CHAT", "LIVE_OPEN_PLATFORM_SUPER_CHAT_DEL" };
		private static readonly string[] gift = { "SEND_GIFT", "gift", "LIVE_OPEN_PLATFORM_SEND_GIFT" };
		private static readonly string[] gift_star = { "gift_star" };
		private static readonly string[] gift_combo = { "combo_end", "combo_send" };
		private static readonly string[] welcome = { "welcome" };
		private static readonly string[] share = { "share" };
		private static readonly string[] follow = { "follow" };
		private static readonly string[] special_follow = { "special_follow" };
		private static readonly string[] matual_follow = { "matual_follow" };
		private static readonly string[] welcome_guard = { "welcome_guard" };
		private static readonly string[] effect = { "effect" };
		private static readonly string[] anchor = { "anchor_lot_start", "anchor_lot_checkstatus", "anchor_lot_end", "anchor_lot" };
		private static readonly string[] raffle = { "raffle_start" };
		private static readonly string[] new_guard = { "new_guard" };
		private static readonly string[] new_guard_msg = { "new_guard_msg" };
		private static readonly string[] guard_msg = { "guard_msg" };
		private static readonly string[] guard_lottery_msg = { "guard_lottery_msg" };
		private static readonly string[] blocklist = { "blocklist" };
		private static readonly string[] room_change = { "room_change" };
		private static readonly string[] room_perparing = { "room_perparing" };
		private static readonly string[] room_live = { "room_live" };
		private static readonly string[] like = { "like_info", "LIVE_OPEN_PLATFORM_LIKE" };
		private static readonly string[] global = { "global" };
		private static readonly string[] junk = { "junk", "unkown", "banned" };
		private static readonly string[] room_boardcast = { "common_notice" };
		private static readonly string[] system = { "warning", "cut_off", "plugin_message", "login_in_notice" };
		private static readonly string[] pk = { "pk_pre", "pk_start", "pk_end"};
		private static readonly string[] red_packet = { "red_pocket_start", "red_pocket_new", "red_pocket_result" };

		public static bool typeInList(string msgType, string type)
		{
			string[] target = { };
			switch (type)
			{
				case "danmuku":
					target = danmuku;
					break;
				case "sc":
					target = sc;
					break;
				case "gift":
					target = gift;
					break;
				case "gift_star":
					target = gift_star;
					break;
				case "gift_combo":
					target = gift_combo;
					break;
				case "welcome":
					target = welcome;
					break;
				case "share":
					target = share;
					break;
				case "follow":
					target = follow;
					break;
				case "special_follow":
					target = special_follow;
					break;
				case "matual_follow":
					target = matual_follow;
					break;
				case "welcome_guard":
					target = welcome_guard;
					break;
				case "effect":
					target = effect;
					break;
				case "anchor":
					target = anchor;
					break;
				case "raffle":
					target = raffle;
					break;
				case "new_guard":
					target = new_guard;
					break;
				case "new_guard_msg":
					target = new_guard_msg;
					break;
				case "guard_msg":
					target = guard_msg;
					break;
				case "guard_lottery_msg":
					target = guard_lottery_msg;
					break;
				case "blocklist":
					target = blocklist;
					break;
				case "room_change":
					target = room_change;
					break;
				case "room_perparing":
					target = room_perparing;
					break;
				case "room_live":
					target = room_live;
					break;
				case "like":
					target = like;
					break;
				case "global":
					target = global;
					break;
				case "junk":
					target = junk;
					break;
				case "system":
					target = system;
					break;
				case "pk":
					target = pk;
					break;
				case "red_packet":
					target = red_packet;
					break;
			}

			return target.ToList().Contains(msgType);
		}

		public delegate void GiftTimerHandler(Assembly arg1, BilibiliChatMessage bmessage);
		//public GiftTimerHandler GiftDelegate;

		public string BilibiliLiveAppSecretChecker()
		{
#if (DEBUG)
			var config = new ConfigurationBuilder().AddUserSecrets(Assembly.GetExecutingAssembly(), true).Build();
			return $"bilibili_live_app_id: {config["bilibili_live_app_id"]}\nbilibili_live_access_key_id: {config["bilibili_live_access_key_id"]}\nbilibili_live_access_key_secret: {config["bilibili_live_access_key_secret"]}";
#else
            return $"bilibili_live_app_id: HIDDEN\nbilibili_live_access_key_id: HIDDEN\nbilibili_live_access_key_secret: HIDDEN";
#endif
		}

		public string DisplayName { get; } = "BilibiliLive";

		public event Action<IChatService, string> OnRawMessageReceived
		{
			add => _rawMessageReceivedCallbacks.AddAction(Assembly.GetCallingAssembly(), value);
			remove => _rawMessageReceivedCallbacks.RemoveAction(Assembly.GetCallingAssembly(), value);
		}
		public event Action<string, List<string>> OnBilibiliMessageReceived;

		public BilibiliService(ILogger<BilibiliService> logger, IWebSocketService websocketService, MainSettingsProvider settings, IUserAuthProvider authManager, IOpenBLiveProvider openBLiveProvider, IWebSocketServerService socketServerService, Random rand)
		{
			_logger = logger;
			_settings = settings;
			_websocketService = websocketService;
			_authManager = authManager;
			_openBLiveProvider = openBLiveProvider;
			_socketServerService = socketServerService;

			_rawMessageReceivedCallbacks = new ConcurrentDictionary<Assembly, Action<IChatService, string>>();
			_channels = new ConcurrentDictionary<string, IChatChannel>();
			_messageReceivedLock = new object();
			_initLock = new object();
			_chatTokenLock = new object();
			_roomID = _authManager.Credentials.Bilibili_room_id;
			_cookies = _authManager.Credentials.Bilibili_cookies;
			_logger.LogInformation($"[BilibiliService] Constructor - _roomID set to: {_roomID} from Credentials.Bilibili_room_id: {_authManager.Credentials.Bilibili_room_id}");

			//_randomUid = rand.Next(10000, 1000000);

			Channels = new ReadOnlyDictionary<string, IChatChannel>(_channels);

			_websocketService.OnOpen += _websocketService_OnOpen;
			_websocketService.OnClose += _websocketService_OnClose;
			_websocketService.OnError += _websocketService_OnError;
			_websocketService.OnMessageReceived += _websocketService_OnMessageReceived;
			_websocketService.OnDataRecevied += _websocketService_OnDataRecevied;
			OnBilibiliMessageReceived += _socketServerService.SendMessage;

			_openBLiveProvider.onWssUpdate += onOpenBLiveWssUpdate;
			_authManager.OnBilibiliCredentialsUpdated += _authManager_OnCredentialsUpdated;

			packetTimer = new System.Timers.Timer(1000 * 30);
			packetTimer.Elapsed += PacketTimer_Elapsed;
		}

		private async void _authManager_OnCredentialsUpdated(LoginCredentials credentials)
		{
			if (_isStarted && _enable)
			{
				var reload_flag = false;
				if (_roomID != _authManager.Credentials.Bilibili_room_id)
				{
					_roomID = _authManager.Credentials.Bilibili_room_id;
					GetChannelConfigAsync(_roomID);
					GetChannelGiftRoomInfoAsync(_roomID);
					reload_flag = true;
				}
				if (_cookies != _authManager.Credentials.Bilibili_cookies)
				{
					var _old_cookie_valid = _cookie_valid;
					_cookie_valid = false;
					if (_authManager.Credentials.Bilibili_cookies != "")
					{
						await UpdateCookieStatus();
					}

					if (!_cookie_valid && !_old_cookie_valid)
					{ }
					else
					{
						_chatToken = "";
						_buvid3 = "";
						reload_flag = true;
					}
				}
				// Console.WriteLine($"Connecting to {_roomID}");
				if (reload_flag)
				{
					Start(true);
				}
			}
		}

		public void onOpenBLiveWssUpdate() {
			if (_settings.danmuku_service_method == "OpenBLive")
			{
				reloadWebsocketConnection();
			}
		}

		public void reloadWebsocketConnection() {
			if (_enable)
			{
				lock (_reconnectLock)
				{
					// 实现指数退避延迟
					var timeSinceLastReconnect = DateTime.Now - _lastReconnectTime;
					if (timeSinceLastReconnect < TimeSpan.FromSeconds(5))
					{
						// 计算延迟时间：2^attempts 秒，最多30秒
						var delaySeconds = Math.Min(30, Math.Pow(2, _reconnectAttempts));
						_logger.LogInformation($"[BilibiliService] | [ws_reload] | Delaying reconnection by {delaySeconds} seconds (attempt #{_reconnectAttempts + 1})");

						// 使用 Task.Delay 异步等待
						Task.Delay(TimeSpan.FromSeconds(delaySeconds)).ContinueWith(_ =>
						{
							if (_enable)
							{
								_logger.LogInformation($"[BilibiliService] | [ws_reload] | Connecting using {_settings.danmuku_service_method} after delay");
								Start(true);
							}
						});

						_reconnectAttempts++;
						return;
					}

					// 如果距离上次重连超过5秒，重置重连计数
					if (timeSinceLastReconnect > TimeSpan.FromMinutes(1))
					{
						_reconnectAttempts = 0;
					}

					_lastReconnectTime = DateTime.Now;
					_reconnectAttempts++;
				}

				_logger.LogInformation($"[BilibiliService] | [ws_reload] | Connecting using {_settings.danmuku_service_method}");
				Start(true);
			}
		}

		private void _websocketService_OnDataRecevied(Assembly arg1, byte[] arg2)
		{
			_logger.LogInformation($"[BilibiliService] | [ws_OnDataRecevied] | Received {arg2.Length} bytes");
			
			// 输出原始数据的前16个字节（头部信息）
			if (arg2.Length >= 16)
			{
				var packetLength = DataView.GetInt32(arg2, 0);
				var headerLength = DataView.GetInt16(arg2, 4);
				var version = DataView.GetInt16(arg2, 6);
				var operation = DataView.GetInt32(arg2, 8);
				var sequence = DataView.GetInt32(arg2, 12);
				_logger.LogInformation($"[BilibiliService] | [ws_OnDataRecevied] | Header: PacketLen={packetLength}, HeaderLen={headerLength}, Version={version}, Operation={operation}, Sequence={sequence}");
			}
			
			//var buffer = new byte[arg2.Length];
			// Receive the greeting ack notify, then a HeartBeat timer should be setup.
			var messageCount = 0;
			
			// 直接使用 foreach 遍历，避免 ToList() 导致的栈溢出
			foreach (var message in DanmakuMessage.ParsePackets(arg2))
			{
				messageCount++;
				var json = new JSONObject();
				json["operation_code"] = new JSONNumber((int)message.Operation);
				json["body"] = new JSONString(message.Body);
				
				// 限制Body输出长度，避免日志过长
				var bodyPreview = message.Body.Length > 200 ? message.Body.Substring(0, 200) + "..." : message.Body;
				_logger.LogInformation($"[BilibiliService] | [ws_OnDataRecevied] | Message #{messageCount} - Operation: {message.Operation}, Body: {bodyPreview}");
				// _logger.LogInformation("Data: " + json.ToString());
				if (message.Operation == BilibiliPacket.DanmakuOperation.GreetingAck)
				{
					_logger.LogInformation("[BilibiliService] | [ws_OnDataRecevied] | Bilibili Connected");

					// 连接成功，重置重试计数器
					lock (_reconnectLock)
					{
						_reconnectAttempts = 0;
						_logger.LogInformation("[BilibiliService] | [ws_OnDataRecevied] | Reset reconnect attempts counter");
					}

					if (!_channels.ContainsKey($"{RoomID}"))
					{
						forwardPacket(json, true);
						_channels[$"{RoomID}"] = new BilibiliChatChannel();
						_logger.LogInformation($"[BilibiliService] | [ws_OnDataRecevied] | Added channel {RoomID} to the channel list.");
						JoinRoomCallbacks?.InvokeAll(arg1, this, _channels[$"{RoomID}"], _logger);
					}
					StartHeartBeat();
				} else if (message.Operation == BilibiliPacket.DanmakuOperation.HeartBeatAck)
				{
					//Console.WriteLine($"Popularity: {message.Body}");
					forwardPacket(json, true);
					/*_logger.LogInformation($"Popularity: {message.Body}");*/
				} else if (message.Operation == BilibiliPacket.DanmakuOperation.ChatMessage) {
					//_logger.LogInformation($"Body: {message.Body}");
					try
					{
						forwardPacket(json, true);
						_rawMessageReceivedCallbacks?.InvokeAll(arg1, this, message.Body);
						DanmukuProcessor(arg1, message.Body);
					}
					catch (Exception r)
					{
						_logger.LogError($"[BilibiliService] | [ws_OnDataRecevied] | {r}");
					}
				} else if (message.Operation == BilibiliPacket.DanmakuOperation.StopRoom || message.Operation == BilibiliPacket.DanmakuOperation.StopLiveRoomList) {

				}  else
				{
					forwardPacket(json, true);
					_logger.LogInformation($"[BilibiliService] | [ws_OnDataRecevied] | Unknown Msg(Body: {message.Body})");
				}
			}
			
			// 输出总共处理的消息数量
			_logger.LogInformation($"[BilibiliService] | [ws_OnDataRecevied] | Processed {messageCount} messages in total");
		}

		public void DanmukuProcessor(Assembly arg1, string body)
		{
			// 添加调试日志
			try {
				var bodyJson = JSONNode.Parse(body);
				var cmd = bodyJson["cmd"]?.Value ?? "unknown";
				_logger.LogInformation($"[BilibiliService] | [DanmukuProcessor] | Received cmd: {cmd}");
				
				// 如果是 DANMU_MSG，记录详细信息
				if (cmd == "DANMU_MSG" || cmd.StartsWith("DANMU_MSG"))
				{
					_logger.LogInformation($"[BilibiliService] | [DanmukuProcessor] | DANMU_MSG detected! Full body: {body}");
				}
			} catch (Exception ex) {
				_logger.LogError($"[BilibiliService] | [DanmukuProcessor] | Failed to parse body: {ex.Message}");
			}
			
			var bmessage = new BilibiliChatMessage(body, _settings.danmuku_service_method);
			BanDetection(bmessage);
			if (bmessage.MessageType != "banned" && ShowDanmuku(bmessage.MessageType) && (!string.IsNullOrEmpty(bmessage.Message) || !int.TryParse(bmessage.Message, out _)))
			{
				// Gift Delay
				if (_settings.danmuku_gift_combine && (Array.Exists(gift, el => el == bmessage.MessageType) || Array.Exists(gift_combo, el => el == bmessage.MessageType)))
				{
					enqueueGiftDanmuku(arg1, bmessage);
				}
				else
				{
					_logger.LogInformation($"[BilibiliService] | [DanmukuProcessor] | allowed {bmessage.MessageType} {bmessage.Message}");
					// switch (bmessage.extra is )
					forwardPacket(JsonSerializer.Serialize(bmessage));
					TextMessageReceivedCallbacks?.InvokeAll(arg1, this, bmessage);
				}
			}
		}

		private void StartHeartBeat()
		{
			packetTimer.Start();
		}

		private async void GetChannelConfigAsync(int roomID) {
			try
			{
				_logger.LogInformation($"[BilibiliService] | [GetChannelConfigAsync] | Getting channel config for room ID: {roomID}");
				var apiResult = await (new HttpClientUtils()).HttpClient(BilibiliChannelInfoApi + roomID, HttpMethod.Get, null, null);
				if (apiResult != null && apiResult[0] == "OK")
				{
					_logger.LogInformation($"[BilibiliService] | [GetChannelConfigAsync] | API Response: {(apiResult[1].Length > 500 ? apiResult[1].Substring(0, 500) + "..." : apiResult[1])}");
					var NewChannelInfo = JSONNode.Parse(apiResult[1]);
					if (NewChannelInfo["data"]["room_info"]["room_id"] != string.Empty)
					{
						// 先获取 UID 字符串
						var uidString = NewChannelInfo["data"]["room_info"]["uid"].Value;
						_logger.LogInformation($"[BilibiliService] | [GetChannelConfigAsync] | Raw UID string: '{uidString}'");

						// 使用 TryParse 避免溢出异常
						if (long.TryParse(uidString, out var parsedUserId))
						{
							_userID = parsedUserId;
							_logger.LogInformation($"[BilibiliService] | [GetChannelConfigAsync] | Successfully parsed user ID: {_userID}");
						}
						else
						{
							_logger.LogWarning($"[BilibiliService] | [GetChannelConfigAsync] | Failed to parse user ID: {uidString}");
							_userID = 0;
						}

						// 输出完整的 room_info 结构
						if (NewChannelInfo["data"]["room_info"] != null)
						{
							_logger.LogInformation($"[BilibiliService] | [GetChannelConfigAsync] | room_info data: {NewChannelInfo["data"]["room_info"]}");
						}
						
						// 尝试从不同的位置获取房间号
						var roomIdNode = NewChannelInfo["data"]["room_info"]["room_id"];
						if (roomIdNode == null)
						{
							_logger.LogWarning($"[BilibiliService] | [GetChannelConfigAsync] | room_id node is null");
						}
						else
						{
							_logger.LogInformation($"[BilibiliService] | [GetChannelConfigAsync] | room_id value: '{roomIdNode.Value}', type: {roomIdNode.GetType().Name}");
						}

						if (int.TryParse(NewChannelInfo["data"]["room_info"]["room_id"].Value, out var parsedRoomId))
						{
							_roomID = parsedRoomId;
							_logger.LogInformation($"[BilibiliService] | [GetChannelConfigAsync] | Successfully parsed room_id: {_roomID}");
						}
						else
						{
							_logger.LogWarning($"[BilibiliService] | [GetChannelConfigAsync] | Failed to parse room ID: {NewChannelInfo["data"]["room_info"]["room_id"].Value}");
							// 保留原有的 roomID，不要重置为 0
							_logger.LogInformation($"[BilibiliService] | [GetChannelConfigAsync] | Keeping original _roomID: {_roomID}");
						}
						_authManager.Credentials.Bilibili_room_id = _roomID;
						LoggedInUser = new BilibiliChatUser();
						LoggedInUser.Id = _userID.ToString();
						LoggedInUser.UserName = NewChannelInfo["data"]["anchor_info"]["base_info"]["uname"]!;
						LoggedInUser.DisplayName = LoggedInUser.UserName;
						LoggedInUser.Color = "#FF0000";
						LoggedInUser.IsBroadcaster = true;
						LoggedInUser.IsModerator = true;
						LoggedInUser.IsFan = true;
						_logger.LogInformation($"[BilibiliService] | [GetChannelConfigAsync] | Success");
					}
				}
				else
				{
					_logger.LogInformation($"[BilibiliService] | [GetChannelConfigAsync] | Get Channel Info failedfff. ({(apiResult == null ? "connection failed" : (apiResult[0] + " " + apiResult[1]))})");
				}

			}
			catch (Exception ex) {
				_logger.LogInformation($"[BilibiliService] | [GetChannelConfigAsync] | aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
				_logger.LogInformation($"[BilibiliService] | [GetChannelConfigAsync] | Exception: {ex.GetType().Name}: {ex.Message}");
				_logger.LogInformation($"[BilibiliService] | [GetChannelConfigAsync] | Stack trace: {ex.StackTrace}");
				_logger.LogInformation($"[BilibiliService] | [GetChannelConfigAsync] | Get Channel Info failedddd. (Exception)");
			}
		}

		private async void GetChatBuvidAsync()
		{
			_logger.LogInformation($"[BilibiliService] | [GetChatBuvidAsync] | Starttttttttt");

			try
			{
				// 调试日志：输出原始 Cookie 信息
				// _logger.LogInformation($"[BilibiliService] | [GetChatBuvidAsync] | Raw Cookie: {(_authManager.Credentials.Bilibili_cookies?.Length > 50 ? _authManager.Credentials.Bilibili_cookies.Substring(0, 50) + "..." : _authManager.Credentials.Bilibili_cookies)}");

				_buvid3 = GetValueFromCookie("buvid3");
				// _logger.LogInformation($"[BilibiliService] | [GetChatBuvidAsync] | buvid3 value: '{_buvid3}'");

				var _dedeUserID = GetValueFromCookie("DedeUserID");
				// _logger.LogInformation($"[BilibiliService] | [GetChatBuvidAsync] | DedeUserID raw value: '{_dedeUserID}' (length: {_dedeUserID?.Length ?? 0})");

				// 检查 DedeUserID 是否为有效数字
				if (!string.IsNullOrWhiteSpace(_dedeUserID))
				{
					// 先检查是否为纯数字
					if (!System.Text.RegularExpressions.Regex.IsMatch(_dedeUserID, @"^\d+$"))
					{
						_logger.LogWarning($"[BilibiliService] | [GetChatBuvidAsync] | DedeUserID contains non-numeric characters: '{_dedeUserID}'");
						_randomUid = 0;
					}
					else
					{
						// 尝试解析
						if (long.TryParse(_dedeUserID, out _randomUid))
						{
							_logger.LogInformation($"[BilibiliService] | [GetChatBuvidAsync] | Successfully parsed DedeUserID to long: {_randomUid}");
						}
						else
						{
							_logger.LogWarning($"[BilibiliService] | [GetChatBuvidAsync] | Failed to parse DedeUserID to long: '{_dedeUserID}'");
							_randomUid = 0;
						}
					}
				}
				else
				{
					_logger.LogInformation($"[BilibiliService] | [GetChatBuvidAsync] | DedeUserID is empty or whitespace");
					_randomUid = 0;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError($"[BilibiliService] | [GetChatBuvidAsync] | Exception in initial phase: {ex.GetType().Name}: {ex.Message}");
				_logger.LogError($"[BilibiliService] | [GetChatBuvidAsync] | Stack trace: {ex.StackTrace}");
				_randomUid = 0;
			}
			if (_buvid3 == "")
			{
				_logger.LogInformation($"[BilibiliService] | [GetChatBuvidAsync] | No Cookie found, try to get one!");

				// 重试机制：最多尝试3次
				int maxRetries = 3;
				for (int attempt = 1; attempt <= maxRetries; attempt++)
				{
					try
					{
						var apiResult = await (new HttpClientUtils()).HttpClient(BilibiliFingerprintApi, HttpMethod.Get, _authManager.Credentials.Bilibili_cookies, null);
						if (apiResult != null && apiResult[0] == "OK")
						{
							_logger.LogInformation($"[BilibiliService] | [GetChatBuvidAsync] | API Response: {(apiResult[1].Length > 200 ? apiResult[1].Substring(0, 200) + "..." : apiResult[1])}");

							// 在解析 JSON 之前记录完整响应，以便调试
							_logger.LogInformation($"[BilibiliService] | [GetChatBuvidAsync] | Full API Response for debugging: {apiResult[1]}");

							JSONNode NewChatBuvidInfo;
							try
							{
								NewChatBuvidInfo = JSONNode.Parse(apiResult[1]);
							}
							catch (OverflowException overflowEx)
							{
								_logger.LogError($"[BilibiliService] | [GetChatBuvidAsync] | OverflowException during JSON Parse!");
								_logger.LogError($"[BilibiliService] | [GetChatBuvidAsync] | API Response that caused overflow: {apiResult[1]}");
								_logger.LogError($"[BilibiliService] | [GetChatBuvidAsync] | Exception: {overflowEx.Message}");
								_logger.LogError($"[BilibiliService] | [GetChatBuvidAsync] | Stack trace: {overflowEx.StackTrace}");

								// 尝试使用 Newtonsoft.Json 或其他方法解析，看看具体是哪个字段
								try
								{
									// 用正则表达式查找可能的大数字
									var matches = System.Text.RegularExpressions.Regex.Matches(apiResult[1], @"""(\w+)""\s*:\s*(\d{10,})");
									foreach (System.Text.RegularExpressions.Match match in matches)
									{
										_logger.LogError($"[BilibiliService] | [GetChatBuvidAsync] | Found large number - Field: {match.Groups[1].Value}, Value: {match.Groups[2].Value}");
									}
								}
								catch { }

								throw;
							}
							catch (Exception parseEx)
							{
								_logger.LogError($"[BilibiliService] | [GetChatBuvidAsync] | JSON Parse Exception: {parseEx.GetType().Name}: {parseEx.Message}");
								_logger.LogError($"[BilibiliService] | [GetChatBuvidAsync] | Parse Stack trace: {parseEx.StackTrace}");
								throw;
							}

							// 安全地检查 code 字段
							var codeNode = NewChatBuvidInfo["code"];
							_logger.LogInformation($"[BilibiliService] | [GetChatBuvidAsync] | Code node type: {codeNode?.GetType().Name}, value: {codeNode?.ToString()}");

							// 使用字符串比较避免 int 解析
							if (codeNode != null && codeNode.ToString() == "0")
							{
								_buvid3 = NewChatBuvidInfo["data"]["b_3"].Value;
								_logger.LogInformation($"[BilibiliService] | [GetChatBuvidAsync] | Success on attempt {attempt}");
								break; // 成功获取，退出重试循环
							}
							else
							{
								// 安全地获取 message 字段，避免潜在的类型转换问题
								string messageValue = "null";
								try
								{
									var messageNode = NewChatBuvidInfo["message"];
									if (messageNode != null)
									{
										messageValue = messageNode.ToString();
									}
								}
								catch (Exception msgEx)
								{
									_logger.LogError($"[BilibiliService] | [GetChatBuvidAsync] | Error accessing message field: {msgEx.Message}");
								}
								_logger.LogInformation($"[BilibiliService] | [GetChatBuvidAsync] | Get buvid3 failed on attempt {attempt}. (code: {codeNode?.ToString() ?? "null"} message: {messageValue})");
							}
						}
						else
						{
							_logger.LogInformation($"[BilibiliService] | [GetChatBuvidAsync] | Get buvid3 failed on attempt {attempt}. ({(apiResult == null ? "connection failed" : (apiResult[0] + " " + apiResult[1]))})");
						}
					}
					catch (Exception ex)
					{
						_logger.LogInformation($"[BilibiliService] | [GetChatBuvidAsync] | Get buvid3 failed on attempt {attempt}. (Exception: {ex.Message})");

						// 如果是 OverflowException，说明 API 返回了异常数据
						if (ex is OverflowException)
						{
							_logger.LogError($"[BilibiliService] | [GetChatBuvidAsync] | OverflowException detected - API may be returning invalid data");
							break; // 立即退出重试循环
						}
					}

					// 如果不是最后一次尝试，等待后重试
					if (attempt < maxRetries && string.IsNullOrEmpty(_buvid3))
					{
						await Task.Delay(1000 * attempt); // 递增延迟：1秒、2秒
						_logger.LogInformation($"[BilibiliService] | [GetChatBuvidAsync] | Retrying... (attempt {attempt + 1}/{maxRetries})");
					}
				}

				// 如果所有重试都失败，自动降级到 Legacy 模式
				if (string.IsNullOrEmpty(_buvid3))
				{
					_logger.LogWarning($"[BilibiliService] | [GetChatBuvidAsync] | Failed to get buvid3 after {maxRetries} attempts, switching to Legacy mode");
					_settings.danmuku_service_method = "Legacy";
					_settings.Save();
				}
			}
			else
			{
				_logger.LogInformation($"[BilibiliService] | [GetChatBuvidAsync] | Get buvid3 from cookie");
			}

			// 继续连接流程
			reloadWebsocketConnection();
		}

		private string GetValueFromCookie(string key) {
			try
			{
				// _logger.LogInformation($"[BilibiliService] | [GetValueFromCookie] | Getting value for key: '{key}'");

				var rawCookie = _authManager.Credentials.Bilibili_cookies;
				// _logger.LogInformation($"[BilibiliService] | [GetValueFromCookie] | Raw cookie length: {rawCookie?.Length ?? 0}");

				var clean_cookie = new HttpClientUtils().RemoveExpiredTimeAndPath(rawCookie);
				// _logger.LogInformation($"[BilibiliService] | [GetValueFromCookie] | Clean cookie length: {clean_cookie?.Length ?? 0}");

				if (string.IsNullOrEmpty(clean_cookie))
				{
					_logger.LogWarning($"[BilibiliService] | [GetValueFromCookie] | Clean cookie is empty");
					return "";
				}

				var _cookie_items = Regex.Matches(clean_cookie, @"(.+?)(?:=(.+?))?(?:;|$|,(?!\s))").Cast<Match>()
									 .ToDictionary(m => m.Groups[1].Value.Trim(), m => m.Groups[2].Value.Trim(), StringComparer.OrdinalIgnoreCase);

				// _logger.LogInformation($"[BilibiliService] | [GetValueFromCookie] | Found {_cookie_items.Count} cookie items");

				if (_cookie_items.TryGetValue(key, out var value))
				{
					_logger.LogInformation($"[BilibiliService] | [GetValueFromCookie] | Found value for '{key}': '{value}' (length: {value?.Length ?? 0})");
					return value;
				}
				else
				{
					_logger.LogInformation($"[BilibiliService] | [GetValueFromCookie] | Key '{key}' not found in cookies");
					return "";
				}
			}
			catch (Exception ex)
			{
				_logger.LogError($"[BilibiliService] | [GetValueFromCookie] | Exception: {ex.GetType().Name}: {ex.Message}");
				_logger.LogError($"[BilibiliService] | [GetValueFromCookie] | Stack trace: {ex.StackTrace}");
				return "";
			}
		}

                private async Task<bool> GetCookieStatusAsync()
                {
                        _logger.LogInformation($"[BilibiliService] | [GetCookieStatusAsync] | Start");
                        var _csrf = GetValueFromCookie("bili_jct");
                        var _status = false;
                        if (_csrf != "")
                        {
                                try
                                {
                                        var content = new FormUrlEncodedContent(new Dictionary<string, string>
                                        {
                                                { "msg_notify", "1" },
                                                { "show_unfollowed_msg", "1" },
                                                { "build", "0" },
                                                { "mobi_app", "web" },
                                                { "csrf_token", _csrf },
                                                { "csrf", _csrf }
                                        });
                                        var apiResult = await (new HttpClientUtils()).HttpClient(BilibiliValidateLoginInfoApi, HttpMethod.Post, _authManager.Credentials.Bilibili_cookies, content);
                                        if (apiResult != null && apiResult[0] == "OK")
                                        {
                                                var NewCookieStatusInfo = JSONNode.Parse(apiResult[1]);
                                                // 使用字符串比较避免 int 溢出
                                                var cookieCodeNode = NewCookieStatusInfo["code"];
                                                if (cookieCodeNode != null && cookieCodeNode.ToString() == "0")
                                                {
                                                        _logger.LogInformation($"[BilibiliService] | [GetCookieStatusAsync] | Valid!");
                                                        _status = true;
                                                }
                                                else
                                                {
                                                        _logger.LogInformation($"[BilibiliService] | [GetCookieStatusAsync] | Failed, Cookie is invalid!");
                                                }
                                        }
                                        else
                                        {
                                                _logger.LogInformation($"[BilibiliService] | [GetCookieStatusAsync] | Get cookie status failed. ({(apiResult == null ? "connection failed" : (apiResult[0] + " " + apiResult[1]))})");
                                        }
				}
				catch
				{
					_logger.LogInformation($"[BilibiliService] | [GetCookieStatusAsync] | Get cookie status failed. (Exception)");
				}
			}
			else
			{
				_logger.LogInformation($"[BilibiliService] | [GetCookieStatusAsync] | Failed, Cookie is lack of csrf");
			}

			return _status;
		}

		private async Task UpdateCookieStatus() {
			_cookie_valid = await GetCookieStatusAsync();
		}

		private async void UpdateCookieStatusAndDo(string functionName)
		{
			await UpdateCookieStatus();
			switch (functionName)
			{
				case "emotion":
					GetEmotionsAsync(_roomID);
					break;
				case "chatToken":
					GetChatTokenAsync(_roomID);
					break;
			}
		}

		private async void GetChatTokenAsync(int roomID)
		{
			_chatToken = "";
			_logger.LogInformation($"[BilibiliService] | [GetChatTokenAsync] | Start");
			//if (_settings.danmuku_service_method == "Default" && GetValueFromCookie("DedeUserID") == "")
			//{
			//	_logger.LogInformation($"[BilibiliService] | [GetChatTokenAsync] | No Cookie found, switch to Legacy Mode!");
			//	_settings.danmuku_service_method = "Legacy";
			//	_settings.Save();
			//	_settings.updateBilibili(_enable);
			//	return;
			//}

			try
			{
				// 构建基础参数
				var parameters = new Dictionary<string, string>
				{
					["id"] = roomID.ToString(),
					["type"] = "0"
				};

				// 尝试使用 WBI 签名
				string finalUrl;
				try
				{
					// getDanmuInfo API 使用特殊的 web_location 值
					parameters["web_location"] = "444.8";
					var signedParams = await ChatCore.Utilities.BLive.WbiUtils.SignParametersAsync(parameters, _cookie_valid ? _authManager.Credentials.Bilibili_cookies : "");

					// 验证签名是否成功
					if (!signedParams.ContainsKey("w_rid"))
					{
						throw new InvalidOperationException("WBI signing failed - no w_rid generated");
					}

					var queryString = string.Join("&", signedParams.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
					finalUrl = $"https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?{queryString}";
					_logger.LogInformation($"[BilibiliService] | [GetChatTokenAsync] | Using WBI signed request");
				}
				catch (Exception wbiEx)
				{
					// WBI 签名失败，立即切换到 Legacy 模式
					_logger.LogError($"[BilibiliService] | [GetChatTokenAsync] | WBI signing failed: {wbiEx.Message}, switching to Legacy mode immediately");

					// 清除 WBI 缓存
					ChatCore.Utilities.BLive.WbiUtils.ClearCache();

					// 自动降级到 Legacy 模式
					if (_settings.danmuku_service_method == "Default")
					{
						_settings.danmuku_service_method = "Legacy";
						_settings.Save();
						_logger.LogWarning($"[BilibiliService] | [GetChatTokenAsync] | Permanently switched to Legacy mode due to WBI signing failure");

						// 触发重新连接，使用新的 Legacy 模式
						reloadWebsocketConnection();
						return;
					}

					// 如果已经是 Legacy 模式，使用原始 URL
					finalUrl = $"https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo?type=0&web_location=444.8&id={roomID}";
				}

				// 添加必要的 cookie: opus-goback=1
				var cookieHeader = _cookie_valid ? _authManager.Credentials.Bilibili_cookies : "";
				if (!string.IsNullOrEmpty(cookieHeader))
				{
					if (!cookieHeader.Contains("opus-goback"))
					{
						cookieHeader += "; opus-goback=1";
					}
				}
				else
				{
					cookieHeader = "opus-goback=1";
				}

				var apiResult = await (new HttpClientUtils()).HttpClient(finalUrl, HttpMethod.Get, cookieHeader, null);
				if (apiResult != null && apiResult[0] == "OK")
				{
					Console.WriteLine(apiResult[1]);
					var NewChatTokenInfo = JSONNode.Parse(apiResult[1]);
					// 使用字符串比较避免 int 溢出
					var tokenCodeNode = NewChatTokenInfo["code"];
					if (tokenCodeNode != null && tokenCodeNode.ToString() == "0")
					{
						_chatToken = NewChatTokenInfo["data"]["token"].Value;
						_logger.LogInformation($"[BilibiliService] | [GetChatTokenAsync] | Success");
						//_logger.LogInformation(apiResult[1]);
					}
					else
					{
						_logger.LogInformation($"[BilibiliService] | [GetChatTokenAsync] | Get token failed. ({NewChatTokenInfo["code"]} {NewChatTokenInfo["message"]})");
						// 如果是 -352 错误，清除 WBI 缓存
						// 检查是否为 -352 错误
						if (tokenCodeNode != null && tokenCodeNode.ToString() == "-352")
						{
							ChatCore.Utilities.BLive.WbiUtils.ClearCache();
							_logger.LogInformation($"[BilibiliService] | [GetChatTokenAsync] | Cleared WBI cache due to -352 error");

							// 自动切换到传统模式
							_logger.LogWarning($"[BilibiliService] | [GetChatTokenAsync] | Switching to Legacy mode due to persistent -352 errors");
							_settings.danmuku_service_method = "Legacy";
							_settings.Save();
						}
					}
				}
				else
				{
					_logger.LogInformation($"[BilibiliService] | [GetChatTokenAsync] | Get token failed. ({(apiResult == null ? "connection failed" : (apiResult[0] + " " + apiResult[1]))})");
				}
				reloadWebsocketConnection();
			}
			catch
			{
				_logger.LogInformation($"[BilibiliService] | [GetChatTokenAsync] | Get token failed. (Exception)");
				reloadWebsocketConnection();
			}
		}

		private async void GetChannelGiftRoomInfoAsync(int roomID)
		{
			try
			{
				var apiResult = await (new HttpClientUtils()).HttpClient(BilibiliGiftRoomInfoApi + roomID, HttpMethod.Get, _cookie_valid ? _authManager.Credentials.Bilibili_cookies : "", null);
				if (apiResult != null && apiResult[0] == "OK")
				{
					var NewGiftInfo = JSONNode.Parse(apiResult[1]);
					// 使用字符串比较避免 int 溢出
					var giftCodeNode = NewGiftInfo["code"];
					if (giftCodeNode != null && giftCodeNode.ToString() == "0")
					{
						var giftList = NewGiftInfo["data"]["list"].AsArray!;
						foreach (JSONObject gift in giftList)
						{
							var gift_id = gift["id"].ToString();
							bilibiliGiftCoinType[gift_id] = gift["coin_type"]!;
							if (gift["gif"].IsNull)
							{
								bilibiliGiftInfo[gift_id] = gift["img_basic"]!;
							}
							else
							{
								bilibiliGiftInfo[gift_id] = gift["gif"]!;
							}
							bilibiliGiftPrice[gift_id] = Math.Round(gift["price"].AsInt / 1000.0f, 1);
							bilibiliGiftName[gift_id] = gift["name"]!;
						}
					}
					else
					{
						_logger.LogInformation($"[BilibiliService] | [GetChannelGiftRoomInfoAsync] | Get Channel Gift Room Info failed. ({NewGiftInfo["code"]} {NewGiftInfo["message"]})");
					}
				}
				else
				{
					_logger.LogInformation($"[BilibiliService] | [GetChannelGiftRoomInfoAsync] | Get Channel Gift Room Info failed. ({(apiResult == null ? "connection failed" : apiResult[0])})");
				}
			}
			catch
			{
				_logger.LogInformation($"[BilibiliService] | [GetChannelGiftRoomInfoAsync] | Get Channel Gift Room Info failed. (Exception)");
			}
		}

		private async void GetWealthAsync()
		{
			try
			{
				var apiResult = await (new HttpClientUtils()).HttpClient(BilibiliWealthApi, HttpMethod.Get, _cookie_valid? _authManager.Credentials.Bilibili_cookies: "", null);
				if (apiResult != null && apiResult[0] == "OK")
				{
					var NewWealthInfo = JSONNode.Parse(apiResult[1]);
					// 使用字符串比较避免 int 溢出
					var wealthCodeNode = NewWealthInfo["code"];
					if (wealthCodeNode != null && wealthCodeNode.ToString() == "0")
					{
						var wealthList = JSONNode.Parse(NewWealthInfo["data"]["content"]!)["wealth_level_medal"].AsArray!;
						foreach (JSONObject wealth in wealthList)
						{
							var wealth_id = wealth["id"].ToString();
							bilibiliWealth[wealth_id] = wealth["url"]!;
						}
					}
					else
					{
						_logger.LogInformation($"[BilibiliService] | [GetWealthAsync] | Get Wealth Info failed. ({NewWealthInfo["code"]} {NewWealthInfo["message"]})");
					}
				}
				else
				{
					_logger.LogInformation($"[BilibiliService] | [GetWealthAsync] | Get Wealth Info failed. ({(apiResult == null ? "connection failed" : apiResult[0])})");
				}
			}
			catch
			{
				_logger.LogInformation($"[BilibiliService] | [GetWealthAsync] | Get Wealth Info failed. (Exception)");
			}
		}

		private async void GetEmotionsAsync(int roomID)
		{
			_logger.LogInformation($"[BilibiliService] | [GetEmotionsAsync] | Start");

			initBilibiliEmoticonsDictionary();

			if (GetValueFromCookie("DedeUserID") != "" && _cookie_valid)
			{
				_logger.LogInformation($"[BilibiliService] | [GetEmotionsAsync] | Cookie found, get the emotions from the api!");
				try
				{
					var apiResult = await (new HttpClientUtils()).HttpClient(BilibiliEmotionsApi + roomID, HttpMethod.Get, _authManager.Credentials.Bilibili_cookies, null);
					if (apiResult != null && apiResult[0] == "OK")
					{
						var NewEmotionsInfo = JSONNode.Parse(apiResult[1]);
						// 使用字符串比较避免 int 溢出
						var emotionCodeNode = NewEmotionsInfo["code"];
						if (emotionCodeNode != null && emotionCodeNode.ToString() == "0")
						{
							var emotionPackageList = NewEmotionsInfo["data"]["data"].AsArray!;
							foreach (JSONObject emotionPackage in emotionPackageList)
							{
								// 使用字符串比较避免 int 溢出
								var pkgIdNode = emotionPackage["pkg_id"];
								if (pkgIdNode != null && pkgIdNode.ToString() == "100")
								{
									// 100: emoji / emoji表情
									var emotionList = emotionPackage["emoticons"].AsArray!;
									foreach (JSONObject emotion in emotionList)
									{
										var emoji = new Dictionary<string, string>();
										emoji.Add("id", emotion["emoticon_unique"].Value);
										emoji.Add("name", emotion["emoji"].Value);
										emoji.Add("url", emotion["url"].Value);
										bilibiliEmoticons[emotion["emoji"].Value] = emoji;
									}
								}
							}
							_logger.LogInformation($"[BilibiliService] | [GetEmotionsAsync] | Success");
						}
						else
						{
							_logger.LogInformation($"[BilibiliService] | [GetEmotionsAsync] | Get emotions failed. ({NewEmotionsInfo["code"]} {NewEmotionsInfo["message"]})");
						}
					}
					else
					{
						_logger.LogInformation($"[BilibiliService] | [GetEmotionsAsync] | Get emotions failed. ({(apiResult == null ? "connection failed" : (apiResult[0] + " " + apiResult[1]))})");
					}
				}
				catch
				{
					_logger.LogInformation($"[BilibiliService] | [GetEmotionsAsync] | Get emotions failed. (Exception)");
				}
			}
			else
			{
				// Hard coded list
				_logger.LogInformation($"[BilibiliService] | [GetEmotionsAsync] | No Cookie found, use the hard-code emotion table!");
			}
		}

		private void PacketTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
		{
			SendHeartBeatPacket();
		}

		internal void Start(bool forceReconnect = false)
		{
			if (forceReconnect)
			{
				Stop();
			}
			lock (_initLock)
			{
				if (!_isStarted && _enable)
				{
					_logger.LogInformation($"[BilibiliService] | [ws_Start] | [Bilibili] | Start");

					_isStarted = true;
					_logger.LogInformation($"[BilibiliService] | [ws_Start] | [Bilibili] | Start | " + _settings.danmuku_service_method);
					try
					{
						switch (_settings.danmuku_service_method)
						{
							case "OpenBLive":
								if (_wssLink != "")
								{
									UpdateCookieStatusAndDo("emotion");

									if (_websocketService.IsConnected)
									{
										_websocketService.Disconnect();
									}
									_websocketService.Connect(_wssLink, forceReconnect);
								}
								else
								{
									_isStarted = false;
								}
								break;
							case "Default":
								lock (_chatTokenLock)
								{
									if (_chatToken == "")
									{
										UpdateCookieStatusAndDo("chatToken");
									}
									else if (_buvid3 == "")
									{
										GetChatBuvidAsync();
									}
									else
									{
										_websocketService.Connect("wss://broadcastlv.chat.bilibili.com:443/sub", forceReconnect || _websocketService.IsConnected, HttpClientUtils.UserAgent, "https://live.bilibili.com");
									}
								}
								break;
							case "Legacy":
								_websocketService.Connect("wss://broadcastlv.chat.bilibili.com:443/sub", forceReconnect || _websocketService.IsConnected, HttpClientUtils.UserAgent, "https://live.bilibili.com");
								break;

						}

						if (_isStarted)
						{
							Task.Run(ProcessQueuedMessages);
						}
					}
					catch (Exception ex) {
						Console.Error.WriteLine(ex.ToString());
					}
				}
			}
		}

		internal void Stop()
		{
			lock (_initLock)
			{
				if (!_isStarted)
				{
					return;
				}
				try
				{
					packetTimer?.Stop();
					_isStarted = false;
					_channels?.Clear();
					LoggedInUser = null;
					_websocketService?.Disconnect();
				}
				catch (Exception ex)
				{
					Console.WriteLine("[STOP] Exceptions: " + ex.ToString());
				}
			}
		}

		private void _websocketService_OnMessageReceived(Assembly assembly, string rawMessage)
		{
			lock (_messageReceivedLock)
			{
				//_logger.LogInformation("RawMessage: " + rawMessage);

				_rawMessageReceivedCallbacks?.InvokeAll(assembly, this, rawMessage);
				TextMessageReceivedCallbacks?.InvokeAll(assembly, this, new BilibiliChatMessage(rawMessage), _logger);
			}
		}

		private void _websocketService_OnClose()
		{
			if (_channels.TryRemove($"{RoomID}", out var channel))
			{
				_logger.LogInformation($"[BilibiliService] | [ws_OnClose] | Removed channel {RoomID} from the channel list.");
				//LeaveRoomCallbacks?.InvokeAll(arg1, this, channel, _logger);
			}
			_logger.LogInformation("[BilibiliService] | [ws_OnClose] | Bilibili live connection closed");
		}

		private void _websocketService_OnError()
		{
			_logger.LogError("[BilibiliService] | [ws_OnError] | An error occurred in Bilibili connection");
		}

		private void _websocketService_OnOpen()
		{
			_logger.LogInformation("[BilibiliService] | [ws_OnOpen] | Bilibili live connection opened");
			SendGreetingPacket();
		}

		private void SendRawMessage(Assembly assembly, string rawMessage, bool forwardToSharedClients = false)
		{
			if (_websocketService.IsConnected)
			{
				//_websocketService.SendMessage(rawMessage);
				if (forwardToSharedClients)
				{
					_websocketService_OnMessageReceived(assembly, rawMessage);
				}
				return;
			}
			else
			{
				_logger.LogWarning("[BilibiliService] | [ws_send] | WebSocket service is not connected!");
			}
		}

		private async Task ProcessQueuedMessages()
		{
			while (_isStarted)
			{
				if (_currentMessageCount >= 20)
				{
					var remainingMilliseconds = (float)(30000 - (DateTime.UtcNow - _lastResetTime).TotalMilliseconds);
					if (remainingMilliseconds > 0)
					{
						await Task.Delay((int)remainingMilliseconds);
					}
				}
				if ((DateTime.UtcNow - _lastResetTime).TotalSeconds >= 30)
				{
					_currentMessageCount = 0;
					_lastResetTime = DateTime.UtcNow;
				}

				if (_textMessageQueue.TryDequeue(out var msg))
				{
					SendRawMessage(msg.Key, msg.Value, true);
					_currentMessageCount++;
				}
				await Task.Delay(10);
			}
		}

		/// <summary>
		/// Sends a raw message to the Twitch server
		/// </summary>
		/// <param name="rawMessage">The raw message to send.</param>
		/// <param name="forwardToSharedClients">
		/// Whether or not the message should also be sent to other clients in the assembly that implement StreamCore, or only to the Twitch server.<br/>
		/// This should only be set to true if the Twitch server would rebroadcast this message to other external clients as a response to the message.
		/// </param>
		public void SendRawMessage(string rawMessage, bool forwardToSharedClients = false)
		{
			// SendRawMessage(Assembly.GetCallingAssembly(), rawMessage, forwardToSharedClients);
		}

		internal void SendTextMessage(Assembly assembly, string message, string channel)
		{
			// Fake
			var bilibiliChatMessage = new BilibiliChatMessage("{\"cmd\": \"plugin_message\"}");
			bilibiliChatMessage.Message = message;

			TextMessageReceivedCallbacks?.InvokeAll(assembly, this, bilibiliChatMessage);
			//_textMessageQueue.Enqueue(new KeyValuePair<Assembly, string>(assembly, $"@id={Guid.NewGuid().ToString()} PRIVMSG #{channel} :{message}"));
		}

		/*public void SendTextMessage(string message, string channel)
		{
			SendTextMessage(Assembly.GetCallingAssembly(), message, channel);
		}*/

		public void SendTextMessage(string message, IChatChannel channel)
		{
			/*if (channel == null)
			{
				channel = _channels[0];
			}
			if (channel is BilibiliChatChannel)
			{
				SendTextMessage(Assembly.GetCallingAssembly(), message, channel.Id);
			}*/
			SendTextMessage(Assembly.GetCallingAssembly(), message, channel.Id);
		}

		/*public void SendCommand(string command, string channel)
		{
			SendRawMessage(Assembly.GetCallingAssembly(), $"PRIVMSG #{channel} :/{command}");
		}

		public void JoinChannel(string channel)
		{
			_logger.LogInformation($"Trying to join channel #{channel}");
			SendRawMessage(Assembly.GetCallingAssembly(), $"JOIN #{channel.ToLower()}");
		}

		public void PartChannel(string channel)
		{
			SendRawMessage(Assembly.GetCallingAssembly(), $"PART #{channel.ToLower()}");
		}*/

		private void SendGreetingPacket()
		{
			// 添加调试信息
			_logger.LogInformation($"[BilibiliService] | [SendGreetingPacket] | Debug info:");
			_logger.LogInformation($"  - _roomID: {_roomID}");
			_logger.LogInformation($"  - _randomUid: {_randomUid}");
			_logger.LogInformation($"  - _chatToken: {(_chatToken?.Length > 0 ? $"Set ({_chatToken.Length} chars)" : "Empty/Null")}");
			// _logger.LogInformation($"  - _buvid3: {(_buvid3?.Length > 0 ? $"Set ({_buvid3.Length} chars)" : "Empty/Null")}");
			_logger.LogInformation($"  - Method: {_settings.danmuku_service_method}");
			
			_logger.LogInformation($"[BilibiliService] | [SendGreetingPacket] | Send Greeting packet. Connect to room {_roomID} via {_settings.danmuku_service_method}");
			switch (_settings.danmuku_service_method)
			{
				case "Default":
					_websocketService.SendMessage(BilibiliPacket.CreateGreetingPacket(_randomUid, _roomID, _chatToken, _buvid3).PacketBuffer);
					break;
				case "Legacy":
					_websocketService.SendMessage(BilibiliPacket.CreateGreetingPacket(_randomUid, _roomID).PacketBuffer);
					break;
				case "OpenBLive":
					_logger.LogInformation($"{_wssLink} {_auth_body}");
					_websocketService.SendMessage(BilibiliPacket.CreateAuthPacket(_auth_body).PacketBuffer);
					break;
			}
		}

		private void SendHeartBeatPacket()
		{
			if (!_websocketService.IsConnected)
			{
				return;
			}

			var packet = BilibiliPacket.CreateHeartBeatPacket();
			_websocketService.SendMessage(packet.PacketBuffer);
		}

		private void BanDetection(BilibiliChatMessage msg) {
			if (BanListDetect(msg.Uid.ToString(), "uid") || BanListDetect(msg.Username, "username") || BanListDetect(msg.Content, "content"))
			{
				_logger.Log(LogLevel.Information, $"[BilibiliService] | [BanDetection] | \"{msg.Username}(UID: {msg.Uid}): {msg.Content}\" has been banned!");
				msg.BanMessage();
			}
		}

		private bool BanListDetect(string value, string mode)
		{
			if (string.IsNullOrEmpty(value))
			{
				return false;
			}

			switch (mode)
			{
				case "username":
					JSONArray usernameArray = JSON.Parse(_settings.bilibili_block_list_username).AsArray!;
					foreach (var username in usernameArray)
					{
						if (value.Contains(username.Value.Value))
						{
							return true;
						}
						else if (username.Value.Value.StartsWith("#")) // Match username that represent numbers
						{
							var num = "";
							var target_username = value;
							var rule = username.Value.Value.Substring(1);
							target_username = target_username
								.Replace("一", "1").Replace("壹", "1").Replace("①", "1")
								.Replace("二", "2").Replace("贰", "2").Replace("②", "2")
								.Replace("三", "3").Replace("叁", "3").Replace("③", "3")
								.Replace("四", "4").Replace("肆", "4").Replace("④", "4")
								.Replace("五", "5").Replace("伍", "5").Replace("⑤", "5")
								.Replace("六", "6").Replace("陆", "6").Replace("⑥", "6")
								.Replace("七", "7").Replace("柒", "7").Replace("⑦", "7")
								.Replace("八", "8").Replace("捌", "8").Replace("⑧", "8")
								.Replace("九", "9").Replace("玖", "9").Replace("⑨", "9")
								.Replace("零", "0").Replace("〇", "0")
								;
							foreach (var character in target_username)
							{
								if (int.TryParse(character.ToString(), out _))
								{
									num += character;
								}
							}
							return (num.Equals(rule));
						}
					}
					return false;
				case "uid":
					//Console.WriteLine($"UID: {value}");
					JSONArray uidArray = JSON.Parse(_settings.bilibili_block_list_uid).AsArray!;
					foreach (var uid in uidArray)
					{
						if (value == uid.Value)
						{
							return true;
						}
					}
					return false;
				case "content":
					//Console.WriteLine($"Content: {value}");
					JSONArray keywordArray = JSON.Parse(_settings.bilibili_block_list_keyword).AsArray!;
					foreach (var keywords in keywordArray)
					{
						if (value.Contains(keywords.Value))
						{
							return true;
						}
					}
					return false;
				default:
					return false;
			}
		}

		private bool ShowDanmuku(string type) {
			var result = false;
			if (
				(Array.Exists(danmuku, el => el == type) && _settings.danmuku_danmuku) ||
				(Array.Exists(sc, el => el == type) && _settings.danmuku_superchat) ||
				(Array.Exists(gift, el => el == type) && _settings.danmuku_gift) ||
				(Array.Exists(gift_star, el => el == type) && _settings.danmuku_gift_star) ||
				(Array.Exists(gift_combo, el => el == type) && _settings.danmuku_gift_combo) ||
				(Array.Exists(welcome, el => el == type) && _settings.danmuku_interaction_enter) ||
				(Array.Exists(share, el => el == type) && _settings.danmuku_interaction_share) ||
				(Array.Exists(follow, el => el == type) && _settings.danmuku_interaction_follow) ||
				(Array.Exists(special_follow, el => el == type) && _settings.danmuku_interaction_special_follow) ||
				(Array.Exists(matual_follow, el => el == type) && _settings.danmuku_interaction_mutual_follow) ||
				(Array.Exists(welcome_guard, el => el == type) && _settings.danmuku_interaction_guard_enter) ||
				(Array.Exists(effect, el => el == type) && _settings.danmuku_interaction_effect) ||
				(Array.Exists(anchor, el => el == type) && _settings.danmuku_interaction_anchor) ||
				(Array.Exists(raffle, el => el == type) && _settings.danmuku_interaction_raffle) ||
				(Array.Exists(red_packet, el => el == type) && _settings.danmuku_interaction_red_packet) ||
				(Array.Exists(new_guard, el => el == type) && _settings.danmuku_new_guard) ||
				(Array.Exists(new_guard_msg, el => el == type) && _settings.danmuku_new_guard_msg) ||
				(Array.Exists(guard_msg, el => el == type) && _settings.danmuku_guard_msg) ||
				(Array.Exists(guard_lottery_msg, el => el == type) && _settings.danmuku_guard_lottery) ||
				(Array.Exists(blocklist, el => el == type) && _settings.danmuku_notification_block_list) ||
				(Array.Exists(room_change, el => el == type) && _settings.danmuku_notification_room_info_change) ||
				(Array.Exists(room_perparing, el => el == type) && _settings.danmuku_notification_room_prepare) ||
				(Array.Exists(room_live, el => el == type) && _settings.danmuku_notification_room_online) ||
				(Array.Exists(like, el => el == type) && _settings.danmuku_notification_like) ||
				(Array.Exists(global, el => el == type) && _settings.danmuku_notification_boardcast) ||
				(Array.Exists(room_boardcast, el => el == type) && _settings.danmuku_notification_room_rank) ||
				(Array.Exists(junk, el => el == type) && _settings.danmuku_notification_junk) ||
				(Array.Exists(pk, el => el == type) && _settings.danmuku_notification_pk) ||
				(Array.Exists(system, el => el == type))
				)
			{
				result = true;
			}

			return result;
		}

		private void enqueueGiftDanmuku(Assembly arg1, BilibiliChatMessage bmessage) {
			var extra = (BilibiliChatMessageExtraGift)(bmessage.extra);
			var TimerName = $"{bmessage.Uid}_{extra.gift_id}";

			if (giftTimerDict.TryGetValue(TimerName, out var GiftTimer))
			{
				giftTimerDict[TimerName].AddNumber(extra.gift_num);
				giftTimerDict[TimerName].UpdateSender(bmessage.Sender as BilibiliChatUser ?? new BilibiliChatUser());
				giftTimerDict[TimerName].UpdateArg1(arg1);
				giftTimerDict[TimerName].ResetTimer();
			}
			else
			{
				giftTimerDict.Add(TimerName, new BilibiliGiftTimer(TimerName, extra.gift_action, extra.gift_id, extra.gift_name, extra.gift_num, (double)(extra.gift_price / extra.gift_num), extra.gift_img, extra.gift_type, bmessage.Sender as BilibiliChatUser ?? new BilibiliChatUser(), arg1, bmessage));
				giftTimerDict[TimerName].Elapsed += (sender, e) =>
				{
					((BilibiliGiftTimer)sender).GetMessage();
					TextMessageReceivedCallbacks?.InvokeAll(((BilibiliGiftTimer)sender).Arg1, this, ((BilibiliGiftTimer)sender).bmessage);
					var _bmessage = JSON.Parse(JsonSerializer.Serialize(((BilibiliGiftTimer)sender).bmessage));
					Console.WriteLine("Parse good");
					var _extra = _bmessage["extra"].AsObject!;
					Console.WriteLine("Obj get");
					_extra["gift_id"] = new JSONString(extra.gift_id);
					_extra["gift_action"] = new JSONString(extra.gift_action);
					_extra["gift_num"] = new JSONNumber(extra.gift_num);
					_extra["gift_name"] = new JSONString(extra.gift_name);
					_extra["origin_gift"] = new JSONString(extra.origin_gift);
					_extra["gift_type"] = new JSONString(extra.gift_type);
					_extra["gift_price"] = new JSONNumber(extra.gift_price);
					_extra["gift_img"] = new JSONString(extra.gift_img);
					Console.WriteLine(_bmessage.ToString());
					forwardPacket(_bmessage.ToString());
					((BilibiliGiftTimer)sender).Close();
					giftTimerDict.Remove(((BilibiliGiftTimer)sender).Name);
				};
				giftTimerDict[TimerName].Start();
			}
		}

		public void connectBilibili() {
			Start(true);
		}

		public void disconnectBilibili()
		{
			Stop();
		}

		public void Enable()
		{
			_enable = true;
			_logger.LogInformation($"[BilibiliService] Enable() called - Current _roomID: {_roomID}");
			GetWealthAsync();
			if (_roomID != 0)
			{
				_logger.LogInformation($"[BilibiliService] Enable() - Calling GetChannelConfigAsync with roomID: {_roomID}");
				GetChannelConfigAsync(_roomID);
				GetChannelGiftRoomInfoAsync(_roomID);
			}
			else
			{
				_logger.LogWarning($"[BilibiliService] Enable() - Skipping GetChannelConfigAsync because _roomID is 0");
			}
			Start();
		}

		public void Disable()
		{
			_enable = false;
			_buvid3 = "";
			_chatToken = "";
			 Stop();
		}

		private void forwardPacket(JSONObject json, bool isRaw = false) {
			OnBilibiliMessageReceived?.Invoke(json.ToString(), isRaw ? new List<string> { "bilibili_raw" } : new List<string> { "bilibili" });
		}

		private void forwardPacket(string json, bool isRaw = false)
		{
			OnBilibiliMessageReceived?.Invoke(json.ToString(), isRaw ? new List<string> { "bilibili_raw" } : new List<string> { "bilibili" });
		}

		private void initBilibiliEmoticonsDictionary() {
			// In case, for the user who do not want to use the cookie in OpenBLive mode

			// For Official Package
			//addItem("official_100", "", "");
			//addItem("official_101", "", "");
			addItem("official_102", "awsl", "http://i0.hdslb.com/bfs/live/328e93ce9304090f4035e3aa7ef031d015bbc915.png");
			addItem("official_103", "泪目", "http://i0.hdslb.com/bfs/live/aa93b9af7ba03b50df23b64e9afd0d271955cd71.png");
			addItem("official_104", "暗中观察", "http://i0.hdslb.com/bfs/live/18af5576a4582535a3c828c3ae46a7855d9c6070.png");
			addItem("official_105", "保熟吗", "http://i0.hdslb.com/bfs/live/0e28444c8e2faef3169e98e1a41c487144d877d4.png");
			addItem("official_106", "比心", "http://i0.hdslb.com/bfs/live/1ba5126b10e5efe3e4e29509d033a37f128beab2.png");
			addItem("official_107", "好耶", "http://i0.hdslb.com/bfs/live/4cf43ac5259589e9239c4e908c8149d5952fcc32.png");
			addItem("official_108", "禁止套娃", "http://i0.hdslb.com/bfs/live/6a644577437d0bd8a314990dd8ccbec0f3b30c92.png");
			addItem("official_109", "妙啊", "http://i0.hdslb.com/bfs/live/7b7a2567ad1520f962ee226df777eaf3ca368fbc.png");
			addItem("official_110", "咸鱼翻身", "http://i0.hdslb.com/bfs/live/7db4188c050f55ec59a1629fbc5a53661e4ba780.png");
			addItem("official_111", "mua", "http://i0.hdslb.com/bfs/live/08f1aebaa4d9c170aa79cbafe521ef0891bdf2b5.png");
			//addItem("official_112", "", "");
			addItem("official_113", "有点东西", "http://i0.hdslb.com/bfs/live/39e518474a3673c35245bf6ef8ebfff2c003fdc3.png");
			addItem("official_114", "what", "http://i0.hdslb.com/bfs/live/40db7427f02a2d9417f8eeed0f71860dfb28df5a.png");
			addItem("official_115", "来了来了", "http://i0.hdslb.com/bfs/live/61e790813c51eab55ebe0699df1e9834c90b68ba.png");
			addItem("official_116", "贴贴", "http://i0.hdslb.com/bfs/live/88b49dac03bfd5d4cb49672956f78beb2ebd0d0b.png");
			addItem("official_117", "牛牛牛", "http://i0.hdslb.com/bfs/live/343f7f7e87fa8a07df63f9cba6b776196d9066f0.png");
			addItem("official_118", "雀食", "http://i0.hdslb.com/bfs/live/7251dc7df587388a3933743bf38394d12a922cd7.png");
			addItem("official_119", "颠个勺", "http://i0.hdslb.com/bfs/live/625989e78079e3dc38d75cb9ac392fe8c1aa4a75.png");
			addItem("official_120", "离谱", "http://i0.hdslb.com/bfs/live/9029486931c3169c3b4f8e69da7589d29a8eadaa.png");
			addItem("official_121", "笑死", "http://i0.hdslb.com/bfs/live/aa48737f877cd328162696a4f784b85d4bfca9ce.png");
			addItem("official_122", "好家伙", "http://i0.hdslb.com/bfs/live/c2650bf9bbc79b682a4b67b24df067fdd3e5e9ca.png");
			addItem("official_123", "那我走", "http://i0.hdslb.com/bfs/live/c3326ceb63587c79e5b4106ee4018dc59389b5c0.png");
			addItem("official_124", "2333", "http://i0.hdslb.com/bfs/live/a98e35996545509188fe4d24bd1a56518ea5af48.png");
			addItem("official_125", "下次一定", "http://i0.hdslb.com/bfs/live/cc2652cef69b22117f1911391567bd2957f27e08.png");
			addItem("official_126", "不上Ban", "http://i0.hdslb.com/bfs/live/eff44c1fc03311573e8817ca8010aca72404f65c.png");
			addItem("official_127", "就这", "http://i0.hdslb.com/bfs/live/ff840c706fffa682ace766696b9f645e40899f67.png");
			addItem("official_128", "赢麻了", "http://i0.hdslb.com/bfs/live/1d4c71243548a1241f422e90cd8ba2b75c282f6b.png");
			addItem("official_129", "烦死了", "http://i0.hdslb.com/bfs/live/2af0e252cc3082384edf8165751f6a49eaf76d94.png");
			//addItem("official_130", "", "");
			//addItem("official_131", "", "");
			//addItem("official_132", "", "");
			addItem("official_133", "钝角", "http://i0.hdslb.com/bfs/live/38cf68c25d9ff5d364468a062fc79571db942ff3.png");
			addItem("official_134", "上热榜", "http://i0.hdslb.com/bfs/live/83d5b9cdaaa820c2756c013031d34dac1fd4156b.png");
			addItem("official_135", "中奖喷雾", "http://i0.hdslb.com/bfs/live/9640c6ab1a848497b8082c2111d44493c6982ad3.png");
			addItem("official_136", "打扰了", "http://i0.hdslb.com/bfs/live/a9e2acaf72b663c6ad9c39cda4ae01470e13d845.png");
			addItem("official_137", "鸡汤来咯", "http://i0.hdslb.com/bfs/live/b371151503978177b237afb85185b0f5431d0106.png");
			addItem("official_138", "我不理解", "http://i0.hdslb.com/bfs/live/fdefb600cf40d8e5a7e566cc97058b47d946cad6.png");
			//addItem("official_139", "", "");
			//addItem("official_140", "", "");
			//addItem("official_141", "", "");
			//addItem("official_142", "", "");
			//addItem("official_143", "", "");
			//addItem("official_144", "", "");
			//addItem("official_145", "", "");
			addItem("official_146", "打call", "http://i0.hdslb.com/bfs/live/fa1eb4dce3ad198bb8650499830560886ce1116c.png");
			addItem("official_147", "赞", "http://i0.hdslb.com/bfs/live/bbd9045570d0c022a984c637e406cb0e1f208aa9.png");
			addItem("official_148", "多谢款待", "http://i0.hdslb.com/bfs/live/4609dad97c0dfa61f8da0b52ab6fff98e0cf1e58.png");
			addItem("official_149", "干杯", "http://i0.hdslb.com/bfs/live/8fedede4028a72e71dae31270eedff5f706f7d18.png");
			addItem("official_150", "很有精神", "http://i0.hdslb.com/bfs/live/e91cbe30b2db1e624bd964ad1f949661501f42f8.png");

			// For emoji Package
			addItem("emoji_208", "[dog]", "http://i0.hdslb.com/bfs/live/4428c84e694fbf4e0ef6c06e958d9352c3582740.png");
			addItem("emoji_209", "[花]", "http://i0.hdslb.com/bfs/live/7dd2ef03e13998575e4d8a803c6e12909f94e72b.png");
			addItem("emoji_210", "[妙]", "http://i0.hdslb.com/bfs/live/08f735d950a0fba267dda140673c9ab2edf6410d.png");
			addItem("emoji_211", "[哇]", "http://i0.hdslb.com/bfs/live/650c3e22c06edcbca9756365754d38952fc019c3.png");
			addItem("emoji_212", "[爱]", "http://i0.hdslb.com/bfs/live/1daaa5d284dafaa16c51409447da851ff1ec557f.png");
			addItem("emoji_213", "[手机]", "http://i0.hdslb.com/bfs/live/b159f90431148a973824f596288e7ad6a8db014b.png");
			addItem("emoji_214", "[撇嘴]", "http://i0.hdslb.com/bfs/live/4255ce6ed5d15b60311728a803d03dd9a24366b2.png");
			addItem("emoji_215", "[委屈]", "http://i0.hdslb.com/bfs/live/69312e99a00d1db2de34ef2db9220c5686643a3f.png");
			addItem("emoji_216", "[抓狂]", "http://i0.hdslb.com/bfs/live/a7feb260bb5b15f97d7119b444fc698e82516b9f.png");
			addItem("emoji_217", "[比心]", "http://i0.hdslb.com/bfs/live/4e029593562283f00d39b99e0557878c4199c71d.png");
			addItem("emoji_218", "[赞]", "http://i0.hdslb.com/bfs/live/2dd666d3651bafe8683acf770b7f4163a5f49809.png");
			addItem("emoji_219", "[滑稽]", "http://i0.hdslb.com/bfs/live/8624fd172037573c8600b2597e3731ef0e5ea983.png");
			addItem("emoji_220", "[吃瓜]", "http://i0.hdslb.com/bfs/live/ffb53c252b085d042173379ac724694ce3196194.png");
			addItem("emoji_221", "[笑哭]", "http://i0.hdslb.com/bfs/live/c5436c6806c32b28d471bb23d42f0f8f164a187a.png");
			addItem("emoji_222", "[捂脸]", "http://i0.hdslb.com/bfs/live/e6073c6849f735ae6cb7af3a20ff7dcec962b4c5.png");
			addItem("emoji_223", "[喝彩]", "http://i0.hdslb.com/bfs/live/b51824125d09923a4ca064f0c0b49fc97d3fab79.png");
			addItem("emoji_224", "[偷笑]", "http://i0.hdslb.com/bfs/live/e2ba16f947a23179cdc00420b71cc1d627d8ae25.png");
			addItem("emoji_225", "[大笑]", "http://i0.hdslb.com/bfs/live/e2589d086df0db8a7b5ca2b1273c02d31d4433d4.png");
			addItem("emoji_226", "[惊喜]", "http://i0.hdslb.com/bfs/live/9c75761c5b6e1ff59b29577deb8e6ad996b86bd7.png");
			addItem("emoji_227", "[傲娇]", "http://i0.hdslb.com/bfs/live/b5b44f099059a1bafb2c2722cfe9a6f62c1dc531.png");
			addItem("emoji_228", "[疼]", "http://i0.hdslb.com/bfs/live/492b10d03545b7863919033db7d1ae3ef342df2f.png");
			addItem("emoji_229", "[吓]", "http://i0.hdslb.com/bfs/live/c6bed64ffb78c97c93a83fbd22f6fdf951400f31.png");
			addItem("emoji_230", "[阴险]", "http://i0.hdslb.com/bfs/live/a4df45c035b0ca0c58f162b5fb5058cf273d0d09.png");
			addItem("emoji_232", "[惊讶]", "http://i0.hdslb.com/bfs/live/bc26f29f62340091737c82109b8b91f32e6675ad.png");
			addItem("emoji_233", "[生病]", "http://i0.hdslb.com/bfs/live/84c92239591e5ece0f986c75a39050a5c61c803c.png");
			addItem("emoji_234", "[嘘]", "http://i0.hdslb.com/bfs/live/b6226219384befa5da1d437cb2ff4ba06c303844.png");
			addItem("emoji_235", "[奸笑]", "http://i0.hdslb.com/bfs/live/5935e6a4103d024955f749d428311f39e120a58a.png");
			addItem("emoji_236", "[囧]", "http://i0.hdslb.com/bfs/live/204413d3cf330e122230dcc99d29056f2a60e6f2.png");
			addItem("emoji_237", "[捂脸2]", "http://i0.hdslb.com/bfs/live/a2ad0cc7e390a303f6d243821479452d31902a5f.png");
			addItem("emoji_238", "[出窍]", "http://i0.hdslb.com/bfs/live/bb8e95fa54512ffea07023ea4f2abee4a163e7a0.png");
			addItem("emoji_239", "[吐了啊]", "http://i0.hdslb.com/bfs/live/2b6b4cc33be42c3257dc1f6ef3a39d666b6b4b1a.png");
			addItem("emoji_240", "[鼻子]", "http://i0.hdslb.com/bfs/live/f4ed20a70d0cb85a22c0c59c628aedfe30566b37.png");
			addItem("emoji_241", "[调皮]", "http://i0.hdslb.com/bfs/live/84fe12ecde5d3875e1090d83ac9027cb7d7fba9f.png");
			addItem("emoji_242", "[酸]", "http://i0.hdslb.com/bfs/live/98fd92c6115b0d305f544b209c78ec322e4bb4ff.png");
			addItem("emoji_243", "[冷]", "http://i0.hdslb.com/bfs/live/b804118a1bdb8f3bec67d9b108d5ade6e3aa93a9.png");
			addItem("emoji_244", "[OK]", "http://i0.hdslb.com/bfs/live/86268b09e35fbe4215815a28ef3cf25ec71c124f.png");
			addItem("emoji_245", "[微笑]", "http://i0.hdslb.com/bfs/live/f605dd8229fa0115e57d2f16cb019da28545452b.png");
			addItem("emoji_246", "[藏狐]", "http://i0.hdslb.com/bfs/live/05ef7849e7313e9c32887df922613a7c1ad27f12.png");
			addItem("emoji_247", "[龇牙]", "http://i0.hdslb.com/bfs/live/8b99266ea7b9e86cf9d25c3d1151d80c5ba5c9a1.png");
			addItem("emoji_248", "[防护]", "http://i0.hdslb.com/bfs/live/17435e60dcc28ce306762103a2a646046ff10b0a.png");
			addItem("emoji_249", "[笑]", "http://i0.hdslb.com/bfs/live/a91a27f83c38b5576f4cd08d4e11a2880de78918.png");
			addItem("emoji_250", "[一般]", "http://i0.hdslb.com/bfs/live/8d436de0c3701d87e4ca9c1be01c01b199ac198e.png");
			addItem("emoji_251", "[嫌弃]", "http://i0.hdslb.com/bfs/live/c409425ba1ad2c6534f0df7de350ba83a9c949e5.png");
			addItem("emoji_252", "[无语]", "http://i0.hdslb.com/bfs/live/4781a77be9c8f0d4658274eb4e3012c47a159f23.png");
			addItem("emoji_253", "[哈欠]", "http://i0.hdslb.com/bfs/live/6e496946725cd66e7ff1b53021bf1cc0fc240288.png");
			addItem("emoji_254", "[可怜]", "http://i0.hdslb.com/bfs/live/8e88e6a137463703e96d4f27629f878efa323456.png");
			addItem("emoji_255", "[歪嘴笑]", "http://i0.hdslb.com/bfs/live/bea1f0497888f3e9056d3ce14ba452885a485c02.png");
			addItem("emoji_256", "[亲亲]", "http://i0.hdslb.com/bfs/live/10662d9c0d6ddb3203ecf50e77788b959d4d1928.png");
			addItem("emoji_257", "[问号]", "http://i0.hdslb.com/bfs/live/a0c456b6d9e3187399327828a9783901323bfdb5.png");
			addItem("emoji_258", "[波吉]", "http://i0.hdslb.com/bfs/live/57dee478868ed9f1ce3cf25a36bc50bde489c404.png");
			addItem("emoji_259", "[OH]", "http://i0.hdslb.com/bfs/live/0d5123cddf389302df6f605087189fd10919dc3c.png");
			addItem("emoji_260", "[再见]", "http://i0.hdslb.com/bfs/live/f408e2af700adcc2baeca15510ef620bed8d4c43.png");
			addItem("emoji_261", "[白眼]", "http://i0.hdslb.com/bfs/live/7fa907ae85fa6327a0466e123aee1ac32d7c85f7.png");
			addItem("emoji_262", "[鼓掌]", "http://i0.hdslb.com/bfs/live/d581d0bc30c8f9712b46ec02303579840c72c42d.png");
			addItem("emoji_263", "[大哭]", "http://i0.hdslb.com/bfs/live/816402551e6ce30d08b37a917f76dea8851fe529.png");
			addItem("emoji_264", "[呆]", "http://i0.hdslb.com/bfs/live/179c7e2d232cd74f30b672e12fc728f8f62be9ec.png");
			addItem("emoji_265", "[流汗]", "http://i0.hdslb.com/bfs/live/b00e2e02904096377061ec5f93bf0dd3321f1964.png");
			addItem("emoji_266", "[生气]", "http://i0.hdslb.com/bfs/live/2c69dad2e5c0f72f01b92746bc9d148aee1993b2.png");
			addItem("emoji_267", "[加油]", "http://i0.hdslb.com/bfs/live/fbc3c8bc4152a65bbf4a9fd5a5d27710fbff2119.png");
			addItem("emoji_268", "[害羞]", "http://i0.hdslb.com/bfs/live/d8ce9b05c0e40cec61a15ba1979c8517edd270bf.png");
			addItem("emoji_269", "[虎年]", "http://i0.hdslb.com/bfs/live/a51af0d7d9e60ce24f139c468a3853f9ba9bb184.png");
			addItem("emoji_270", "[doge2]", "http://i0.hdslb.com/bfs/live/f547cc853cf43e70f1e39095d9b3b5ac1bf70a8d.png");
			addItem("emoji_271", "[金钱豹]", "http://i0.hdslb.com/bfs/live/b6e8131897a9a718ee280f2510bfa92f1d84429b.png");
			addItem("emoji_272", "[瓜子]", "http://i0.hdslb.com/bfs/live/fd35718ac5a278fd05fe5287ebd41de40a59259d.png");
			addItem("emoji_273", "[墨镜]", "http://i0.hdslb.com/bfs/live/5e01c237642c8b662a69e21b8e0fbe6e7dbc2aa1.png");
			addItem("emoji_274", "[难过]", "http://i0.hdslb.com/bfs/live/5776481e380648c0fb3d4ad6173475f69f1ce149.png");
			addItem("emoji_275", "[抱抱]", "http://i0.hdslb.com/bfs/live/abddb0b621b389fc8c2322b1cfcf122d8936ba91.png");
			addItem("emoji_276", "[跪了]", "http://i0.hdslb.com/bfs/live/4f2155b108047d60c1fa9dccdc4d7abba18379a0.png");
			addItem("emoji_277", "[摊手]", "http://i0.hdslb.com/bfs/live/1e0a2baf088a34d56e2cc226b2de36a5f8d6c926.png");
			addItem("emoji_278", "[热]", "http://i0.hdslb.com/bfs/live/6df760280b17a6cbac8c1874d357298f982ba4cf.png");
			addItem("emoji_279", "[三星堆]", "http://i0.hdslb.com/bfs/live/0a1ab3f0f2f2e29de35c702ac1ecfec7f90e325d.png");
			addItem("emoji_280", "[鼠]", "http://i0.hdslb.com/bfs/live/98f842994035505c728e32e32045d649e371ecd6.png");
			addItem("emoji_281", "[汤圆]", "http://i0.hdslb.com/bfs/live/23ae12d3a71b9d7a22c8773343969fcbb94b20d0.png");
			addItem("emoji_282", "[泼水]", "http://i0.hdslb.com/bfs/live/29533893115c4609a4af336f49060ea13173ca78.png");
			addItem("emoji_283", "[鬼魂]", "http://i0.hdslb.com/bfs/live/5d86d55ba9a2f99856b523d8311cf75cfdcccdbc.png");
			addItem("emoji_284", "[不行]", "http://i0.hdslb.com/bfs/live/607f74ccf5eec7d2b17d91b9bb36be61a5dd196b.png");
			addItem("emoji_285", "[响指]", "http://i0.hdslb.com/bfs/live/3b2fedf09b0ac79679b5a47f5eb3e8a38e702387.png");
			addItem("emoji_286", "[牛]", "http://i0.hdslb.com/bfs/live/5e61223561203c50340b4c9b41ba7e4b05e48ae2.png");
			addItem("emoji_287", "[保佑]", "http://i0.hdslb.com/bfs/live/241b13adb4933e38b7ea6f5204e0648725e76fbf.png");
			addItem("emoji_288", "[抱拳]", "http://i0.hdslb.com/bfs/live/3f170894dd08827ee293afcb5a3d2b60aecdb5b1.png");
			addItem("emoji_289", "[给力]", "http://i0.hdslb.com/bfs/live/d1ba5f4c54332a21ed2ca0dcecaedd2add587839.png");
			addItem("emoji_290", "[耶]", "http://i0.hdslb.com/bfs/live/eb2d84ba623e2335a48f73fb5bef87bcf53c1239.png");

			addItem("[点赞图标]", "[点赞图标]", "https://i0.hdslb.com/bfs/live/23678e3d90402bea6a65251b3e728044c21b1f0f.png");

			void addItem(string id, string name, string uri)
			{
				var emoji = new Dictionary<string, string>();
				emoji.Add("id", id);
				emoji.Add("name", name);
				emoji.Add("url", uri);
				bilibiliEmoticons[name] = emoji;
			}
		}
	}
}
