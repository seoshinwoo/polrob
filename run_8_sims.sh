#!/bin/bash

echo "🚀🚀🚀 Starting 8 iOS Simulators Launch Script 🚀🚀🚀"

# 1. 기기 식별자(UDID) 설정
SIM1="1789F7CF-143A-4A63-AE0B-7EE0C446A7A9" # 시뮬레이터 1: iPhone 17 Pro
SIM2="6CB176BD-A408-40A6-9C43-847E4A1E2B9C" # 시뮬레이터 2: iPhone 17 Pro Max
SIM3="7ECD30F2-163A-44AD-9AEC-BC91A698EA9B" # 시뮬레이터 3: iPhone 17
SIM4="A1AC0098-D76F-47B8-A433-DE9EFDE27C3D" # 시뮬레이터 4: iPhone Air
SIM5="07185B37-BDA8-452D-B5A5-5F1DE01367AD" # 시뮬레이터 5: iPhone 16 Pro
SIM6="AA859068-054A-427D-8391-FAC8EF7B35D5" # 시뮬레이터 6: iPhone 16 Pro Max
SIM7="3DF93812-C336-4BCC-BFD4-00AD67AB99B6" # 시뮬레이터 7: iPhone 16
SIM8="CE34E79E-1CCA-40D2-9175-26732D0EF0E6" # 시뮬레이터 8: iPhone 16 Plus
BUNDLE_ID="com.companyname.polrob"

echo "========================================="
echo "🛠️ 1. iOS 시뮬레이터용 앱 빌드 (1번만)"
dotnet build polrob.Client/polrob.Client.csproj -f net10.0-ios -r iossimulator-arm64
APP_PATH="polrob.Client/bin/Debug/net10.0-ios/iossimulator-arm64/polrob.Client.app"

echo "========================================="
echo "🍎 2. 8개의 시뮬레이터 부팅"
for SIM_UDID in $SIM1 $SIM2 $SIM3 $SIM4 $SIM5 $SIM6 $SIM7 $SIM8; do
    xcrun simctl boot "$SIM_UDID" || true
done
open -a Simulator

# 시뮬레이터가 완전히 부팅될 때까지 대기
for SIM_UDID in $SIM1 $SIM2 $SIM3 $SIM4 $SIM5 $SIM6 $SIM7 $SIM8; do
    echo "Waiting for $SIM_UDID to finish booting..."
    xcrun simctl bootstatus "$SIM_UDID"
done

echo "========================================="
echo "📲 3. 각 시뮬레이터에 앱 설치 및 실행"
for SIM_UDID in $SIM1 $SIM2 $SIM3 $SIM4 $SIM5 $SIM6 $SIM7 $SIM8; do
    echo "Installing and launching on $SIM_UDID ..."
    xcrun simctl install "$SIM_UDID" "$APP_PATH"
    xcrun simctl launch "$SIM_UDID" $BUNDLE_ID &
done

wait
echo "========================================="
echo "✅✨ 성공! 8개의 시뮬레이터에서 앱이 실행되었습니다!"
