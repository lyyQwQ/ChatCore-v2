# ChatCore Agent 指南（中文版）

本文件是 `AGENTS.md` 的中文翻译版，主要提供项目结构、代码风格及设计思路的概要，便于中文读者使用。

## 同步声明
- 本文档是对 `AGENTS.md`的简单翻译，以中文提供。
- 请在阅读代码时忽略此文件，如果对指南文档有修改，必须先更改 `AGENTS.md`，然后将修改同步到本文档。

## 项目概述
ChatCore 是一个 .NET 多项目解决方案，提供 Twitch 和 Bilibili 聊天服务的共用客户端库。主要包含：
- `ChatCore`：基于 .NET Standard 2.0 的核心库，实现 Twitch 和 Bilibili 等服务，包含配置与日志功能。
- `ChatCoreGUI`：Windows Forms 示例程序，用于配置并示范使用。
- `ChatCoreTester`：用于手动测试的示例程序。
- `ChatCoreSVG`：从 SVG 生成图像的工具项目。

## 项目架构
以下为仓库目录结构（展示至四层）：
```text
    .
    ├── AGENTS.md
    ├── ChatCore
    │   ├── ChatCore.csproj
    │   ├── ChatCoreInstance.cs
    │   ├── Config
    │   ├── Exceptions
    │   ├── Interfaces
    │   ├── Libs
    │   ├── Logging
    │   ├── Models
    │   ├── Resources
    │   ├── Services
    │   ├── Utilities
    │   └── internalize_excludes.txt
    ├── ChatCore.sln
    ├── ChatCoreGUI
    │   ├── App.config
    │   ├── ChatCoreGUI.csproj
    │   ├── Form1.Designer.cs
    │   ├── Form1.cs
    │   ├── Form1.resx
    │   ├── Form1.zh-CN.resx
    │   ├── MultiLanguage.cs
    │   ├── Program.cs
    │   └── Properties
    ├── ChatCoreSVG
    │   ├── ChatCoreSVG.csproj
    │   ├── Properties
    │   ├── SVG.cs
    │   ├── app.config
    │   └── packages.config
    ├── ChatCoreTester
    │   ├── App.config
    │   ├── ChatCoreTester.csproj
    │   ├── ChatCoreTester.csproj.user
    │   ├── Form1.Designer.cs
    │   ├── Form1.cs
    │   ├── Form1.resx
    │   ├── Program.cs
    │   └── Properties
    ├── LICENSE
    ├── README.md
    ├── agents_cn.md
    
    17 directories, 28 files

    ChatCore
    ├── ChatCore.csproj
    ├── ChatCoreInstance.cs
    ├── Config
    │   ├── ConfigBase.cs
    │   ├── ConfigHeader.cs
    │   ├── ConfigMeta.cs
    │   ├── ConfigSection.cs
    │   ├── HTMLIgnore.cs
    │   ├── ObjectSerializer.cs
    │   └── StreamCoreConfigConverter.cs
    ├── Exceptions
    │   └── ChatCoreNotInitializedException.cs
    ├── Interfaces
    │   ├── IBApiClient.cs
    │   ├── IChatBadge.cs
    │   ├── IChatChannel.cs
    │   ├── IChatEmote.cs
    │   ├── IChatMessage.cs
    │   ├── IChatMessageHandler.cs
    │   ├── IChatMessageParser.cs
    │   ├── IChatResourceData.cs
    │   ├── IChatResourceProvider.cs
    │   ├── IChatService.cs
    │   ├── IChatServiceManager.cs
    │   ├── IChatUser.cs
    │   ├── IDefaultBrowserLauncherService.cs
    │   ├── IEmojiParser.cs
    │   ├── IOpenBLiveProvider.cs
    │   ├── IPathProvider.cs
    │   ├── IShortcodeAuthProvider.cs
    │   ├── IUserAuthProvider.cs
    │   ├── IWebLoginProvider.cs
    │   ├── IWebSocketServerService.cs
    │   └── IWebSocketService.cs
    ├── Libs
    │   ├── BrotliSharpLib.dll
    │   ├── SuperSocket.ClientEngine.dll
    │   ├── WebSocket4Net.dll
    │   └── websocket-sharp.dll
    ├── Logging
    │   ├── CustomLogLevel.cs
    │   ├── CustomLoggerSink.cs
    │   └── CustomSinkProvider.cs
    ├── Models
    │   ├── BiliBili
    │   │   ├── BiliBiliChatBadge.cs
    │   │   ├── BiliBiliChatChannel.cs
    │   │   ├── BiliBiliChatEmote.cs
    │   │   ├── BiliBiliChatMessage.cs
    │   │   ├── BiliBiliChatUser.cs
    │   │   ├── BiliBiliDataView.cs
    │   │   ├── BiliBiliPacket.cs
    │   │   └── DanmakuMessage.cs
    │   ├── Bilibili
    │   │   ├── BilibiliChatGiftTimer.cs
    │   │   ├── BilibiliChatMessageExtra.cs
    │   │   └── OpenBLive
    │   │       ├── AnchorInfo.cs
    │   │       ├── AppStartInfo.cs
    │   │       ├── Dm.cs
    │   │       ├── EmptyInfo.cs
    │   │       ├── GameIds.cs
    │   │       ├── Guard.cs
    │   │       ├── SendGift.cs
    │   │       ├── SuperChat.cs
    │   │       ├── SuperChatDel.cs
    │   │       └── UserInfo.cs
    │   ├── ChatResourceData.cs
    │   ├── CookieInfo.cs
    │   ├── Emoji.cs
    │   ├── EmoteType.cs
    │   ├── ImageRect.cs
    │   ├── LoginCredentials.cs
    │   ├── OAuth
    │   │   ├── OAuthCredentials.cs
    │   │   └── OAuthShortcodeRequest.cs
    │   ├── Twitch
    │   │   ├── TwitchBadge.cs
    │   │   ├── TwitchChannel.cs
    │   │   ├── TwitchCheermoteData.cs
    │   │   ├── TwitchEmote.cs
    │   │   ├── TwitchMessage.cs
    │   │   ├── TwitchRoomstate.cs
    │   │   └── TwitchUser.cs
    │   ├── UnknownChatBadge.cs
    │   ├── UnknownChatChannel.cs
    │   ├── UnknownChatEmote.cs
    │   ├── UnknownChatMessage.cs
    │   ├── UnknownChatUser.cs
    │   └── WebSocketServerBehavior.cs
    ├── Resources
    │   └── Web
    │       ├── index.html
    │       ├── overlay.html
    │       └── statics
    │           ├── css
    │           ├── fonts
    │           ├── images
    │           ├── js
    │           └── lang
    ├── Services
    │   ├── BiliBili
    │   │   ├── BLive
    │   │   │   ├── BApi.cs
    │   │   │   ├── BApiClient.cs
    │   │   │   └── InteractivePlayHeartBeat.cs
    │   │   ├── BiliBiliService.cs
    │   │   ├── BiliBiliServiceManager.cs
    │   │   ├── BilibiliLoginProvider.cs
    │   │   └── OpenBLiveProvider.cs
    │   ├── ChatServiceBase.cs
    │   ├── ChatServiceManager.cs
    │   ├── ChatServiceMultiplexer.cs
    │   ├── DefaultBrowserLauncherService.cs
    │   ├── FrwTwemojiParser.cs
    │   ├── MainSettingsProvider.cs
    │   ├── PathProvider.cs
    │   ├── Twitch
    │   │   ├── BTTVDataProvider.cs
    │   │   ├── FFZDataProvider.cs
    │   │   ├── TwitchBadgeProvider.cs
    │   │   ├── TwitchCheermoteProvider.cs
    │   │   ├── TwitchDataProvider.cs
    │   │   ├── TwitchMessageParser.cs
    │   │   ├── TwitchService.cs
    │   │   └── TwitchServiceManager.cs
    │   ├── UserAuthProvider.cs
    │   ├── WebLoginProvider.cs
    │   ├── WebSocket4NetServiceProvider.cs
    │   └── WebSocketServerProvider.cs
    ├── Utilities
    │   ├── BLive
    │   │   ├── BigEndianBitConverter.cs
    │   │   ├── EndianBitConverter.cs
    │   │   ├── HttpUtility.cs
    │   │   ├── LittleEndianBitConverter.cs
    │   │   ├── Logger.cs
    │   │   ├── SignUtility.cs
    │   │   └── SingleConverter.cs
    │   ├── ChatUtils.cs
    │   ├── CryptoUtils.cs
    │   ├── DictionaryUtils.cs
    │   ├── HttpClientUtils.cs
    │   ├── ImageUtils.cs
    │   ├── ObjectUtils.cs
    │   ├── SimpleJson.cs
    │   ├── StringUtils.cs
    │   └── TimeUtils.cs
    └── internalize_excludes.txt
    
    26 directories, 124 files
```

