# Third-Party Notices

## SoundFlow 1.2.1.3

Source: https://github.com/ClassIsland/SoundFlow (distributed through the ClassIsland MyGet feed)

Copyright (c) 2025 LSXPrime

Licensed under the MIT License:

```text
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## miniaudio

SoundFlow's bundled playback runtime uses miniaudio by David Reid. miniaudio
is dual licensed under MIT or the Unlicense; SecRandom redistributes it under
the MIT terms above. See SoundFlow's `SOUNDFLOW-THIRD-PARTY-NOTICES.txt` for
the upstream notice set.

## EdgeTtsSharp

Source: https://github.com/SECTL/EdgeTtsSharp (fork of https://github.com/ClassIsland/EdgeTtsSharp, itself a fork of https://github.com/Entity-Now/Edge_tts_sharp)

The repository is included as the `vendors/EdgeTtsSharp` Git submodule (pinned at `45dca0e`) for Edge voice metadata and synthesis protocol code. `SecRandom/SecRandom.csproj` compiles `Edge_tts.cs`, `Tools.cs`, `Model/eVoice.cs`, `Model/PlayOption.cs`, and `Model/Log.cs` from that submodule and embeds `Source/VoiceList.json`, while SecRandom replaces the upstream `Wss`/NAudio pieces with its own app-layer implementations (`SecRandom/Services/Voice/EdgeTtsSharpCompatibility.cs`, `EdgeTtsSharpPlaybackStubs.cs`).

License status (checked 2026-10-04):

- No repository in that fork chain contains a `LICENSE` file. GitHub reports no license for `Entity-Now/Edge_tts_sharp`, `ClassIsland/EdgeTtsSharp`, and `SECTL/EdgeTtsSharp`, and the upstream history contains no commit that ever added one.
- Upstream's README has declared `## 许可证 [MIT License]` since commit `7dcb484c` (2025-11-10) and links to a `LICENSE` file that does not exist in the repository; before that commit (2023-10-28 through 2025-10-13) the README had no license section at all. That declaration carries no license text and no copyright notice, and it appeared in the same commit that rewrote the README into wording closely following the separately licensed MIT project https://github.com/niker/EdgeTtsSharp.
- The published NuGet package `Edge_tts_sharp` (nuspec 1.1.7) carries no `license` metadata at all.
- The upstream author's own Gitee copy of the same project, https://gitee.com/Entity-now/edge_tts_sharp — the shared commit history through `e50aedba` (2023-11-08) plus one extra commit `4d5ecff` "add LICENSE." (2023-12-16), unchanged since — does contain the full GPL-2.0 license text, and Gitee reports the repository as GPL-2.0. That text carries no "or later" wording.

The MIT declaration (no text) and the GPL-2.0 license (text, on the stale mirror) therefore conflict, and only the GPL one has actual license text. Distribution review is still required before shipping this dependency: confirm the intended license with upstream — an explicit MIT `LICENSE` matching the README would resolve it cleanly — because a GPL-2.0-only reading is not compatible with redistributing this component inside a GPL-3.0 application. If the MIT declaration is accepted as-is, this file must reproduce the MIT notice text with the upstream copyright holder's attribution.
