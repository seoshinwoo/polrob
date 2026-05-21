#!/bin/bash

echo "🚀 Restoring and Building the project globally..."

# 등록한 모든 런타임(RuntimeIdentifiers)을 대상으로 한꺼번에 복원(Restore)합니다.
dotnet restore polrob.Client/polrob.Client.csproj

echo "========================================="
echo "🚀 Now launching them (Building & Running individually)..."

# 안드로이드 기기/에뮬레이터로 배포 및 실행 (백그라운드)
echo "📱 Launching Android..."
dotnet build polrob.Client/polrob.Client.csproj -t:Run -f net10.0-android -r android-arm64 &

# iOS 실제 기기로 배포 및 실행 (ShinWoo's iPhone)
echo "🍎 Launching iOS on ShinWoo's iPhone..."
dotnet build polrob.Client/polrob.Client.csproj -t:Run -f net10.0-ios -r ios-arm64 -p:_DeviceName=00008150-001623DC2620401C &

# 백그라운드 작업들이 모두 끝날 때까지 대기
wait

echo "✅ Both processes have started!"
