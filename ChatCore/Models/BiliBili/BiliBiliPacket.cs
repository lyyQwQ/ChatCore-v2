using System;
using System.Linq;
using System.Text;
using ChatCore.Utilities;

namespace ChatCore.Models.Bilibili
{
	public class BilibiliPacket
	{
		public const int HEADERLENGTH = 16;
		public const int PACKETOFFSET = 0;
		public const int HEADEROFFSET = 4;
		public const int VERSIONOFFSET = 6;
		public const int OPERATIONOFFSET = 8;
		public const int SEQUENCEOFFSET = 12;

		public byte[] PacketBuffer { get; private set; }

		private byte[] Encoder(string value, DanmakuOperation operation)
		{
			var data = Encoding.UTF8.GetBytes(value);
			var packetLen = 16 + data.Length;
			// 头部协议版本使用1（不压缩），不要和JSON中的protover混淆
			var header = new byte[] { 0, 0, 0, 0, 0, 16, 0, 1, 0, 0, 0, (byte)operation, 0, 0, 0, 1 };
			WriteInt(header, 0, 4, packetLen);
			return header.Concat(data).ToArray();
		}

		private byte[] Decoder(string value, DanmakuOperation operation)
		{
			return new byte[0];
		}

		private void WriteInt(byte[] buffer, int start, int length, int value)
		{
			for (var i = 0; i + start < length; i++)
			{
				buffer[start + i] = (byte)(value / Math.Pow(256, length - i - 1));
			}
		}

		/// <summary>
		/// Constructor of DanmakuPacket.
		/// </summary>
		/// <param name="operation"></param>
		/// <param name="json"></param>
		private BilibiliPacket(DanmakuOperation operation, JSONObject json)
		{
			//var headerBytes = new byte[HeaderLength];
			//var bodyBuffer = Encoding.UTF8.GetBytes(json.ToString());
			//DataView.SetInt32(headerBytes, PacketOffset, HeaderLength + bodyBuffer.Length);
			//DataView.SetInt16(headerBytes, HeaderOffset, HeaderLength);
			//DataView.SetInt16(headerBytes, VersionOffset, version);
			//DataView.SetInt32(headerBytes, OperationOffset, (int)operation);
			//DataView.SetInt32(headerBytes, SequenceOffset, sequence);

			//var packetBuffer = DataView.MergeBytes(new List<byte[]> {
			//	headerBytes, bodyBuffer
			//});
			PacketBuffer = Encoder(json.ToString(), operation);
		}

		private BilibiliPacket(DanmakuOperation operation, string json)
		{
			PacketBuffer = Encoder(json, operation);
		}

		/// <summary>
		/// Create greeting packet.
		/// </summary>
		/// <param name="uid"></param>
		/// <param name="roomId"></param>
		/// <returns></returns>
		public static BilibiliPacket CreateGreetingPacket(long uid, int roomId)
		{
                       var json = new JSONObject();
                       json["uid"] = new JSONNumber(uid);
                       json["roomid"] = new JSONNumber(roomId);
                       json["protover"] = new JSONNumber(1);  // 修复：与包头协议版本保持一致
                       json["platform"] = "web";
                       // 关键修复：移除 clientver 字段，这是导致连接断开的原因
                       json["type"] = new JSONNumber(2);

			return new BilibiliPacket(DanmakuOperation.GreetingReq, json);
		}

		/// <summary>
		/// Create greeting packet.
		/// </summary>
		/// <param name="uid"></param>
		/// <param name="roomId"></param>
		/// <param name="token"></param>
		/// <param name="buvid"></param>
		/// <returns></returns>
		public static BilibiliPacket CreateGreetingPacket(long uid, int roomId, string token, string buvid)
		{
                       var json = new JSONObject();
                       json["uid"] = new JSONNumber(uid);
                       json["roomid"] = new JSONNumber(roomId);
                       json["protover"] = new JSONNumber(1);  // 修复：与包头协议版本保持一致
                       json["buvid"] = new JSONString(buvid);
                       json["platform"] = new JSONString("web");
                       // 关键修复：移除 clientver 字段
                       json["type"] = new JSONNumber(2);
                       json["key"] = new JSONString(token);

			// 手动构建 JSON 字符串，避免科学计数法和格式问题
			var jsonStr = "{" +
				$"\"uid\":{uid}," +
				$"\"roomid\":{roomId}," +
				$"\"protover\":1," +
				$"\"buvid\":\"{buvid}\"," +
				$"\"platform\":\"web\"," +
				$"\"type\":2," +
				$"\"key\":\"{token}\"" +
				"}";

			Console.WriteLine($"[CreateGreetingPacket] Greeting JSON: {jsonStr}");
			return new BilibiliPacket(DanmakuOperation.GreetingReq, jsonStr);
		}

		public static BilibiliPacket CreateAuthPacket(string authBody)
		{
			return new BilibiliPacket(DanmakuOperation.GreetingReq, authBody);
		}

		/// <summary>
		/// Create HeartBeat Packet..
		/// </summary>
		/// <returns></returns>
		public static BilibiliPacket CreateHeartBeatPacket()
		{
			// 关键修复：心跳包内容为特定字符串，匹配 Python 版本
			return new BilibiliPacket(DanmakuOperation.HeartBeatReq, "[object Object]");
		}

		public enum DanmakuOperation
		{
			// Send HeartBeat packet to server.
			HeartBeatReq = 2,

			// Server has got the HeartBeat packet successfully.
			HeartBeatAck = 3,

			// Chat message from server.
			ChatMessage = 5,

			// Send greeting request to server.
			GreetingReq = 7,

			// Server has got the Greeting packet successfully.
			GreetingAck = 8,

			// Room ids stops live message from Server
			StopRoom = 1398034256,
			StopLiveRoomList = 0
		}

		public static string ByteArrayToString(byte[] ba)
		{
			var hex = new StringBuilder(ba.Length * 2);
			foreach (var b in ba)
			{
				hex.AppendFormat("{0:x2}", b);
			}

			return hex.ToString();
		}
	}
}
