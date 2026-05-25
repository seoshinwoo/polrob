#!/bin/bash

echo "🚀 Building projects..."

echo "========================================="
echo "🍎 Launching iOS Simulator (iPhone 17 Pro)..."
# 에러가 나는 dotnet run 대신 Apple의 공식 커맨드(xcrun)를 직접 사용하여 설치/실행합니다.
xcrun simctl boot 1789F7CF-143A-4A63-AE0B-7EE0C446A7A9 || true
open -a Simulator
dotnet build polrob.Client/polrob.Client.csproj -f net10.0-ios -r iossimulator-arm64
xcrun simctl install 1789F7CF-143A-4A63-AE0B-7EE0C446A7A9 polrob.Client/bin/Debug/net10.0-ios/iossimulator-arm64/polrob.Client.app
xcrun simctl launch 1789F7CF-143A-4A63-AE0B-7EE0C446A7A9 com.companyname.polrob.client &

echo "📱 Launching Physical iOS Device (ShinWoo's iPhone)..."
# 실제 기기에는 dotnet 커맨드를 사용해 배포합니다.
dotnet build polrob.Client/polrob.Client.csproj -t:Run -f net10.0-ios -r ios-arm64 -p:_DeviceName=00008150-001623DC2620401C &

# 백그라운드 작업들이 모두 끝날 때까지 대기
wait

echo "✅ Both iOS apps have started!"
