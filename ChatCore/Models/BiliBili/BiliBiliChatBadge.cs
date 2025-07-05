using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ChatCore.Interfaces;
using ChatCore.Services;
using ChatCore.Utilities;
using ChatCoreSVG;

namespace ChatCore.Models.Bilibili
{
	public class BilibiliChatBadge : IChatBadge
	{
		public string Id { get; internal set; } = null!;
		public string Name { get; internal set; } = null!;
		public string Uri { get; internal set; } = null!;
		public string Color { get; internal set; } = null!;
		public string BorderColor { get; internal set; } = null!;
		public string LinearGradientColorA { get; internal set; } = null!;
		public string LinearGradientColorB { get; internal set; } = null!;
		public int Level { get; internal set; } = 0;
		public int Guard { get; internal set; } = 0;

		// %ImageWeight% %BoardColor% %LinearGradientColorA% %LinearGradientColorB% %BadgeBody% %GuardImage% %CONTENT% %LEVEL%

		// private const string SVG_FRAME = @"<?xml version=""1.0"" encoding=""utf-8""?><svg version=""1.1"" id=""Badge"" xmlns=""http://www.w3.org/2000/svg"" xmlns:xlink=""http://www.w3.org/1999/xlink"" x=""0px"" y=""0px"" viewBox=""0 0 %ImageWidth% 44"" style=""enable-background:new 0 0 128 44;"" xml:space=""preserve""><style type=""text/css"">.st1{fill:%BoardColor%;}.st2{fill:#FFFFFF;}.st3{fill:%LinearGradientColorA%;}.st4{font-family:'MicrosoftYaHeiUISemilight'}.st5{font-size:24px}.st6{font-family:'MicrosoftYaHeiUILight'}.st7{font-size:22px}</style><defs><linearGradient id=""bg_color"" gradientTransform=""rotate(45)""><stop offset=""0"" style=""stop-color:%LinearGradientColorB%""/><stop offset=""1"" style=""stop-color:%LinearGradientColorA%""/></linearGradient></defs>%BadgeBody%%GuardImage%</svg>";
		private const string SVG_FRAME = @"<?xml version=""1.0"" encoding=""utf-8""?><svg version=""1.1"" id=""Badge"" xmlns=""http://www.w3.org/2000/svg"" xmlns:xlink=""http://www.w3.org/1999/xlink"" viewBox=""0 0 %ImageWidth% 44""><defs><linearGradient id=""a"" gradientTransform=""rotate(45)""><stop offset=""0"" stop-color=""%LinearGradientColorB%""/><stop offset=""1"" stop-color=""%LinearGradientColorA%""/></linearGradient></defs><rect x=""%OFFSET_X%"" y=""%OFFSET_Y%"" rx=""4"" ry=""4"" width=""%WIDTH_1%"" height=""28"" fill=""url(#a)"" stroke=""%BorderColor%"" stroke-width=""2"" paint-order=""stroke fill""/><text x=""%OFFSET_BADGE_NAME_X%"" y=""%OFFSET_BADGE_NAME_Y%"" font-family=""Microsoft YaHei UI Semibold"" font-size=""22"" fill=""#fff"">%CONTENT%</text><rect x=""%OFFSET_Level_0%"" y=""%OFFSET_Y%"" rx=""4"" ry=""4"" width=""32"" height=""28"" fill=""#fff""/><text x=""%OFFSET_Level_1%"" y=""%OFFSET_BADGE_NAME_Y%"" text-anchor=""middle"" font-family=""Microsoft YaHei UI Semibold"" font-size=""24"" fill=""%LinearGradientColorA%"">%LEVEL%</text>%GuardImage%</svg>";
		/*private string[] GUARD_IMAGE = new string[] {
			"",
			@"<image x=""0"" y=""0"" width=""44"" height=""44"" xlink:href=""data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAEIAAABCCAMAAADUivDaAAAB+FBMVEUAAABNFRhNFhlNFBVSGRxNFhpOFxtWGx5OFhpNFxuLKilOFhpOFhtiHSBRGR1aHR5/TEWJQi+IYFx0ISVsHyNqLypnKSOAPixhIyF3OS5aJCZVGx5NFhpyISR/VEz98d3/w6PvRkb+9ur/p5D/s5uBGh25NDzndlvCOUF4FBfhaVX//vz1nFVxDhPyoob/5cH8unLKPkd0NCxpChD1uJqAVFH+2sLwi2T6tGP/so/dXVD++vT94cr4qVr5rWiTUkr+upeqMTSdLTDpgWKUKSyAJyfZVUyQSzRiJyb+w3fthkRzPDj+xm5pMjFqKib/yX/8vmjylEz/rIhcHR+/qKCgf31yREPRPT6Wcm3jQkJ/QTp0JCTvkFbqilKTW1HbQEDn3dnl1sr90bPnzq/+za+hVUPpfT3kaTFoHSJQGB3UtJjpsZKmiIbsmn+we2jzomH7omCba17Fak/LUkrndTrfXSjdUiWqPB7x6eK4mpDFi3XVhnKnbFueXVKvdEvLe0rESEW3UD/MWizMSh6bNh3ez8jUwrv33bbIs67ou5brpY3jkXrBeGm0bV7WXF3cf0TLaTmhPjK2RSPcThfawar+0JD0rI3doYjTmoPfnmLNc1badVW0Y0DLcj+pWzmfUTXu4Mj0yabLpI/zwYWzinjZi1SrQDvusWxfpkoIAAAAH3RSTlMAHj4Nr2qRzC2B/ltR2KK//f788+nm0/nq6f7fd9/roc+HuwAAB2tJREFUWMPFl2dXE0EUQDekJxB6s+0mCoKuENYAQUxINQkhoaQACR3pSO9dQAQBQbD37t/0zWwaYdVz5Hi832bm7d03O3WJP5CYdFHEI86C3NKsLBCfSZGkBExnSiMdKYwJZ1Gct8isFqvk7wUJ4qatjo6tpkxhXL1IxJ0YX5QsONFrYWnH9evXK1WqzjTpiXpZY6NMzvnpGpXt6ZGcExWZpSAABThUTecUcn6ohSdTAklcWZugoZlt4UkveT+mzEUVpL978FJogogakSKdzzGJjHgAoZN8adpT8tP+MGtQYcX8sobsHleAhJ+kRHBNFp4VtTQqiIRLgw8fPS6xPkCGYaezCSk+9pc8fvSwO01ISPNRtlbO7yk1IYdMMPJw6HFJSYmnEgxbgSLNuBcUpAuqQPJVYFEq2y1JvxgReZKpvZkZ/LQI0SGFd7lI83kYDKQfVz7+tBNoNwlAwIWQEUgkYmZHM1mC6Z+rrKycc+4tOB8ghZ2tnRzaCYgIvjAnkUORTLfaFJlDj9YKWKzbKqDD58OGrWBBiN2dFHnW2wMRRzdyaXrsVWCpIIyxVBWCBJoskYbFNhtdXy3gGJDUMb1e7VgJGvNDeLxRw1N7f7ja6P6m1dH1XArbGBiePXvFmNhQU8C1Bc9jOv3d4xa22sL8cKi1Om4FrVbX1j770srIcGxwcX9gnlV4z33t7nYbUQpWm/5ZbZ1Wx9WRxFRa7ait1Y/a/E6r0WTtP+f3eTtZRee2r9R/Lmgx2f2ulS/aOkedrlccN7B8aeaHwxZHrUM/+sGnqvSl+bwdeI2SLCpV5fUOry/tgWrOdUBr62q01QcfMqW8mBWWs0qP6aEb6tFWJ4pH4C8ZAQpspddWT2trarQ6Hb2aGtkcJNloMLCh5V0phGPIOMK17up6ndZRo4VhoQ9ywvMydxUMjlq1nq5eT7sdoiyeUD0zhRw1yLGaJY+MxoXcV6xhaj1AAVdCXL585fblELiCojTBPtZRp88W8WOHVLGiVtOtU9Mzbg0VC0nGKfY2p/t6q2ld3assSdyZk5r9raW3b2ZmtkqjcdqdbQsvQFBGRhS7e8tO+xL18tzsMXasMheIk4je3LcfgmHD6rLbv99Z+x70DDi/kiHFXmDA0/9kbdLqYeZT8jbA8Y75pohTCFq+W/o3j4Ipn4vNd4C1PKA0rKgKQsl0B9FV/LTUun7Yb2rJivkM4mQhkdxitOQNlA4WFxffQpGWk4pCVFxDDY8gYtDvyTO1phIJAnFyIhgy2pXK/IzcVqtnvLsY0NwCIInTCiNq2YUQkKT0r9rEpmal0iIkFI1KhN3V9LAYU4QCjacVwC0EhGCJ084+eZFIBhMw8EITwozicD8Ko4obWDGJmp6HAwOswkoI85VAgZvp2S3CXL0G5BUCrOIKVuDyJGq6x4btMW4jent7MoyFpcCYLs2d8LiX7/1ScTNOsRjw2FKFSaYCYwYPrVNhIp93vueg9yi4D+1mFGcpBG78SrFbZV2femtPxI+GV3vuFy0skOlNZp9VmG4AsQpcRi1Xi+71WI9m+qrpH0vSmBXCwKrR1feubxxvMhNXgfsxCoBV5F1F7AdnN46mp1pH1bUrwuiW815XVwNpuJnZ441ZFHetkENxH8s3N47crvXeFtgfdNEJKnmvQ2m8q+r2y46P8bue3ARiFEAhbpie2XQ+HXlSTesdajo5ZvfGe5ltsYscds/24dDCUwpIApi1+8iyHQb64dC3xqy07HrUE+b5Fditm9w9b8xm85sbcYonZvPVCZvL10mSdymmBZIYeyuPOU+raegJU3EZbRAf5/1Mj9k8cYNVUEiBDTZXkxfV3KYoJw1J0Km8qOLCIUqDaWig2J22w24uL38ji1HcLy8vd4eOlS5QjKrV+pbYm47EBrshUjTcxorhnnLAHFVMoPJr/5wKJwEK1I/DE3dJMdoNmaKGBgMJbEMSUQU42l7jMrNdCY67oGBQPzL5sQr5e0jDtljR0FCGs7C/jioArJhwea/DQVIGZQ0Dk+Kd9OSJmAUfdGXEUFFRgboCl1Wm5+1EVNHzHr5lhwoMuPzS5tDSmby4W2d2PW1rowyGCoo9uua2h5vmw4rPvvkHKgB3Axixr+g/nLoqCVOz81wUtfDCcDdy/pFhRaQIhqEFikrxDMAZcAqJSCQed7mdGnDEgo8TFpxDits+Lib4fIITiSLH4mnDb40SLeLhXLJb0tGlkRthBmxlpoFx9N5Yxe2wwGCgRgZkymZjlpQ7CakR76by88sU0BW2UF0kUNZFVVQYDEvnEwWNaK9N4nKw/wiNIrjylA5RGJzO3S4Yapi3MN7PqwT80F29XUD87k+AkLtesgJIHd4e5p4TTyZFMz4+ONKQmJA89D8iaIskYgCQoGhZLGHfZcIKgoPkfGVjBkRh5EkgibE8X8oRht8ryIcjVERwcUEQ+2MsF6csaEKOhSoxCKI/cRnpnAauySYOjLx8MZJyXnSGP9WMfIvMKEv4z3/LgnY023hnUUhy8vNlQuJM8BMSeMQ/5ycTeVoye9COGwAAAABJRU5ErkJggg==""></image>",
			@"<image x=""0"" y=""0"" width=""44"" height=""44"" xlink:href=""data:image/png;base64,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""></image>",
			@"<image x=""0"" y=""0"" width=""44"" height=""44"" xlink:href=""data:image/png;base64,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""></image>"
		};*/
		/*private string[,] SVG_BODY = new string[,] {
			{
				@"<g id=""svg_body""><g id=""bg""><path style=""fill:url(#bg_color);"" d=""M8,36.5c-2.5,0-4.5-2-4.5-4.5V12 c0-2.5,2-4.5,4.5-4.5h76c2.5,0,4.5,2,4.5,4.5v20c0,2.5-2,4.5-4.5,4.5H8z""/><path class=""st1"" d=""M84,8c2.2,0,4,1.8,4,4v20c0,2.2-1.8,4-4,4H8c-2.2,0-4-1.8-4-4V12c0-2.2,1.8-4,4-4H84 M84,7.1H8 c-2.7,0-4.9,2.2-4.9,4.9v20c0,2.7,2.2,4.9,4.9,4.9h76c2.7,0,4.9-2.2,4.9-4.9V12C88.9,9.3,86.7,7.1,84,7.1L84,7.1z""/></g><text id=""BadgeName"" transform=""matrix(1 0 0 1 8.0001 29.5367)"" class=""st2 st6 st7"">%CONTENT%</text><g id=""Level_group""><path id=""Level_bg"" class=""st2"" d=""M84,36H60c-2.2,0-4-1.8-4-4V12 c0-2.2,1.8-4,4-4h24c2.2,0,4,1.8,4,4v20C88,34.2,86.2,36,84,36z""/><text id=""Level_num"" transform=""matrix(1 0 0 1 59.3555 30.2247)"" class=""st3 st4 st5"">%LEVEL%</text></g></g>",
				@"<g id=""svg_body""><g id=""bg""><path style=""fill:url(#bg_color);"" d=""M8,36.5c-2.5,0-4.5-2-4.5-4.5V12 c0-2.5,2-4.5,4.5-4.5h98c2.5,0,4.5,2,4.5,4.5v20c0,2.5-2,4.5-4.5,4.5H8z""/><path class=""st1"" d=""M106,8c2.2,0,4,1.8,4,4v20c0,2.2-1.8,4-4,4H8c-2.2,0-4-1.8-4-4V12c0-2.2,1.8-4,4-4H106 M106,6.9H8 c-2.8,0-5.1,2.3-5.1,5.1v20c0,2.8,2.3,5.1,5.1,5.1h98c2.8,0,5.1-2.3,5.1-5.1V12C111.1,9.2,108.8,6.9,106,6.9L106,6.9z""/></g><text id=""BadgeName"" transform=""matrix(1 0 0 1 8.0001 29.5367)"" class=""st2 st6 st7"">%CONTENT%</text><g id=""Level_group""><path id=""Level_bg"" class=""st2"" d=""M106,36H82c-2.2,0-4-1.8-4-4V12 c0-2.2,1.8-4,4-4h24c2.2,0,4,1.8,4,4v20C110,34.2,108.2,36,106,36z""/><text id=""Level_num"" transform=""matrix(1 0 0 1 81.3555 30.2247)"" class=""st3 st4 st5"">%LEVEL%</text></g></g>",
				@"<g id=""svg_body""><g id=""bg""><path style=""fill:url(#bg_color);"" d=""M8,36.6c-2.5,0-4.6-2-4.6-4.6V12c0-2.5,2-4.6,4.6-4.6h114c2.5,0,4.6,2,4.6,4.6v20c0,2.5-2,4.6-4.6,4.6H8z""/><path class=""st1"" d=""M122,8c2.2,0,4,1.8,4,4v20c0,2.2-1.8,4-4,4H8c-2.2,0-4-1.8-4-4V12c0-2.2,1.8-4,4-4H122 M122,6.9H8 c-2.8,0-5.1,2.3-5.1,5.1v20c0,2.8,2.3,5.1,5.1,5.1h114c2.8,0,5.1-2.3,5.1-5.1V12C127.1,9.2,124.8,6.9,122,6.9L122,6.9z""/></g><text id=""BadgeName"" transform=""matrix(1 0 0 1 8 29.5367)"" class=""st2 st6 st7"">%CONTENT%</text><g id=""Level_group""><path id=""Level_bg"" class=""st2"" d=""M122,36H98c-2.2,0-4-1.8-4-4V12 c0-2.2,1.8-4,4-4h24c2.2,0,4,1.8,4,4v20C126,34.2,124.2,36,122,36z""/><text id=""Level_num"" transform=""matrix(1 0 0 1 97.3555 30.2247)"" class=""st3 st4 st5"">%LEVEL%</text></g></g>"
			},
			{
				@"<g id=""svg_body""><g id=""bg""><path style=""fill:url(#bg_color);"" d=""M26,36.5c-2.5,0-4.5-2-4.5-4.5V12 c0-2.5,2-4.5,4.5-4.5h98c2.5,0,4.5,2,4.5,4.5v20c0,2.5-2,4.5-4.5,4.5H26z""/><path class=""st1"" d=""M124,8c2.2,0,4,1.8,4,4v20c0,2.2-1.8,4-4,4H26c-2.2,0-4-1.8-4-4V12c0-2.2,1.8-4,4-4H124 M124,6.9H26 c-2.8,0-5.1,2.3-5.1,5.1v20c0,2.8,2.3,5.1,5.1,5.1h98c2.8,0,5.1-2.3,5.1-5.1V12C129.1,9.2,126.8,6.9,124,6.9L124,6.9z""/></g><text id=""BadgeName"" transform=""matrix(1 0 0 1 48.0001 29.5367)"" class=""st2 st6 st7"">%CONTENT%</text><g id=""Level_group""><path id=""Level_bg"" class=""st2"" d=""M124,36h-24c-2.2,0-4-1.8-4-4V12 c0-2.2,1.8-4,4-4h24c2.2,0,4,1.8,4,4v20C128,34.2,126.2,36,124,36z""/><text id=""Level_num"" transform=""matrix(1 0 0 1 99.3555 30.2247)"" class=""st3 st4 st5"">%LEVEL%</text></g></g>",
				@"<g id=""svg_body""><g id=""bg""><path style=""fill:url(#bg_color);"" d=""M26,36.6c-2.5,0-4.6-2.1-4.6-4.6V12 c0-2.5,2.1-4.6,4.6-4.6h120c2.5,0,4.6,2.1,4.6,4.6v20c0,2.5-2.1,4.6-4.6,4.6H26z""/><path class=""st1"" d=""M146,8c2.2,0,4,1.8,4,4v20c0,2.2-1.8,4-4,4H26c-2.2,0-4-1.8-4-4V12c0-2.2,1.8-4,4-4H146 M146,6.8H26 c-2.8,0-5.2,2.3-5.2,5.2v20c0,2.8,2.3,5.2,5.2,5.2h120c2.8,0,5.2-2.3,5.2-5.2V12C151.2,9.2,148.8,6.8,146,6.8L146,6.8z""/></g><text id=""BadgeName"" transform=""matrix(1 0 0 1 48.0001 29.5367)"" class=""st2 st6 st7"">%CONTENT%</text><g id=""Level_group""><path id=""Level_bg"" class=""st2"" d=""M146,36h-24c-2.2,0-4-1.8-4-4V12 c0-2.2,1.8-4,4-4h24c2.2,0,4,1.8,4,4v20C150,34.2,148.2,36,146,36z""/><text id=""Level_num"" transform=""matrix(1 0 0 1 121.3555 30.2247)"" class=""st3 st4 st5"">%LEVEL%</text></g></g>",
				@"<g id=""svg_body""><g id=""bg""><path style=""fill:url(#bg_color);"" d=""M26,36.6c-2.5,0-4.6-2.1-4.6-4.6V12c0-2.5,2.1-4.6,4.6-4.6h136c2.5,0,4.6,2.1,4.6,4.6v20 c0,2.5-2.1,4.6-4.6,4.6H26z""/><path class=""st1"" d=""M162,8c2.2,0,4,1.8,4,4v20c0,2.2-1.8,4-4,4H26c-2.2,0-4-1.8-4-4V12c0-2.2,1.8-4,4-4H162 M162,6.8H26 c-2.9,0-5.2,2.3-5.2,5.2v20c0,2.9,2.3,5.2,5.2,5.2h136c2.9,0,5.2-2.3,5.2-5.2V12C167.2,9.1,164.9,6.8,162,6.8L162,6.8z""/></g><text id=""BadgeName"" transform=""matrix(1 0 0 1 48.0001 29.5367)"" class=""st2 st6 st7"">%CONTENT%</text><g id=""Level_group""><path id=""Level_bg"" class=""st2"" d=""M162,36h-24c-2.2,0-4-1.8-4-4V12 c0-2.2,1.8-4,4-4h24c2.2,0,4,1.8,4,4v20C166,34.2,164.2,36,162,36z""/><text id=""Level_num"" transform=""matrix(1 0 0 1 137.3385 30.2247)"" class=""st3 st4 st5"">%LEVEL%</text></g></g>"
			}
		};*/

