# Releasing Archopy

The Visual Studio solution targets .NET 10 for Windows. On a Windows x64 machine with the .NET 10 SDK and Inno Setup, run `scripts/Build-Installer.ps1`. The self-contained installer is written to `artifacts/installer/Archopy-Setup-x64.exe`.

## GitHub

Push the source to the intended repository and tag the version declared in `src/Archopy/Archopy.csproj` (for this build, `v1.0.0`). The Windows installer workflow attaches an installer to a draft release. Review the asset before publishing. For a signed release, attach the signed build.

## Microsoft Store

Use the traditional EXE submission in Partner Center. The developer is qupix, the product name is Archopy and the architecture is x64. Host the privacy page in this directory at a public URL. The [customiser screenshot](store/customiser.png) is 1366 × 768. Use qupix.dev@proton.me for support.

**Suggested listing description**

Arrange a clock, photos, a calendar and a compact Spotify player on your Windows desktop. Drag items into place and snap them to the center or edge of your screen. Change the size and proportions of the time, crop photos and choose a framed or image-only layout.

Connect your Spotify account to control playback and see your progress through a track. Enable lyrics to look up available timed lines and display them over your wallpaper. Archopy remembers your layout and can reopen it at Windows sign-in.

Sign the published PE payload in `artifacts/publish/Archopy-win-x64` using a trusted publisher certificate or signing service, retaining valid signatures on bundled files. Package the signed directory with `Build-Installer.ps1 -SkipPublish -SignToolCommand <command>`. The Inno signing command uses its `$f` file placeholder. Verify the signed installer and uninstaller, test a clean install and upgrade, and host the final EXE at an immutable, version-specific HTTPS URL. Submit that URL and the silent switches `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`. [Microsoft Store EXE requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements)

## Music integration

Register the exact redirect URI `http://127.0.0.1:43921/callback` for the Archopy Spotify app. Test consent, disconnect, account restoration, remote playback and account restrictions with an authorized account. Public Spotify access depends on the app’s quota mode. Review the intended timed-lyrics use and rights for public distribution with the relevant providers. [Spotify quota modes](https://developer.spotify.com/documentation/web-api/concepts/quota-modes) · [Spotify Developer Policy](https://developer.spotify.com/policy)
