# Building the Stik-er-in Android APK

This guide builds an installable Android package from this repository. Run commands in the repository root. Building the APK does not deploy the friends/Pinterest services or copy your browser library onto your phone.

## Project requirements

- .NET SDK **10.0.402**, with workload set **10.0.402.1** (pinned in `global.json`).
- The **maui-android** workload.
- Android SDK **platform 36**, its build tools and platform tools.
- A compatible Java JDK. The current Mac has Microsoft OpenJDK **17** installed at `~/Library/Java/maui-jdk`.
- Internet for the first dependency restore. Subsequent builds can reuse downloaded dependencies.

The Android project is `StickerApp/StickerApp.csproj`, targeting `net10.0-android`. The app ID is `com.stickerstudio.app`; the minimum Android version is **7.0 / API 24**. Always specify the Android framework so this multi-target project does not also try to build iOS. Android builds do not need Xcode.

## 1. Set up this Mac

Open a terminal in the project folder:

```sh
cd /Users/dinnyvanhuizen/Projects/whatsapp-sticker-bullshit
export PATH="$HOME/.dotnet:$PATH"
dotnet --version
dotnet workload list
```

If the Android workload is missing, install it:

```sh
dotnet workload install maui-android --version 10.0.402.1
```

If the Android SDK/JDK dependencies are missing, the Android workload can install them. The following command accepts the Android SDK licences:

```sh
dotnet build StickerApp/StickerApp.csproj \
  -f net10.0-android \
  -t:InstallAndroidDependencies \
  -p:AndroidSdkDirectory="$HOME/Library/Android/sdk" \
  -p:JavaSdkDirectory="$HOME/Library/Java/maui-jdk" \
  -p:AcceptAndroidSDKLicenses=True
```

On another computer, replace the SDK/JDK paths with that computer's installation paths. The commands below use this Mac's paths. Do not run the dependency installer again for every build.

## 2. Build an APK for personal testing

This is the quickest option for installing and testing on your own phone:

```sh
dotnet publish StickerApp/StickerApp.csproj \
  -f net10.0-android \
  -c Debug \
  -p:AndroidPackageFormats=apk \
  -p:EmbedAssembliesIntoApk=true \
  -p:AndroidSdkDirectory="$HOME/Library/Android/sdk" \
  -p:JavaSdkDirectory="$HOME/Library/Java/maui-jdk"
```

`EmbedAssembliesIntoApk=true` makes the APK self-contained for installation without a debugger's fast-deployment files. Debug signing uses the local development key; keep that key if you want future test APKs to update the same installation.

Find the signed APK after the command finishes successfully:

```sh
find StickerApp/bin/Debug/net10.0-android -name '*-Signed.apk' -print
```

The normal publish location is:

```text
StickerApp/bin/Debug/net10.0-android/publish/com.stickerstudio.app-Signed.apk
```

Use the **Signed.apk** file. A successful compile-only `-t:Compile` check does not produce an installable APK. A first build may take several minutes while downloading/compiling dependencies; later builds are usually faster.

## 3. Install it on your Android phone

### Transfer the APK

Send the signed APK to your phone using USB, a private file transfer, or your preferred file-sharing method. Open it on the phone. If Android asks, allow **Install unknown apps** for the app opening the file, then install. You can turn that permission off afterward.

### Or install over USB with ADB

Enable Developer options and USB debugging on your phone, connect it, and accept the phone's computer-authorisation prompt:

```sh
"$HOME/Library/Android/sdk/platform-tools/adb" devices
"$HOME/Library/Android/sdk/platform-tools/adb" install -r \
  StickerApp/bin/Debug/net10.0-android/publish/com.stickerstudio.app-Signed.apk
```

If the APK is in a different location, use the path printed by `find`. `-r` updates an existing compatible installation while retaining its app data. The phone has its own local library: use editable ZIP pack backups to move packs from the browser app.

## 4. Build a Release APK with your own signing key

Use a stable private signing key for APKs you distribute and intend to update. Keep the keystore and its passwords backed up securely, outside this repository. Losing the signing key prevents updating sideloaded installations signed with it.

Create the key once; `keytool` prompts for passwords and certificate details:

```sh
mkdir -p "$HOME/.stikerin-signing"
"$HOME/Library/Java/maui-jdk/bin/keytool" -genkeypair \
  -keystore "$HOME/.stikerin-signing/stikerin.keystore" \
  -alias stikerin \
  -keyalg RSA \
  -keysize 2048 \
  -validity 10000
```

In your editor, create these two local files outside the repository, each containing only its password on one line:

```text
~/.stikerin-signing/store-password.txt
~/.stikerin-signing/key-password.txt
```

The first is the keystore password; the second is the key password (often the same, depending on the keystore). Restrict their permissions:

```sh
chmod 700 "$HOME/.stikerin-signing"
chmod 600 "$HOME/.stikerin-signing/stikerin.keystore" \
  "$HOME/.stikerin-signing/store-password.txt" \
  "$HOME/.stikerin-signing/key-password.txt"
```

Publish using password-file references, keeping passwords out of command history:

```sh
dotnet publish StickerApp/StickerApp.csproj \
  -f net10.0-android \
  -c Release \
  -p:AndroidPackageFormats=apk \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore="$HOME/.stikerin-signing/stikerin.keystore" \
  -p:AndroidSigningKeyAlias=stikerin \
  -p:AndroidSigningStorePass="file:$HOME/.stikerin-signing/store-password.txt" \
  -p:AndroidSigningKeyPass="file:$HOME/.stikerin-signing/key-password.txt" \
  -p:AndroidSdkDirectory="$HOME/Library/Android/sdk" \
  -p:JavaSdkDirectory="$HOME/Library/Java/maui-jdk"
```

Find the result:

```sh
find StickerApp/bin/Release/net10.0-android -name '*-Signed.apk' -print
```

Before distributing an update, increase `ApplicationVersion` (the integer Android version code) in `StickerApp/StickerApp.csproj`. Update `ApplicationDisplayVersion` for the visible version number. Keep the same app ID and signing key across updates.

Google Play publishing is a separate process, normally using an **AAB** and Play App Signing. An APK build alone does not publish the app or complete its store/privacy/security requirements.

## Troubleshooting

| Problem | What to check |
| --- | --- |
| `dotnet: command not found` | Add `$HOME/.dotnet` to `PATH`, or invoke `$HOME/.dotnet/dotnet` directly. |
| SDK/workload version error | Run from the repository root and install the SDK/workload versions pinned in `global.json`. |
| Android SDK or Java not found | Check the two directory properties above. The Java directory must contain `bin/java`. |
| Missing platform or licence error | Run the dependency installation command in step 1. |
| iOS/Xcode build error | Make sure the command includes `-f net10.0-android`. |
| Restore cannot reach NuGet | Restore requires internet unless all needed packages are already cached. Avoid `--no-restore` on a fresh checkout. |
| `INSTALL_FAILED_UPDATE_INCOMPATIBLE` | The installed app uses a different signing key. Prefer rebuilding with its original key. Uninstalling removes its local data: export backups first if you choose that route. |
| `INSTALL_FAILED_VERSION_DOWNGRADE` | Increase `ApplicationVersion` and rebuild; do not uninstall simply to work around it without backing up local data. |
| Online imports/friends do not work | Check internet access and the relevant service deployment/configuration. Packaging the app does not start or deploy those services. |

After installing, check image/GIF editing, pack import/export, and any native-only features you changed on the actual phone. A successful build does not verify phone behaviour.
