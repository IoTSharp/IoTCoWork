#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 3 ]]; then
  echo "Usage: $0 <publish-dir> <dist-dir> <rid>" >&2
  exit 64
fi

publish_dir="$(cd "$1" && pwd)"
dist_dir="$2"
rid="$3"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
app_name="IoTCoWork"
host_name="IoTCoWork"
macos_deployment_target="${MACOSX_DEPLOYMENT_TARGET:-12.0}"
app_version="${IOTCOWORK_INFORMATIONAL_VERSION:-${IOTCOWORK_APP_VERSION:-1.0.0}}"
bundle_short_version="${app_version#v}"
bundle_short_version="${bundle_short_version#V}"
bundle_short_version="${bundle_short_version%%+*}"
bundle_version="${bundle_short_version%%-*}"
work_dir="$repo_root/artifacts/app/$rid"
staging_dir="$work_dir/dmg-root"
app_dir="$staging_dir/$app_name.app"
contents_dir="$app_dir/Contents"
macos_dir="$contents_dir/MacOS"
resources_dir="$contents_dir/Resources"
icon_file="iotcowork.icns"
dmg_path="$dist_dir/IoTCoWork-$rid.dmg"

case "$rid" in
  osx-arm64)
    swift_target="arm64-apple-macosx$macos_deployment_target"
    ;;
  osx-x64)
    swift_target="x86_64-apple-macosx$macos_deployment_target"
    ;;
  *)
    echo "Unsupported macOS runtime identifier: $rid" >&2
    exit 64
    ;;
esac

if [[ ! -x "$publish_dir/$host_name" ]]; then
  echo "Missing executable host: $publish_dir/$host_name" >&2
  exit 66
fi

rm -rf "$work_dir"
mkdir -p "$macos_dir" "$resources_dir" "$dist_dir"
ln -s /Applications "$staging_dir/Applications"

sdk_path="$(xcrun --sdk macosx --show-sdk-path)"

MACOSX_DEPLOYMENT_TARGET="$macos_deployment_target" swiftc \
  "$repo_root/packaging/macos/IoTCoWorkApp.swift" \
  -target "$swift_target" \
  -sdk "$sdk_path" \
  -o "$macos_dir/$app_name" \
  -framework AppKit \
  -framework WebKit

cp "$publish_dir/$host_name" "$macos_dir/$host_name"
cp "$repo_root/packaging/macos/$icon_file" "$resources_dir/$icon_file"
chmod +x "$macos_dir/$app_name" "$macos_dir/$host_name"

cat > "$contents_dir/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleDisplayName</key>
  <string>IoTCoWork</string>
  <key>CFBundleExecutable</key>
  <string>IoTCoWork</string>
  <key>CFBundleIdentifier</key>
  <string>net.iotsharp.iotcowork</string>
  <key>CFBundleIconFile</key>
  <string>$icon_file</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>IoTCoWork</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$bundle_short_version</string>
  <key>CFBundleVersion</key>
  <string>$bundle_version</string>
  <key>LSApplicationCategoryType</key>
  <string>public.app-category.developer-tools</string>
  <key>LSMinimumSystemVersion</key>
  <string>$macos_deployment_target</string>
  <key>NSAppTransportSecurity</key>
  <dict>
    <key>NSAllowsLocalNetworking</key>
    <true/>
  </dict>
</dict>
</plist>
PLIST

codesign --force --deep --sign - "$app_dir"

rm -f "$dmg_path"
hdiutil create \
  -volname "$app_name" \
  -srcfolder "$staging_dir" \
  -ov \
  -format UDZO \
  "$dmg_path"

echo "$dmg_path"
