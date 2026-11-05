# 操作とCodex翻訳 / Controls and Codex translation

## 日本語

映像上のダブルクリック、F5、Alt+Enterで全画面を切り替えます。Escで通常表示へ戻ります。シングルクリックの再生・一時停止はすぐに反映します。ダブルクリックした場合は、最初のクリック前の再生状態を保って全画面を切り替えます。「設定 → マウス」でそれぞれ無効にできます。

- **前・次のチャプター:** 再生バーの「|◀ / ▶|」ボタンで動画内のチャプター境界に移動します。字幕移動とは独立しており、キー設定にも登録できます。
- **前・次の字幕:** 再生バーの「CC＋矢印」ボタン、またはCtrl+Alt+← / →。第1字幕が有効なら第1字幕を使い、無効なら第2字幕を使います。既存のA / Dなども利用できます。
- **キー設定:** Ctrl+K、または右クリックメニューの「ショートカットキー」で、全操作を検索・編集できます。「適用」で一括反映し、「保存して閉じる」で次回起動時も保持します。
- **MPC風プリセット:** キー設定画面で選択して「適用」すると、Ctrl+← / →が字幕移動、Ctrl+Alt+← / →がコマ送りになります。F5・Alt+Enterは両プリセットで全画面切り替えです。既存設定への更新時は、使用中のキーを上書きしません。
- **字幕位置:** 「設定 → 字幕 → 位置・サイズ」で第1字幕（CC）・第2字幕それぞれの「上部」「下部」「既存の位置設定」を選択します。「両方を下部に表示」「第1字幕は下部・第2字幕は上部」ボタンでも設定できます。同じ位置なら縦に並びます。新規設定では両方とも下部が既定です。下部字幕は再生バーの高さと余白を避け、バーが隠れても位置を維持します。テキスト字幕と画像字幕に適用されます。
- **マウスで字幕を表示:** 各字幕の「マウスが画面端にあるときだけ表示」を有効にすると、上部の字幕は上端、下部の字幕は下端にマウスを近づけたとき表示します。字幕の単語操作中も表示を維持します。
- **字幕一覧:** Ctrl+B、再生バーの一覧ボタン、右クリックメニューの「字幕一覧を表示」で右側の一覧を切り替えます。

### ChatGPTアカウントでCodex翻訳