		public BilibiliChatBadge() { }
		public BilibiliChatBadge(string json)
		{
			var obj = JSON.Parse(json);
			if (obj.TryGetKey(nameof(Id), out var id))
			{ Id = id.Value; }
			if (obj.TryGetKey(nameof(Name), out var name))
			{ Name = name.Value; }
			if (obj.TryGetKey(nameof(Uri), out var uri))
			{ Uri = uri.Value; }
			if (obj.TryGetKey(nameof(Color), out var color))
			{ Color = color.Value; }
			if (obj.TryGetKey(nameof(Level), out var level))
			{ Level = level.AsInt; }
			if (obj.TryGetKey(nameof(Guard), out var guard))
			{ Guard = guard.AsInt; }
		}
		public JSONObject ToJson()
		{
			var obj = new JSONObject();
			obj.Add(nameof(Id), new JSONString(Id));
			obj.Add(nameof(Name), new JSONString(Name));
			obj.Add(nameof(Uri), new JSONString(Uri));
			obj.Add(nameof(Color), new JSONString(Color));
			obj.Add(nameof(Level), new JSONNumber(Level));
			obj.Add(nameof(Guard), new JSONNumber(Guard));
			return obj;
		}

		public void genImage()
		{
			Task.Run(() =>
			{
				var scale = 3;
				var offset = Guard == 0 ? new int[2] { 2, 8 } : new int[2] { 22, 8 };
				var offset_badge_name = Guard == 0 ? new int[2] { 6, 30 } : new int[2] { 44, 30 };
				var offset_level = Guard == 0 ? new int[2] { 8, 24 } : new int[2] { 46, 62 };
				var width = Guard == 0 ? new int[2] { 14, 18 } : new int[2] { -6, 18 };
				var sb = new StringBuilder(SVG_FRAME);
				var nameLength = getNameLengthAsync().Result;
				sb.Replace("%ImageWidth%", (offset_level[1] + width[1] + nameLength).ToString());
				sb.Replace("%OFFSET_X%", offset[0].ToString());
				sb.Replace("%OFFSET_Y%", offset[1].ToString());
				sb.Replace("%OFFSET_BADGE_NAME_X%", offset_badge_name[0].ToString());
				sb.Replace("%OFFSET_BADGE_NAME_Y%", offset_badge_name[1].ToString());
				sb.Replace("%OFFSET_Level_0%", (offset_level[0] + nameLength).ToString());
				sb.Replace("%OFFSET_Level_1%", (offset_level[1] + nameLength).ToString());
				sb.Replace("%WIDTH_1%", (offset_level[1] + width[0] + nameLength).ToString());
				sb.Replace("%BorderColor%", BorderColor);
				sb.Replace("%LinearGradientColorA%", LinearGradientColorA);
				sb.Replace("%LinearGradientColorB%", LinearGradientColorB);
				var GuardImageBase64 = Guard == 0 ? "" : ImageUtils.AddBase64DataType(ImageUtils.Base64fromResourceImg($"BilibiliLiveGuard{Guard}_full.png"));
				sb.Replace("%GuardImage%", Guard == 0 ? "" : $"<image x=\"0\" y=\"0\" width=\"44\" height=\"44\" xlink:href=\"{GuardImageBase64}\"/>");
				sb.Replace("%CONTENT%", Name);
				sb.Replace("%LEVEL%", Level.ToString());

				var path = (new PathProvider()).GetBadgesImagePath();
				var badgeId = Name + "_" + Level.ToString() + (Guard == 3 ? "_舰长" : (Guard == 2 ? "_提督" : (Guard == 1 ? "_总督" : "")));
				// 生成唯一的文件名以避免并发冲突
				var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
				var validBadgeId = ImageUtils.convertToValidFilename(badgeId);
				// 如果转换后的文件名为空，使用默认名称
				if (string.IsNullOrWhiteSpace(validBadgeId))
				{
					validBadgeId = "badge";
				}
				var filename = Path.Combine(path, $"{validBadgeId}_{uniqueId}.svg");
				var imagename = Path.Combine(path, $"{validBadgeId}.png");

				if (!Directory.Exists(path))
				{
					Directory.CreateDirectory(path);
				}
				using (var writer = new StreamWriter(filename))
				{
					writer.WriteLine(sb.ToString());
				}

				try
				{
					ImageUtils.genImg(filename.ToString(), imagename.ToString(), (offset_level[1] + width[1] + nameLength) * scale, 44 * scale, true);
				}
				finally
				{
					// 确保临时 SVG 文件被删除
					try
					{
						if (File.Exists(filename))
						{
							File.Delete(filename);
						}
					}
					catch
					{
						// 忽略删除失败的情况
					}
				}
				Uri = (new System.Uri(imagename)).AbsoluteUri;
				Id = badgeId;
			});
		}

