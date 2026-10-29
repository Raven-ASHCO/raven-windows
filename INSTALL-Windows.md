# Install Raven on Windows

Raven for Windows comes as a zip file, `Raven-windows-x64-0.1.0.zip`. You need Windows 11, or
Windows 10 version 1809 or later, on a 64-bit (x64) PC. You do not need administrator rights.

Raven is not signed, so Windows warns you the first time. The steps below get past that once.
Please read "An honest note" at the end before you install.

## 1. Check the download (recommended)

Next to the zip there is `Raven-windows-x64-0.1.0.zip.sha256`. Put both files in the same folder
(for example Downloads). Open PowerShell (Start, type PowerShell, Enter), go to that folder with
`cd Downloads`, and run:

```powershell
(Get-FileHash .\Raven-windows-x64-0.1.0.zip -Algorithm SHA256).Hash -eq ((Get-Content .\Raven-windows-x64-0.1.0.zip.sha256) -split ' ')[0]
```

It must print `True`. If it prints `False`, do not use the file. (If your files have another version
number, use their names in the line above.)

## 2. Unzip

Right-click the zip > **Extract All** > **Extract**. In the new folder you find the `Raven` folder,
`install.ps1`, `uninstall.ps1` and this guide. Always unzip first: install.ps1 does not work from
inside the zip.

## 3. Install

Right-click `install.ps1` > **Run with PowerShell**. (On Windows 11 it can be under **Show more
options**.) A window shows what it does and ends with "Raven is installed". Press Enter to close it.

If the window closes at once or says that running scripts is disabled, run it this way instead:
in the unzipped folder, right-click an empty spot > **Open in Terminal** (or type `powershell` in
the folder's address bar and press Enter), then:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

`-ExecutionPolicy Bypass` lets this one script run this one time; it does not change any Windows
setting.

What install.ps1 does, and nothing else: it copies Raven to `%LOCALAPPDATA%\Programs\Raven` (a
folder of your own user account) and adds **Raven** to your Start menu. No service, no firewall
rule, no changes for other users of the PC.

## 4. Open Raven the first time: SmartScreen

Open **Start** and click **Raven**. Windows may show a blue window: **Windows protected your PC**
("Microsoft Defender SmartScreen prevented an unrecognized app from starting").

1. Click **More info**. It shows App: `Raven.exe`, Publisher: Unknown publisher.
2. Click **Run anyway**.

Windows normally asks only this once.

If Windows says instead that **Smart App Control** blocked Raven (Windows Security > App & browser
control), or there is no **Run anyway** button (often on a PC that an organisation manages), this
PC does not run unsigned apps. Raven cannot run there until it is signed. We do not suggest turning
Smart App Control off just for Raven.

## 5. What Raven does on first start

1. It asks for a name. The name is only shown on this computer and is never sent to anyone.
2. It asks whether to receive messages. If you say yes, Raven starts its small background program,
   `raven-node` (in the Raven folder), which listens on your network (port 7420) so your friends
   can reach you. Only friends you add can send you anything.
3. It creates your Raven identity: a private key that never leaves this PC. Raven keeps it in the
   folder `%USERPROFILE%\.raven`, together with your friends and messages. Your key and the key of
   your message history are protected by Windows for your user account, so other accounts on this
   PC cannot use them.
4. It shows your security words. Compare them with a friend's (in person or on a call) to verify
   each other.

## 6. The Windows Firewall prompt (raven-node)

When raven-node starts listening, Windows may show **Windows Security Alert: Windows Defender
Firewall has blocked some features of this app**, for raven-node.

1. Tick **Private networks**.
2. Leave **Public networks** unticked.
3. Click **Allow access**. Windows asks an administrator to confirm (if that is you, click Yes).

**What Public means.** Windows sorts networks into Private (your home or office network, which you
trust) and Public (cafes, hotels, airports, and any network you did not mark as private). Allowing
Public lets raven-node accept connections on those networks too: strangers there still cannot send
you anything, but they could see that this PC runs a program that answers. Leave it off.

**When to allow Public: almost never.** If your own home network shows as Public, mark that network
as Private instead: Settings > Network & internet > Wi-Fi (or Ethernet) > your network > Network
profile type > **Private network**. Do the same for a phone hotspot or a cable between two PCs that
you use with a friend; Windows often files those as Public.

If you clicked **Cancel**, Windows blocks raven-node for incoming connections: friends on your
network cannot reach you directly. To fix it: Windows Security > Firewall & network protection >
**Allow an app through firewall** > **Change settings** > tick **Private** next to raven-node > OK
(an administrator must confirm).

**Tor.** To reach friends who are not on your network, Raven can use Tor, a network that hides
where you are. Its helper program, `raven-tor-helper`, only makes outgoing connections and talks to
raven-node on this PC alone, so Windows normally does not ask about it. If another firewall program
asks about raven-tor-helper, allow it, or Raven cannot use Tor.

## 7. Bluetooth: not on Windows

Raven for Windows does not use Bluetooth. Reaching a friend close by over Bluetooth, without any
network, works between Macs only for now. On Windows, Raven reaches friends over your network and
the internet. If Raven's Network page lists Bluetooth nearby, it stays off on Windows.

## 8. Quit, and remove Raven

**Quit.** Closing the window keeps Raven receiving in the background. Its icon is in the
notification area near the clock (click the ^ arrow if you do not see it). Clicking it opens
Raven; right-click it for the menu:

- **Quit window (keeps receiving)**: friends can still reach you.
- **Quit and stop the background program**: Raven stops completely until you open it again.

Settings > **When I quit** sets what Quit does by default.

**Remove.**

1. Right-click the Raven icon > **Quit and stop the background program**.
2. Press Windows key + R, type `%LOCALAPPDATA%\Programs\Raven` and press Enter. Right-click
   `uninstall.ps1` > **Run with PowerShell**. (Or in PowerShell:
   `powershell -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\Raven\uninstall.ps1"`.)

This removes the Raven app folder and the Start menu entry. It never deletes your Raven data:
your identity, friends and messages stay in `%USERPROFILE%\.raven`, and the app's own settings
(theme) in `%LOCALAPPDATA%\Raven`. Installing Raven again brings them back.

To erase them too: in Raven, Settings > Privacy > **Erase Raven from this computer**, when your
version offers it (otherwise it shows "Preview"); or `raven erase` with Raven's command-line tool,
if you use it; or, after uninstalling, delete the two folders above yourself. **This cannot be
undone:** your identity is gone and your friends must add you again. The firewall rule for
raven-node stays until you remove it in Windows Security > Firewall & network protection > Allow an
app through firewall.

## An honest note

- **Not signed.** This Raven has no code-signing certificate, so Windows cannot tell you who made
  it, and SmartScreen warns about it. Get it only from Raven's own site or repository and check its
  SHA-256 (step 1).
- **Not independently reviewed.** Raven's security has not been reviewed by an independent
  security firm yet. Do not rely on it where your safety depends on it.
- Problems: please open an issue in the repository. Security problems: please write to
  security@raven-messager.com, not a public issue.