1. [Codex CLI](https://developers.openai.com/codex/cli/)をインストールします。`app-server`対応版が必要です（動作確認: 0.162.1）。LLPlayerはPATHおよび標準のnpmインストール先から`codex.exe`を検出します。独自のインストール先は設定画面で指定できます。
2. 「設定 → 字幕 → 翻訳」の翻訳エンジンで「Codex（ChatGPTアカウント）」を選択します。
3. 「モデル一覧・接続を更新」を押します。未認証の場合は「ChatGPTでログイン」を押し、開いたブラウザーでログインを完了します。CodexにAPIキーでログインしている場合も、ChatGPTアカウントでのログインが必要です。
4. モデルと推論の強さを選択します。選択肢は[Codex App Server](https://developers.openai.com/codex/app-server/)から取得します。「Codexの既定値」は取得した既定モデルを利用します。モデル名・利用可能な推論強度はコードに固定していません。
5. 翻訳先の言語を選び、画面上部「訳文の表示」の「第1字幕を翻訳して表示」または「第2字幕を翻訳して表示」を有効にして保存します。表示のオン・オフは次回起動時も保持します。翻訳中、元言語が不明、エラーなどの状態を各チェック欄の下で確認できます。既存の内蔵・外部テキスト字幕を利用でき、Whisperモデルは不要です。画像字幕の翻訳にはOCRが必要です。

Codexでは「動画を開いたら字幕全体を先行翻訳する」と「AIによる文章解説をあらかじめ生成する」が既定で有効です。字幕を読み込み次第、再生・シークを待たずバックグラウンドで準備します。訳文の表示をオンにしたトラックは、訳文が届くまで原文を表示しません。再生画面の右下にカーソルを合わせると、第1・第2字幕それぞれの翻訳とAI解説の件数・百分率・進捗バーを表示します。カーソルを領域から外すと隠れます。読み込み中・OCR待ち・完了・停止・エラーも表示し、全画面でも確認できます。進捗表示の高さを避ける余白は保持し、表示・非表示で字幕位置が動かないようにしています。翻訳設定でも状態を確認できます。画像字幕はOCRで文章を取得できた部分から処理します。

翻訳は既定で**12字幕ずつ、前後4字幕と直前までの文脈メモを添えて**依頼します。設定で件数を変更できます。入力が長い場合は小さく分割します。返された字幕ID・件数・空の訳文を検証し、不正な応答は小さい単位に分割して再試行します。字幕の順序やタイミングは変更しません。同じプレイヤー内で、字幕・前後の文脈・文脈メモ・言語・モデル等の設定が同じCodexリクエストは、処理中の依頼と完了結果を共有します。同じ字幕を2枠に指定しても、同一リクエストを二重送信しません。一方を停止しても、他方が利用中の共有処理は継続します。結果は上限付きのメモリキャッシュに保持し、ディスクには保存しません。語調・指示語・固有名詞・用語のつながりを文脈として扱います。

**文章解説:** 単語間のスペースや字幕の少し外側に約0.45秒マウスを合わせると、文章全体が囲われ、意味・ニュアンス・文法・重要表現・言い換え・例文の解説が出ます。単語の上では従来のクリック検索・範囲選択が使えます。解説はUIの言語（日本語 / 英語）で生成します。読み込んだ字幕の原文をもとに解説し、訳文を表示していないトラックや、元言語と翻訳先が同じ場合も先読みできます。生成済みの解説は動画を開いている間メモリに保持します。先読みをオフにした場合は表示時に生成します。右上の×で閉じます。

シークしても全体の準備は継続します。動画・言語・モデルの変更では古い処理を中止します。再生位置の前後だけ翻訳するには、全体の先行翻訳と解説の先読みを両方オフにしてください。訳文表示と解説先読みを両方オフにすると自動処理を停止します。

翻訳と解説にはChatGPTのCodex利用枠を使用し、字幕の文章・前後の字幕・生成済みの文脈メモをCodexへ送ります。APIキーは不要です。認証情報はCodexが管理し、LLPlayerはトークン・メールアドレスを設定に保存しません。動画ファイルやファイル名は送信しません。会話は一時的に作成し、ファイル編集・シェル・既存のMCPやプラグイン連携を無効にします。

モデルが利用できなくなった場合は一覧を更新して選び直してください。待ち時間が長い場合は推論強度を下げるか、タイムアウトを延ばせます。

## English

Double-click the video, F5, or Alt+Enter toggles fullscreen; Esc returns to windowed mode. Single-click play/pause responds immediately; double-click restores the original playback state while switching fullscreen. Mouse settings can disable single-click play/pause and double-click fullscreen separately.

- Previous/next chapter: the |◀ / ▶| buttons seek chapter markers. These actions can also be assigned in the shortcut editor.
- Previous/next subtitle: the CC + arrow buttons or Ctrl+Alt+Left/Right, using the first enabled subtitle track. Existing A/D bindings remain available.
- Ctrl+K opens the unified shortcut editor. Search, edit cells, then Apply and Save and Close to persist assignments. The MPC-style preset uses Ctrl+Left/Right for subtitles and Ctrl+Alt+Left/Right for frame stepping. Existing customized keys are preserved when installing new shortcuts.
- Settings → Subtitles → Position/Size offers independent Top/Bottom/Existing positions for both subtitle tracks, including bitmap subtitles. New settings default to Bottom for both tracks; bottom subtitles reserve the playback bar’s actual height even while it is hidden. Each track can appear only when the pointer is near its screen edge.
- Ctrl+B, the toolbar button, or the context menu toggles the subtitle sidebar.

For Codex translation, install a Codex CLI version supporting app-server (tested with 0.162.1), select **Codex (ChatGPT account)** in subtitle translation settings, and refresh the connection/model list. Use **Sign in with ChatGPT** if needed and complete authentication in your browser. An API-key login is not accepted by this provider. Models and reasoning options come from Codex; **Codex default** follows its advertised default model and reasoning setting. Enable **Translate and display first/second subtitle** at the top of translation settings and save. These choices survive restart, and progress or errors are shown beneath each switch.

Translation uses your Codex allowance. Codex manages credentials; LLPlayer does not store account tokens or email addresses. Only caption text, surrounding captions and a bounded continuity memo are sent for translation and explanations; video files and filenames are not sent. Temporary translation threads disable shell, integrations and file writes. Embedded and external text subtitles work without Whisper; bitmap translation requires OCR. Refresh model selection if a model becomes unavailable, or adjust reasoning/timeout if translation is slow.

Codex whole-track translation and AI explanation pre-generation are enabled by default. Preparation starts when a track is loaded, without waiting for playback. Hover in the bottom-right corner to reveal separate translation and AI explanation counts, percentages and progress bars for each track, including loading, OCR waits, completion, stopping and errors. Moving away hides the panel; fullscreen uses the same behavior. Bottom captions keep their reserved space so hovering does not move subtitles. While translated display is enabled, pending translations stay hidden instead of flashing the source. Requests contain up to 12 cues with four context cues before/after and a bounded continuity memo. Counts are configurable; character limits split long input. Returned IDs and nonempty translations are validated, and invalid batches are retried in smaller groups without changing cue timing/order. Identical Codex requests share in-flight work and a bounded in-memory result cache within one player, including their continuity memo. Selecting the same subtitles in both slots does not send identical requests twice. Canceling one consumer preserves work still needed by the other. Different context, language or model settings remain independent.

Hover over spaces between words or the padded area around a caption for about 0.45 seconds to outline the sentence and see its meaning, nuance, grammar, key phrases, alternatives and an example. Word click/drag lookup remains available. Explanations use the UI language and original cue, including tracks displaying their original text or matching the target language. Results are cached in memory for the open video. Disable pre-generation for on-demand explanations; use × to close the popup.

Seeking preserves whole-track preparation; changing video, language or provider cancels stale work. Disable both whole-track translation and explanation pre-generation to process only the playback window. Disable translated display and pre-generation to stop automatic processing. OCR text is processed as it becomes available.

## 実装の参考 / Implementation references

[LLM-Subtrans](https://github.com/machinewrapped/llm-subtrans) の字幕バッチと用語の継続性、[TransLora](https://github.com/SloMR/TransLora) の前後文脈・ID検証・分割再試行を参考に、LLPlayer向けに実装しています。解説の構成は [nani.now](https://nani.now/en) の文脈・ニュアンス・言い換え・重要表現を参考にしています。Naniとの通信は行いません。

The implementation draws on LLM-Subtrans’s batching/terminology continuity, TransLora’s overlapping context and validation/retries, and Nani’s nuance, alternative wording and key-phrase explanations. LLPlayer uses its existing Codex account connection, not a Nani integration. Structured responses use [Codex App Server’s per-turn output schema](https://learn.chatgpt.com/docs/app-server).
