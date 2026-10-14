# ソースからビルドする

公開リポジトリにはゲーム本体・Unity・MelonLoaderの参照DLLを含めません。自分のSynth Riders環境で参照ファイルを準備してください。ゲームのexeだけでは参照DLLを用意できませんが、参照DLLを準備した後のビルドにexeは不要です。

## 必要なもの

- Windows x64と、`net6.0` をビルドできる.NET SDK（本プロジェクトは.NET 6を対象とします）。
- Windows PC版Synth RidersとMelonLoader。現在のビルド参照版はMelonLoader 0.7.3です。
- MelonLoaderを導入してゲームを一度起動し、生成を完了した `MelonLoader/Il2CppAssemblies`。
- 同じMelonLoader環境の `MelonLoader/net6`。

生成元のゲームとMelonLoaderのバージョンを揃えてください。ゲーム更新後は生成済み参照DLLも更新します。exe・ゲーム素材・`Cpp2IL/cpp2il_out` のコピーは不要です。

## 開発PCでビルドし、ゲームPCで動作確認する

ゲームPCから、同じMelonLoaderの次の2フォルダーを開発PCへコピーします。公開リポジトリ・配布ZIPには追加しません。

```text
SynthRidersFullBody/
└─ Libs/                         # ローカルのみ。Gitの除外対象
   └─ MelonLoader/
      ├─ net6/
      │  ├─ MelonLoader.dll
      │  ├─ Il2CppInterop.Common.dll
      │  ├─ Il2CppInterop.Runtime.dll
      │  └─ ...
      └─ Il2CppAssemblies/
         ├─ Assembly-CSharp.dll
         ├─ Assembly-CSharp-firstpass.dll
         ├─ Il2CppVRM.dll
         ├─ Il2CppVRM10.dll
         ├─ UnityEngine.CoreModule.dll
         └─ ...
```

この配置では次だけでビルドできます。

v0.51.0の表情操作は `Il2CppVRM.dll` と `Il2CppVRM10.dll` も参照します。足りない場合はゲームPCで生成された `Il2CppAssemblies` フォルダー全体をコピーしてください。

```powershell
dotnet build .\SynthRidersFullBody.sln -c Release
```

参照先を設定する `.props` ファイルは不要です。Visual Studioでも、ソリューションを開いてRelease構成でビルドすると、この配置のDLLを自動検出します。

ビルドした `SynthRidersFullBody/bin/Release/net6.0/SynthRidersFullBody.dll` をゲームPCへコピーし、ゲームの `Mods` に配置して動作確認します。ゲーム更新やMelonLoader更新時は、開発PCの参照DLLもゲームPCからコピーし直します。

## ゲームをインストールしているPCでビルドする

ゲームのインストール先から直接参照する場合は、コマンドにパスを指定します。

```powershell
dotnet build .\SynthRidersFullBody.sln -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\SynthRiders"
```

## 参照DLLを別の場所へ置く場合

リポジトリの外へ置く場合は、コピー先の `MelonLoader` を含む親フォルダーを `GameDir` に指定します。

```powershell
dotnet build .\SynthRidersFullBody.sln -c Release -p:GameDir="C:\SynthRidersRefs"
```

参照の置き場所を別々にする場合は、**両方**のパスを指定します。`MelonDir` は `net6` の親フォルダーです。

```powershell
dotnet build .\SynthRidersFullBody.sln -c Release -p:MelonDir="C:\SynthRidersRefs\MelonLoader" -p:UnityAssembliesDir="C:\SynthRidersRefs\Il2CppAssemblies"
```

## 出力と動作確認

出力先は `SynthRidersFullBody/bin/Release/net6.0/` です。ゲームの `Mods` へ配置するファイルは `SynthRidersFullBody.dll` です。Unity・ゲーム・MelonLoaderの参照DLLはコピーしません。

ライセンス・第三者表記のテキストもビルド出力へコピーします。配布ZIPを作る方法は [公開・配布手順](PUBLISHING.md) を参照してください。

ゲーム参照DLLを使わない計算・設定テストは、次で実行できます。

```powershell
dotnet run --project .\tests\AvatarPoseMath\AvatarPoseMath.csproj -c Release
dotnet run --project .\tests\ConfigSmoke\ConfigSmoke.csproj -c Release
```

ビルドや上記テストの成功は、VR機器での動作確認を意味しません。実機確認は [チェックリスト](../REAL_DEVICE_CHECKLIST.md) を使用してください。
