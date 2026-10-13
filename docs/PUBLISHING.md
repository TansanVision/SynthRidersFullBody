# GitHubへの公開・MODの配布

## 公開する内容

- MODのソース、プロジェクト、テスト、ドキュメント、配布用スクリプト。
- MOD独自部分のMITライセンス：ルートの `LICENSE`。
- 第三者表記：`THIRD_PARTY_NOTICES.md` と `SynthRidersFullBody/OpenVrLicense.txt`。
- ReleaseにはMODのDLL、導入説明、ライセンス類を同梱します。

ゲーム・Unity・MelonLoaderの参照DLL、Cpp2IL生成物、ゲームexe、VRM、ユーザーの設定、ログは公開しません。開発に必要な参照DLLの準備は [ビルド手順](BUILDING.md) を参照してください。

## 既存のGit履歴について

この開発リポジトリの過去のコミットには `SynthRidersFullBody/Libs/` が含まれています。現在の追跡対象から外し、`.gitignore` に追加しても、過去のコミットには残ります。

**この開発リポジトリをそのままPublicに変更したり、既存のブランチ・タグを新しい公開先へpushしたりしないでください。** リモートとの共有履歴を扱う場合は、利用者・ブランチ・タグへの影響を確認してから整理します。[GitHubの履歴削除について](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository)

初回公開では、以下のソースZIPを別フォルダーへ展開し、そこから新しい履歴を開始する方法を推奨します。ZIPには `.git` と過去のコミットを含めません。今後の公開先へ更新するときも、古い開発履歴をmergeせず、変更したソースをコピーしてコミットしてください。

既にPublicで公開されていた場合、新しいソースZIPを作るだけでは既存の公開物を回収できません。過去のコミット・タグ・Release・fork等の状況を別途確認してください。

## 公開用ZIPを作成する

リポジトリのルートで実行します。スクリプトは指定したソースファイルと配布ファイルだけをコピーする方式です。Gitに登録されているファイルを丸ごとコピーしません。

```powershell
# ゲーム参照DLLがなくても、公開用ソースを作成できます。
powershell -NoProfile -File .\scripts\New-PublicationPackage.ps1 -Mode Source

# ソースとMODのZIPを両方作成します。先にビルド用参照DLLを準備します。
powershell -NoProfile -File .\scripts\New-PublicationPackage.ps1 -Mode Both -OutputDirectory .\artifacts\publication-both
```

`GameDir`、`MelonDir`、`UnityAssembliesDir` はビルド時にも指定できます。

```powershell
powershell -NoProfile -File .\scripts\New-PublicationPackage.ps1 -Mode Both -GameDir "D:\SteamLibrary\steamapps\common\SynthRiders" -OutputDirectory .\artifacts\publication-game
```

既存の成果物を上書きしないよう、出力先には空のフォルダーを指定してください。既定は `artifacts/publication` です。成果物のバージョンは `FullBodyMod.cs` の `MelonInfo` から取得します。

| 成果物 | 内容 |
| --- | --- |
| `SynthRidersFullBody-v<version>-source.zip` | ソース、テスト、説明、ライセンス。DLL・exe・Git履歴は含みません。 |
| `SynthRidersFullBody-v<version>-mod.zip` | `Mods/SynthRidersFullBody.dll`、説明、MIT・OpenVRライセンス、第三者表記。 |
| `SHA256SUMS.txt` | 作成したZIPのSHA-256。 |
| 同名の展開済みフォルダー | ZIPの内容確認や、新しい公開用リポジトリの作成に使用できます。 |

MODのZIP作成時はReleaseビルドを行います。ビルド出力フォルダー全体をZIPにせず、MOD本体のDLLのみを選びます。ソースとMODのZIPは、それぞれ検査してから作成します。

## 新しい公開用リポジトリを作る

1. `source.zip` を、開発リポジトリの外にある新しい空のフォルダーへ展開します。
2. `Libs`、`.git`、ログ、ユーザー設定がないことを確認します。
3. GitHubに新しい空のリポジトリを用意します。既存リポジトリの名前を再利用する場合は、旧履歴や公開物の取り扱いを先に確認します。
4. 展開先で新しいGit履歴を開始します。コミットの名前・メールアドレスには公開してよい情報を設定します。
5. LICENSE・第三者表記・READMEを含むことを確認してから、新しい公開先へpushします。

```powershell
# source.zipの展開先で実行する例。元の開発リポジトリでは実行しません。
git init -b main
git add .
git diff --cached --stat
git commit -m "Initial public source"
```

## Release説明とログの扱い

- 非公式MODであること、MOD・ゲーム・MelonLoaderのバージョンを記載します。
- 実機確認したHMD・トラッカー・アバターと、未確認の機能を区別して記載します。
- v0.49.0のUltimate Tracker水平補正は計算テスト済みですが、実機での改善確認は完了していません。実機確認が済むまで試験的な機能として案内してください。
- 公開Issueへログを添付する場合、デバイスの個体番号・ユーザー名を含むパス等は伏せます。同じ個体には同じ伏せ字を使用してください。
- MODの設定ファイルにはトラッカーの個体番号が保存されます。自分やテスターの設定をサンプルとしてそのまま公開しません。
- スクリーンショット・動画に他者のVRMや素材を使う場合は、その素材の利用条件を確認します。

現在の許諾・同梱範囲は [第三者ソフトウェアについて](../THIRD_PARTY_NOTICES.md) を参照してください。
