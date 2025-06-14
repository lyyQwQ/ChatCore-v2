using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using ChatCore.Interfaces;
using ChatCore.Services;
using ChatCore.Services.Bilibili;
using ChatCore.Utilities;

namespace ChatCore.Models.Bilibili
{
	public class BilibiliChatUser : IChatUser
	{
		public string Id { get; internal set; } = "";
		public string UserName { get; internal set; } = "";
		public string DisplayName { get; internal set; } = "";
		public string Avatar { get; internal set; } = "";
		public string Color { get; internal set; } = "#FFFFFF";
		public bool IsBroadcaster { get; internal set; } = false;
		public bool IsModerator { get; internal set; } = false;
		public bool IsFan { get; internal set; } = false;
		public IChatBadge[] Badges { get; internal set; } = new BilibiliChatBadge[0];
		public int GuardLevel { get; internal set; } = 0;
		public int HonorLevel { get; internal set; } = 0;

		// private static readonly string BilibiliUserInfoApi = "https://api.bilibili.com/x/space/acc/info?mid=";
		private static readonly string BilibiliUserInfoApi = "https://api.bilibili.com/x/web-interface/card?mid=";

		private const string SVG_FRAME = @"<?xml version=""1.0"" encoding=""utf-8""?><svg version=""1.1"" id=""Avatar"" xmlns=""http://www.w3.org/2000/svg"" xmlns:xlink=""http://www.w3.org/1999/xlink"" viewBox=""0 0 44 44""><defs><clipPath id=""circle""><circle cx=""22"" cy=""22"" r=""18""/></clipPath></defs><image id=""AvatarImg"" x=""4"" y=""4"" width=""36"" height=""36"" clip-path=""url(#circle)"" xlink:href=""%Image%""/></svg>";

		public BilibiliChatUser() { }
		public BilibiliChatUser(JSONNode info)
		{
			var displaySettings = ChatCoreInstance.BilibiliDisplaySettings();
			var infos = info.AsArray;
			if (infos == null)
			{
				return;
			}
			var userData = infos[2].AsArray;
			if (userData == null)
			{
				return;
			}
			Id = userData[0].ToString();
			UserName = userData[1].Value;
			DisplayName = userData[1].Value;
			Color = !string.IsNullOrEmpty(userData[7].Value) ? userData[7].Value : "#FFFFFF";
			IsModerator = userData[2].AsInt == 1;
			IsBroadcaster = userData[0].AsInt == BilibiliService._roomID;
			if (infos[3].AsArray!.Count > 0)
			{
				IsFan = (infos[3].AsArray!)[3] == BilibiliService._roomID;
			}
			var badgeList = new List<IChatBadge>();
			if (!string.IsNullOrEmpty(infos[3][1].Value) && displaySettings["showBadge"])
			{
				if (displaySettings["showBadgeText"])
				{
					DisplayName = $"[{infos[3][1].Value} {infos[3][0].AsInt}] {DisplayName}";
				}
				else
				{
					badgeList.Add(new BilibiliChatBadge("{\"Name\":\"" + infos[3][1].Value + "\",\"Level\":" + infos[3][0].AsInt + ",\"Guard\":" + infos[3][10].AsInt + ",\"Color\":" + infos[3][4].AsInt + "}"));
					/*Console.WriteLine(infos[3][10].AsInt.ToString());
					Console.WriteLine(infos[7][0].AsInt.ToString());*/
				}
			}
			Badges = badgeList.ToArray();
			GuardLevel = infos[7].AsInt;
			HonorLevel = infos[16][0].AsInt;

			UpdateDisplayName();
		}
		public JSONObject ToJson()
		{
			var obj = new JSONObject();
			obj.Add(nameof(Id), new JSONString(Id));
			obj.Add(nameof(UserName), new JSONString(UserName));
			obj.Add(nameof(DisplayName), new JSONString(DisplayName));
			obj.Add(nameof(Color), new JSONString(Color));
			obj.Add(nameof(IsBroadcaster), new JSONBool(IsBroadcaster));
			obj.Add(nameof(IsModerator), new JSONBool(IsModerator));
			var badges = new JSONArray();
			foreach (var badge in Badges)
			{
				badges.Add(badge.ToJson());
			}
			obj.Add(nameof(Badges), badges);
			obj.Add(nameof(GuardLevel), new JSONNumber(GuardLevel));
			return obj;
		}
		public async Task<string> GetUserInfoAsync(string uid)
		{
			if (!BilibiliService.bilibiliuserInfo.ContainsKey(uid))
			{
				if (uid == "0")
				{
					var avatar_img = "https://i0.hdslb.com/bfs/face/member/noface.jpg";
					BilibiliService.bilibiliuserInfo.Add(uid, avatar_img);
					return avatar_img;
				}
				try
				{
					var apiResult = await (new HttpClientUtils()).HttpClient(BilibiliUserInfoApi + uid, HttpMethod.Get, null, null);
					if (apiResult != null && apiResult[0] == "OK")
					{
						var NewUserInfo = JSONNode.Parse(apiResult[1]);
						if (NewUserInfo["code"].AsInt == 0)
						{
							var avatar_img = NewUserInfo["data"]["card"]["face"].IsNull ? "" : NewUserInfo["data"]["card"]["face"].Value.ToString();
							BilibiliService.bilibiliuserInfo.Add(uid, avatar_img);
							return avatar_img;
						}
					}
					else
					{
						//_logger.LogInformation($"[BilibiliChatUser] | [GetUserInfoAsync] | Get User Info failed. ({(apiResult == null ? "connection failed" : apiResult[0])})");
					}

				}
				catch
				{
					//_logger.LogInformation($"[BilibiliChatUser] | [GetUserInfoAsync] | Get User Info failed. (Exception)");
				}
			}
			else
			{
				return BilibiliService.bilibiliuserInfo[uid];
			}
			return "";
		}