依赖关系示意：
```
ChatCoreGUI  -->  ChatCore  <--  ChatCoreTester
                     ^
                     |
                 ChatCoreSVG
```
项目通过 `Microsoft.Extensions.DependencyInjection` 实现依赖注入，服务以 `ChatServiceMultiplexer` 为中心转发消息。

## 设计模式
- **服务/提供器模式**：每个聊天服务都实现 `IChatService`，并由对应的服务管理器管理。
- **事件驱动**：`ChatServiceBase` 提供多种事件，与 `DictionaryUtils` 配合使用以管理并发回调。
- **依赖注入**：`ChatCoreInstance.Create()` 构建 `ServiceProvider`，配置所有服务。
- **多流程处理**：`ChatServiceMultiplexer` 聚合多个服务，并可动态开启或关闭。
- **配置文件**：`ConfigBase<T>` 通过 `FileSystemWatcher`监听文件更新，自动重新加载。
- **日志记录**：`Logging` 相关文件定义了自定义日志级别与打印接口。

## 代码风格
项目通过 `.editorconfig` 约束编程风格：
- 缩进使用 **制表符**（indent_style = tab），并以 tab 位定字节数。
- 最大行宽为 **200** 字符，并使用 LF 终端符。
- 去掉行末空格，`using` 以 System 测过组存写。
- C# 风格兼顾使用 var 及映射式方法，常量和 static readonly 字段使用全大写名称。

## 编译与测试
主项目属于 `ChatCore.sln`，编译需要取用 NuGet 包，在无网环境下可能以失败结束：
```bash
# 编译示例
 dotnet build -c Release
```
当前项目没有自动化测试项目。

