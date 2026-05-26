#!/bin/bash

echo "🚀🚀🚀 Starting Multi-Device Launch Script (6 Devices) 🚀🚀🚀"

# 1. 기기 식별자(UDID) 설정
# (참고: 현재 맥북에 iPhone 16 시리즈 모델이 설치되어 있지 않아 사용 가능한 다른 기기로 대체해 두었습니다)
SIM1="1789F7CF-143A-4A63-AE0B-7EE0C446A7A9" # 시뮬레이터 1: iPhone 17 Pro
SIM2="6CB176BD-A408-40A6-9C43-847E4A1E2B9C" # 시뮬레이터 2: iPhone 17 Pro Max
SIM3="7ECD30F2-163A-44AD-9AEC-BC91A698EA9B" # 시뮬레이터 3: iPhone 17 (iPhone 16 Pro 대용)
SIM4="A1AC0098-D76F-47B8-A433-DE9EFDE27C3D" # 시뮬레이터 4: iPhone Air (iPhone 16 Pro Max 대용)

REAL_IOS="00008150-001623DC2620401C"       #제 실제 아이폰 (ShinWoo's iPhone)
BUNDLE_ID="com.companyname.polrob" # 여기가 원인이었습니다! (.client 제거)

echo "========================================="
echo "🛠️ 1. iOS 시뮬레이터용 앱 1번만 빌드 (전체 시뮬레이터 공용)"
# 시뮬레이터용은 앱을 하나만 빌드해서 4군데에 똑같이 복사해 넣으면 훨씬 빠릅니다.
dotnet build polrob.Client/polrob.Client.csproj -f net10.0-ios -r iossimulator-arm64
APP_PATH="polrob.Client/bin/Debug/net10.0-ios/iossimulator-arm64/polrob.Client.app"

echo "========================================="
echo "🍎 2. 4개의 시뮬레이터 부팅 및 앱 실행"
# 시뮬레이터들을 모두 켭니다.
for SIM_UDID in $SIM1 $SIM2 $SIM3 $SIM4; do
    xcrun simctl boot "$SIM_UDID" || true
done
open -a Simulator

# 시뮬레이터가 완전히 부팅될 때까지 대기 (이 과정이 없으면 앱 설치/실행이 무시될 수 있습니다)
for SIM_UDID in $SIM1 $SIM2 $SIM3 $SIM4; do
    echo "Waiting for $SIM_UDID to finish booting..."
    xcrun simctl bootstatus "$SIM_UDID"
done

# 각 시뮬레이터에 방금 빌드한 앱을 설치하고 실행합니다.
for SIM_UDID in $SIM1 $SIM2 $SIM3 $SIM4; do
    echo "Installing and launching on $SIM_UDID ..."
    xcrun simctl install "$SIM_UDID" "$APP_PATH"
    xcrun simctl launch "$SIM_UDID" $BUNDLE_ID &
done

echo "========================================="
echo "📱 3. 실제 기기(iOS, Android) 빌드 및 실행 시작..."
# 1단계: 순차적으로 '빌드(Build)'만 먼저 진행하여 obj 폴더 충돌을 방지합니다.
echo "🔨 실제 아이폰용 빌드 중..."
dotnet build polrob.Client/polrob.Client.csproj -f net10.0-ios -r ios-arm64
echo "🔨 실제 안드로이드폰용 빌드 중..."
dotnet build polrob.Client/polrob.Client.csproj -f net10.0-android -r android-arm64

# 2단계: 빌드가 완료된 앱을 백그라운드(&)에서 동시에 기기에 설치하고 실행(Run)합니다.
echo "🚀 폰에 설치 및 실행을 시작합니다..."
dotnet build polrob.Client/polrob.Client.csproj -t:Run -f net10.0-ios -r ios-arm64 -p:_DeviceName=$REAL_IOS &
dotnet build polrob.Client/polrob.Client.csproj -t:Run -f net10.0-android -r android-arm64 &

wait
echo "========================================="
echo "✅✨ 성공! 총 6개의 기기/시뮬레이터에서 앱이 실행되었습니다!"