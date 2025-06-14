using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using ChatCore.Config;
using ChatCore.Interfaces;
using ChatCore.Models;
using ChatCore.Utilities;

namespace ChatCore.Services
{
	public class MainSettingsProvider
	{
		 internal const int WEB_APP_PORT = 8338;
		//internal const int WEB_APP_PORT = 18338;

		[ConfigSection("WebApp")]
		[HtmlIgnore]
		[ConfigMeta(Comment = "Set to true to disable the webapp entirely.")]
		public bool DisableWebApp = false;
		[ConfigMeta(Comment = "Whether or not to launch the webapp in your default browser when ChatCore is started.")]
		public bool LaunchWebAppOnStartup = true;
		[ConfigMeta(Comment = "The language you want to use.")]
		public string Language = "";

		[ConfigSection("Global")]
		[ConfigMeta(Comment = "When enabled, emojis will be parsed.")]
		public bool ParseEmojis = true;
		[ConfigMeta(Comment = "When enabled, Twitch will enable.")]
		public bool EnableTwitch = false;
		[ConfigMeta(Comment = "When enabled, Bilibili will enable.")]
		public bool EnableBilibili = true;

		[ConfigSection("Twitch")]
		[ConfigMeta(Comment = "When enabled, BetterTwitchTV emotes will be parsed.")]
		// ReSharper disable once InconsistentNaming
		public bool ParseBTTVEmotes = true;
		[ConfigMeta(Comment = "When enabled, FrankerFaceZ emotes will be parsed.")]
		// ReSharper disable once InconsistentNaming
		public bool ParseFFZEmotes = true;
		[ConfigMeta(Comment = "When enabled, Twitch emotes will be parsed.")]
		public bool ParseTwitchEmotes = true;
		[ConfigMeta(Comment = "When enabled, Twitch cheermotes will be parsed.")]
		public bool ParseCheermotes = true;

		[ConfigSection("Bilibili")]
		[ConfigMeta(Comment = "Which method/service to connect to the danmuku server. (Legacy(no login required)/OpenBLive(requre identity code)/Default(require cookies))")]
		public string danmuku_service_method = "Default";
		[ConfigMeta(Comment = "When enabled, Danmuku Msg will be parsed.")]
		public bool danmuku_danmuku = true;
		[ConfigMeta(Comment = "When enabled, Super Chat Msg will be parsed.")]
		public bool danmuku_superchat = true;
		[ConfigMeta(Comment = "When enabled, users' avatar will be parsed.")]
		public bool danmuku_avatar = true;
		[ConfigMeta(Comment = "When enabled, Users' badge will be parsed.")]
		public bool danmuku_badge_prefix = true;
		[ConfigMeta(Comment = "When enabled, users' badge will be parsed as Icon, otherwise, it will be text.")]
		public bool danmuku_badge_prefix_type = true;
		[ConfigMeta(Comment = "When enabled, Users' Honor badge will be parsed.")]
		public bool danmuku_honor_badge_prefix = true;
		[ConfigMeta(Comment = "When enabled, users' Honor badge will be parsed as Icon, otherwise, it will be text.")]
		public bool danmuku_honor_badge_prefix_type = true;
		[ConfigMeta(Comment = "When enabled, broadcaster prefix will be parsed.")]
		public bool danmuku_broadcaster_prefix = true;
		[ConfigMeta(Comment = "When enabled, broadcaster prefix will be parsed as Icon, otherwise, it will be text.")]
		public bool danmuku_broadcaster_prefix_type = true;
		[ConfigMeta(Comment = "When enabled, moderator prefix will be parsed.")]
		public bool danmuku_moderator_prefix = true;
		[ConfigMeta(Comment = "When enabled, moderator prefix will be parsed as Icon, otherwise, it will be text.")]
		public bool danmuku_moderator_prefix_type = true;
		[ConfigMeta(Comment = "When enabled, Gift Msg will be parsed.")]
		public bool danmuku_gift = true;
		[ConfigMeta(Comment = "When enabled, Gift Combo Msg will be parsed.")]
		public bool danmuku_gift_combo = false;
		[ConfigMeta(Comment = "When enabled, Gift Msg will be delayed, and combined msg will displayed.")]
		public bool danmuku_gift_combine = true;
		[ConfigMeta(Comment = "When enabled, Gift Star Msg will be parsed.")]
		public bool danmuku_gift_star = true;
		[ConfigMeta(Comment = "When enabled, Enter Room Msg will be parsed.")]
		public bool danmuku_interaction_enter = true;
		[ConfigMeta(Comment = "When enabled, Follow Msg will be parsed.")]
		public bool danmuku_interaction_follow = true;
		[ConfigMeta(Comment = "When enabled, Share Msg will be parsed.")]
		public bool danmuku_interaction_share = true;
		[ConfigMeta(Comment = "When enabled, Special Follow Msg will be parsed.")]
		public bool danmuku_interaction_special_follow = false;
		[ConfigMeta(Comment = "When enabled, Mutual Follow Msg will be parsed.")]
		public bool danmuku_interaction_mutual_follow = false;
		[ConfigMeta(Comment = "When enabled, Guard Enter Msg will be parsed.")]
		public bool danmuku_interaction_guard_enter = true;
		[ConfigMeta(Comment = "When enabled, Effect Msg will be parsed.")]
		public bool danmuku_interaction_effect = false;
		[ConfigMeta(Comment = "When enabled, Anchor Msg will be parsed.")]
		public bool danmuku_interaction_anchor = false;
		[ConfigMeta(Comment = "When enabled, Raffle Msg will be parsed.")]
		public bool danmuku_interaction_raffle = false;
		[ConfigMeta(Comment = "When enabled, Red Packet Msg will be parsed.")]
		public bool danmuku_interaction_red_packet = false;
		[ConfigMeta(Comment = "When enabled, New Guard Msg will be parsed.")]
		public bool danmuku_new_guard = true;
		[ConfigMeta(Comment = "When enabled, New Guard Details Msg will be parsed.")]
		public bool danmuku_new_guard_msg = false;
		[ConfigMeta(Comment = "When enabled, Guard Msg will be parsed.")]
		public bool danmuku_guard_msg = false;
		[ConfigMeta(Comment = "When enabled, Guard Lottery Msg will be parsed.")]
		public bool danmuku_guard_lottery = false;
		[ConfigMeta(Comment = "When enabled, users' guard prefix will be parsed.")]
		public bool danmuku_guard_prefix = true;
		[ConfigMeta(Comment = "When enabled, users' guard prefix will be parsed as Icon, otherwise, it will be text.")]
		public bool danmuku_guard_prefix_type = true;
		[ConfigMeta(Comment = "When enabled, Block List Msg will be parsed.")]
		public bool danmuku_notification_block_list = false;
		[ConfigMeta(Comment = "When enabled, Room Info Change Msg will be parsed.")]
		public bool danmuku_notification_room_info_change = false;
		[ConfigMeta(Comment = "When enabled, Room Preparing Msg will be parsed.")]
		public bool danmuku_notification_room_prepare = false;
		[ConfigMeta(Comment = "When enabled, Room Online Msg will be parsed.")]
		public bool danmuku_notification_room_online = false;
		[ConfigMeta(Comment = "When enabled, Room Rank Msg will be parsed.")]
		public bool danmuku_notification_room_rank = false;
		[ConfigMeta(Comment = "When enabled, Like Msg will be parsed.")]
		public bool danmuku_notification_like = true;
		[ConfigMeta(Comment = "When enabled, Boardcast Msg will be parsed.")]
		public bool danmuku_notification_boardcast = false;
		[ConfigMeta(Comment = "When enabled, Junk Msg will be parsed.")]
		public bool danmuku_notification_junk = false;
		[ConfigMeta(Comment = "When enabled, PK Msg will be parsed.")]
		public bool danmuku_notification_pk = false;
		[ConfigMeta(Comment = "User with keyword in the list in their username will be blocked.")]
		public string bilibili_block_list_username = "[]";
		[ConfigMeta(Comment = "User with UID will be blocked.")]
		public string bilibili_block_list_uid = "[]";
		[ConfigMeta(Comment = "Message contains the  keyword in the list will be blocked.")]
		public string bilibili_block_list_keyword = "[]";

