using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ChatCore.Interfaces;
using ChatCore.Services;
using ChatCore.Services.Bilibili;
using ChatCore.Utilities;
using Microsoft.Extensions.Logging;

namespace ChatCore.Models.Bilibili
{
	public class BilibiliChatMessage : IChatMessage
	{
		public string Id { get; internal set; } = "";
		public bool IsSystemMessage { get; internal set; }
		public bool IsActionMessage { get; internal set; }
		public bool IsHighlighted { get; internal set; }
		public bool IsPing { get; internal set; }
		public string Message { get; internal set; } = "";
		public string Username { get; internal set; } = "";
		public string Uid { get; internal set; } = "-1";
		public string Content { get; internal set; } = "";
		public IChatUser Sender { get; internal set; } = new BilibiliChatUser();
		public IChatChannel Channel { get; internal set; } = new BilibiliChatChannel();
		public IChatEmote[] Emotes { get; internal set; } = Array.Empty<IChatEmote>();
		public ReadOnlyDictionary<string, string> Metadata { get; internal set; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
		public string MessageType { get; private set; } = "";
		public string Color { get; internal set; } = "#FFFFFF";
		public BilibiliChatMessageExtra extra { get; internal set; } = new BilibiliChatMessageExtraDanmuku();
		private static readonly Dictionary<string, Action<BilibiliChatMessage, JSONNode>> commands = new Dictionary<string, Action<BilibiliChatMessage, JSONNode>>();
		// private static Dictionary<string, dynamic> gift = new Dictionary<string, dynamic>();

		public string service_method { get; internal set; }

		static BilibiliChatMessage()
		{
			CreateCommands();
		}
		public BilibiliChatMessage(string json, string service_method = "Legacy")
		{
			var obj = JSON.Parse(json);
			this.service_method = service_method;
			if (obj == null)
			{
				return;
			}
			Id = Guid.NewGuid().ToString();
			IsSystemMessage = false;
			IsActionMessage = false;
			IsHighlighted = false;
			IsPing = false;
			//obj["room_id"] = new JSONNumber(_room_id);
			//Console.WriteLine("[DEBUG] | [RAW] " + json);
			CreateMessage(JSON.Parse(json));
		}
		public JSONObject ToJson()
		{
			var obj = new JSONObject();
			obj.Add(nameof(Id), new JSONString(Id));
			obj.Add(nameof(IsSystemMessage), new JSONBool(IsSystemMessage));
			obj.Add(nameof(IsActionMessage), new JSONBool(IsActionMessage));
			obj.Add(nameof(IsActionMessage), new JSONBool(IsActionMessage));
			obj.Add(nameof(IsHighlighted), new JSONBool(IsHighlighted));
			obj.Add(nameof(IsPing), new JSONBool(IsPing));
			obj.Add(nameof(Message), new JSONString(Message));
			obj.Add(nameof(Sender), Sender?.ToJson());
			obj.Add(nameof(Channel), Channel?.ToJson());
			var emotes = new JSONArray();
			foreach (var emote in Emotes)
			{
				emotes.Add(emote.ToJson());
			}
			obj.Add(nameof(Emotes), emotes);
			return obj;
		}

		private static void CreateCommands()
		{
			Action<BilibiliChatMessage, JSONNode> danmuku_action = (b, danmuku) => {
				var info = danmuku["info"].AsArray!;
				if (int.Parse(info[0][9].Value) > 0)
				{
					b.MessageType = "ignore";
				}
				else
				{
					var isEmotion = int.Parse(info[0][12].Value) == 1;
					// 处理 extra 字段，增加空值检查
					JSONNode extra = null;
					try
					{
						if (info[0].Count > 15 && info[0][15] != null && info[0][15]["extra"] != null)
						{
							extra = JSON.Parse(info[0][15]["extra"].Value);
						}
					}
					catch { }
					
					b.MessageType = isEmotion ? "danmuku_motion" : "danmuku";
					b.Uid = info[2][0].ToString();
					b.Username = info[2][1].Value;
					b.Content = (isEmotion ? "[表情]" : "") + info[1].Value.ToString();
					
					// 处理颜色，增加空值检查
					if (extra != null && extra["color"] != null)
					{
						try
						{
							b.Color = "#" + int.Parse(extra["color"]).ToString("X");
						}
						catch
						{
							b.Color = "#FFFFFF"; // 默认白色
						}
					}
					else
					{
						b.Color = "#FFFFFF"; // 默认白色
					}
					
					b.Message = info[1].Value;
					// b.Message = (isEmotion ? "[表情]" : "") + info[1].Value;
					var Sender = new BilibiliChatUser();
					Sender.SetUid(b.Uid);
					Sender.SetUserName(b.Username);
					Sender.SetIsModerator(info[2][2].AsInt);
					Sender.SetGuardLevel(info[7].AsInt);
					
					// 处理 HonorLevel，增加边界检查
					if (info.Count > 16 && info[16] != null && info[16].Count > 0)
					{
						Sender.SetHonorLevel(info[16][0].AsInt);
					}
					else
					{
						Sender.SetHonorLevel(0); // 默认值
					}
					var MedalNull = (info[3].AsArray!).Count;
					if (MedalNull == 0)
					{
						Sender.SetMedal();
					}
					else
					{
						Sender.SetMedal(info[3][0].AsInt, info[3][1].Value.ToString(), true, new string[] { info[3][9].Value.ToString(), info[3][8].Value.ToString(), info[3][7].Value.ToString() }, info[3][10].AsInt, info[3][3].AsInt, info[3][12].AsInt);
					}
					Sender.UpdateDisplayName(true);
					b.Sender = Sender;

					//b.Channel = new BilibiliChatChannel(danmuku);
					if (isEmotion)
					{
						var extraMsg = new BilibiliChatMessageExtraEmotionDanmuku();
						extraMsg.raw_msg = info[1].Value.ToString();
						extraMsg.emoticon_id = info[0][13]["emoticon_unique"].Value.ToString();
						extraMsg.emoticon_name = extraMsg.raw_msg;
						extraMsg.emoticon_img = info[0][13]["url"].Value.ToString();
						b.extra = extraMsg;
						b.Emotes = new IChatEmote[] { new BilibiliChatEmote(extraMsg.emoticon_id, extraMsg.emoticon_name, extraMsg.emoticon_img) };
					}
					else
					{
						var emote_list = new List<IChatEmote>();
						BilibiliChatMessageExtra extraMsg = new BilibiliChatMessageExtraDanmuku();
						if (extra != null && extra["emots"] != null)
						{
							extraMsg = new BilibiliChatMessageExtraEmotionDanmuku();
							foreach (var emote in extra["emots"])
							{
								var target = new Regex(Regex.Escape((emote.Value)["emoji"].Value.ToString()), RegexOptions.Compiled);
								Match match = target.Match(b.Message);
								while (match.Success)
								{
									emote_list.Add(new BilibiliChatEmote((emote.Value)["emoticon_id"].Value.ToString(), (emote.Value)["emoji"].Value.ToString(), (emote.Value)["url"].Value.ToString(), false, match.Index));
									match = match.NextMatch();
								}
							};
							emote_list.Sort((a, b) => b.StartIndex - a.StartIndex);
						}
						if (extraMsg is BilibiliChatMessageExtraDanmuku danmuku1)
						{
							danmuku1.raw_msg = info[1].Value.ToString();
						}
						else
						{
							((BilibiliChatMessageExtraEmotionDanmuku)extraMsg).raw_msg = info[1].Value.ToString();
						}

						b.extra = extraMsg;
						b.Emotes = emote_list.ToArray();
					}
				}
			};
			commands.Add("DANMU_MSG", danmuku_action);
			commands.Add("DANMU_MSG:4:0:2:2:2:0", danmuku_action);
			commands.Add("DANMU_AGGREGATION", (b, danmuku) => {
				b.MessageType = "ignore";
			});
			commands.Add("SEND_GIFT", (b, danmuku) => {
				/*b.MessageType = "wait";
				b.Content = "";
				var data = danmuku["data"].AsObject!;

				var key = data["uid"].Value.ToString() + data["giftId"].Value.ToString();
				if (gift.TryGetValue(key, out var gift_info) && gift_info[0] > 0)
				{
					gift_info[0] = 3;
					gift_info[1] += data["num"].AsInt;
				}
				else
				{
					gift.Add(key, new int[] { 3, data["num"].AsInt });
				}

				Task.Run(() => {
					var count = gift[key][0];
					var gift_count = gift[key][1];
					if (count > 0 && gift_count == gift[key][1])
					{
						Thread.Sleep(1000);
						gift[key][0]--;
					}
					else if (count == 0)
					{
						var info = new JSONArray();
						info[2] = new JSONObject();
						info[7] = new JSONNumber(data["medal_info"]["guard_level"].AsInt);

						info[2][0] = new JSONNumber(b.Uid);
						info[2][1] = new JSONString(b.Username);
						info[2][2] = new JSONNumber(0);
						info[2][7] = new JSONString("");
						info[2][3] = new JSONArray();
						info[2][3][0] = new JSONNumber(data["medal_info"]["medal_level"].AsInt);
						info[2][3][1] = new JSONNumber(data["medal_info"]["medal_name"].Value);

						b.Uid = data["uid"].AsInt;
						b.Username = data["uname"].Value;
						b.MessageType = "gift";
						b.Content = "";
						b.IsHighlighted = true;

						*//*if (string.IsNullOrEmpty(data["combo_num"].Value))
						{*//*
						b.Message = data["action"].Value + gift[key][1] + "个" + data["giftName"].Value;
						*//*}
						else
						{
							b.Message = data["action"].Value + data["num"].Value + "个" + data["giftName"].Value + " x" + data["combo_num"].Value;
						}*//*
						b.Sender = new BilibiliChatUser(info, danmuku["room_id"]);
						gift.Remove(key);
					}
				});*/
				var data = danmuku["data"].AsObject!;
				// Console.WriteLine(data["uid"].GetType().ToString());
				b.Uid = data["uid"].ToString();
				b.Username = data["uname"].Value;
				b.MessageType = "gift";
				b.Content = "";
				b.IsHighlighted = true;

				var Sender = new BilibiliChatUser();
				Sender.SetUid(b.Uid);
				Sender.SetUserName(b.Username);
				Sender.SetGuardLevel(data["guard_level"].AsInt);
				Sender.SetMedal(data["medal_info"]["medal_level"].AsInt, data["medal_info"]["medal_name"].Value, true, new string[] { data["medal_info"]["medal_color_start"].Value.ToString(), data["medal_info"]["medal_color_end"].Value.ToString(), data["medal_info"]["medal_color_border"].Value.ToString() }, data["medal_info"]["guard_level"].AsInt, data["medal_info"]["anchor_roomid"].AsInt, data["medal_info"]["target_id"]);
				Sender.UpdateDisplayName(true);
				b.Sender = Sender;

				var extra = new BilibiliChatMessageExtraGift();
				extra.gift_id = data["giftId"].Value.ToString();
				extra.gift_action = data["action"].Value.ToString();
				extra.gift_num = data["num"].AsInt;
				extra.gift_name = data["giftName"].Value.ToString();
				extra.origin_gift = data["blind_gift"].IsNull ? "" : data["blind_gift"]["original_gift_name"].Value.ToString();
				extra.gift_type = BilibiliService.bilibiliGiftCoinType[extra.gift_id];
				extra.gift_price = (double)BilibiliService.bilibiliGiftPrice[extra.gift_id] * (double)(data["num"].AsInt);
				extra.gift_img = BilibiliService.bilibiliGiftInfo[extra.gift_id];
				b.extra = extra;
				var MessagePrice = "(" + (extra.gift_type == "silver" ? "免￥" : "￥") + string.Format("{0:0.0}", extra.gift_price) + ")";
				var GiftPlacholder = $"%GIFT_{data["giftId"].Value}%";

				if (data["blind_gift"].IsNull)
				{
					b.Message = data["action"].Value + data["num"].Value + "个" + data["giftName"].Value + GiftPlacholder + MessagePrice;
					b.Content = data["action"].Value + data["num"].Value + "个" + data["giftName"].Value;
				}
				else
				{
					b.Message = data["action"].Value + data["num"].Value + "个" + data["giftName"].Value + GiftPlacholder + MessagePrice + $"(来自{extra.origin_gift})";
					b.Content = data["action"].Value + data["num"].Value + "个" + data["giftName"].Value + $"(来自{extra.origin_gift})";
				}

				var emote_list = new List<IChatEmote>();
				var target = new Regex(GiftPlacholder, RegexOptions.Compiled);
				Match match = target.Match(b.Message);
				while (match.Success)
				{
					emote_list.Add(new BilibiliChatEmote(GiftPlacholder, GiftPlacholder, extra.gift_img, true, match.Index));
					match = match.NextMatch();
				}
				b.Emotes = emote_list.ToArray();
			});
			commands.Add("COMBO_END", (b, danmuku) => {
				b.MessageType = "ignore";
				/*b.MessageType = "combo_end";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].AsInt;
				b.Username = data["uname"].Value;
				b.Content = "";
				b.IsHighlighted = true;
				var Sender = new BilibiliChatUser();
				Sender.SetUid(b.Uid, danmuku["room_id"]);
				Sender.SetUserName(b.Username);
				Sender.SetMedal(data["medal_info"]["medal_level"].AsInt, data["medal_info"]["medal_name"].Value, true, data["medal_info"]["medal_color"].Value.ToString(), data["medal_info"]["guard_level"].AsInt, 0, data["medal_info"]["target_id"]);
				Sender.UpdateDisplayName();
				b.Sender = Sender;

				b.Message = data["action"].Value + (data["gift_num"].AsInt == 0 ? 1 : data["gift_num"].AsInt) + "个" + data["gift_name"].Value + " x" + data["combo_num"].Value;
				b.Sender = new BilibiliChatUser();

				b.extra.Add("id", data["giftId"].Value.ToString());
				b.extra.Add("num", data["num"].AsInt == 0 ? 1 : data["num"].AsInt);
				b.extra.Add("total_num", data["total_num"].AsInt);
				b.extra.Add("gift_name", data["giftName"].Value.ToString());
				b.extra.Add("type", BilibiliService.bilibiliGiftCoinType[data["giftId"].Value.ToString()]);
				b.extra.Add("price", BilibiliService.bilibiliGiftPrice[data["giftId"].Value.ToString()] * data["total_num"].AsInt);
				b.extra.Add("img", BilibiliService.bilibiliGiftInfo[data["giftId"].Value.ToString()]);*/
			});
			commands.Add("COMBO_SEND", (b, danmuku) => {
				b.MessageType = "ignore";
				/*b.MessageType = "combo_send";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].AsInt;
				b.Username = data["uname"].Value;
				b.Content = "";
				b.IsHighlighted = true;
				var info = new JSONArray();
				info[2] = new JSONObject();
				info[7] = new JSONNumber(data["medal_info"]["guard_level"].AsInt);

				info[2][0] = new JSONNumber(b.Uid);
				info[2][1] = new JSONString(b.Username);
				info[2][2] = new JSONNumber(0);
				info[2][7] = new JSONString("");
				info[2][3] = new JSONArray();
				info[2][3][0] = new JSONNumber(data["medal_info"]["medal_level"].AsInt);
				info[2][3][1] = new JSONNumber(data["medal_info"]["medal_name"].Value);

				b.Message = data["action"].Value + (data["gift_num"].AsInt == 0 ? 1 : data["gift_num"].AsInt) + "个" + data["gift_name"].Value + " x" + data["combo_num"];
				b.Sender = new BilibiliChatUser(info, danmuku["room_id"]);

				b.extra.Add("id", data["giftId"].Value.ToString());
				b.extra.Add("num", data["num"].AsInt == 0 ? 1 : data["num"].AsInt);
				b.extra.Add("total_num", data["total_num"].AsInt);
				b.extra.Add("gift_name", data["giftName"].Value.ToString());
				b.extra.Add("type", BilibiliService.bilibiliGiftCoinType[data["giftId"].Value.ToString()]);
				b.extra.Add("price", BilibiliService.bilibiliGiftPrice[data["giftId"].Value.ToString()] * data["total_num"].AsInt);
				b.extra.Add("img", BilibiliService.bilibiliGiftInfo[data["giftId"].Value.ToString()]);*/
			});
			commands.Add("GIFT_STAR_PROCESS", (b, danmuku) => {
				b.MessageType = "gift_star";
				var data = danmuku["data"].AsObject!;

				b.IsSystemMessage = true;

				b.Message = data["tip"].Value;
			});
			commands.Add("SUPER_CHAT_MESSAGE", (b, danmuku) => {
				b.MessageType = "super_chat";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].ToString();
				b.Username = data["user_info"]["uname"].Value;
				b.Content = data["message"].Value;
				b.IsHighlighted = true;

				var Sender = new BilibiliChatUser();
				Sender.SetUid(b.Uid);
				Sender.SetUserName(b.Username);
				Sender.SetIsModerator(data["user_info"]["manager"].AsInt);
				Sender.SetMedal(data["medal_info"]["medal_level"].AsInt, data["medal_info"]["medal_name"].Value, true, new string[] { data["medal_info"]["medal_color_start"].Value.ToString(), data["medal_info"]["medal_color_end"].Value.ToString(), data["medal_info"]["medal_color_border"].Value.ToString() }, data["user_info"]["guard_level"].AsInt, data["medal_info"]["anchor_roomid"].AsInt, data["medal_info"]["target_id"].AsInt);
				Sender.UpdateDisplayName(true);
				b.Sender = Sender;

				b.Message = "【SC (￥" + data["price"].AsInt + ")】" + b.Content;

				var extra = new BilibiliChatMessageExtraSuperChat();
				extra.sc_price = data["price"].Value.ToString();
				extra.sc_time = data["time"].Value.ToString();
				b.extra = extra;
			});
			commands.Add("SUPER_CHAT_MESSAGE_JPN", (b, danmuku) => {
				b.MessageType = "super_chat_japanese";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].ToString();
				b.Username = data["user_info"]["uname"].Value;
				b.Content = data["message_jpn"].Value;
				b.IsHighlighted = true;

				var Sender = new BilibiliChatUser();
				Sender.SetUid(b.Uid);
				Sender.SetUserName(b.Username);
				Sender.SetIsModerator(data["user_info"]["manager"].AsInt);
				Sender.SetMedal(data["medal_info"]["medal_level"].AsInt, data["medal_info"]["medal_name"].Value, true, new string[] { data["medal_info"]["medal_color_start"].Value.ToString(), data["medal_info"]["medal_color_end"].Value.ToString(), data["medal_info"]["medal_color_border"].Value.ToString() }, data["user_info"]["guard_level"].AsInt, data["medal_info"]["anchor_roomid"].AsInt, data["medal_info"]["target_id"].AsInt);
				Sender.UpdateDisplayName(true);
				b.Sender = Sender;

				b.Message = "【SC (JP￥" + data["price"].AsInt + ")】" + b.Content;

				var extra = new BilibiliChatMessageExtraSuperChat();
				extra.sc_price = data["price"].Value.ToString();
				extra.sc_time = data["time"].Value.ToString();
				b.extra = extra;
			});
			commands.Add("WELCOME", (b, danmuku) => {
				b.MessageType = "ignore";
				/*b.MessageType = "welcome";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].AsInt;
				b.Username = data["uname"].Value;
				b.Content = "";
				b.IsSystemMessage = true;

				b.Message = "欢迎老爷 " + b.Username + " 进入直播间";*/
			});
			commands.Add("INTERACT_WORD", (b, danmuku) => {
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].ToString();
				b.Username = data["uname"].Value;
				b.Content = "";
				b.IsSystemMessage = true;

				var Sender = new BilibiliChatUser();
				Sender.SetUid(b.Uid);
				Sender.SetUserName(b.Username);
				//Sender.SetIsModerator(info[2][2].AsInt);
				Sender.SetMedal(data["fans_medal"]["medal_level"].AsInt, data["fans_medal"]["medal_name"].Value, true, new string[] { data["fans_medal"]["medal_color_start"].Value.ToString(), data["fans_medal"]["medal_color_end"].Value.ToString(), data["fans_medal"]["medal_color_border"].Value.ToString() }, data["fans_medal"]["guard_level"].AsInt, data["fans_medal"]["anchor_roomid"].AsInt, data["fans_medal"]["target_id"].AsInt);
				Sender.UpdateDisplayName();
				b.Sender = Sender;

				switch (data["msg_type"].Value.ToString())
				{
					case "1":
						b.MessageType = "welcome";
						b.Message = "欢迎 " + b.Username + " 进入直播间";
						break;
					case "2":
						b.MessageType = "follow";
						b.Message = "感谢 " + b.Username + " 关注直播间";
						break;
					case "3":
						b.MessageType = "share";
						b.Message = "感谢 " + b.Username + " 分享直播间";
						break;
					case "4":
						b.MessageType = "special_follow";
						b.Message = "感谢 " + b.Username + " 特别关注";
						break;
					case "5":
						b.MessageType = "mutual_follow";
						b.Message = "感谢 " + b.Username + " 相互关注";
						break;
					default:
						b.MessageType = "unknown";
						b.Message = "【暂不支持该消息】";
						break;
				}
			});
			commands.Add("WELCOME_GUARD", (b, danmuku) => {
				b.MessageType = "welcome_guard";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].ToString();
				b.Username = data["username"].Value;
				b.Content = "";
				b.IsSystemMessage = true;
				b.IsHighlighted = true;

				b.Message = "欢迎舰长 " + b.Username + " 进入直播间";
			});
			commands.Add("ENTRY_EFFECT", (b, danmuku) => {
				b.MessageType = "effect";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].ToString();
				b.Username = data["copy_writing"].Value.Replace("<%", "").Replace("%>", "");
				b.Content = data["copy_writing"].Value.Replace("<%", "").Replace("%>", "");
				b.IsSystemMessage = true;

				var Sender = new BilibiliChatUser();
				Sender.SetUid(b.Uid);
				Sender.SetUserName(b.Username);
				Sender.SetGuardLevel(data["privilege_type"].AsInt);
				b.Color = data["copy_color"].Value;

				b.Message = b.Content;
			});
			commands.Add("ROOM_RANK", (b, danmuku) => {
				b.MessageType = "global";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【打榜】" + data["rank_desc"].Value;
			});
			commands.Add("ROOM_BANNER", (b, danmuku) => {
				b.MessageType = "global";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【打榜】" + (string.IsNullOrEmpty(data["bls_rank_info"]["rank_info"]["title"].Value)? "小时榜" : data["bls_rank_info"]["rank_info"]["title"].Value + "-" + data["bls_rank_info"]["team_name"].Value) + " 排名: " + data["bls_rank_info"]["rank_info"]["rank_info"]["rank"].Value;
			});
			commands.Add("ACTIVITY_BANNER_UPDATE_V2", (b, danmuku) => {
				b.MessageType = "global";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【横幅】当前分区排名" + data["title"].Value;
			});
			commands.Add("ONLINERANK", (b, danmuku) => {
				b.MessageType = "global";
				var data = danmuku["data"].AsObject!;
				/*var online_rank = data["list"].AsArray;*/
				b.IsSystemMessage = true;

				b.Message = "【在线排名】当前分区排名" + data["title"].Value;
			});
			commands.Add("ROOM_REAL_TIME_MESSAGE_UPDATE", (b, danmuku) => {
				b.MessageType = "global";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【粉丝数】" + data["fans"].Value;
			});
			commands.Add("ONLINE_RANK_COUNT", (b, danmuku) => {
				b.MessageType = "global";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【高能榜】人数: " + data["count"].Value;
			});
			commands.Add("ONLINE_RANK_V2", (b, danmuku) => {
				b.MessageType = "global";
				var data = danmuku["data"].AsObject!;
				var online_rank_list = data["list"].AsArray!;

				b.IsSystemMessage = true;

				b.Message = "【高能榜】";
				for (var i = 0; i < online_rank_list.Count; i++) {
					b.Message += "#" + online_rank_list[i]["rank"].Value + " " + online_rank_list[i]["uname"].Value + "(贡献值: " + online_rank_list[i]["score"].Value + ")";
				}
			});
			commands.Add("ONLINE_RANK_TOP3", (b, danmuku) => {
				b.MessageType = "global";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【高能榜】" + data["list"][0]["msg"].Value.Replace("<%", "").Replace("%>", "");
			});
			commands.Add("NOTICE_MSG", (b, danmuku) => {
				switch (danmuku["id"].Value.ToString()) {
					case "207":
						//上舰跑马灯 msg_type=3
						b.MessageType = "guard_msg";
						b.Message = "【上舰】" + danmuku["msg_self"].Value.Replace("<%", "").Replace("%>", "");
						break;
					case "277":
						// 大乱斗连胜 msg_type=9
						b.MessageType = "pk_notice";
						b.Message = danmuku["msg_self"].Value.Replace("<%", "").Replace("%>", "");
						break;
					default:
						b.MessageType = "junk";
						var data = danmuku["data"].AsObject!;
						b.Message = "【喇叭】" + data["msg_common"].Value;
						break;
				}

				b.IsSystemMessage = true;
			});
			commands.Add("ANCHOR_LOT_START", (b, danmuku) => {
				b.MessageType = "anchor_lot_start";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【天选】天选之子活动开始啦!" + data["require_text"].Value + "赢得" + data["award_name"].Value;
			});
			commands.Add("ANCHOR_LOT_CHECKSTATUS", (b, danmuku) => {
				b.MessageType = "anchor_lot_checkstatus";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【天选】天选之子活动开始啦!";
			});
			commands.Add("ANCHOR_LOT_END", (b, danmuku) => {
				b.MessageType = "anchor_lot_end";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【天选】天选之子活动结束啦!";
			});
			commands.Add("ANCHOR_LOT_AWARD", (b, danmuku) => {
				b.MessageType = "anchor_lot";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;
				var list = data["award_users"].AsArray!;
				var usernameList = "";

				foreach (var item in list)
				{
					usernameList += (item.Value)["uname"].Value.ToString();
				}

				b.Message = "【天选】恭喜" + usernameList + "获得" + data["award_name"].Value;
			});
			commands.Add("POPULARITY_RED_POCKET_START", (b, danmuku) => {
				b.MessageType = "red_pocket_start";
				b.IsSystemMessage = true;
				var data = danmuku["data"].AsObject!;
				b.Uid = data["sender_uid"].ToString();
				b.Username = data["sender_username"].Value;
				var Sender = new BilibiliChatUser();
				Sender.SetUid(b.Uid);
				Sender.SetUserName(b.Username);
				b.Sender = Sender;

				var extra = new BilibiliChatMessageExtraRedPacket();
				extra.gift_price = Math.Round((double)(data["total_price"] / 800), 1);
				extra.gift_img = BilibiliService.bilibiliGiftInfo["13000"];
				b.extra = extra;

				b.Message = $"【红包】{b.Username}正在派发{extra.gift_price}元红包";
			});
			commands.Add("POPULARITY_RED_POCKET_NEW", (b, danmuku) => {
				b.MessageType = "red_pocket_new";
				b.IsSystemMessage = true;
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].ToString();
				b.Username = data["uname"].Value;
				var Sender = new BilibiliChatUser();
				Sender.SetUid(b.Uid);
				Sender.SetUserName(b.Username);
				b.Sender = Sender;

				var extra = new BilibiliChatMessageExtraRedPacket();
				extra.gift_price = Math.Round((double)(data["total_price"] / 800), 1);
				extra.gift_img = BilibiliService.bilibiliGiftInfo["13000"];
				b.extra = extra;

				b.Message = $"【红包】{b.Username}正在派发{extra.gift_price}元红包";
			});
			commands.Add("POPULARITY_RED_POCKET_WINNER_LIST", (b, danmuku) => {
				b.MessageType = "red_pocket_result";
				b.IsSystemMessage = true;
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].ToString();
				b.Username = data["uname"].Value;
				var Sender = new BilibiliChatUser();
				Sender.SetUid(b.Uid);
				Sender.SetUserName(b.Username);
				b.Sender = Sender;

				var list = data["winner_info"].AsArray!;
				var RewardList = data["awards"].AsArray!;
				var UsernameList = "";

				foreach (var user in list)
				{
					UsernameList += $"{(user.Value)[1].Value} 获得了 {RewardList[(user.Value)[3].Value]["award_name"]} ";
				}

				b.Message = $"【红包】中奖结果：{UsernameList.Substring(0, UsernameList.Length - 2)}";
			});
			commands.Add("RAFFLE_START", (b, danmuku) => {
				b.MessageType = "raffle_start";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【抽奖】" + data["title"].Value + "开始啦!";
			});
			commands.Add("ROOM_BLOCK_MSG", (b, danmuku) => {
				b.MessageType = "blocklist";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【封禁】" + data["uname"].Value + "(UID: " + data["uid"].Value + ")";
			});
			commands.Add("GUARD_BUY", (b, danmuku) => {
				b.MessageType = "new_guard";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].ToString();
				b.Username = data["username"].Value;
				b.Content = "";
				b.IsHighlighted = true;

				b.Message = "感谢 " + b.Username + " 成为 " + data["gift_name"].Value + " 加入舰队~";

				var extra = new BilibiliChatMessageExtraNewGuard();
				extra.gift_name = data["gift_name"].Value;
				extra.gift_img = BilibiliService.bilibiliGiftInfo[data["gift_name"].Value.ToString()];
				extra.gift_price = BilibiliService.bilibiliGiftPrice[data["gift_name"].Value.ToString()];
				b.extra = extra;
			});
			commands.Add("USER_TOAST_MSG", (b, danmuku) => {
				b.MessageType = "new_guard_msg";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].ToString();
				b.Username = data["username"].Value;
				b.Content = "";
				b.IsSystemMessage = true;
				b.IsHighlighted = true;

				b.Message = b.Username + " 开通了 " + data["num"].Value + "个" + data["unit"] + "的" + data["role_name"] + " 进入舰队啦";

				var extra = new BilibiliChatMessageExtraNewGuardMsg();
				extra.role_name = data["role_name"].Value;
				extra.num = data["num"].Value.ToString();
				extra.unit = data["unit"].Value.ToString();
				extra.gift_img = BilibiliService.bilibiliGiftInfo[data["role_name"].Value.ToString()];
				extra.gift_price = BilibiliService.bilibiliGiftPrice[data["role_name"].Value.ToString()];
				b.extra = extra;

			});
			commands.Add("GUARD_MSG", (b, danmuku) => {
				var data = danmuku["data"].AsObject!;
				var broadcast_type = danmuku["broadcast_type"].Value;
				if (broadcast_type != "0")
				{
					b.MessageType = "guard_msg";
					b.Message = "【上舰】" + data["msg"].Value.Replace(":?", "");
				}
				else
				{
					b.MessageType = "junk";
					b.Message = "【上舰广播】" + data["msg"].Value.Replace(":?", "");
				}
				b.IsSystemMessage = true;
			});
			commands.Add("GUARD_LOTTERY_START", (b, danmuku) => {
				b.MessageType = "guard_lottery_msg";
				b.IsSystemMessage = true;

				b.Message = "【抽奖】上舰抽奖开始啦";
			});
			commands.Add("ROOM_CHANGE", (b, danmuku) => {
				var data = danmuku["data"].AsObject!;
				b.MessageType = "room_change";
				b.IsSystemMessage = true;

				b.Message = "【变更】直播间名称为: " + data["title"].Value;
			});
			commands.Add("PREPARING", (b, danmuku) => {
				b.MessageType = "room_preparing";
				b.IsSystemMessage = true;

				b.Message = "【下播】直播间准备中";
			});
			commands.Add("LIVE", (b, danmuku) => {
				b.MessageType = "room_live";
				b.IsSystemMessage = true;
				if (danmuku.TryGetKey("live_time", out var live_time))
				{
					b.Message = "【开播】直播间开播啦";
				}
				else
				{
					b.Message = "【开播】直播推流成功";
				}
				
			});
			commands.Add("WARNING", (b, danmuku) => {
				b.MessageType = "warning";
				b.IsHighlighted = true;

				b.Message = "【超管】" + danmuku["msg"]?.Value;
			});
			commands.Add("CUT_OFF", (b, danmuku) => {
				b.MessageType = "cut_off";
				b.IsHighlighted = true;

				b.Message = "【切断】" + danmuku["msg"]?.Value;
			});
			commands.Add("LIKE_INFO_V3_CLICK", (b, danmuku) => {
				b.MessageType = "like_info";
				b.IsSystemMessage = false;
				var data = danmuku["data"].AsObject!;

				b.Uid = data["uid"].Value.ToString();
				b.Username = data["uname"].Value.ToString();
				b.Content = data["like_text"].Value.ToString();
				// b.Color = "#" + int.Parse(data["uname_color"]).ToString("X");

				var Sender = new BilibiliChatUser();
				Sender.SetUid(b.Uid);
				Sender.SetUserName(b.Username);
				Sender.SetMedal(data["fans_medal"]["medal_level"].AsInt, data["fans_medal"]["medal_name"].Value, true, new string[] { data["fans_medal"]["medal_color_start"].Value.ToString(), data["fans_medal"]["medal_color_end"].Value.ToString(), data["fans_medal"]["medal_color_border"].Value.ToString() }, data["fans_medal"]["guard_level"].AsInt, data["fans_medal"]["anchor_roomid"].AsInt, data["fans_medal"]["target_id"]);
				Sender.UpdateDisplayName(true);
				b.Sender = Sender;

				var emote_list = new List<IChatEmote>();
				emote_list.Add(new BilibiliChatEmote("[点赞图标]", "[点赞图标]", data["like_icon"].Value.ToString(), false, b.Content.Length));
				b.Emotes = emote_list.ToArray();

				b.Message = b.Content + "[点赞图标]";
			});
			commands.Add("STOP_LIVE_ROOM_LIST", (b, danmuku) => {
				var data = danmuku["data"].AsObject!;
				b.MessageType = "junk";
				b.IsSystemMessage = true;

				b.Message = "以下房间停止直播：" + data["room_id_list"].AsArray!.ToString();
			});
			commands.Add("PK_BATTLE_PRE", (b, danmuku) => {
				b.MessageType = "ignore";
				b.IsSystemMessage = true;
			});
			commands.Add("PK_BATTLE_PRE_NEW", (b, danmuku) => {
				b.MessageType = "pk_pre";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;
				var extra = new BilibiliChatMessageExtraPK();
				extra.pk_timer = int.Parse(data["pre_timer"].Value);
				extra.pk_uname = data["uname"].Value;
				b.extra = extra;

				b.Message = "【大乱斗】距离与" + data["uname"].Value + "的PK还有" + data["pre_timer"].Value + "秒";
			});
			commands.Add("PK_BATTLE_START", (b, danmuku) => {
				b.MessageType = "ignore";
				b.IsSystemMessage = true;
			});
			commands.Add("PK_BATTLE_START_NEW", (b, danmuku) => {
				b.MessageType = "pk_start";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;
				var extra = new BilibiliChatMessageExtraPK();
				extra.pk_timer = int.Parse(data["pk_frozen_time"].Value) - int.Parse(data["pk_start_time"].Value);
				b.extra = extra;

				b.Message = "【大乱斗】距离结束还有" + (int.Parse(data["pk_frozen_time"].Value) - int.Parse(data["pk_start_time"].Value)) + "秒";
			});
			commands.Add("PK_BATTLE_SETTLE", (b, danmuku) => {
				b.MessageType = "pk_end";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				switch (data["result_type"].Value.ToString()) {
					case "-1":
						b.Message = "【大乱斗】这场大乱斗输掉啦~";
						break;
					case "1":
						b.Message = "【大乱斗】这场大乱斗平局啦~";
						break;
					case "2":
						b.Message = "【大乱斗】这场大乱斗获胜啦~";
						break;
				}
			});
			commands.Add("COMMON_NOTICE_DANMAKU", (b, danmuku) => {
				b.MessageType = "common_notice";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = data["content_segments"][0]["text"].Value.Replace("<%", "").Replace("%>", "").Replace("<$", "").Replace("$>", "");
			});
			commands.Add("WIDGET_GIFT_STAR_PROCESS", (b, danmuku) => {
				b.MessageType = "widget_gift_start";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = "【礼物星球活动开始了】";
			});
			commands.Add("LOG_IN_NOTICE", (b, danmuku) => {
				b.MessageType = "login_in_notice";
				var data = danmuku["data"].AsObject!;
				b.IsSystemMessage = true;

				b.Message = data["notice_msg"].Value;
			});
			commands.Add("plugin_message", (b, danmuku) => {
				b.MessageType = "plugin_message";
				b.IsSystemMessage = true;
			});

			// OpenBLive Only
			commands.Add("LIVE_OPEN_PLATFORM_DM", (b, danmuku) => {
				var data = danmuku["data"].AsObject!;
				var isEmotion = data["dm_type"] == 1;
				
				b.MessageType = isEmotion ? "danmuku_motion" : "danmuku";
				b.Uid = data["uid"].ToString();
				b.Username = data["uname"].Value;
				b.Content = (isEmotion ? "[表情]" : "") + data["msg"].Value;
				b.Message = data["msg"].Value;
				// b.Message = (isEmotion ? "[表情]" : "") + info[1].Value;
				var Sender = new BilibiliChatUser();
				Sender.SetUid(b.Uid);
				Sender.SetUserName(b.Username);
				Sender.SetGuardLevel(data["guard_level"].AsInt);
				Sender.SetMedal(data["fans_medal_level"].AsInt, data["fans_medal_name"].Value, data["guard_level"].AsInt, data["fans_medal_wearing_status"].AsBool ? -1 : 0, data["fans_medal_wearing_status"].AsBool ? -1 : 0);
				Sender.SetAvatar(b.Uid, data["uface"].Value);
				Sender.UpdateDisplayName(true);
				b.Sender = Sender;

				//b.Channel = new BilibiliChatChannel(danmuku);
				if (isEmotion)
				{
					var extraMsg = new BilibiliChatMessageExtraEmotionDanmuku();
					extraMsg.raw_msg = data["msg"].Value;
					extraMsg.emoticon_id = extraMsg.raw_msg;
					extraMsg.emoticon_name = extraMsg.raw_msg;
					extraMsg.emoticon_img = data["emoji_img_url"].Value;
					b.extra = extraMsg;
					b.Emotes = new IChatEmote[] { new BilibiliChatEmote(extraMsg.emoticon_id, extraMsg.emoticon_name, extraMsg.emoticon_img) };
				}
				else
				{
					var emote_list = new List<IChatEmote>();
					BilibiliChatMessageExtra extraMsg = new BilibiliChatMessageExtraDanmuku();
					var possibleEmojis = new Regex(@"(\[.*?\])", RegexOptions.Compiled);
					Match emojisMatches = possibleEmojis.Match(b.Message);
					while (emojisMatches.Success)
					{
						var emoji = emojisMatches.Value;
						if (BilibiliService.bilibiliEmoticons.TryGetValue(emoji, out var emoji_info))
						{
							emote_list.Add(new BilibiliChatEmote(emoji_info["id"], emoji_info["name"], emoji_info["url"], false, emojisMatches.Index));
						}

						emojisMatches = emojisMatches.NextMatch();
					}
					if (extraMsg is BilibiliChatMessageExtraDanmuku danmuku1)
					{
						danmuku1.raw_msg = data["msg"].Value;
					}
					else
					{
						((BilibiliChatMessageExtraEmotionDanmuku)extraMsg).raw_msg = data["msg"].Value;
					}

					b.extra = extraMsg;
					b.Emotes = emote_list.ToArray();
				}
			});

			commands.Add("LIVE_OPEN_PLATFORM_SEND_GIFT", (b, danmuku) => {
				var data = danmuku["data"].AsObject!;
				// Console.WriteLine(data["uid"].GetType().ToString());
				b.Uid = data["uid"].ToString();
				b.Username = data["uname"].Value;
				b.MessageType = "gift";
				b.Content = "";
				b.IsHighlighted = true;

				var Sender = new BilibiliChatUser();
				Sender.SetUid(b.Uid);
				Sender.SetUserName(b.Username);
				Sender.SetGuardLevel(data["guard_level"].AsInt);
				Sender.SetMedal(data["fans_medal_level"].AsInt, data["fans_medal_name"].Value, data["guard_level"].AsInt, data["fans_medal_wearing_status"].AsBool ? -1 : 0, data["fans_medal_wearing_status"].AsBool ? -1 : 0);
				Sender.SetAvatar(b.Uid, data["uface"].Value);
				Sender.UpdateDisplayName(true);
				b.Sender = Sender;

				var extra = new BilibiliChatMessageExtraGift();
				extra.gift_id = data["gift_id"].Value.ToString();
				extra.gift_action = "赠送";
				extra.gift_num = data["gift_num"].AsInt;
				extra.gift_name = data["gift_name"].Value;
				extra.origin_gift = "";
				extra.gift_type = data["paid"].AsBool? "gold" : "silver";
				extra.gift_price = (double)Math.Round(data["price"].AsInt / 1000.0f, 1) * (double)(extra.gift_num);
				extra.gift_img = data["gift_icon"].Value;
				b.extra = extra;
				var MessagePrice = "(" + (extra.gift_type == "silver" ? "免￥" : "￥") + string.Format("{0:0.0}", extra.gift_price) + ")";
				var GiftPlacholder = $"%GIFT_{extra.gift_id}%";

				b.Message = data["action"].Value + data["num"].Value + "个" + data["giftName"].Value + GiftPlacholder + MessagePrice;

				var emote_list = new List<IChatEmote>();
				var target = new Regex(GiftPlacholder, RegexOptions.Compiled);
				Match match = target.Match(b.Message);
				while (match.Success)
				{
					emote_list.Add(new BilibiliChatEmote(GiftPlacholder, GiftPlacholder, extra.gift_img, true, match.Index));
					match = match.NextMatch();
				}
				b.Emotes = emote_list.ToArray();
			});

			commands.Add("LIVE_OPEN_PLATFORM_SUPER_CHAT", (b, danmuku) => {
				b.MessageType = "super_chat";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["uid"].ToString();
				b.Username = data["uname"].Value;
				b.Content = data["message"].Value;
				b.IsHighlighted = true;

				var Sender = new BilibiliChatUser();
				Sender.SetUid(b.Uid);
				Sender.SetUserName(b.Username);
				Sender.SetMedal(data["fans_medal_level"].AsInt, data["fans_medal_name"].Value, data["guard_level"].AsInt, data["fans_medal_wearing_status"].AsBool ? -1 : 0, data["fans_medal_wearing_status"].AsBool ? -1 : 0);
				Sender.SetAvatar(b.Uid, data["uface"].Value);
				Sender.UpdateDisplayName(true);
				b.Sender = Sender;

				b.Message = "【SC (￥" + data["rmb"].AsInt + ")】" + b.Content;

				var extra = new BilibiliChatMessageExtraSuperChat();
				extra.sc_price = data["rmb"].Value.ToString();
				extra.sc_time = (data["end_time"].AsInt - data["start_time"].AsInt).ToString();
				b.extra = extra;
			});

			commands.Add("LIVE_OPEN_PLATFORM_SUPER_CHAT_DEL", (b, danmuku) => {
				// Superchat removed
				/*
					"room_id":1,//直播间id
					"message_ids":[1,2],// 留言id
					"msg_id":""//消息唯一id
				 */
			});

			commands.Add("LIVE_OPEN_PLATFORM_GUARD", (b, danmuku) => {
				b.MessageType = "new_guard_msg";
				var data = danmuku["data"].AsObject!;
				b.Uid = data["user_info"]["uid"].ToString();
				b.Username = data["user_info"]["uname"].Value;
				b.Content = "";
				b.IsSystemMessage = true;
				b.IsHighlighted = true;

				b.Message = b.Username + " 开通了 " + data["guard_num"].Value + "个" + data["guard_unit"] + "的" + GuardLevelToName(data["guard_level"].AsInt) + " 进入舰队啦";

				var extra = new BilibiliChatMessageExtraNewGuardMsg();
				extra.role_name = GuardLevelToName(data["guard_level"].AsInt);
				extra.num = data["guard_num"].Value.ToString();
				extra.unit = data["guard_unit"].Value.ToString();
				extra.gift_img = BilibiliService.bilibiliGiftInfo[extra.role_name];
				extra.gift_price = BilibiliService.bilibiliGiftPrice[extra.role_name];
				b.extra = extra;
			});

			commands.Add("LIVE_OPEN_PLATFORM_LIKE", (b, danmuku) => {
				b.MessageType = "like_info";
				b.IsSystemMessage = false;
				var data = danmuku["data"].AsObject!;

				b.Uid = data["uid"].Value.ToString();
				b.Username = data["uname"].Value.ToString();
				b.Content = data["like_text"].Value.ToString();
				// b.Color = "#" + int.Parse(data["uname_color"]).ToString("X");

				var Sender = new BilibiliChatUser();
				Sender.SetUid(b.Uid);
				Sender.SetUserName(b.Username);
				Sender.SetAvatar(b.Uid, data["uface"].Value);
				Sender.SetMedal(data["fans_medal_level"].AsInt, data["fans_medal_name"].Value, 0, data["fans_medal_wearing_status"].AsBool ? -1 : 0, data["fans_medal_wearing_status"].AsBool ? -1 : 0);
				Sender.UpdateDisplayName(true);
				b.Sender = Sender;

				var emote_list = new List<IChatEmote>();
				emote_list.Add(new BilibiliChatEmote("[点赞图标]", "[点赞图标]", data["like_icon"].Value.ToString(), false, b.Content.Length));
				b.Emotes = emote_list.ToArray();

				b.Message = b.Content + "[点赞图标]";
			});

			/*comands.Add("GIFT_TOP", (b, danmuku) => {
				// 高能榜
			});*/
		}

		private void CreateMessage(JSONNode danmuku)
		{
			if (commands.TryGetValue(danmuku["cmd"].Value, out var commandAction))
			{
				commandAction?.Invoke(this, danmuku);
			}
			else
			{
				MessageType = "unkown";
				Message = "【暂不支持该消息】";
				IsSystemMessage = true;
			}
		}

		public void BanMessage() {
			MessageType = "banned";
		}

		public void UpdateContent(string content) {
			Message = content;
		}

		private static string GuardLevelToName(int GuardLevel) {
			switch (GuardLevel)
			{
				case 1:
					return "总督";
				case 2:
					return "提督";
				case 3:
					return "舰长";
			}
			return "";
		}
	}
}