		private async Task<int> getNameLengthAsync()
		{
			var count = await Task.Run(() =>
			{
				var count = 0f;
				for (var i = 0; i < Name.Length; i++)
				{
					var rx_ch = new Regex("^[\u4e00-\u9fa5]$");
					var rx_en_full = new Regex("[WM]");
					var rx_en_twoThird = new Regex("[ABCDGHKNOPQRUVXYZbdghkmnopqw023456789]");
					var rx_en_half = new Regex("[EFJLSTacesuvxyz]");
					var rx_en_quarter = new Regex("[Ifijlrt1]");
					if (rx_ch.IsMatch(Name.Substring(i, 1)) || rx_en_full.IsMatch(Name.Substring(i, 1)))
					{
						count += 1f;
					}
					else if (rx_en_twoThird.IsMatch(Name.Substring(i, 1)))
					{
						count += 2 / 3f;
					}
					else if (rx_en_half.IsMatch(Name.Substring(i, 1)))
					{
						count += 0.5f;
					}
					else if (rx_en_quarter.IsMatch(Name.Substring(i, 1)))
					{
						count += 0.25f;
					}
					else
					{
						count += 1f;
					}
				}
				return count;
			});
			return (int)Math.Ceiling(22 * count);
		}

		public void setMedalColorByLevel(int level, int guardLevel = 0) {
			switch (level)
			{
				
				case 1:
				case 2:
				case 3:
				case 4:
					LinearGradientColorA = "#5c968e";
					LinearGradientColorB = "#5c968e";
					BorderColor = "#5c968e";
					break;
				case 5:
				case 6:
				case 7:
				case 8:
					LinearGradientColorA = "#5d7b9e";
					LinearGradientColorB = "#5d7b9e";
					BorderColor = "#5d7b9e";
					break;
				case 9:
				case 10:
				case 11:
				case 12:
					LinearGradientColorA = "#8d7ca6";
					LinearGradientColorB = "#8d7ca6";
					BorderColor = "#8d7ca6";
					break;
				case 13:
				case 14:
				case 15:
				case 16:
					LinearGradientColorA = "#be6686";
					LinearGradientColorB = "#be6686";
					BorderColor = "#be6686";
					break;
				case 17:
				case 18:
				case 19:
				case 20:
					LinearGradientColorA = "#c79d24";
					LinearGradientColorB = "#c79d24";
					BorderColor = "#c79d24";
					break;
				case 21:
				case 22:
				case 23:
				case 24:
					LinearGradientColorA = "#529d92";
					LinearGradientColorB = "#1a544b";
					BorderColor = "#1a544b";
					break;
				case 25:
				case 26:
				case 27:
				case 28:
					LinearGradientColorA = "#6888f1";
					LinearGradientColorB = "#06154c";
					BorderColor = "#06154c";
					break;
				case 29:
				case 30:
				case 31:
				case 32:
					LinearGradientColorA = "#9d9bff";
					LinearGradientColorB = "#06154c";
					BorderColor = "#06154c";
					break;
				case 33:
				case 34:
				case 35:
				case 36:
					LinearGradientColorA = "#e986bb";
					LinearGradientColorB = "#7a0423";
					BorderColor = "#7a0423";
					break;
				case 37:
				case 38:
				case 39:
				case 40:
					// Not correct
					LinearGradientColorA = "#ffa869";
					LinearGradientColorB = "#fe7645";
					BorderColor = "#fe7645";
					break;
				default:
					LinearGradientColorA = "#000000";
					LinearGradientColorB = "#FFFFFF";
					BorderColor = "#FFFFFF";
					break;
			}

			switch (guardLevel)
			{
				case 1:
				case 2:
					BorderColor = "#ffe854";
					break;
				case 3:
					BorderColor = "#67e8ff";
					break;
			}


			// Medal Data from Bilibili Live
			// | Level | Guard | ColorStart | ColorEnd | Border |
			// | ----- | ----- | ---------- | -------- | ------- |
			// |   1   |   0   | #5c968e    | #5c968e  | #5c968e |
			// |   2   |   0   | #5c968e    | #5c968e  | #5c968e |
			// |   3   |   0   | #5c968e    | #5c968e  | #5c968e |
			// |   4   |   0   | #5c968e    | #5c968e  | #5c968e |
			// |   5   |   0   | #5d7b9e    | #5d7b9e  | #5d7b9e |
			// |   6   |   0   | #5d7b9e    | #5d7b9e  | #5d7b9e |
			// |   7   |   0   | #5d7b9e    | #5d7b9e  | #5d7b9e |
			// |   8   |   0   | #5d7b9e    | #5d7b9e  | #5d7b9e |
			// |   9   |   0   | #8d7ca6    | #8d7ca6  | #8d7ca6 |
			// |  10   |   0   | #8d7ca6    | #8d7ca6  | #8d7ca6 |
			// |  11   |   0   | #8d7ca6    | #8d7ca6  | #8d7ca6 |
			// |  12   |   0   | #8d7ca6    | #8d7ca6  | #8d7ca6 |
			// |  13   |   0   | #be6686    | #be6686  | #be6686 |
			// |  14   |   0   | #be6686    | #be6686  | #be6686 |
			// |  15   |   0   | #be6686    | #be6686  | #be6686 |
			// |  16   |   0   | #be6686    | #be6686  | #be6686 |
			// |  17   |   0   | #c79d24    | #c79d24  | #c79d24 |
			// |  18   |   0   | #c79d24    | #c79d24  | #c79d24 |
			// |  19   |   0   | #c79d24    | #c79d24  | #c79d24 |
			// |  20   |   0   | #c79d24    | #c79d24  | #c79d24 |
			// |  21   |   0   | #1a544b    | #529d92  | #1a544b |
			// |  21   |   3   | #1a544b    | #529d92  | #67e8ff |
			// |  22   |   0   | #1a544b    | #529d92  | #1a544b |
			// |  22   |   3   | #1a544b    | #529d92  | #67e8ff |
			// |  23   |   0   | #1a544b    | #529d92  | #1a544b |
			// |  23   |   3   | #1a544b    | #529d92  | #67e8ff |
			// |  24   |   0   | #1a544b    | #529d92  | #1a544b |
			// |  24   |   3   | #1a544b    | #529d92  | #67e8ff |
			// |  25   |   0   | #06154c    | #6888f1  | #06154c |
			// |  25   |   2   | #06154c    | #6888f1  | #ffe854 |
			// |  25   |   3   | #06154c    | #6888f1  | #67e8ff |
			// |  26   |   0   | #06154c    | #6888f1  | #06154c |
			// |  26   |   2   | #06154c    | #6888f1  | #ffe854 |
			// |  26   |   3   | #06154c    | #6888f1  | #67e8ff |
			// |  27   |   0   | #06154c    | #6888f1  | #06154c |
			// |  27   |   2   | #06154c    | #6888f1  | #ffe854 |
			// |  27   |   3   | #06154c    | #6888f1  | #67e8ff |
			// |  28   |   0   | #06154c    | #6888f1  | #06154c |
			// |  28   |   2   | #06154c    | #6888f1  | #ffe854 |
			// |  28   |   3   | #06154c    | #6888f1  | #67e8ff |
			// |  29   |   0   | #2d0855    | #9d9bff  | #2d0855 |
			// |  29   |   1   | #2d0855    | #9d9bff  | #ffe854 |
			// |  29   |   2   | #2d0855    | #9d9bff  | #ffe854 |
			// |  29   |   3   | #2d0855    | #9d9bff  | #67e8ff |
			// |  30   |   1   | #2d0855    | #9d9bff  | #ffe854 |
			// |  30   |   2   | #2d0855    | #9d9bff  | #ffe854 |
			// |  30   |   3   | #2d0855    | #9d9bff  | #67e8ff |
			// |  31   |   1   | #2d0855    | #9d9bff  | #ffe854 |
			// |  31   |   2   | #2d0855    | #9d9bff  | #ffe854 |
			// |  31   |   3   | #2d0855    | #9d9bff  | #67e8ff |
			// |  33   |   1   | #7a0423    | #e986bb  | #ffe854 |
			// |  33   |   2   | #7a0423    | #e986bb  | #ffe854 |

		}
	}
}
