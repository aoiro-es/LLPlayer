# 操作とCodex翻訳 / Controls and Codex translation

## 日本語

映像上のダブルクリック、F5、Alt+Enterで全画面を切り替えます。Escで通常表示へ戻ります。シングルクリックの再生・一時停止はすぐに反映します。ダブルクリックした場合は、最初のクリック前の再生状態を保って全画面を切り替えます。「設定 → マウス」でそれぞれ無効にできます。

- **前・次の字幕:** 再生バーの字幕移動ボタン、またはCtrl+Alt+← / →。第1字幕が有効なら第1字幕を使い、無効なら第2字幕を使います。既存のA / Dなども利用できます。
- **キー設定:** Ctrl+K、または右クリックメニューの「ショートカットキー」で、全操作を検索・編集できます。「適用」で一括反映し、「保存して閉じる」で次回起動時も保持します。
- **MPC風プリセット:** キー設定画面で選択して「適用」すると、Ctrl+← / →が字幕移動、Ctrl+Alt+← / →がコマ送りになります。F5・Alt+Enterは両プリセットで全画面切り替えです。既存設定への更新時は、使用中のキーを上書きしません。
- **字幕位置:** 「設定 → 字幕 → 位置・サイズ」で第1字幕（CC）・第2字幕それぞれの「上部」「下部」「既存の位置設定」を選択します。「両方を下部に表示」「第1字幕は下部・第2字幕は上部」ボタンでも設定できます。同じ位置なら縦に並びます。テキスト字幕と画像字幕に適用されます。
- **マウスで字幕を表示:** 各字幕の「マウスが画面端にあるときだけ表示」を有効にすると、上部の字幕は上端、下部の字幕は下端にマウスを近づけたとき表示します。字幕の単語操作中も表示を維持します。
- **字幕一覧:** Ctrl+B、再生バーの一覧ボタン、右クリックメニューの「字幕一覧を表示」で右側の一覧を切り替えます。

### ChatGPTアカウントでCodex翻訳

1. [Codex CLI](https://developers.openai.com/codex/cli/)をインストールします。`app-server`対応版が必要です（動作確認: 0.162.1）。LLPlayerはPATHおよび標準のnpmインストール先から`codex.exe`を検出します。独自のインストール先は設定画面で指定できます。
2. 「設定 → 字幕 → 翻訳」の翻訳エンジンで「Codex（ChatGPTアカウント）」を選択します。
3. 「モデル一覧・接続を更新」を押します。未認証の場合は「ChatGPTでログイン」を押し、開いたブラウザーでログインを完了します。CodexにAPIキーでログインしている場合も、ChatGPTアカウントでのログインが必要です。
4. モデルと推論の強さを選択します。選択肢は[Codex App Server](https://developers.openai.com/codex/app-server/)から取得します。「Codexの既定値」は取得した既定モデルを利用します。モデル名・利用可能な推論強度はコードに固定していません。
5. 翻訳先の言語を選び、画面上部「訳文の表示」の「第1字幕を翻訳して表示」または「第2字幕を翻訳して表示」を有効にして保存します。表示のオン・オフは次回起動時も保持します。翻訳中、元言語が不明、エラーなどの状態を各チェック欄の下で確認できます。既存の内蔵・外部テキスト字幕を利用でき、Whisperモデルは不要です。画像字幕の翻訳にはOCRが必要です。

Codexでは「動画を開いたら字幕全体を先行翻訳する」が既定で有効です。訳文表示をオンにした字幕を読み込み次第、再生・シークを待たず先頭から全体をバックグラウンドで翻訳します。進捗は翻訳設定画面で確認できます。シークしても先行翻訳は継続し、動画の切り替えや翻訳表示のオフで中止します。オプションをオフにすると、従来どおり再生位置の前後に指定した範囲だけを翻訳します。画像字幕はOCRで文章を取得できた部分から翻訳します。

翻訳にはChatGPTのCodex利用枠を使用し、字幕の文章と設定した件数の過去の字幕をCodexへ送ります。APIキーは不要です。認証情報はCodexが管理し、LLPlayerはトークン・メールアドレスを設定に保存しません。翻訳用の会話は一時的に作成し、ファイル編集・シェル・既存のMCPやプラグイン連携を無効にします。シークや翻訳の無効化で処理をキャンセルします。

モデルが利用できなくなった場合は一覧を更新して選び直してください。待ち時間が長い場合は推論強度を下げるか、タイムアウトを延ばせます。

## English

Double-click the video, F5, or Alt+Enter toggles fullscreen; Esc returns to windowed mode. Single-click play/pause responds immediately; double-click restores the original playback state while switching fullscreen. Mouse settings can disable single-click play/pause and double-click fullscreen separately.

- Previous/next subtitle: toolbar buttons or Ctrl+Alt+Left/Right, using the first enabled subtitle track. Existing A/D bindings remain available.
- Ctrl+K opens the unified shortcut editor. Search, edit cells, then Apply and Save and Close to persist assignments. The MPC-style preset uses Ctrl+Left/Right for subtitles and Ctrl+Alt+Left/Right for frame stepping. Existing customized keys are preserved when installing new shortcuts.
- Settings → Subtitles → Position/Size offers independent Top/Bottom/Existing positions for both subtitle tracks, including bitmap subtitles. Each track can appear only when the pointer is near its screen edge.
- Ctrl+B, the toolbar button, or the context menu toggles the subtitle sidebar.

For Codex translation, install a Codex CLI version supporting app-server (tested with 0.162.1), select **Codex (ChatGPT account)** in subtitle translation settings, and refresh the connection/model list. Use **Sign in with ChatGPT** if needed and complete authentication in your browser. An API-key login is not accepted by this provider. Models and reasoning options come from Codex; **Codex default** follows its advertised default model and reasoning setting. Enable **Translate and display first/second subtitle** at the top of translation settings and save. These choices survive restart, and progress or errors are shown beneath each switch.

Translation uses your Codex allowance. Codex manages credentials; LLPlayer does not store account tokens or email addresses. Only subtitle text and the configured recent subtitle context are sent for translation. Temporary translation threads disable shell, integrations and file writes. Embedded and external text subtitles work without Whisper; bitmap translation requires OCR. Refresh model selection if a model becomes unavailable, or adjust reasoning/timeout if translation is slow.

Codex whole-track pretranslation is enabled by default. Once an enabled subtitle track is loaded, translation starts from the beginning in the background, without waiting for playback or seeking. Progress appears in translation settings. Seeking preserves ongoing pretranslation; switching videos or disabling translation cancels it. Turn off **Pretranslate the entire subtitle track on video open (Codex)** to use the configured playback window instead. Bitmap subtitles become available for translation as OCR extracts their text.
