# 第三者ソフトウェアについて

MOD独自のコードとドキュメントの利用条件はルートの [LICENSE](LICENSE)（MIT）です。以下の第三者コード・外部依存には、それぞれの権利者の条件が適用されます。MITライセンスはゲーム本体・参照DLL・アバターの再配布許可を付与するものではありません。

## 同梱しているコード：OpenVR C# bindings

- 出典：[ValveSoftware/openvr — headers/openvr_api.cs](https://github.com/ValveSoftware/openvr/blob/master/headers/openvr_api.cs)
- 対象：`SynthRidersFullBody/OpenVrApi.cs`
- 著作権：Copyright (c) 2015, Valve Corporation. All rights reserved.
- ライセンス：BSD 3-Clause。[原文](https://github.com/ValveSoftware/openvr/blob/master/LICENSE)
- ローカルの原文：ソースでは `SynthRidersFullBody/OpenVrLicense.txt`、配布ZIPでは `OpenVrLicense.txt`。

ソース先頭のValveの著作権表示を保持してください。OpenVRライセンス全文はMODのDLLにも埋め込みます。配布時は、読み取れるテキストとして `OpenVrLicense.txt` も同梱してください。Valveや貢献者が本MODを推奨・承認しているという表現は使用しません。

## 利用者が別途用意する依存関係

以下のバイナリは公開用ソースZIP・MOD配布ZIPに同梱しません。ビルド時の参照と、ゲーム実行時の外部依存として使用します。

| 依存関係 | 用途・入手先 |
| --- | --- |
| Synth Ridersの生成済みアセンブリ | 利用者のゲーム環境でMelonLoaderを起動して生成する `Assembly-CSharp.dll` 等。ゲームのアバター・カメラAPIを参照します。 |
| Unity、VRM、FinalIK等のゲーム同梱コンポーネント | 同じゲーム環境から生成した参照DLLを使用します。ゲーム同梱コンポーネントのコードや素材を本MODのMIT条件で再配布することはできません。 |
| MelonLoader | [LavaGang/MelonLoader](https://github.com/LavaGang/MelonLoader)。ビルド参照版0.7.3の[ライセンスはApache 2.0](https://github.com/LavaGang/MelonLoader/blob/v0.7.3/LICENSE.md)です。公式配布から別途導入します。 |
| Il2CppInterop | [BepInEx/Il2CppInterop](https://github.com/BepInEx/Il2CppInterop)。MelonLoaderが提供する参照DLLを使用します。 |
| SteamVR / OpenVRのネイティブ実装 | SteamVR環境の `openvr_api.dll` を実行時に読み込みます。このDLLは本MODの配布物に含めません。 |

`Libs` の中身を別途再配布する場合は、本書だけでなく、配布する各ファイルのライセンス・NOTICE・再配布許諾を個別に確認してください。

本MODはSynth Riders、Valve、HTC、Meta、MelonLoaderの公式製品・公式サポートではありません。各名称は対応する製品・依存関係を説明するために使用しています。