		[ConfigSection("StreamingOverlay")]
		[ConfigMeta(Comment = "When enabled, an init welcome message will be shown.")]
		public bool overlay_show_init_welcome = true;
		[ConfigMeta(Comment = "When enabled, the username of messages will be shown.")]
		public bool overlay_show_username = true;
		[ConfigMeta(Comment = "When enabled, the gift type messages will be shown in SuperChat style.")]
		public bool overlay_show_gift_in_sc = true;
		[ConfigMeta(Comment = "When enabled, the guard type messages will be shown in SuperChat style.")]
		public bool overlay_show_guard_in_sc = true;

		[ConfigSection("TextToSpeach")]
		[ConfigMeta(Comment = "When enabled, Text-To-Speach Engine will be actived in the streaming overlay.")]
		public bool overlay_tts_enable = false;
		[ConfigMeta(Comment = "Text-To-Speach Engine will use this package when the browser supports it.")]
		public string overlay_tts_voice_package = "";
		[ConfigMeta(Comment = "Text-To-Speach Engine will use this speed to speak. (10 x actual speed)")]
		public int overlay_tts_voice_speed = 10;
		[ConfigMeta(Comment = "Text-To-Speach Engine will use this pitch to speak. (10 x actual pitch)")]
		public int overlay_tts_voice_pitch = 10;

		private readonly IPathProvider _pathProvider;
		private readonly ObjectSerializer _configSerializer;
		private bool _updateTwitch, _updateBilibili;

		public MainSettingsProvider(IPathProvider pathProvider)
		{
			_pathProvider = pathProvider;
			_configSerializer = new ObjectSerializer();

			var path = Path.Combine(_pathProvider.GetDataPath(), "settings.ini");
			_configSerializer.Load(this, path);
			_configSerializer.Save(this, path);
		}

		public void Save()
		{
			_configSerializer.Save(this, Path.Combine(_pathProvider.GetDataPath(), "settings.ini"));

		}

		public void Reload() {
			var path = Path.Combine(_pathProvider.GetDataPath(), "settings.ini");
			_configSerializer.Load(this, path);
		}

		public JSONObject GetSettingsAsJson()
		{
			return _configSerializer.GetSettingsAsJson(this);
		}

		public void SetFromDictionary(JSONObject postData)
		{
			bool _temp_twitch = EnableTwitch, _temp_bilibili = EnableBilibili;
			_configSerializer.SetFromDictionary(this, postData);
			_updateTwitch = EnableTwitch == _temp_twitch;
			_updateBilibili = EnableBilibili == _temp_bilibili;
		}

		public event Action<bool>? onTwitchUpdate, onBilibiliUpdate;

		public void updateTwitch(bool enable)
		{
			onTwitchUpdate?.Invoke(enable);
		}

		public void updateBilibili(bool enable)
		{
			onBilibiliUpdate?.Invoke(enable);
		}

		public void OpenConfigDirectory()
		{
			Process.Start("explorer.exe", _pathProvider.GetDataPath());
		}
	}
}
