# ChatCore Agent Guidelines (English)

This document summarizes the project structure, code style and design considerations for contributors.

## Synchronization Note
- This document is written in **English**.
- A Chinese translation is available in `agents_cn.md`.
- When navigating the codebase, ignore `agents_cn.md` unless you intend to modify the guidelines.
- **Any changes to `AGENTS.md` must also be reflected in `agents_cn.md`.**

## Project Overview
ChatCore is a multi-project .NET solution providing a shared chat client library for Twitch and Bilibili services. The repository contains:
- `ChatCore`: Main library targeting .NET Standard 2.0 with implementations for Twitch and Bilibili, configuration helpers and logging utilities.
- `ChatCoreGUI`: Windows Forms application demonstrating usage and configuration.
- `ChatCoreTester`: Example application used for manual testing.
- `ChatCoreSVG`: Helper project for generating images from SVG assets.

## Project Architecture
Below is an overview of the repository layout (showing four levels where applicable):
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

A simplified dependency view:
```
ChatCoreGUI  -->  ChatCore  <--  ChatCoreTester
                     ^
                     |
                 ChatCoreSVG
```
Dependency injection is implemented via `Microsoft.Extensions.DependencyInjection`. Services subscribe to events through a multiplexer (`ChatServiceMultiplexer`) which routes messages from Twitch and Bilibili providers to consumers.

## Design Patterns and Concepts
- **Service and Provider Pattern**: Each chat service (Twitch, Bilibili) implements `IChatService` and is managed by a corresponding service manager.
- **Event Driven Architecture**: `ChatServiceBase` exposes events (e.g., `OnTextMessageReceived`) stored in thread-safe `ConcurrentDictionary` collections. `DictionaryUtils` provides helpers for safely adding, removing and invoking callbacks.
- **Dependency Injection**: `ChatCoreInstance.Create()` builds a `ServiceProvider` that configures all services and utilities.
- **Multiplexer**: `ChatServiceMultiplexer` aggregates multiple services and forwards events to registered consumers, enabling/disabling individual providers dynamically.
- **Configuration Files**: `ConfigBase<T>` wraps file-based configuration using `ObjectSerializer` and `FileSystemWatcher` to reload on changes.
- **Logging**: Custom logging levels defined in `Logging/CustomLogLevel.cs` are integrated with `Microsoft.Extensions.Logging` via `CustomSinkProvider`.

## Code Style
The repository enforces coding conventions via `.editorconfig`:
- Use **tabs** for indentation (`indent_style = tab` and `indent_size = tab`).
- Maximum line length is **200** characters.
- Trailing whitespace is trimmed and lines use **LF** endings.
- Organize `using` directives with `System` namespaces first.
- C# style preferences encourage braces, `var` usage, and expression-bodied members where appropriate.
- Constant and static readonly fields are expected to be **UPPER_CASE**.

## Build and Testing
The solution file is `ChatCore.sln`. Building requires network access to restore NuGet packages:
```bash
# Build (may fail without network)
dotnet build -c Release
```
There are currently no automated test projects.

