using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ChatCore.Config;
using ChatCore.Interfaces;
using ChatCore.Models;
using Microsoft.Extensions.Logging;

namespace ChatCore.Services
{
	internal class OldStreamCoreConfig
	{
		public string? TwitchChannelName { get; set; }
		public string? TwitchUsername { get; set; }
		public string? TwitchOAuthToken { get; set; }
	}

	internal class OldChatCoreConfig_2_1_3
	{
		[ConfigSection("Bilibili")]
		[ConfigMeta(Comment = "When value is postive number, Bilibili Live Danmuku will be listened.")]
		public int bilibili_room_id = 0;
	}

	public class UserAuthProvider : IUserAuthProvider
	{
		private readonly string _credentialsPath, _oldConfigPath_2_1_3;
		private readonly ObjectSerializer _credentialSerializer;

		public event Action<LoginCredentials>? OnTwitchCredentialsUpdated, OnBilibiliCredentialsUpdated;

		public LoginCredentials Credentials { get; } = new LoginCredentials();

		// If this is set, old StreamCore config data will be read in from this file.
		internal static string OldConfigPath = null!;

		public UserAuthProvider(ILogger<UserAuthProvider> logger, IPathProvider pathProvider)
		{
			_oldConfigPath_2_1_3 = Path.Combine(pathProvider.GetDataPath(), "settings.ini.bak");

			_credentialsPath = Path.Combine(pathProvider.GetDataPath(), "auth.ini");
			_credentialSerializer = new ObjectSerializer();
			_credentialSerializer.Load(Credentials, _credentialsPath);
			// logger.LogInformation($"[UserAuthProvider] Loaded credentials from {_credentialsPath} - Bilibili_room_id: {Credentials.Bilibili_room_id}");

			Task.Delay(500).ContinueWith(_ =>
			{
				if (!string.IsNullOrEmpty(OldConfigPath) && File.Exists(OldConfigPath))
				{
					logger.LogInformation($"Trying to convert old StreamCore config at path {OldConfigPath}");
					var old = new OldStreamCoreConfig();
					_credentialSerializer.Load(old, OldConfigPath);

					if (!string.IsNullOrEmpty(old.TwitchChannelName))
					{
						var oldName = old.TwitchChannelName?.ToLower().Replace(" ", "");
						if (oldName != null && !Credentials.Twitch_Channels.Contains(oldName))
						{
							Credentials.Twitch_Channels.Add(oldName);
							logger.LogInformation($"Added channel {oldName} from old StreamCore config.");
						}
					}

					if (!string.IsNullOrEmpty(old.TwitchOAuthToken))
					{
						Credentials.Twitch_OAuthToken = old.TwitchOAuthToken!;
						logger.LogInformation($"Pulled in old Twitch auth info from StreamCore config.");
					}

					var convertedPath = OldConfigPath + ".converted";
					try
					{
						if (!File.Exists(convertedPath))
						{
							File.Move(OldConfigPath, convertedPath);
						}
						else
						{
							File.Delete(OldConfigPath);
						}
					}
					catch (Exception ex)
					{
						logger.LogWarning(ex, "An exception occurred while trying to yeet old StreamCore config!");
					}
				}

				if (File.Exists(_oldConfigPath_2_1_3))
				{
					var old = new OldChatCoreConfig_2_1_3();
					_credentialSerializer.Load(old, _oldConfigPath_2_1_3);
					if (old.bilibili_room_id > 0)
					{
						Credentials.Bilibili_room_id = old.bilibili_room_id;
						SaveBilibili(true);
						logger.LogInformation($"Pulled in Bilibili room info from ChatCore 2.1.3 config.");
					}
				}
			});
		}

		public void SaveTwitch(bool callback = true)
		{
			_credentialSerializer.Save(Credentials, _credentialsPath);
			if (callback)
			{
				OnTwitchCredentialsUpdated?.Invoke(Credentials);
			}
		}

		public void SaveBilibili(bool callback = true)
		{
			_credentialSerializer.Save(Credentials, _credentialsPath);
			if (callback)
			{
				OnBilibiliCredentialsUpdated?.Invoke(Credentials);
			}
		}

		public void Reload()
		{
			_credentialSerializer.Load(Credentials, _credentialsPath);
		}
	}
}
