# Folder Tagger

Unity の Project ウィンドウで、フォルダにタグを付けて色分けし、左上にアイコンを表示するエディタ拡張です。
VRChat のアバタープロジェクトのように、`Assets` 直下にフォルダが大量に並ぶプロジェクトの整理を想定しています。

- タグごとに名前・色・アイコンを自由に設定（初期タグ: アバター / 服 / 髪 / 小物 / テクスチャ / ギミック / ツール / お気に入り / 作業中）
- 1列表示（リスト）と2列表示（グリッド）の両方に対応
- フォルダは GUID で記録するので、リネームや移動をしてもタグが外れない
- エディタ専用。ビルドやアバターのアップロードには影響しない

対応バージョン: Unity 2022.3

## 導入

Package Manager の「+」→「Add package from git URL...」に次の URL を入力します。

```
https://github.com/Azukimochi/FolderTagger.git
```

非公開リポジトリの場合は、Git に GitHub の認証情報が登録されている必要があります。

## 使い方

- **タグの割り当て**: Project ウィンドウでフォルダを右クリック →「Folder Tagger」→「タグを割り当て…」
- **タグの解除**: 同じメニューの「タグを外す」
- **タグの編集**: `Tools > Folder Tagger` で設定ウィンドウを開き、タグの追加・並べ替え・削除、色とアイコンの変更を行います。
  アイコンは「選択…」から同梱アイコンを選ぶか、任意の PNG をドラッグして設定できます。

複数のフォルダを選択して、まとめて割り当てることもできます。

## データの保存先

タグと割り当ては `Assets/FolderTagger/FolderTagDatabase.asset` に保存されます（設定ウィンドウを初めて開いたときに自動で作られます）。
Git から導入したパッケージは読み取り専用のため、データはパッケージの外に置いています。プロジェクトごとに別々のデータになります。

## 開発メモ

- テクスチャ用の `AssetPostprocessor`（`OnPreprocessTexture` など）は使わないでください。
  追加・変更・削除のたびに、プロジェクト内の全テクスチャがリインポートされます。
- 同梱アイコンの読み込み設定（Editor GUI / ミップマップなし / 無圧縮）は、各アイコンの `.meta` に保存したものを配布しています。
  アイコンを追加したときは、同じ設定にしてから `.meta` ごとコミットしてください。
- 同梱アイコンは `Tools~/make_icons.py` で生成しています。Google の Material Symbols から SVG を取得し、白抜きの PNG に変換します（Python と Pillow が必要）。

  ```
  python Tools~/make_icons.py <SVGのキャッシュ先> Icons 64
  ```

## ライセンス

同梱アイコンは Google の [Material Symbols](https://fonts.google.com/icons) で、Apache License 2.0 で配布されています（`Icons/LICENSE.txt`）。