		public void SetUid(string uid) {
			Id = uid;
			IsBroadcaster = uid == BilibiliService._userID.ToString();
		}

		public void SetUserName(string username) {
			UserName = username;
			SetDisplayName(username);
		}

		public void SetDisplayName(string username)
		{
			DisplayName = username;
		}

		public void SetColor(string ColorData, bool IsInt = false) {
			if (ColorData != "")
			{
				Color = "#" + (IsInt ? int.Parse(ColorData).ToString("X") : ColorData);
			}
		}

		public void SetIsModerator(int IsModerator) {
			this.IsModerator = IsModerator == 1;
		}

		public void SetIsFan(int MedalOwnerRoomId, long BoardcasterUid = 0) {
			IsFan = (MedalOwnerRoomId == -1 && BoardcasterUid == -1) || (MedalOwnerRoomId == BilibiliService._roomID) || (BoardcasterUid == BilibiliService._userID);
		}

		public void SetGuardLevel(int Level) {
			GuardLevel = Level;
		}

		public void SetHonorLevel(int Level)
		{
			HonorLevel = Level;
		}

		public void SetMedal(int Level = 0, string MedalName = "", int GuardLevel = 0, int MedalOwnerRoomId = 0, long BoardcasterUid = 0)
		{
			var badgeList = new List<IChatBadge>();
			var newBadge = new BilibiliChatBadge();
			newBadge.Name = MedalName;
			newBadge.Level = Level;
			newBadge.Color = "#000000";
			newBadge.Guard = GuardLevel;
			newBadge.setMedalColorByLevel(Level, GuardLevel);
			newBadge.genImage();
			//SetGuardLevel(GuardLevel);
			if (Level != 0)
			{
				badgeList.Add(newBadge);
			}
			Badges = badgeList.ToArray();
			SetIsFan(MedalOwnerRoomId, BoardcasterUid);
		}

		public void SetMedal(int Level, string MedalName, bool ColorInInt, string[] ColorData, int GuardLevel, int MedalOwnerRoomId, long BoardcasterUid) {
			var badgeList = new List<IChatBadge>();
			var newBadge = new BilibiliChatBadge();
			newBadge.Name = MedalName;
			newBadge.Level = Level;
			newBadge.Color = "#" + (ColorInInt ? int.Parse(ColorData[0]).ToString("X6") : ColorData[0]);
			newBadge.LinearGradientColorA = "#" + int.Parse(ColorData[0]).ToString("X6");
			newBadge.LinearGradientColorB = "#" + int.Parse(ColorData[1]).ToString("X6");
			newBadge.BorderColor = "#" + int.Parse(ColorData[2]).ToString("X6");
			newBadge.Guard = GuardLevel;
			if (Level < 35)
			{
				newBadge.setMedalColorByLevel(Level, GuardLevel);
			}
			newBadge.genImage();
			//SetGuardLevel(GuardLevel);
			if (Level != 0)
			{
				badgeList.Add(newBadge);
			}
			Badges = badgeList.ToArray();
			SetIsFan(MedalOwnerRoomId, BoardcasterUid);
		}

