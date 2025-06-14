using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using BrotliSharpLib;
using static ChatCore.Models.Bilibili.BilibiliPacket;

namespace ChatCore.Models.Bilibili
{
	public class DanmakuMessage
	{
		public int PacketLength { get; private set; }
		public int HeaderLength { get; private set; }
		public int Version { get; private set; }
		public DanmakuOperation Operation { get; private set; }
		public int Sequence { get; private set; }
		public string Body { get; private set; } = string.Empty;
		public byte[] Buffer { get; private set; } = new byte[0];

		public static IEnumerable<DanmakuMessage> ParsePackets(byte[] buffer)
		{
			var packetLength = DataView.GetInt32(buffer);
			var headerLength = DataView.GetInt16(buffer, HEADEROFFSET);
			var version = DataView.GetInt16(buffer, VERSIONOFFSET);
			var operation = DataView.GetInt32(buffer, OPERATIONOFFSET);
			var sequence = DataView.GetInt32(buffer, SEQUENCEOFFSET);
			var offset = 0;

			if (operation == 5)
			{
				byte[] decomp;
				if (version == 2) // Deflate
				{
					DataView.ByteSlice(ref buffer, headerLength, packetLength);
					
					using (var dest = new MemoryStream())
					{
						using (var ds = new DeflateStream(new MemoryStream(buffer, 2, packetLength - headerLength - 2), CompressionMode.Decompress, true))
						{
							ds.CopyTo(dest);
						}
						decomp = dest.ToArray();
					}
				}
				else if (version == 3) // Brotli - 暂时跳过
				{
					// 暂时不处理 Brotli 压缩，避免栈溢出问题
					yield break;
				}
				else
				{
					// 对于其他版本，提取数据部分（去掉头部）
					decomp = buffer;
				}

				while (offset < decomp.Length)
				{
					// 确保有足够的字节读取头部
					if (offset + 16 > decomp.Length)
						break;
						
					var packetLength1 = DataView.GetInt32(decomp, offset);
					var headerLength1 = DataView.GetInt16(decomp, HEADEROFFSET + offset);
					var version1 = DataView.GetInt16(decomp, VERSIONOFFSET + offset);
					var operation1 = DataView.GetInt32(decomp, OPERATIONOFFSET + offset);
					var sequence1 = DataView.GetInt32(decomp, SEQUENCEOFFSET + offset);

					// 验证数据包长度的合理性
					if (packetLength1 <= 0 || packetLength1 > decomp.Length - offset)
					{
						break;
					}
					
					// 确保不会越界
					if (offset + packetLength1 > decomp.Length)
					{
						break;
					}

					var data = (byte[])decomp.Clone();
					DataView.ByteSlice(ref data, offset + headerLength1, offset + packetLength1);
					yield return new DanmakuMessage()
					{
						PacketLength = packetLength1,
						HeaderLength = headerLength1,
						Version = version1,
						Operation = (DanmakuOperation)operation1,
						Sequence = sequence1,
						Body = Encoding.UTF8.GetString(data, 0, data.Length),
						Buffer = decomp
					};
					
					offset += packetLength1;
					if (packetLength1 <= 0)
					{
						break;
					}
				}
			}
			else
			{
				yield return new DanmakuMessage()
				{
					PacketLength = packetLength,
					HeaderLength = headerLength,
					Version = version,
					Operation = (DanmakuOperation)operation,
					Sequence = sequence,
					Body = DataView.GetInt32(buffer, BilibiliPacket.HEADERLENGTH).ToString(),
					Buffer = buffer
				};
			}
		}
	}
}
