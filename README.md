# Raven for Windows

The native Windows app of [Raven](https://raven-messager.com), a private messenger with no central
server: C#, .NET 8, Windows App SDK 2.5.1 (WinUI 3, Fluent controls). It follows the shared design in
`core/desktop/design/` (`DESIGN.md`, `tokens.json`, `strings.en.json`, mockup) and talks to Raven's
engine only through the JSON contract in `core/desktop/api/API.md`.

The engine (`raven-client`), the background program `raven-node`, the protocol and the shared design
live in the [`raven-core`](https://github.com/Raven-ASHCO/raven-core) repository, included here as a
git submodule at `core/`. Get the code with:

```powershell
git clone --recurse-submodules https://github.com/Raven-ASHCO/raven-windows.git
cd raven-windows
# or, in an existing clone:
git submodule update --init
```

> **Status: in development, built and tested in CI only.** There is no installer, no signed build and
> nothing to download yet. CI (`.github/workflows/windows.yml`) builds Raven's engine, the app and the
> tests, and runs them (including the engine tests); the app has not been tried by hand on Windows yet
> (see "Unverified" below). The app uses whatever the engine allows: in a release engine only direct
> messaging on the local network is on, and every other way to reach friends is a lab feature that
> stays off (see `core/README.md`). The Calm restyle (the approved Calm design, with groups in the lab
> and Settings in seven sections) has not been compiled on a Windows machine yet.

## Layout

| Path | What |
|---|---|
| `Raven.Windows.sln` | Solution: app, core library, tests (platforms x64 / x86 / ARM64) |
| `Directory.Build.props` | Shared settings (nullable on, C# 12, English-only satellites) |
| `Directory.Packages.props` | **All package versions, pinned**: Microsoft.WindowsAppSDK 2.5.1, Microsoft.Windows.SDK.BuildTools 10.0.28000.2705; tests: Microsoft.NET.Test.Sdk 17.14.1, MSTest.TestAdapter / MSTest.TestFramework 3.11.1. Nothing else. |
| `src/Raven.Windows.Core/` | UI-free core (plain `net8.0`, so its tests run on any OS): `IRavenEngine` + typed wrappers (`RavenApi`), `BackgroundEngine` (keeps engine calls off the UI thread), the JSON envelope, models, `RavenClientEngine` (Raven's engine, `raven_client.dll`, through P/Invoke), `EngineChoice` (engine or demo), `MockRaven` + `SecurityWords` (debug only), string table (`Loc`), sanitiser, relative times, presenters (avatar rule, status line, tick text, ticker), the tray menu and its breathing (`TrayMenu`, `TrayBreath`), check-words session (words or old code), typed-word tokenizer and the old code in groups of four, motion tokens, `AppIdentity` (the app id) |
| `src/Raven.Windows/` | The WinUI 3 app (`Raven.exe`): `Program.cs` (single instance, AppUserModelID), `App`, `MainWindow`, `Views/` (Welcome, Setup, Shell, Chat, Network, Check network, Security, Settings, Locked, Error), `Dialogs/` (Add a friend, Check words, Message details, Erase, Quit, Consent, Restart the background program, a friend's Routes, New group, Members, Security words), `Controls/` (friend and group rows, avatar, path chip, tick and group ring, Calm switch, segmented control, lock glyph, steps bar, word grid), `Views/GroupChatView.cs`, `Helpers/` (motion, tray icon, Win32 window helpers, `{h:Str}` markup extension), `Services/RavenSession` (event pump, cached settings, node, privacy and Lock v2 state), `Themes/` |
| `src/Raven.Windows.Core/ViewModels/` | UI-free view models of the Network screens (`NetworkViewModel`, `NetworkCheckViewModel`, `ContactRoutesViewModel`, `SecurityViewModel`, `LockViewModel`); the pages render their state, the tests drive them on MockRaven |
| `src/Raven.Windows/Themes/` | `Light.xaml`, `Dark.xaml` (colours from `tokens.json`), `Raven.xaml` (theme dictionaries incl. high contrast, spacing, radii, layout, Segoe UI Variable / Cascadia Mono type, named styles) |
| `src/Raven.Windows/Assets/` | From `core/desktop/design/assets/`: `RavenMark.png` (= raven-mark-1024), `RavenLogo.png` (= raven-logo-192); generated from them: `Raven.ico` (app/window), `RavenTray.ico` (tray), MSIX tiles; `RavenTrayDim1.ico` / `RavenTrayDim2.ico` (the tray's breathing frames, 80% and 60%, made by `tools/make-tray-frames.py`) |
| `tools/make-tray-frames.py` | Makes the breathing frames from `RavenTray.ico` (Python standard library only) |
| `tests/Raven.Windows.Tests/` | MSTest: the real engine (`EngineTests`: `raven_client.dll` on a throwaway profile, incl. the Network, Check and Security view models), the engine wrapper without its library, MockRaven (incl. the privacy operations, error texts and the network ops in release and lab mode), the Network screens' view models (`NetworkViewModelTests`, `NetworkScreensTests`: every way state, consent, restart, NOT_AVAILABLE_YET / CONSENT_NEEDED, Lock v2, routes), path chips and network presenters, verify words and old code (match / typo / mismatch, groups of four, the old-code intro), string keys, sanitiser, envelope, background engine, app id, theme tokens, presenters and time phrases, the tray menu and breathing |

## Build and run (Windows 10 1809+ / Windows 11)

Needs the .NET 8 SDK (any recent 8.0.x feature band). Visual Studio 2022 17.10+ with the
"Windows application development" workload also works (open `Raven.Windows.sln`, pick x64, F5).

Raven's engine comes from `core/node/` (Rust 1.98, MSVC; see `core/node/crates/raven-client/README.md`):

```powershell
cd core\node
cargo build --release -p raven-client -p raven-node   # raven_client.dll + raven-node.exe for the app
cargo build -p raven-client                           # debug raven_client.dll for the engine tests
cd ..\..
dotnet build Raven.Windows.sln -c Debug -p:Platform=x64
$app = ".\src\Raven.Windows\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\Raven.exe"
& $app                 # Raven's engine on the CLI's profile (%USERPROFILE%\.raven, or RAVEN_DATA_DIR)
& $app --demo          # Debug only: the demo engine (MockRaven), first-run flow
& $app --mock-ready    # Debug only: the demo, straight to the main window
& $app --mock-locked   # Debug only: the demo on the Locked screen (review only)
& $app --mock-lab      # Debug only: the demo, ready, answering the network ops as a developer build with lab switches (and groups)
dotnet test tests\Raven.Windows.Tests\Raven.Windows.Tests.csproj
```

- **The engine next to Raven.exe.** `Raven.Windows.csproj` copies `raven_client.dll` and `raven-node.exe` from
  `core\node\target\release\` (ARM64: `core\node\target\aarch64-pc-windows-msvc\release\`, x86:
  `core\node\target\i686-pc-windows-msvc\release\`; another folder: `-p:RavenEngineDir=<folder>\`) when they
  exist. `RavenClientEngine` loads the library only from its own folder and the safe system folders (never
  the current directory or PATH); without it, or with one of another architecture, every call answers
  `NOT_INITIALIZED` and the app opens on its error screen. The engine starts `raven-node.exe` from next to
  `Raven.exe` (or `RAVEN_NODE_BIN`) when the app needs the background program. The engine uses the CLI's
  profile, so the app, `raven` and `ash` share one identity and one friends list; a run without
  `RAVEN_DATA_DIR` opens your real profile, exactly as the CLI does.
- **Engine tests.** `EngineTests` call the real `raven_client.dll` with a throwaway profile
  (`RAVEN_DATA_DIR` in the temp folder) and the file-backed secret stores (`RAVEN_*_BACKEND=locked-file`,
  `RAVEN_ALLOW_EPHEMERAL_DATA_DIR=1`, set before the first call), a raven-node pipe name of their own and
  `receive` off: `app.info`, `profile.state`, `identity.create`, `identity.me`, the settings round trip,
  `privacy.*`, `events.poll` beside a call, `BAD_REQUEST`. Release builds of the engine refuse the
  file-backed stores, so the test project copies the **debug** library from `core\node\target\debug\`
  (`-p:RavenEngineTestDir=<folder>\`). Without it the engine tests are inconclusive (skipped);
  `RAVEN_REQUIRE_ENGINE_TESTS=1` (CI) makes that a failure.

- The app builds **unpackaged and self-contained** by default (`WindowsPackageType=None`,
  `WindowsAppSDKSelfContained=true`): nothing to install, `Raven.exe` runs from the output folder.
- **MSIX:** `dotnet build ... -p:Platform=x64 -p:WindowsPackageType=MSIX`, or the "Raven (MSIX
  package)" launch profile in Visual Studio. `Package.appxmanifest` is ready (English resources,
  `runFullTrust`, tiles from the logo); `Publisher="CN=Raven"` is a placeholder that must match the
  signing certificate.
- **App id:** `io.github.raven-ashco.Raven` (`core/desktop/README.md`, "App identity";
  `Raven.Windows.Core/AppIdentity.cs`): the MSIX `Identity Name`, the `app.manifest` assembly
  identity, the single-instance key (`AppInstance.FindOrRegisterForKey`) and, for the unpackaged
  build, the AppUserModelID (`SetCurrentProcessExplicitAppUserModelID`; an MSIX package gets its
  own from the manifest). `AppIdentityTests` checks it against the README table and both manifests.
- The tests (`net8.0`) also run on macOS and Linux: `dotnet test tests/Raven.Windows.Tests` (the engine
  tests need Windows and are skipped there).
- Without `-p:Platform=...` the app project defaults to x64 (WinUI has no AnyCPU build).

## What is mocked

- **The engine.** Every build runs on Raven's engine (`RavenClientEngine`: `raven_call` / `raven_free`
  through P/Invoke, UTF-8 both ways, every response freed with `raven_free`, calls on the thread pool
  through `BackgroundEngine`). Debug builds started with `--demo`, `--mock-ready`, `--mock-locked` or `--mock-lab`
  (`EngineChoice`) run on `MockRaven` instead (in-memory, API.md "Mocks"): it answers every operation of
  contract v1 through the real JSON envelope (`Handle(string) -> string`, the shape of `raven_call`).
  Release builds do not contain MockRaven (CI checks the Release core library for its namespace).
- **Demo data:** Ali (verified, in touch), Mina (verified, one waiting message), Rena (not
  verified, old code `Tq7m-29Xa-hKe4`, the same in the macOS and Linux mocks). Your security words
  are the design's demo words (`turtle frog bread wolf paper laptop funnel mango honey wagon`, old
  code `If4x-36FU-omFi`).
- **Demo events,** once, from the first `events.poll` (as in the concept mockup): 1.2 s Ali
  writes; 2.6 s Rena writes; 6 s Raven tries Mina again (retry ring); 10.5 s Mina is back and her
  waiting message gets a signed receipt. A status check (`status_ok`) every 2 s drives the heart.
  Sending to Ali: `send_started` at once, "Sent, not confirmed yet" at 0.7 s, receipt at 1.1 s.
  Sending to someone not in touch: it waits (24 h).
- **Trying Add a friend:** paste any invite of the mock format, for example
  `raven:rvn1qnewfriendaddress0123456789abcdefgh:00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff@192.168.1.50:7420`.
- **Trying Check words** (the app never shows a friend's words; these are demo data):
  Rena's words are `river lime cactus eagle harp potato puzzle elephant fish bracelet` → match.
  Swap two of them or mishear one → "one word doesn't fit" (typo). Type your own words → mismatch.
  The mock uses Raven's draft 256-word list (linked from
  `core/desktop/design/raven-security-words-v1.txt`, debug builds only, SHA-256 pinned by a test).
  **Old code:** "Older Raven? Use the old code instead", then Rena's `Tq7m-29Xa-hKe4` → match
  (Rena is verified, as with the words); anything else → mismatch. Like the engine, the mock ignores
  spaces, separators and invisible characters, folds Persian, Arabic-Indic and full-width digits,
  and compares case-sensitively. Other friends have no old code in the mock (always mismatch).
- **Network** (`Mock/MockRaven.Network.cs`): by default a release build's answers (API.md, "Mocks"):
  LAN direct on, every other way `preview`, a held way refused with `NOT_AVAILABLE_YET`, Lock v2,
  security notices and updates not in this version. `--mock-lab` (tests: `labNetwork: true`) answers
  as a developer build with `RAVEN_LAB_TEST_A`, `..._FRIEND_RELAY`, `..._CUSTODY_HANDOFF`,
  `..._NEARBY`, `..._ROUTE_HINTS`, `..._LOCAL_PROTECTION`, `..._SECURITY_EVENTS` and `..._ADVISORY` set:
  those ways read `off` / `lab` and switch on (WebSocket, proxy, letters, beacon and the other
  protections stay `preview`), `carry_for_friends`, `friends_carry_mine` and `handoff` need
  `agreed: true` (`CONSENT_NEEDED`), Internet direct, p2p, port mapping and the proxy apply at the
  next start (`restart_needed`), and `node.stop` follows the engine's Windows rules: `stopped` for a
  background program this app started, `advice` (`taskkill /F /PID 4242`) for the demo's, which was
  started earlier, `managed` for a login service (tests). Lock v2's demo passphrase is
  `open sesame` (three wrong ones: wait 30 s); Ali's routes in lab mode show what Ali told this
  computer and Mina as a mail friend; there is one demo security notice. Bluetooth nearby reads
  `lab` once switched on, with the Windows note (inactive: public address).
- **Privacy:** `privacy.state` answers `available: false`, and `privacy.lock`, `privacy.unlock`,
  `privacy.encrypt` and `privacy.erase` answer `NOT_AVAILABLE_YET` (erase first checks
  `confirm: "erase"`: `BAD_REQUEST` otherwise). An unknown operation or a missing argument is
  `BAD_REQUEST`.
- **Error messages** in the mock use the wording of `strings.en.json` (`error.*`); a few cases only the
  mock has (your own invite pasted, a friend added twice, a message that no longer exists) keep a mock
  text.
- **Groups** (`Mock/MockRaven.Groups.cs`, API.md "Groups"): lab only, as in the engine. A release mock
  answers `groups.list` with `available: false` (no Groups section) and every other `groups.*` op with
  `NOT_AVAILABLE_YET`. `--mock-lab` has one demo group, "Weekend trip": admin Ali, members you, Mina,
  Rena and Lena (not your friend; her messages are passed on by Ali). Your messages carry one receipt
  per member: Ali confirms within about a second, Rena a little later, Lena's copy is passed on by Ali
  (`via: relay`) and confirmed after that, Mina's waits (she is away). Four seconds after the first
  look Lena writes once (`group_message`). Create, add, remove, leave, history, send and mark read
  follow the engine's rules and words: 16 members at most, you included (owner decision 2026-10-28);
  only your friends can be added; only the admin adds and removes; the admin can't leave; 8,192 bytes
  of text; `GROUP_*` errors. The lab mock also lists `RAVEN_LAB_GROUPS` and `RAVEN_LAB_HORIZON`.

## How the design is implemented

### Calm (the design chosen on 2026-10-28)

The prototype `concept-1-calm.html` is the spec for look and motion, adapted to WinUI 3 where it
would feel foreign. What changed from the earlier look, and how:

- **Tokens v2** (`tokens.json`): every colour, radius, layout size and the Calm type ramp is a XAML
  resource in `Themes/` (`ThemeTokensTests`, `CalmTokensTests`); the Calm durations are `CalmMs` and
  the CSS curves `MotionCurves` (Core). XAML's `KeySpline` can't overshoot, so `Helpers/Motion.cs`
  samples each curve into linear key frames: the springs overshoot as in the prototype. Animations
  release their values when done (FillBehavior Stop); each recipe sets the end value first.
- **Theme-aware from code:** every element built in code takes a named style from `Raven.xaml`
  whose setters use `ThemeResource`, never a brush looked up in code, so Settings > Appearance >
  Theme (System / Light / Dark: `Root.RequestedTheme`; dialogs get it in `Ui.ShowDialogAsync`)
  re-colours everything. Every brush has a light, a dark and a high-contrast value
  (`CalmStylesTests`).
- **Fluent adaptations:** shadows are Fluent's 1-pixel strokes (cards, incoming bubbles, the
  composer, the selected sidebar row) rather than drop shadows; the sidebar is a `NavigationView`
  restyled through its lightweight resources (keyboard, Narrator and the E1 fixes stay native);
  Mica shows behind the translucent sidebar on Windows 11 (not in high contrast; the pages keep the
  Calm background); dialogs are `ContentDialog`s with their own Fluent entrance; "When I quit" uses
  `RadioButtons`; the friends' and groups' unread counts use `InfoBadge`; icons are Segoe Fluent
  Icons (Tor's onion is two ellipses: there is no glyph for it).
- **New controls** (`Controls/`): `CalmSwitch` (a ToggleButton: Narrator "toggle switch", Space
  toggles; the knob springs on the person's toggle, values from the engine show quietly),
  `Segmented` (radio buttons on a track with a sliding, resizing thumb; arrow keys choose),
  `Avatar` (avatarRule colours, the presence dot, a breathing halo, the retry ring), `PathChip`,
  `TickView`, `GroupRing`, `LockGlyph` (a shackle that closes before Raven locks), `StepsBar`.
- **Sidebar:** brand row with the heartbeat (the dot beats once per real `status_ok`), search, You,
  FRIENDS, GROUPS (only when `groups.list` says available), New group, Add a friend; Network ("3 ways
  on" from `node.status` carriers), Security words, Security, Settings. Below 760 DIP it takes the
  tokens' minimum width (220).
- **Chat:** the header (40 pt avatar, Verified, status and path, the path pill, Routes: the E1 header
  fit kept), the route line (packets on `send_started`, `receipt`, `incoming`), day lines, Calm
  bubbles (yours on the gradient; a waiting one as an outline), the tick with its icon (the filled
  check pops with a ripple only on the receipt), Calm path chips ("LAN direct", "Bluetooth", "via
  Tor", "Direct", "Held by a friend": the engine never names the friend who holds a copy), your
  bubble lifting out of the composer, the away card (the engine's deadline; a friend holding a copy
  only when a waiting message's path kind says so; Try now, Details with the engine's own status /
  why / what to do, Stop trying), the toast (announced politely to Narrator) and the "new messages"
  pill. Still one Tab stop for the whole chat (E1).
- **Group chat** (`Views/GroupChatView.cs`, API.md "Groups"): members' names in their avatar colour,
  "Passed on by Ali", your messages with the ring that fills as members confirm and "Delivered to 3
  of 5" from the engine's counts, Details with each member's copy; Members (admin: add, remove; a
  member: Leave; the admin never gets Leave), New group (up to 15 friends and you). After leaving or
  removal the history stays and the composer says so.
- **Settings:** seven sections with the gliding selection and sections that slide in the direction of
  travel; Lock (the sheet whose shackle closes, then the locked window that fades in and shakes on a
  wrong passphrase) and Updates moved here from the Security page, which keeps the notices. The theme
  and "Show technical details" (the engine's exact path words under messages; the Network page's
  technical lines) are this app's own, kept in `%LOCALAPPDATA%\Raven\desktop-ui.json` (they name no
  one; never in Raven's profile).
- **Network page:** This computer with a radar that pulses on each real status check, ways as Calm
  cards (two columns when there is room) with their path family's tile and a Calm switch; every
  behaviour of the previous page stays.

### Earlier notes (still true)

- **Texts:** `strings.en.json` is embedded into `Raven.Windows.Core` at build time (linked, single
  source) and read at runtime by `Loc`; XAML uses `{h:Str Key=...}`. There is no fallback table:
  `StringKeysTests` fails when C# or XAML uses a key that is not in `strings.en.json` (a missing key
  would show as `[key]`). Time phrases follow the same rules as the macOS and Linux apps
  (`RelativeTime`: "now", "7 s", "just now", "5 min ago", "3 h ago", "1 day ago", "about 35 min",
  "about 1 hour", "about 22 hours", "about 3 days", "Tue 14:05" with the day from
  `time.weekdays`, and a deadline 6 or more days away with its date, "Sun 1 Nov 17:15"); a waiting
  message says "{left} left" when less than 2 hours remain.
- **Events `reset`:** when `events.poll` answers `reset: true`, `RavenSession` raises `Resynced` after
  the batch and `ShellPage` reads the node status, the friends and the open chat again (API.md, "Events").
- **Threading:** `raven_call` may block (API.md), so `App` wraps the engine in
  `BackgroundEngine`: every call runs on the thread pool, in the order the app made it, and
  `events.poll` (which never blocks) runs beside that queue so a slow `node.start` cannot stall the
  event pump. Pages simply `await`; `Program.Main` installs the DispatcherQueue synchronization
  context, so every continuation (and every `RavenSession` event) comes back on the UI thread.
  MockRaven serialises its state behind a lock, so it behaves as before.
- **Theme:** `Light.xaml` / `Dark.xaml` were written from `tokens.json` (CSS `#RRGGBBAA` becomes
  XAML `#AARRGGBB`); a test compares every colour, spacing, radius, layout size, font size and
  motion duration with `tokens.json`. Fluent accent resources point at Raven's accent. High
  contrast uses the system colours.
- **Honest motion** (`Helpers/Motion.cs`): heartbeat on `status_ok` (500 ms), packet out on
  `send_started` (700 ms), receipt back on `receipt` (600 ms, and only then "Delivered"), packet in
  + bubble rise/fade (350 ms) + a soft ring for 1.5 s on `incoming`, dot blink on `presence`, slow breathing
  of "in touch" dots (3 s), retry ring on `retry` (1 s), spinner while the node is starting, logo
  reveal once on Welcome (600 ms). Nothing moves when Windows "Animation effects" is off
  (`UISettings.AnimationsEnabled`) or Raven's "Reduced motion" is on; events older than 3 s update
  texts only. Idle: a 1-second clock for relative times; `events.poll` every 500 ms while the window
  is shown and every 5 s when only the tray icon is left (`RavenSession.SetWindowVisible`, API.md).
- **Sanitising:** every string in every engine response is cleaned in the envelope (control,
  bidi, zero-width and other invisible characters removed, stacked combining marks capped,
  newlines normalised); names use a single-line form. A response with a field named like a secret
  (`seed`, `private_key`, `plaintext`, `recovery`) is refused unread.
- **Security words:** your words with read-aloud highlight (800 ms steps, click to pick a word,
  "Say: {word}"), word 10 marked as the check word, old code shown. Check words: typed words become
  neutral chips (tooltip "Raven word"), completion after 3 letters (`verify.word_hint`), Enter
  completes and never submits, no per-word right/wrong, one result at the end (match / typo /
  mismatch), Check withdrawn after 3 mismatches. The friend's words are never shown. A small link,
  "Older Raven? Use the old code instead", switches to the old code: the intro talks about the
  code (`check.oldCode.intro`), your old code (Cascadia Mono, in groups of four as in the design:
  `If4x 36FU omFi`, also on the Setup words page) to read aloud and one plain field for theirs
  (spaces and separators ignored, letter case matters), no per-character marks, Check gives match (the
  friend is verified exactly as with the words) or mismatch (`verify.check_old_code`); "Use the
  10 words instead" switches back. Mismatches of words and code share the limit of 3.
- **Privacy settings:** "Encrypt everything stored on this computer" and "Erase Raven from this
  computer…" follow `privacy.state`. While `available` is false (this release) both are disabled
  with "Preview, not in this release". When available, the switch shows `encrypted` and turns
  encryption on with `privacy.encrypt`, and Erase opens a dialog (`Dialogs/EraseDialog.cs`) whose
  Erase button is enabled only after typing "erase", then calls `privacy.erase` and returns to
  Welcome. "Lock now" in the tray is greyed out while `available` is false.
- **Tray:** `Helpers/TrayIcon.cs`, a small `Shell_NotifyIcon` P/Invoke helper (no package), with
  the menu from `Core/Presentation/TrayMenu.cs`, the same as on macOS and Linux: the status
  ("Raven is listening on Wi-Fi" / "Raven isn't receiving", then "♥ background program ok" or
  "Raven's background program isn't responding"), the friends in the sidebar's order ("Ali · In
  touch 5 min ago", "2 new from Rena"; choosing one opens that chat), Open Raven, Add a friend…,
  Lock now (greyed out until the privacy pack), Quit window (keeps receiving), Quit and stop the
  background program. Closing the window hides Raven to the tray; quitting follows Settings >
  "When I quit" (keep / stop if started here / ask with the Quit dialog).
- **Breathing tray icon:** while Raven listens (and only then), the icon swaps prepared frames
  once a second, 100%, 80%, 60%, 80% opacity, like the macOS menu-bar mark (`TrayBreath`; a
  1-second `DispatcherQueueTimer` that runs only while listening). Each step checks Windows
  "Animation effects" and Raven's "Reduced motion": with either, the icon stays still on the
  normal frame. Windows tray icons cannot animate by themselves, hence the frames.
- **English control texts:** `Program.Main` sets the .NET UI culture to en-US for every thread and
  `Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "en-US"` (Windows
  App SDK, packaged and unpackaged) before the first window; a packaged (MSIX) Raven also sets
  `Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride` (it throws when unpackaged).
  So WinUI's own control texts (for example the text box context menu, control tooltips) should be
  English. What still follows the Windows display language is drawn by Windows itself: the title
  bar's button tooltips and window (Alt+Space) menu, the notification area around the icon, the
  firewall prompt and other system dialogs, and the MSIX installer.

- **Network screens** (desktop brief "Network screens"; API.md "Network"; NETWORK_PARITY.md):
  - *Network* (sidebar footer): the listening chip and background program, then one row per way
    from `network.status` with a state chip (On / Off / Preview, not in this release / Lab only /
    Not on this computer), one plain line of what it does (`network.way.*`) and the engine's reason,
    "In use now" where the background program says, Details (the CLI's row, the gate, its lab
    switch) and a switch where `can_set` (turning on needs the gate open; turning off always works).
    Below: ports and addresses, LAN on for a while (`lan_for`), router port opening, relays in use
    (with the friend who lends one), the web proxy (auto / off / `http://host:port`, never a user
    name), Bluetooth nearby, who carries mail for you and for whom you carry (with the "Carry mail
    for friends" switch), the bridge / store / relay flags with their honest effects, hide IP,
    and the protections. A "Lab switches on" banner names the switches when a developer engine lists
    any (a release engine lists none, so never in release).
  - *Windows:* the Bluetooth nearby row and card say plainly that nearby is inactive on Windows for
    now (NETWORK_PARITY.md row N7w: Windows doesn't say which Bluetooth address it uses, so Raven
    treats it as the PC's fixed address), with the lab switch `RAVEN_LAB_NEARBY_ALLOW_PUBLIC_ADDR=1`
    in developer builds, and never say "In use now" for it. While Internet direct or p2p is switched
    on, a Windows Firewall note names the prompt (Private networks, Allow; never Public) and the
    ports: Internet direct TCP (`internet_listen`, default 7422), p2p TCP and UDP (`p2p_listen`,
    default 7423), and the manual Private-profile rule; the restart dialog repeats it.
  - *Consent sheets* (`Dialogs/ConsentDialog.cs`) before `carry_for_friends`, `friends_carry_mine`
    and `handoff`: exactly the three the engine refuses without `agreed` (API.md, the consent
    rule). Each shows the engine's own question (`texts.<setting>`, the terminal's "[y/N]"
    removed), what it does, what it costs (bandwidth, disk, being online), what others can and
    can't see. Nothing is sent before "I agree, turn it on"; then `agreed: true`. Enter and Esc
    mean "Not now". A `CONSENT_NEEDED` from a newer engine opens the same sheet. Lending a relay
    (`relay_for_friends`) and the bridge/store/relay flags have no sheet: their honest effect
    stands beside the switch (`network.relayForFriends.effect`, the engine's `help.effects`).
  - *Restart the background program* (`Dialogs/RestartDialog.cs`) when `network.set` says
    `restart_needed`: `node.stop` then `node.start`; a login service is left running (`managed`), a
    background program this app didn't start gets the engine's `advice` (how to stop it by hand),
    and "Later" keeps a note on the page until a restart.
  - *Check network* (from the Network page): `network.check`'s summary, next step and network lines
    (Raven's sentence first, the doctor's words below); "Ask the router" runs the read-only router
    probe only on request.
  - *A friend's Routes* (Routes button in the chat header, `Dialogs/ContactRoutesDialog.cs`): saved
    routes with chips, route hints, what the friend told this computer, who carries its mail; change
    the Internet / p2p route (with relays), remove a route, choose up to two verified mail friends,
    hide this computer's IP from that friend.
  - *Path chip* on every message from `path_kind` (LAN, Internet, p2p, Relay, Relay · 443, Nearby,
    Friend, Handoff, Letter...; the engine's words when it has no kind), Narrator: "Delivery path: …".
  - *Security* (sidebar footer): security notices (read-only; resolving stays in the terminal) and
    the background program's counts; Lock (Lock v2: Lock now keeps the background program running
    without its keys, then the Locked screen asks for the Raven passphrase; `PASSPHRASE_WRONG`,
    `PASSPHRASE_NEEDED`, `UNLOCK_WAIT` show the engine's words); Updates (read-only; installing stays
    in the terminal). The app opens on the Locked screen when `lock.state` says locked, and the
    tray's "Lock now" uses Lock v2 when the running background program serves it.
  - Every text is in `strings.en.json` (`network.*`, `security.*`, `lock.*`, `updates.*`); every
    control has a Narrator name; nothing animates except a progress ring while the engine answers
    (hidden with reduced motion).

## Unverified (could not be checked on the Mac this was written on)

- **The Calm restyle (2026-10-28) was never compiled or run.** Written on a Mac without .NET: no
  build, no test run, no screenshots. It was checked by reading, by an independent read-only review
  for compile errors (two found and fixed: `Grid.SetColumn` on a `UIElement`, an `x:Double` resource
  given to `ColumnDefinition.Width`), by `xmllint` on every XAML file and by scripts (brackets,
  `using`s, every style/ThemeResource key in every theme, every `x:Name` the code-behind uses, the
  strings checker). WinUI APIs used for the first time here: `Window.SystemBackdrop` with
  `MicaBackdrop` and `MicaController.IsSupported()`, `DoubleAnimationUsingKeyFrames` with
  `LinearDoubleKeyFrame`/`KeyTime.FromTimeSpan` (and `EnableDependentAnimation` for the segmented
  thumb's width), `FillBehavior.Stop`, `ToggleButtonAutomationPeer.GetLocalizedControlTypeCore`,
  `AutomationProperties.SetLiveSetting` with a `LiveRegionChanged` event, `RadioButtons`,
  `PathGeometry`/`ArcSegment` for the group ring, `NavigationView.PaneHeader` and the
  NavigationView lightweight resources (`NavigationViewContentGridCornerRadius` and friends: an
  unknown key is simply unused), `TextBox` lightweight resources set in code, `ContentDialog`
  primary-click deferral for the shackle.
- Look: whether the NavigationView overrides give the Calm sidebar (selected row on the surface, no
  selection pill, the fine line beside the content), the translucent pane over Mica, the
  `CalmSwitch` template, the composer's borderless text box and the radio-button templates look as
  intended; dark and high-contrast rendering; DPI.
- Theme switching at runtime: styles set from code follow `RequestedTheme` through their
  `ThemeResource` setters (as the earlier screens relied on); flyouts (the friends' right-click menu)
  and the title bar may keep the Windows theme.
- Groups were never run against the real engine (the lab switches `RAVEN_LAB_TEST_A=1
  RAVEN_LAB_HORIZON=1 RAVEN_LAB_GROUPS=1` on a debug engine); only the mock and the contract.

- **The Network screens were never compiled or run.** This round was written on a Mac without the
  .NET SDK or Windows: no build, no test run, no screenshots. They were checked by reading, by
  `xmllint` on every XAML and project file, and by Python scripts (every string key used in C#
  and XAML exists, every `Loc.Format` passes exactly its text's placeholders, brackets balance,
  XAML handlers and names exist in the code-behind). CI's Windows job is the first build and the
  first run of `NetworkViewModelTests`, `NetworkScreensTests`, `NetworkPresenterTests` and the new
  engine test. WinUI APIs used for the first time here: `DropDownButton` with a code-built
  `MenuFlyout`, `InfoBar.Closed`, `InfoBar.ActionButton` set in code, `PasswordBox`, `CheckBox`
  lists in a `ContentDialog`.

- **The app on the engine** has not been started: Setup creating a real identity (DPAPI), `node.start`
  launching `raven-node.exe` from next to `Raven.exe`, sending, and the Release engine with the platform
  stores were not run by hand. CI runs the engine tests (debug library, file-backed stores) and a smoke of
  the Release library (`app.info`, `profile.state`, `BAD_REQUEST`, and its refusal of the file-backed
  test stores). `raven_client.dll` and
  `raven-node.exe` link the MSVC C runtime dynamically (`vcruntime140.dll`): an installer must make sure it
  is present (or the engine must be built with a static C runtime).
- The reload after an `events.poll` `reset` (the engine dropped events from its bounded buffer:
  `RavenSession.Resynced`, then `ShellPage` reads the node status, the friends and the open chat again)
  has not run; nothing in the tests makes the engine drop events.
- `messages.retry` of a message that ended returns a new message with a new id (`replaces`, API.md);
  the chat shows it as a new bubble below the old one instead of in its place.
- **Nothing has been run by hand.** CI compiles the app and runs the tests; the XAML and WinUI
  code-behind are compiled there but never exercised. Changes made on the Mac (like the engine wiring)
  were checked by reading and Python scripts (string keys, XML, braces) before CI compiled them.
- Windows App SDK **2.5.1** APIs were written from knowledge of 1.5–1.8 (NavigationView,
  SelectorBar, InfoBar, InfoBadge, Expander, AppWindow.Closing, Window.VisibilityChanged,
  AppInstance redirection). If 2.x renamed anything, the build will say so.
- `dotnet build` with Windows App SDK 2.5.1 and the .NET 8 SDK on `windows-latest`: newer Windows
  App SDKs require a recent `Microsoft.Windows.SDK.NET.Ref`; if the build asks for one, add
  `<WindowsSdkPackageVersion>` (the value the error names) to `Raven.Windows.csproj`.
- MSIX packaging (`-p:WindowsPackageType=MSIX`) and the tile images were never packaged or signed.
- Runtime look and feel: Fluent accent overrides, NavigationView friend rows (custom content in
  `NavigationViewItem`), ContentDialog sizes, dark / high-contrast rendering, DPI scaling of the
  minimum window size, the tray icon and its menu, Explorer-restart recovery, single-instance
  redirection, `ms-appx:///` images in the unpackaged build, the friends in the tray menu and
  the breathing frames (`NIM_MODIFY` once a second), `Microsoft.Windows.Globalization` in
  Windows App SDK 2.5.1 (if 2.x moved it, the build will say so).
- The custom `{h:Str}` markup extension and `UIElement.PreviewKeyDown` are used as documented for
  WinUI 3 but were not exercised.
- The old-code part of Check words, the Erase dialog, the privacy switch states and the greyed
  "Lock now" were checked by reading and by a Python script (string keys used in C# and XAML exist
  in `strings.en.json`; every API.md operation has a `RavenOps` constant, a typed wrapper and a
  MockRaven case; braces balance; XAML and manifests parse), not by a compiler.
- `SetCurrentProcessExplicitAppUserModelID` for the unpackaged build (taskbar grouping by app id)
  was not observed on Windows.

## Open issues

- Calm items left out because the engine doesn't report or keep them (honest states only): "Start
  Raven when I log in" (General), "Lock Raven when this app is idle" (Lock) and "Share my network
  address in my invite" (Privacy; the invite asks each time). They come with engine support.
- The theme and "Show technical details" live in `%LOCALAPPDATA%\Raven\desktop-ui.json` (this app
  only); the macOS and Linux apps keep theirs their own way.
- A few rows still use WinUI's `ToggleSwitch` (carry for friends, hide IP, the help flags, dialogs):
  green when on like the Calm switch, without its spring.

- English control texts: the overrides above were written from the Windows App SDK documentation and
  have not run; whether every WinUI control text (context menus, InfoBar close tooltip) then shows in
  English on a non-English Windows is unverified. The MSIX manifest declares `en-US` only.
- The privacy pack's actions are not in the app yet: "Encrypt everything", "Erase" and "Lock now"
  stay disabled (Preview) until `privacy.state` says `available`. The engine already encrypts where
  the build does, and the Locked screen's Unlock clears a `raven lock` through it
  (`privacy.unlock`); with `--mock-locked` the mock's Unlock says it is not available yet.
- The tray menu is built when it opens, so a friend's line does not change while the menu is open.
- The chat shows the last 100 messages in a simple stack (no virtualisation or "load earlier").
- `core/desktop/design/strings.en.json`, `tokens.json` and the draft word list are build inputs, and
  `core/desktop/README.md` holds the app id the tests check: updating the `core/` submodule can change
  them. Dependabot does not watch NuGet.
- Code signing, installer, auto-start of the app with Windows, and toast notifications are not
  done.

## License

GNU Affero General Public License v3.0 (`LICENSE`). The Raven name, logo and app icons are covered
separately by `core/TRADEMARK.md` and `core/ASSET_LICENSE.md`. Security issues: please write to
security@raven-messager.com (`SECURITY.md`), not a public issue.