		public void UpdateDisplayName(bool withAvatar = false) {
			var displaySettings = ChatCoreInstance.BilibiliDisplaySettings();
			var badgeList = Badges.ToList();
			if (displaySettings["showHonorBadge"] && HonorLevel > 0)
			{
				if (!displaySettings["showHonorBadgeText"])
				{
					DisplayName = $"[荣耀 {HonorLevel}]{DisplayName}";
				}
				else
				{
					var newBadge = new BilibiliChatBadge();
					newBadge.Id = $"荣耀等级{HonorLevel}";
					newBadge.Name = $"荣耀等级{HonorLevel}";
					newBadge.Uri = BilibiliService.bilibiliWealth[HonorLevel.ToString()];
					badgeList.Add(newBadge);
				}
			}

			if (IsFan)
			{
				/*if (Badges[0] is BilibiliChatBadge badge)
				{
					DisplayName = $"[{badge.Name} {badge.Level}] {DisplayName}";
				}*/
			}

			if (displaySettings["showGuard"] && GuardLevel > 0)
			{
				if (!displaySettings["showGuardText"])
				{
					DisplayName = "[" + (GuardLevel == 3 ? "舰长" : (GuardLevel == 2 ? "提督" : "总督")) + "]" + DisplayName;
				}
				else
				{
					var newBadge = new BilibiliChatBadge();
					newBadge.Id = GuardLevel == 3 ? "舰长" : (GuardLevel == 2 ? "提督" : "总督");
					newBadge.Name = GuardLevel == 3 ? "舰长" : (GuardLevel == 2 ? "提督" : "总督");
					newBadge.Uri = $"http://localhost:{MainSettingsProvider.WEB_APP_PORT}/Statics/Images/BilibiliLiveGuard{GuardLevel}.png";
					badgeList.Add(newBadge);
				}
			}

			if (displaySettings["showBroadcaster"] && IsBroadcaster)
			{
				if (!displaySettings["showBroadcasterText"])
				{
					DisplayName = "[主播]" + DisplayName;
				}
				else
				{
					var newBadge = new BilibiliChatBadge();
					newBadge.Id = "主播";
					newBadge.Name = "主播";
					newBadge.Uri = $"http://localhost:{MainSettingsProvider.WEB_APP_PORT}/Statics/Images/BilibiliLiveBroadcaster.png";
					badgeList.Add(newBadge);
				}
			}
			else if (displaySettings["showModerator"] && IsModerator)
			{
				if (!displaySettings["showModeratorText"])
				{
					DisplayName = "[房管]" + DisplayName;
				}
				else
				{
					var newBadge = new BilibiliChatBadge();
					newBadge.Id = "房管";
					newBadge.Name = "房管";
					newBadge.Uri = $"http://localhost:{MainSettingsProvider.WEB_APP_PORT}/Statics/Images/BilibiliLiveModerator.png";
					badgeList.Add(newBadge);
				}
			}

			if (displaySettings["showAvatar"] && withAvatar)
			{
				genAvatarImage().Wait();
				var newBadge = new BilibiliChatBadge();
				newBadge.Id = UserName;
				newBadge.Name = UserName;
				newBadge.Uri = Avatar;
				badgeList.Add(newBadge);
			}

			Badges = badgeList.ToArray();
		}

		public async Task genAvatarImage()
		{
			if (await GetUserInfoAsync(Id) == string.Empty)
			{
				Avatar = string.Empty;
				return;
			}

			var avatarImageUrl = await GetUserInfoAsync(Id) + @"@128w_128h.png";
			var scale = 3;
			var sb = new StringBuilder(SVG_FRAME);
			try
			{
				var AvatarImageBase64 = ImageUtils.AddBase64DataType(ImageUtils.Base64fromHTTPImg(avatarImageUrl));
				sb.Replace("%Image%", AvatarImageBase64);
			}
			catch (Exception ex) {
				Console.WriteLine("[BilibiliChatUser] | genAvatarImage | Generate Base64 Image: " + ex);
			}

			try
			{
				var path = (new PathProvider()).GetAvatarImagePath();
				var filename = Path.Combine(path, ImageUtils.convertToValidFilename(UserName) + ".svg");
				var imagename = Path.Combine(path, ImageUtils.convertToValidFilename(UserName) + ".png");
				if (!Directory.Exists(path))
				{
					Directory.CreateDirectory(path);
				}

				using (var writer = new StreamWriter(filename))
				{
					writer.WriteLine(sb.ToString());
				}

				ImageUtils.genImg(filename.ToString(), imagename.ToString(), 44 * scale, 44 * scale, true);
				if (File.Exists(filename))
				{
					File.Delete(filename);
				}

				Avatar = (new System.Uri(imagename)).AbsoluteUri;
			}
			catch (Exception ex)
			{
				Console.WriteLine("[BilibiliChatUser] | genAvatarImage | Save Image: " + ex);
			}
		}

		public void SetAvatar(string Uid, string Url) {
			if (!BilibiliService.bilibiliuserInfo.ContainsKey(Uid))
			{
				BilibiliService.bilibiliuserInfo.Add(Uid, Url);
			}
		}
	}
}
